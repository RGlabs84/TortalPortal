using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Reflection;
using System.Text;

namespace TortalPortal
{
    public class PortalUIManager : MonoBehaviour
    {
        private static PortalUIManager _instance;
        public static TeleportWorld CurrentSourcePortal { get; private set; }

        private bool _isVisible;
        private bool _isConfigMode;

        // Everything the destination list needs to draw a row, resolved once when the window opens. Biome in
        // particular has to be precomputed: WorldGenerator.GetBiome samples world noise, and OnGUI runs at
        // least twice a frame for every row on screen.
        //
        // The row is built entirely from the server's index - no ZDO. A client does not hold the ZDO of a
        // portal on the far side of the map, so the snapshot and the PIN are fetched separately when the row
        // is actually selected.
        private sealed class PortalEntry
        {
            public ZDOID Id;
            public string Tag;
            public string Pin;
            public string Creator;
            public Vector3 Pos;
            public Quaternion Rot;
            public Heightmap.Biome Biome;
            public float Distance;
            public string Label;
            public string SearchKey;
            public bool Locked;
            public DetailState Details;
        }

        private enum DetailState { Pending, Resolved, Unavailable }

        private readonly List<PortalEntry> _entries = new List<PortalEntry>();
        private PortalEntry _selected;
        private Coroutine _detailFetch;

        private string _searchFilter = "";
        private Vector2 _scrollPos;
        private string _pinInput = "";
        private bool _showPinPrompt;
        private Texture2D _previewTex;
        private bool _previewExpanded;

        private string _configName = "";
        private string _configPin = "";
        private bool _configHasPin;

        // IMGUI control names, so we can tell whether the player is typing into one of our fields rather
        // than steering their character. See InputFocusPatch.
        private const string SearchFieldName = "TortalPortal.Search";
        private const string PinFieldName = "TortalPortal.Pin";
        private const string ConfigNameFieldName = "TortalPortal.ConfigName";
        private const string ConfigPinFieldName = "TortalPortal.ConfigPin";

        private static bool _textInputActive;

        // Set by HideUI, acted on in OnGUI: dropping IMGUI focus has to happen inside OnGUI, and HideUI is
        // called from the portal update tick.
        private bool _releaseFocus;

        public static bool IsTextInputActive() => _textInputActive;

        public static void Init()
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("PortalUIManager");
                _instance = go.AddComponent<PortalUIManager>();
                DontDestroyOnLoad(go);
            }
        }

        public static bool IsUIVisible() => _instance != null && _instance._isVisible;

        public static void ShowUI(TeleportWorld sourcePortal)
        {
            Configuration.LogDebug("PortalUIManager.ShowUI called.");
            if (_instance == null) Init();

            CurrentSourcePortal = sourcePortal;
            _instance._isConfigMode = false;
            _instance._isVisible = true;
            _instance.BuildEntries();
            Configuration.LogDebug($"Found {_instance._entries.Count} destination portals.");

            if (Minimap.instance != null)
            {
                Configuration.LogDebug("Opening Large Minimap.");
                Minimap.instance.SetMapMode(Minimap.MapMode.Large);
            }
        }

        public static void ShowConfigUI(TeleportWorld targetPortal)
        {
            Configuration.LogDebug("PortalUIManager.ShowConfigUI called.");
            if (_instance == null) Init();

            CurrentSourcePortal = targetPortal;
            _instance._isConfigMode = true;
            _instance._isVisible = true;

            ZDO zdo = targetPortal.GetComponent<ZNetView>().GetZDO();
            _instance._configName = zdo.GetString(ZDOVars.s_tag);
            _instance._configPin = zdo.GetString("PortalPIN");
            _instance._configHasPin = !string.IsNullOrEmpty(_instance._configPin);

            if (Minimap.instance != null) Minimap.instance.SetMapMode(Minimap.MapMode.None);
        }

        public static void HideUI()
        {
            Configuration.LogDebug("PortalUIManager.HideUI called.");
            if (_instance != null)
            {
                _instance._isVisible = false;
                _instance._isConfigMode = false;
                _instance._selected = null;
                _instance._showPinPrompt = false;
                _instance._pinInput = "";
                _instance._previewExpanded = false;
                _instance.StopDetailFetch();
                _instance.ClearPreview();

                // Hand the keyboard straight back rather than waiting for the next OnGUI, so the player is
                // never left standing still for a frame after the window closes.
                _textInputActive = false;
                _instance._releaseFocus = true;
            }

            CurrentSourcePortal = null;
            if (Minimap.instance != null && Minimap.instance.m_mode == Minimap.MapMode.Large)
            {
                Configuration.LogDebug("Closing Large Minimap.");
                Minimap.instance.SetMapMode(Minimap.MapMode.None);
            }
        }

        // ---- data ------------------------------------------------------------------------------------------

        private void BuildEntries()
        {
            _entries.Clear();
            _selected = null;
            _showPinPrompt = false;
            _pinInput = "";
            _previewExpanded = false;
            StopDetailFetch();
            ClearPreview();

            ZDOID sourceId = ZDOID.None;
            if (CurrentSourcePortal != null)
            {
                ZNetView nview = CurrentSourcePortal.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid()) sourceId = nview.GetZDO().m_uid;
            }

            Vector3 origin = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;

            foreach (PortalRegistry.Entry src in PortalRegistry.GetEntries())
            {
                if (src.Id == sourceId) continue;

                string tag = Sanitise(src.Tag);
                if (string.IsNullOrEmpty(tag)) tag = "Unnamed portal";
                string creator = Sanitise(src.Creator);
                if (string.IsNullOrEmpty(creator)) creator = "Unknown";

                Vector3 pos = src.Pos;

                PortalEntry e = new PortalEntry
                {
                    Id = src.Id,
                    Tag = tag,
                    Creator = creator,
                    Pos = pos,
                    Rot = src.Rot,
                    Biome = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(pos) : Heightmap.Biome.None,
                    Distance = Vector3.Distance(origin, pos),
                    Locked = src.Locked
                };

                e.SearchKey = tag.ToLowerInvariant();
                e.Label = $"<b>{tag}</b>{(e.Locked ? $"   <color={TortalUITheme.HexLocked}>[LOCKED]</color>" : "")}" +
                          $"\n<size=11><color={TortalUITheme.HexMuted}>{PrettyBiome(e.Biome)}   ·   " +
                          $"{Mathf.RoundToInt(e.Distance)}m   ·   {creator}</color></size>";

                _entries.Add(e);
            }

            _entries.Sort((a, b) => string.Compare(a.Tag, b.Tag, StringComparison.OrdinalIgnoreCase));
        }

        // Portal tags are player-entered, and every label we build is rich text. An unescaped '<' would eat
        // the rest of the row.
        private static string Sanitise(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace('<', '(').Replace('>', ')');
        }

        private static string PrettyBiome(Heightmap.Biome biome)
        {
            string s = biome.ToString();
            StringBuilder sb = new StringBuilder(s.Length + 4);
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i])) sb.Append(' ');
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        // ---- drawing ---------------------------------------------------------------------------------------

        private void OnGUI()
        {
            if (_releaseFocus)
            {
                _releaseFocus = false;
                _textInputActive = false;
                GUI.FocusControl(null);
                GUIUtility.keyboardControl = 0;
            }

            if (!_isVisible) return;
            TortalUITheme.EnsureBuilt();

            // Escape backs out of a field without closing the window, so there is always a way to hand the
            // keyboard back that is not "click somewhere harmless".
            if (_textInputActive && Event.current != null &&
                Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                _releaseFocus = true;
                Event.current.Use();
            }

            // Auto-close if the map is closed, but only while picking a destination - the config window is
            // opened from Interact and has nothing to do with the map.
            if (!_isConfigMode && Minimap.instance != null && Minimap.instance.m_mode != Minimap.MapMode.Large)
            {
                HideUI();
                return;
            }

            if (_isConfigMode) DrawConfigUI();
            else if (_previewExpanded && _previewTex != null && _selected != null) DrawExpandedPreview();
            else DrawSelectUI();

            // Deliberately not auto-focused anywhere: the destination window opens by itself when you walk
            // up to a portal, so grabbing the keyboard unasked would strand the player mid-stride.
            string focused = GUI.GetNameOfFocusedControl();
            _textInputActive = focused == SearchFieldName || focused == PinFieldName ||
                               focused == ConfigNameFieldName || focused == ConfigPinFieldName;
        }

        private void DrawSelectUI()
        {
            // Grows with the text scale, but never off the screen.
            float w = Mathf.Min(Screen.width - 120f, TortalUITheme.S(860f));
            float h = Mathf.Min(Screen.height - 80f, TortalUITheme.S(660f));
            // Pinned left so the large map, which we opened, stays readable on the right.
            float x = Mathf.Max(20f, Mathf.Min(52f, Screen.width - w - 20f));
            Rect win = new Rect(x, Mathf.Round((Screen.height - h) * 0.5f), w, h);

            TortalUITheme.DrawWindow(win, "Total Portal Control");

            Rect body = TortalUITheme.Body(win);
            const float gap = 20f;
            float leftW = Mathf.Round(body.width * 0.54f);
            Rect left = new Rect(body.x, body.y, leftW, body.height);
            Rect right = new Rect(left.xMax + gap, body.y, body.width - leftW - gap, body.height);

            // Search row, with the close button parked at the far right of it.
            float rowH = TortalUITheme.S(26f);
            float labelW = TortalUITheme.S(54f);
            Rect search = new Rect(left.x, left.y, left.width, rowH);
            GUI.Label(new Rect(search.x, search.y, labelW, search.height), "Search", TortalUITheme.Header);
            GUI.SetNextControlName(SearchFieldName);
            _searchFilter = GUI.TextField(new Rect(search.x + labelW + 4f, search.y, search.width - labelW - 4f, search.height),
                                          _searchFilter, 32, TortalUITheme.Field);

            float closeW = TortalUITheme.S(92f);
            if (GUI.Button(new Rect(right.xMax - closeW, search.y, closeW, rowH), "Close", TortalUITheme.Button))
            {
                HideUI();
                return;
            }

            Rect listRect = new Rect(left.x, search.yMax + 12f, left.width, left.height - search.height - 12f);
            TortalUITheme.DrawInset(listRect);
            DrawPortalList(listRect);

            Rect detail = new Rect(right.x, search.yMax + 12f, right.width, right.height - search.height - 12f);
            DrawDetails(detail);

            GUI.Label(TortalUITheme.FooterLine(win),
                      "Pick a destination to preview it  ·  the map marks the selection  ·  close the map to cancel",
                      TortalUITheme.Footer);
        }

        private void DrawPortalList(Rect area)
        {
            GUILayout.BeginArea(new Rect(area.x + 4f, area.y + 4f, area.width - 8f, area.height - 8f));
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            string filter = _searchFilter == null ? "" : _searchFilter.ToLowerInvariant();
            int shown = 0;

            foreach (PortalEntry e in _entries)
            {
                if (filter.Length > 0 && e.SearchKey.IndexOf(filter, StringComparison.Ordinal) < 0) continue;
                shown++;

                Rect row = GUILayoutUtility.GetRect(GUIContent.none, TortalUITheme.Row,
                                                    GUILayout.ExpandWidth(true), GUILayout.Height(TortalUITheme.S(44f)));

                // Painted under the row rather than through the style, so the selection survives hover.
                if (_selected == e) TortalUITheme.DrawSelection(row);

                if (GUI.Button(row, e.Label, TortalUITheme.Row)) Select(e);
            }

            if (shown == 0)
            {
                GUILayout.Space(24f);
                GUILayout.Label(_entries.Count > 0 ? "Nothing matches that search."
                                : PortalRegistry.GetEntries().Count > 0 ? "This is the only portal in the world."
                                : "Waiting for the server's portal list...",
                                TortalUITheme.Note);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void Select(PortalEntry e)
        {
            _selected = e;
            _showPinPrompt = e.Locked && !Configuration.IsAdmin();
            _pinInput = "";
            _previewExpanded = false;
            ClearPreview();

            StopDetailFetch();
            e.Details = DetailState.Pending;
            _detailFetch = StartCoroutine(ResolveDetails(e));

            if (Minimap.instance != null) Minimap.instance.ShowPointOnMap(e.Pos);
        }

        private void StopDetailFetch()
        {
            if (_detailFetch == null) return;
            StopCoroutine(_detailFetch);
            _detailFetch = null;
        }

        // The row only needs the index. The snapshot (tens of kilobytes) and the PIN live on the portal's own
        // ZDO, and a client will not be holding the ZDO of a gate on the far side of the map - so ask the
        // server for that one object and fill the panel in when it lands. On a host, or for a portal already
        // in range, it is there on the first look and this finishes in a frame.
        private IEnumerator ResolveDetails(PortalEntry e)
        {
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(e.Id) : null;

            if (zdo == null && ZDOMan.instance != null)
            {
                Configuration.LogDebug($"Requesting ZDO {e.Id} from the server for portal details.");
                ZDOMan.instance.RequestZDO(e.Id);

                float deadline = Time.realtimeSinceStartup + 6f;
                while (zdo == null && Time.realtimeSinceStartup < deadline)
                {
                    yield return new WaitForSeconds(0.1f);
                    zdo = ZDOMan.instance.GetZDO(e.Id);
                }
            }

            _detailFetch = null;
            if (_selected != e) yield break; // player moved on while we waited

            if (zdo == null)
            {
                e.Details = DetailState.Unavailable;
                Configuration.LogDebug($"ZDO {e.Id} never arrived; portal details unavailable.");
                yield break;
            }

            ApplyDetails(e, zdo);
        }

        // Extracted so the risky part can sit in a try/catch - C# forbids "yield return" inside a try block
        // with a catch clause.
        private void ApplyDetails(PortalEntry e, ZDO zdo)
        {
            try
            {
                e.Pin = zdo.GetString("PortalPIN");
                e.Locked = !string.IsNullOrEmpty(e.Pin);
                e.Details = DetailState.Resolved;
                _showPinPrompt = e.Locked && !Configuration.IsAdmin();
                LoadPreview(zdo);
            }
            catch (Exception ex)
            {
                e.Details = DetailState.Unavailable;
                Plugin.Log.LogError($"TortalPortal: reading portal details failed (non-fatal, travel still works). Reason: {ex}");
            }
        }

        private void DrawDetails(Rect area)
        {
            if (_selected == null)
            {
                GUI.Label(new Rect(area.x, area.y + area.height * 0.4f, area.width, TortalUITheme.S(40f)),
                          "Select a portal from the list.", TortalUITheme.Note);
                return;
            }

            PortalEntry e = _selected;
            float y = area.y;

            // Preview, 16:9, in its own inset frame.
            Rect preview = new Rect(area.x, y, area.width, Mathf.Round(area.width * 9f / 16f));
            TortalUITheme.DrawInset(preview);
            if (_previewTex != null)
            {
                GUI.DrawTexture(new Rect(preview.x + 1f, preview.y + 1f, preview.width - 2f, preview.height - 2f),
                                _previewTex, ScaleMode.ScaleAndCrop);
            }
            else
            {
                GUI.Label(preview,
                          e.Details == DetailState.Pending ? "Reading portal..." :
                          e.Details == DetailState.Unavailable ? "Could not reach that portal." :
                          "No snapshot captured yet.",
                          TortalUITheme.Note);
            }
            TortalUITheme.DrawOutline(preview, new Color(TortalUITheme.Gold.r, TortalUITheme.Gold.g, TortalUITheme.Gold.b, 0.8f), 1f);

            // Invisible until hovered, laid over the thumbnail: clicking it opens the shot full size.
            if (_previewTex != null && GUI.Button(preview, GUIContent.none, TortalUITheme.ImageButton))
            {
                _previewExpanded = true;
                return;
            }
            y = preview.yMax + 4f;

            if (_previewTex != null)
            {
                GUI.Label(new Rect(area.x, y, area.width, TortalUITheme.S(16f)),
                          "click the image to view it full size", TortalUITheme.Footer);
            }
            y += TortalUITheme.S(20f);

            GUI.Label(new Rect(area.x, y, area.width, TortalUITheme.S(24f)), e.Tag, TortalUITheme.SubTitle);
            y += TortalUITheme.S(28f);
            TortalUITheme.DrawRule(new Rect(area.x, y, area.width, 1f));
            y += TortalUITheme.S(14f);

            y = StatRow(area, y, "Owner", e.Creator);
            y = StatRow(area, y, "Biome", PrettyBiome(e.Biome));
            y = StatRow(area, y, "Distance", $"{Mathf.RoundToInt(e.Distance)} m");
            y = StatRow(area, y, "Position", $"{(int)e.Pos.x}, {(int)e.Pos.y}, {(int)e.Pos.z}");
            StatRow(area, y, "Status", e.Locked
                ? $"<color={TortalUITheme.HexLocked}>Locked</color>"
                : $"<color={TortalUITheme.HexOpen}>Open</color>");

            // The action block is pinned to the bottom of the column so it never moves between selections.
            float actionH = TortalUITheme.S(44f);
            Rect action = new Rect(area.x, area.yMax - actionH, area.width, actionH);

            if (_showPinPrompt)
            {
                float pinH = TortalUITheme.S(26f);
                float pinLabelW = TortalUITheme.S(40f);
                Rect pinRow = new Rect(area.x, action.y - pinH - 8f, area.width, pinH);
                GUI.Label(new Rect(pinRow.x, pinRow.y, pinLabelW, pinRow.height), "PIN", TortalUITheme.Header);
                GUI.SetNextControlName(PinFieldName);
                _pinInput = GUI.PasswordField(new Rect(pinRow.x + pinLabelW + 4f, pinRow.y, pinRow.width - pinLabelW - 4f, pinRow.height),
                                              _pinInput, '*', 10, TortalUITheme.Field);

                if (GUI.Button(action, "Unlock & Teleport", TortalUITheme.Primary))
                {
                    // The PIN rides on the portal's own ZDO, which for a distant gate has to come from the
                    // server first. Selecting the row asked for it; this only fires early.
                    if (e.Details != DetailState.Resolved)
                    {
                        Player.m_localPlayer.Message(MessageHud.MessageType.Center,
                            e.Details == DetailState.Pending ? "Still reading that portal..." : "That portal could not be reached.");
                    }
                    else if (_pinInput == e.Pin) ExecuteTeleport(e);
                    else Player.m_localPlayer.Message(MessageHud.MessageType.Center, "Incorrect PIN!");
                }
            }
            else if (GUI.Button(action, "Teleport", TortalUITheme.Primary))
            {
                ExecuteTeleport(e);
            }
        }

        private static float StatRow(Rect area, float y, string key, string value)
        {
            float keyW = TortalUITheme.S(84f);
            float lineH = TortalUITheme.S(20f);
            GUI.Label(new Rect(area.x, y, keyW, lineH), key, TortalUITheme.Key);
            GUI.Label(new Rect(area.x + keyW + 4f, y, area.width - keyW - 4f, lineH), value, TortalUITheme.Value);
            return y + lineH + 2f;
        }

        // The snapshot at full size, sized to the screen and framed like the rest of the mod.
        //
        // Nothing else is drawn while this is up, on purpose: IMGUI hit-tests every control whose rect contains
        // the cursor, so an overlay laid on top of the picker would let one click land on both. Suppressing the
        // picker entirely is the only reliable way to make a modal modal.
        private void DrawExpandedPreview()
        {
            TortalUITheme.DrawFill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.86f));

            float aspect = _previewTex.height > 0 ? _previewTex.width / (float)_previewTex.height : 16f / 9f;
            float chromeW = 2f * (TortalUITheme.Band + TortalUITheme.Pad);
            float chromeH = 2f * TortalUITheme.Band + TortalUITheme.TitleHeight + TortalUITheme.FooterHeight;

            float availW = Mathf.Max(240f, Screen.width - 48f - chromeW);
            float availH = Mathf.Max(160f, Screen.height - 48f - chromeH);

            float imgW = availW, imgH = availW / aspect;
            if (imgH > availH) { imgH = availH; imgW = availH * aspect; }

            Rect win = new Rect(
                Mathf.Round((Screen.width - imgW - chromeW) * 0.5f),
                Mathf.Round((Screen.height - imgH - chromeH) * 0.5f),
                Mathf.Round(imgW + chromeW),
                Mathf.Round(imgH + chromeH));

            TortalUITheme.DrawWindow(win, _selected.Tag);

            // Body() is exactly the image rect - the window was built around it.
            Rect body = TortalUITheme.Body(win);
            GUI.DrawTexture(body, _previewTex, ScaleMode.ScaleToFit);
            TortalUITheme.DrawOutline(body, new Color(TortalUITheme.Gold.r, TortalUITheme.Gold.g, TortalUITheme.Gold.b, 0.8f), 1f);

            GUI.Label(TortalUITheme.FooterLine(win),
                      $"{PrettyBiome(_selected.Biome)}   ·   {(int)_selected.Pos.x}, {(int)_selected.Pos.y}, {(int)_selected.Pos.z}" +
                      "   ·   click anywhere to close",
                      TortalUITheme.Footer);

            // Drawn last so it sits over the frame. GUIStyle.none keeps it genuinely invisible - the row style's
            // hover tint would wash the entire screen gold.
            if (GUI.Button(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none, GUIStyle.none))
            {
                _previewExpanded = false;
            }
        }

        private void DrawConfigUI()
        {
            float w = Mathf.Min(Screen.width - 80f, TortalUITheme.S(540f));
            float h = Mathf.Min(Screen.height - 80f, TortalUITheme.S(440f));
            Rect win = new Rect(Mathf.Round((Screen.width - w) * 0.5f), Mathf.Round((Screen.height - h) * 0.5f), w, h);

            TortalUITheme.DrawWindow(win, "Portal Configuration");

            Rect body = TortalUITheme.Body(win);
            float y = body.y;
            float lineH = TortalUITheme.S(20f);
            float fieldH = TortalUITheme.S(28f);
            float fieldW = TortalUITheme.S(200f);

            GUI.Label(new Rect(body.x, y, body.width, lineH), "Portal name", TortalUITheme.Header);
            y += lineH + 4f;
            GUI.SetNextControlName(ConfigNameFieldName);
            _configName = GUI.TextField(new Rect(body.x, y, body.width, fieldH), _configName, 20, TortalUITheme.Field);
            y += fieldH + 16f;

            if (GUI.Button(new Rect(body.x, y, fieldW, fieldH),
                           _configHasPin ? "PIN lock:  ON" : "PIN lock:  OFF", TortalUITheme.Button))
            {
                _configHasPin = !_configHasPin;
            }
            y += fieldH + 12f;

            if (_configHasPin)
            {
                GUI.Label(new Rect(body.x, y, body.width, lineH), "PIN", TortalUITheme.Header);
                y += lineH + 4f;
                GUI.SetNextControlName(ConfigPinFieldName);
                _configPin = GUI.TextField(new Rect(body.x, y, fieldW, fieldH), _configPin, 10, TortalUITheme.Field);
            }

            float btnH = TortalUITheme.S(40f);
            Rect save = new Rect(body.x, body.yMax - btnH, body.width * 0.5f - 8f, btnH);
            Rect cancel = new Rect(save.xMax + 16f, save.y, save.width, save.height);

            if (GUI.Button(save, "Save", TortalUITheme.Primary))
            {
                string finalPin = _configHasPin ? _configPin : "";
                ZDO zdo = CurrentSourcePortal.GetComponent<ZNetView>().GetZDO();
                zdo.Set(ZDOVars.s_tag, _configName);
                zdo.Set("PortalPIN", finalPin);
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "Portal configuration saved.");
                HideUI();
                return;
            }

            if (GUI.Button(cancel, "Cancel", TortalUITheme.Primary))
            {
                HideUI();
                return;
            }

            GUI.Label(TortalUITheme.FooterLine(win),
                      "A PIN is required from anyone but you and the server admins.", TortalUITheme.Footer);
        }

        // ---- preview ---------------------------------------------------------------------------------------

        // ImageConversion.LoadImage also has a ReadOnlySpan<byte> overload typed against netstandard 2.1,
        // which net48's reference assemblies can't resolve at compile time (CS0518) even though we only
        // ever want the byte[] overload. Binding it reflectively sidesteps compile-time overload
        // resolution entirely; Valheim's actual Mono runtime resolves it fine at runtime.
        private static MethodInfo _loadImageMethod;

        private static void LoadImageViaReflection(Texture2D tex, byte[] data)
        {
            if (_loadImageMethod == null)
            {
                _loadImageMethod = typeof(ImageConversion).GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
            }
            _loadImageMethod.Invoke(null, new object[] { tex, data });
        }

        private void ClearPreview()
        {
            if (_previewTex == null) return;
            Destroy(_previewTex);
            _previewTex = null;
        }

        private void LoadPreview(ZDO zdo)
        {
            ClearPreview();

            byte[] imgData = zdo.GetByteArray("PortalSnapshot");
            if (imgData != null && imgData.Length > 0)
            {
                _previewTex = new Texture2D(2, 2);
                LoadImageViaReflection(_previewTex, imgData);
            }
        }

        // ---- teleport --------------------------------------------------------------------------------------

        // Position and rotation both come off the index, so a destination can be travelled to without the
        // client ever holding its ZDO.
        private void ExecuteTeleport(PortalEntry target)
        {
            Configuration.LogDebug($"ExecuteTeleport called for ZDO {target.Id}");
            if (CurrentSourcePortal == null)
            {
                Configuration.LogDebug("CurrentSourcePortal is null! Cannot teleport.");
                return;
            }

            Quaternion rotation = target.Rot;
            Vector3 pos = target.Pos + rotation * Vector3.forward * CurrentSourcePortal.m_exitDistance + Vector3.up;

            Configuration.LogDebug($"Teleporting player to {pos}.");

            // Tell the behaviour patch to sit on the destination's auto-open before we move. The player lands
            // well inside that portal's activation range, so without this it would greet them with the menu
            // again the instant the trip finished - which looked like the menu had never closed.
            PortalBehaviorPatch.SuppressAutoOpenFor(target.Id);
            HideUI();

            Player.m_localPlayer.TeleportTo(pos, rotation, true);
            Game.instance.IncrementPlayerStat(PlayerStatType.PortalsUsed);

            if (Configuration.allowTames.Value)
            {
                Configuration.LogDebug("Tame teleporting is enabled but not yet implemented.");
            }
        }
    }
}
