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

        // The slender favorites-only strip. Opens that way when the player has favorites in this world (and
        // has not turned OpenFavoritesView off); the arrow on the strip expands to the full window and back.
        private bool _slim;

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
            public bool Fav;
            public DetailState Details;
            public Minimap.PinData MapPin;
        }

        private enum DetailState { Pending, Resolved, Unavailable }

        private readonly List<PortalEntry> _entries = new List<PortalEntry>();
        private readonly List<Minimap.PinData> _pins = new List<Minimap.PinData>();
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

        // The quick PIN modal: the small "PIN, Enter, gone" window that locked portals get when launched
        // from the favorites strip or a map marker, instead of hauling the full window out. While it is up
        // the map is closed and the keyboard is ours outright, like the configuration window.
        private PortalEntry _pinTarget;
        private string _pinModalInput = "";
        private bool _focusPinModalPending;
        private bool _pinReturnSlim;

        // IMGUI control names, so we can tell whether the player is typing into one of our fields rather
        // than steering their character. See InputFocusPatch.
        private const string SearchFieldName = "TortalPortal.Search";
        private const string PinFieldName = "TortalPortal.Pin";
        private const string ConfigNameFieldName = "TortalPortal.ConfigName";
        private const string ConfigPinFieldName = "TortalPortal.ConfigPin";
        private const string PinModalFieldName = "TortalPortal.PinModal";

        private static bool _fieldFocused;

        // Set by HideUI, acted on in OnGUI: dropping IMGUI focus has to happen inside OnGUI, and HideUI is
        // called from the portal update tick.
        private bool _releaseFocus;

        // Focus the search box as the window opens, so you can start typing a portal's name the moment it
        // appears instead of hunting for the field with the mouse.
        private bool _focusSearchPending;

        // "The mod has the keyboard." True while one of our fields is focused, and unconditionally while the
        // configuration window is up - that one is a modal opened deliberately with E, so nothing else should
        // be listening. The destination window is deliberately NOT included: it opens over the large map and
        // the player still needs the map's own pan and zoom keys while choosing where to go.
        public static bool IsTextInputActive()
        {
            if (_fieldFocused) return true;
            return _instance != null && _instance._isVisible &&
                   (_instance._isConfigMode || _instance._pinTarget != null);
        }

        // "The mod needs the mouse." Either window, both modes - see InputFocusPatch.
        public static bool WantsCursor() => _instance != null && _instance._isVisible;

        // "The cursor is on the mod, not the map." The large map's own mouse handling stands down inside
        // this - see MapInputPatch. ZInput.mousePosition is bottom-left origin and GUI rects are top-left,
        // hence the flip.
        public static bool IsPointerOverWindow()
        {
            if (_instance == null || !_instance._isVisible) return false;

            // The configuration window, the PIN modal and the lightbox are all modal - centred over a closed
            // map or covering the whole screen. Nothing behind any of them should hear the mouse.
            if (_instance._isConfigMode) return true;
            if (_instance._pinTarget != null) return true;
            if (_instance._previewExpanded) return true;

            Vector3 mouse = ZInput.mousePosition;
            Vector2 gui = new Vector2(mouse.x, Screen.height - mouse.y);
            return SelectWindowRect().Contains(gui);
        }

        // The destination picker specifically - the mode where map clicks can mean "go there".
        public static bool IsSelectUIVisible()
        {
            return _instance != null && _instance._isVisible && !_instance._isConfigMode &&
                   _instance._pinTarget == null && !_instance._previewExpanded;
        }

        // ---- click-n-go (called from MapInputPatch) ----------------------------------------------------------

        public static bool HasEntryNear(Vector3 world, float radius)
        {
            return _instance != null && _instance.FindEntryNear(world, radius) != null;
        }

        // A map click near one of our markers: first click selects the portal (same as clicking its row),
        // a click on the already-selected marker travels. Returns true when the click was ours, so vanilla
        // does not also toggle a checkmark onto the pin.
        public static bool HandleMapClick(Vector3 world, float radius)
        {
            if (!IsSelectUIVisible()) return false;

            PortalEntry e = _instance.FindEntryNear(world, radius);
            if (e == null) return false;

            // Vanilla turns two quick clicks into OnMapLeftClick, OnMapLeftClick, OnMapDblClick - so the
            // second click of a click-n-go arrives here and travels, and then a double-click follows it in
            // asking to open the pin-name box. This is the window in which that gets refused.
            _suppressDblClickUntil = Time.unscaledTime + 0.6f;

            if (_instance._selected == e) _instance.GoOrPrompt(e);
            else
            {
                // Selecting from the map while in the slim strip expands to the full window - the strip has
                // no details panel, so a selection there would land nowhere visible.
                _instance._slim = false;
                _instance.Select(e, recentreMap: false);
            }
            return true;
        }

        private static float _suppressDblClickUntil;

        // True for a moment after a marker click was acted on. The double-click that trails a click-n-go must
        // not reach vanilla: by then the window has closed and IsSelectUIVisible is false, so the ordinary
        // guard has already stopped applying and the pin-name box would open over the arrival.
        public static bool SuppressMapDblClick() => Time.unscaledTime < _suppressDblClickUntil;

        // Nearest portal marker to a map click, same distance rule as vanilla uses for its own pins.
        private PortalEntry FindEntryNear(Vector3 world, float radius)
        {
            if (!Configuration.showPortalPinsOnMap.Value) return null; // no markers, nothing to click

            PortalEntry best = null;
            float bestDist = radius;
            foreach (PortalEntry e in _entries)
            {
                float dx = e.Pos.x - world.x;
                float dz = e.Pos.z - world.z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d <= bestDist)
                {
                    bestDist = d;
                    best = e;
                }
            }
            return best;
        }

        // The one gate every "just go" action funnels through. A locked portal cannot be quick-launched -
        // but it should not haul the whole window out either. It gets the small PIN modal: map closes,
        // keyboard locks straight to the field, Enter fires it in.
        private void GoOrPrompt(PortalEntry e)
        {
            if (e.Locked && !Configuration.IsAdmin())
            {
                OpenPinModal(e);
                return;
            }

            ExecuteTeleport(e);
        }

        // ---- quick PIN modal ---------------------------------------------------------------------------

        private void OpenPinModal(PortalEntry e)
        {
            _pinTarget = e;
            _pinModalInput = "";
            _focusPinModalPending = true;
            _pinReturnSlim = _slim;

            // The PIN lives on the portal's own ZDO - start it travelling now so it is usually here before
            // the player finishes typing.
            _selected = e;
            StopDetailFetch();
            e.Details = DetailState.Pending;
            _detailFetch = StartCoroutine(ResolveDetails(e));

            // A modal over a live map reads as part of the map; over nothing it reads as a question.
            if (Minimap.instance != null) Minimap.instance.SetMapMode(Minimap.MapMode.None);
        }

        // Cancel path: back to exactly where the player was - map up, same window shape.
        private void ClosePinModal()
        {
            _pinTarget = null;
            _pinModalInput = "";
            _releaseFocus = true;
            _slim = _pinReturnSlim;
            if (Minimap.instance != null) Minimap.instance.SetMapMode(Minimap.MapMode.Large);
        }

        private void SubmitPinModal()
        {
            PortalEntry e = _pinTarget;
            if (e == null) return;

            if (e.Details == DetailState.Pending)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "Still reading that portal...");
                return;
            }
            if (e.Details == DetailState.Unavailable)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "That portal could not be reached.");
                return;
            }

            if (_pinModalInput == e.Pin)
            {
                _pinTarget = null;
                _pinModalInput = "";
                ExecuteTeleport(e);
            }
            else
            {
                // Stay up for another try - the wrong guess is announced, the field is cleared, and the
                // focus never left it.
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "Wrong PIN!");
                _pinModalInput = "";
            }
        }

        private void DrawPinModal()
        {
            // Enter submits from anywhere in the modal. Read BEFORE the field is drawn, because a focused
            // IMGUI field swallows the KeyDown once it has seen it.
            Event ev = Event.current;
            if (ev != null && ev.type == EventType.KeyDown &&
                (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter))
            {
                ev.Use();
                SubmitPinModal();
                if (_pinTarget == null) return; // correct PIN: the window is gone, stop drawing it
            }

            float w = Mathf.Min(Screen.width - 80f, TortalUITheme.S(440f));
            float h = Mathf.Min(Screen.height - 80f, TortalUITheme.S(300f));
            Rect win = new Rect(Mathf.Round((Screen.width - w) * 0.5f), Mathf.Round((Screen.height - h) * 0.5f), w, h);

            TortalUITheme.DrawWindow(win, "PIN Required");

            Rect body = TortalUITheme.Body(win);
            float y = body.y;
            float lineH = TortalUITheme.S(24f);
            float fieldH = TortalUITheme.S(30f);

            GUI.Label(new Rect(body.x, y, body.width, lineH), _pinTarget.Tag, TortalUITheme.SubTitle);
            y += lineH + 10f;

            GUI.SetNextControlName(PinModalFieldName);
            _pinModalInput = GUI.PasswordField(new Rect(body.x, y, body.width, fieldH), _pinModalInput, '*', 10, TortalUITheme.Field);

            if (_focusPinModalPending)
            {
                _focusPinModalPending = false;
                GUI.FocusControl(PinModalFieldName);
            }

            float btnH = TortalUITheme.S(40f);
            Rect unlock = new Rect(body.x, body.yMax - btnH, body.width * 0.5f - 8f, btnH);
            Rect cancel = new Rect(unlock.xMax + 16f, unlock.y, unlock.width, unlock.height);

            if (GUI.Button(unlock, "Unlock", TortalUITheme.Primary))
            {
                SubmitPinModal();
                if (_pinTarget == null) return;
            }
            if (GUI.Button(cancel, "Cancel", TortalUITheme.Primary))
            {
                ClosePinModal();
                return;
            }

            GUI.Label(TortalUITheme.FooterLine(win),
                      _pinTarget.Details == DetailState.Pending ? "reading the portal..." : "Enter unlocks  ·  Esc cancels",
                      TortalUITheme.Footer);
        }

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
            _instance._searchFilter = "";
            _instance.BuildEntries();
            Configuration.LogDebug($"Found {_instance._entries.Count} destination portals.");

            // Favorites strip when there is anything in it to show, full window otherwise. The strip has no
            // search box, so the auto-focus only applies to the full window - and only when it is asked for.
            //
            // It used to be unconditional, which meant the window took the keyboard the instant it appeared.
            // Since the window opens as you walk up to a portal, the rest of the run-up went into the filter
            // as "wwwww" instead of into moving the player. Off by default now; see FocusSearchOnOpen.
            _instance._slim = Configuration.openFavoritesView.Value && _instance._entries.Exists(e => e.Fav);
            _instance._focusSearchPending = !_instance._slim &&
                                            Configuration.focusSearchOnOpen != null &&
                                            Configuration.focusSearchOnOpen.Value;

            if (Minimap.instance != null)
            {
                Configuration.LogDebug("Opening Large Minimap.");
                Minimap.instance.SetMapMode(Minimap.MapMode.Large);
            }

            _instance.AddMapPins();
        }

        public static void ShowConfigUI(TeleportWorld targetPortal)
        {
            Configuration.LogDebug("PortalUIManager.ShowConfigUI called.");
            if (_instance == null) Init();

            ZNetView nview = targetPortal != null ? targetPortal.GetComponent<ZNetView>() : null;
            if (nview == null || !nview.IsValid())
            {
                Configuration.LogDebug("ShowConfigUI called for a portal with no valid ZNetView. Ignoring.");
                return;
            }

            // Reachable from the destination window's "Name & PIN" button as well as from Interact, so the
            // select-mode state has to be torn down here rather than assumed absent. The map pins matter
            // most: they are added to the shared Minimap pin list, so leaving them behind would strand them
            // on the small minimap for as long as the configuration window is up.
            _instance.StopDetailFetch();
            _instance.ClearMapPins();
            _instance.ClearPreview();
            _instance._selected = null;
            _instance._previewExpanded = false;
            _instance._showPinPrompt = false;
            _instance._pinInput = "";
            _instance._focusSearchPending = false;
            _instance._pinTarget = null;
            _instance._pinModalInput = "";

            CurrentSourcePortal = targetPortal;
            _instance._isConfigMode = true;
            _instance._isVisible = true;

            ZDO zdo = nview.GetZDO();
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
                _instance._focusSearchPending = false;
                _instance._pinTarget = null;
                _instance._pinModalInput = "";
                _instance._focusPinModalPending = false;
                _instance.StopDetailFetch();
                _instance.ClearMapPins();
                _instance.ClearPreview();

                // Hand the keyboard straight back rather than waiting for the next OnGUI, so the player is
                // never left standing still for a frame after the window closes.
                _fieldFocused = false;
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
                    Locked = src.Locked,
                    Fav = Favorites.IsFavorite(src.Id)
                };

                e.SearchKey = tag.ToLowerInvariant();
                e.Label = $"<b>{tag}</b>{(e.Locked ? $"   <color={TortalUITheme.HexLocked}>[LOCKED]</color>" : "")}" +
                          $"\n<size=11><color={TortalUITheme.HexMuted}>{PrettyBiome(e.Biome)}   ·   " +
                          $"{Mathf.RoundToInt(e.Distance)}m   ·   {creator}</color></size>";

                _entries.Add(e);
            }

            SortEntries();
        }

        // Favorites first, then alphabetical - one order shared by the full list and the strip, so a portal
        // never appears to move when the window changes shape.
        private void SortEntries()
        {
            _entries.Sort((a, b) => a.Fav != b.Fav
                ? (a.Fav ? -1 : 1)
                : string.Compare(a.Tag, b.Tag, StringComparison.OrdinalIgnoreCase));
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
                _fieldFocused = false;
                GUI.FocusControl(null);
                GUIUtility.keyboardControl = 0;
            }

            if (!_isVisible) return;
            TortalUITheme.EnsureBuilt();

            // Escape: the PIN modal cancels outright, otherwise it backs out of a field without closing the
            // window, so there is always a way to hand the keyboard back that is not "click somewhere
            // harmless" - and the lightbox closes, since a click-anywhere overlay should also be Esc-able.
            if (Event.current != null && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                if (_pinTarget != null)
                {
                    Event.current.Use();
                    ClosePinModal();
                    return;
                }
                if (_previewExpanded)
                {
                    Event.current.Use();
                    _previewExpanded = false;
                }
                else if (_fieldFocused)
                {
                    _releaseFocus = true;
                    Event.current.Use();
                }
            }

            // Auto-close if the map is closed, but only while picking a destination - the config window and
            // the PIN modal deliberately close the map themselves and must survive doing so.
            if (!_isConfigMode && _pinTarget == null &&
                Minimap.instance != null && Minimap.instance.m_mode != Minimap.MapMode.Large)
            {
                HideUI();
                return;
            }

            if (_pinTarget != null) DrawPinModal();
            else if (_isConfigMode) DrawConfigUI();
            else if (_previewExpanded && _selected != null) DrawExpandedPreview();
            else if (_slim) DrawSlimUI();
            else DrawSelectUI();

            // Whichever of our fields currently holds the keyboard, if any. While one does, this mod holds
            // the keyboard outright and Valheim's own Use key is suppressed with it - so anything that used
            // to be reached by pressing E at an open window needs its own on-screen control instead; see the
            // "Name & PIN" button in DrawSelectUI. With FocusSearchOnOpen off (the default) nothing is
            // focused until the player clicks a field, so walking about with the window up steers the player.
            string focused = GUI.GetNameOfFocusedControl();
            _fieldFocused = focused == SearchFieldName || focused == PinFieldName ||
                            focused == ConfigNameFieldName || focused == ConfigPinFieldName ||
                            focused == PinModalFieldName;
        }

        // Shared so the map-centring maths and the map-input blocking know exactly how much of the screen
        // the window is covering, rather than guessing at it. Three shapes: the slim favorites strip, the
        // full window, and the full window without its picture column when previews are off.
        private static Rect SelectWindowRect()
        {
            bool slim = _instance != null && _instance._slim;
            float wDesign = slim ? 380f : (Configuration.showPreviews != null && !Configuration.showPreviews.Value ? 640f : 860f);
            float hDesign = slim ? 560f : 660f;

            // Grows with the text scale, but never off the screen.
            float w = Mathf.Min(Screen.width - 120f, TortalUITheme.S(wDesign));
            float h = Mathf.Min(Screen.height - 80f, TortalUITheme.S(hDesign));
            // Pinned left so the large map, which we opened, stays readable on the right.
            float x = Mathf.Max(20f, Mathf.Min(52f, Screen.width - w - 20f));
            return new Rect(x, Mathf.Round((Screen.height - h) * 0.5f), w, h);
        }

        private void DrawSelectUI()
        {
            Rect win = SelectWindowRect();

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

            // Has to happen after the control exists, so it cannot be done in ShowUI.
            if (_focusSearchPending)
            {
                _focusSearchPending = false;
                GUI.FocusControl(SearchFieldName);
            }

            float closeW = TortalUITheme.S(92f);
            Rect closeRect = new Rect(right.xMax - closeW, search.y, closeW, rowH);
            float buttonEdge = closeRect.x;

            // The way in to this portal's own name and PIN, and the only one that works whatever the keyboard
            // is doing. Pressing E is unreliable by nature here: the moment any of our fields holds focus,
            // InputFocusPatch reports that to Chat.HasFocus, Valheim gates its Use key behind
            // Player.TakeInput(), and the keypress never reaches TeleportWorld.Interact at all. That is what
            // made the configuration window look like it needed several tries to open - whether it worked
            // came down to whether a field happened to be focused at that moment. This button never cares.
            if (SecurityPatch.CanConfigure(CurrentSourcePortal))
            {
                float editW = TortalUITheme.S(128f);
                buttonEdge = closeRect.x - editW - 8f;
                if (GUI.Button(new Rect(buttonEdge, search.y, editW, rowH), "Name & PIN", TortalUITheme.Button))
                {
                    ShowConfigUI(CurrentSourcePortal);
                    return;
                }
            }

            // Back down to the favorites strip. Shown whenever there is a favorite to land on - collapsing
            // into an empty strip would just be a smaller window with nothing in it.
            if (_entries.Exists(e => e.Fav))
            {
                float favW = TortalUITheme.S(34f);
                if (GUI.Button(new Rect(buttonEdge - favW - 8f, search.y, favW, rowH), "«", TortalUITheme.Button))
                {
                    _slim = true;
                    _releaseFocus = true;
                    return;
                }
            }

            if (GUI.Button(closeRect, "Close", TortalUITheme.Button))
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
                      "Type to filter  ·  click a map marker to select, click it again to travel  ·  the heart stars a favorite  ·  Esc frees the keyboard",
                      TortalUITheme.Footer);
        }

        // The quick launcher: a slender strip of nothing but favorites, one click to travel. Everything else
        // - search, details, previews, PINs - lives behind the expand arrow in the full window.
        private void DrawSlimUI()
        {
            Rect win = SelectWindowRect();
            TortalUITheme.DrawWindow(win, "Favorites");

            Rect body = TortalUITheme.Body(win);
            float rowH = TortalUITheme.S(26f);

            // Expand arrow and close, mirroring the full window's top-right controls.
            float closeW = TortalUITheme.S(78f);
            float arrowW = TortalUITheme.S(34f);
            if (GUI.Button(new Rect(body.xMax - closeW, body.y, closeW, rowH), "Close", TortalUITheme.Button))
            {
                HideUI();
                return;
            }
            if (GUI.Button(new Rect(body.xMax - closeW - arrowW - 8f, body.y, arrowW, rowH), "»", TortalUITheme.Button))
            {
                _slim = false;
                _focusSearchPending = true;
                return;
            }
            GUI.Label(new Rect(body.x, body.y, body.width - closeW - arrowW - 16f, rowH), "Click to travel", TortalUITheme.Header);

            Rect listRect = new Rect(body.x, body.y + rowH + 10f, body.width, body.height - rowH - 10f);
            TortalUITheme.DrawInset(listRect);

            GUILayout.BeginArea(new Rect(listRect.x + 4f, listRect.y + 4f, listRect.width - 8f, listRect.height - 8f));
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            int shown = 0;
            foreach (PortalEntry e in _entries)
            {
                if (!e.Fav) continue;
                shown++;

                Rect row = GUILayoutUtility.GetRect(GUIContent.none, TortalUITheme.Row,
                                                    GUILayout.ExpandWidth(true), GUILayout.Height(TortalUITheme.S(44f)));
                if (GUI.Button(row, e.Label, TortalUITheme.Row))
                {
                    GoOrPrompt(e);
                    // GoOrPrompt can tear the window down (teleport) or reshape it (PIN prompt) - either
                    // way this draw pass is done.
                    GUILayout.EndScrollView();
                    GUILayout.EndArea();
                    return;
                }
            }

            if (shown == 0)
            {
                GUILayout.Space(24f);
                GUILayout.Label("No favorites in this world yet.\nExpand with » and mark some portals with the heart.", TortalUITheme.Note);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();

            GUI.Label(TortalUITheme.FooterLine(win), "one click travels  ·  » opens the full list", TortalUITheme.Footer);
        }

        private void DrawPortalList(Rect area)
        {
            GUILayout.BeginArea(new Rect(area.x + 4f, area.y + 4f, area.width - 8f, area.height - 8f));
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            string filter = _searchFilter == null ? "" : _searchFilter.ToLowerInvariant();
            int shown = 0;

            bool resort = false;
            foreach (PortalEntry e in _entries)
            {
                if (filter.Length > 0 && e.SearchKey.IndexOf(filter, StringComparison.Ordinal) < 0) continue;
                shown++;

                Rect row = GUILayoutUtility.GetRect(GUIContent.none, TortalUITheme.Row,
                                                    GUILayout.ExpandWidth(true), GUILayout.Height(TortalUITheme.S(44f)));

                // Painted under the row rather than through the style, so the selection survives hover.
                if (_selected == e) TortalUITheme.DrawSelection(row);

                // The favorite mark gets its own strip of the row. IMGUI fires EVERY control under a click,
                // so the star cannot be laid over the row button - the two rects must not overlap.
                float starW = TortalUITheme.S(30f);
                Rect rowRect = new Rect(row.x, row.y, row.width - starW, row.height);
                Rect starRect = new Rect(row.xMax - starW, row.y, starW, row.height);

                if (GUI.Button(rowRect, e.Label, TortalUITheme.Row)) Select(e);

                float d = TortalUITheme.S(14f);
                Rect mark = new Rect(starRect.center.x - d * 0.5f, starRect.center.y - d * 0.5f, d, d);
                if (GUI.Button(starRect, GUIContent.none, GUIStyle.none))
                {
                    // Click handling runs in its own event pass, never between Layout and Repaint, so the
                    // resort cannot desync IMGUI's control count.
                    Favorites.Toggle(e.Id);
                    e.Fav = !e.Fav;
                    resort = true;
                }
                TortalUITheme.DrawHeartMark(mark, e.Fav);
            }
            if (resort) SortEntries();

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

        // recentreMap: false when the selection came from a click on the map itself.
        //
        // Recentring is right for a click in the LIST - the map swings round to show you where that portal
        // is. It is wrong for a click on the map, because it drags the marker out from under the cursor: the
        // first click selected and recentred, so the second click of click-n-go landed on empty map somewhere
        // else entirely and never reached the portal. That is what broke travelling by marker.
        private void Select(PortalEntry e, bool recentreMap = true)
        {
            _selected = e;
            _showPinPrompt = e.Locked && !Configuration.IsAdmin();
            _pinInput = "";
            _previewExpanded = false;
            ClearPreview();

            StopDetailFetch();
            e.Details = DetailState.Pending;
            _detailFetch = StartCoroutine(ResolveDetails(e));

            HighlightPin(e);
            if (recentreMap) ShowPointClearOfWindow(e.Pos);
        }

        // ---- map ---------------------------------------------------------------------------------------

        // Centring the map on the chosen portal put it squarely behind this window, which is pinned to the
        // left half of the screen. Shifting the centre west by half the window's width lands the selection
        // in the middle of the strip of map the player can actually see.
        //
        // Computed from the map's own zoom and image aspect rather than a fudge factor, so it stays correct
        // at any resolution, UI scale or zoom level.
        private static void ShowPointClearOfWindow(Vector3 point)
        {
            Minimap map = Minimap.instance;
            if (map == null) return;

            float shift = 0f;
            try
            {
                RectTransform image = map.m_mapImageLarge != null ? map.m_mapImageLarge.transform as RectTransform : null;
                if (image != null && Screen.width > 0 && image.rect.height > 0f)
                {
                    // How much world the large map spans from screen edge to screen edge.
                    float aspect = image.rect.width / image.rect.height;
                    float worldWidth = map.m_largeZoom * aspect * map.m_textureSize * map.m_pixelSize;

                    shift = (FreeCentreFraction() - 0.5f) * worldWidth;
                }
            }
            catch (Exception ex)
            {
                // Falling through with shift 0 just centres it the old way - worth having the map at all.
                shift = 0f;
                Plugin.Log.LogError($"TortalPortal: could not offset the map for the window (non-fatal). Reason: {ex}");
            }

            map.ShowPointOnMap(new Vector3(point.x - shift, point.y, point.z));
        }

        // Middle of whatever the destination window is NOT covering, as a 0-1 fraction of the screen width.
        // The map centres on it and the centre message moves to it, so "the part you can actually see" is one
        // number in one place rather than two that can drift apart.
        private static float FreeCentreFraction()
        {
            if (Screen.width <= 0) return 0.5f;
            return Mathf.Clamp01((SelectWindowRect().xMax / Screen.width + 1f) * 0.5f);
        }

        // How far, in screen pixels, Valheim's centre message has to move to clear the destination window.
        // Zero when there is nothing to clear, which is the signal to put it back where vanilla had it.
        //
        // Deliberately only the destination window. The configuration window is centred and narrow, and the
        // one message it raises fires as it closes, so there is nothing to dodge.
        public static float CentreMessageOffset()
        {
            if (_instance == null || !_instance._isVisible || _instance._isConfigMode) return 0f;
            if (Screen.width <= 0) return 0f;
            return (FreeCentreFraction() - 0.5f) * Screen.width;
        }

        // Vanilla marks nothing on the map for a portal, so a list of names was the only clue where any of
        // them actually were. These are transient - never saved, and removed the moment the window closes -
        // so they cannot silt up somebody's pin list.
        private void AddMapPins()
        {
            ClearMapPins();
            if (!Configuration.showPortalPinsOnMap.Value || Minimap.instance == null) return;

            try
            {
                PortalPinIcon chosen = Configuration.portalPinIcon.Value;
                bool vanillaIcon = (int)chosen < 100;

                // Our own pins ride on a vanilla pin type and then have their sprite swapped: AddPin stamps
                // m_icon from the type, and the map's UI element reads pin.m_icon when it is built - so a
                // swap made here, before the next pin update, is all it takes.
                Minimap.PinType icon = vanillaIcon ? (Minimap.PinType)(int)chosen : Minimap.PinType.Icon4;
                Sprite customSprite = vanillaIcon ? null : TortalPinIcon.Get(chosen);

                foreach (PortalEntry e in _entries)
                {
                    e.MapPin = Minimap.instance.AddPin(e.Pos, icon, e.Tag, false, false);
                    if (e.MapPin != null)
                    {
                        if (customSprite != null) e.MapPin.m_icon = customSprite;
                        _pins.Add(e.MapPin);
                    }
                }

                Configuration.LogDebug($"Marked {_pins.Count} portals on the map.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not mark portals on the map (non-fatal). Reason: {ex}");
            }
        }

        private void ClearMapPins()
        {
            if (_pins.Count == 0) return;

            try
            {
                if (Minimap.instance != null)
                {
                    foreach (Minimap.PinData pin in _pins) Minimap.instance.RemovePin(pin);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not clear the portal map markers (non-fatal). Reason: {ex}");
            }

            _pins.Clear();
            foreach (PortalEntry e in _entries) e.MapPin = null;
        }

        // How the chosen destination is picked out from everything else on the map: 30% larger and red,
        // while every other portal marker stays exactly the size and colour of a normal pin.
        private const float SelectedPinScale = 1.3f;
        private static readonly Color SelectedPinColour = new Color(1f, 0.24f, 0.20f, 1f);

        // Nothing is applied here - all this does is ask the map to redraw its pins, which is what gets
        // StyleMapPins called. Selection can change while the map is sitting perfectly still, and Minimap
        // only runs UpdatePins when this flag is set, so without it the highlight would not appear until the
        // player happened to drag or zoom.
        private void HighlightPin(PortalEntry selected)
        {
            if (Minimap.instance != null) Minimap.instance.m_pinUpdateRequired = true;
        }

        // Called from a postfix on Minimap.UpdatePins, which is the only place this can work from.
        //
        // Size used to be vanilla's m_doubleSize, and that is what made some markers look bigger than
        // others: it is a boolean, so the only sizes on offer were "normal" and "twice as big", and the
        // selected one took the second. localScale is the lever instead - Minimap reads and writes
        // sizeDelta but never touches localScale, so nothing fights us for it and any factor is available.
        //
        // Colour has to be re-applied here rather than set once, because UpdatePins assigns
        // m_iconElement.color unconditionally on every run. Running after it is what makes the red stick;
        // it also means unselected markers need no reset, since vanilla has just painted them itself.
        public static void StyleMapPins()
        {
            if (_instance == null || _instance._pins.Count == 0) return;

            Minimap.PinData selected = _instance._selected != null ? _instance._selected.MapPin : null;

            foreach (Minimap.PinData pin in _instance._pins)
            {
                if (pin == null) continue;
                bool isSelected = pin == selected;

                if (pin.m_uiElement != null)
                {
                    // Markers are destroyed and rebuilt as they scroll on and off screen, so a fresh element
                    // arrives at scale 1 - which is why every pin is set every pass and not just the
                    // selected one.
                    Vector3 wanted = isSelected
                        ? new Vector3(SelectedPinScale, SelectedPinScale, 1f)
                        : Vector3.one;
                    if (pin.m_uiElement.localScale != wanted) pin.m_uiElement.localScale = wanted;
                }

                if (!isSelected) continue;

                // Alpha is left as vanilla set it - it carries the shared-map fade, and overwriting it would
                // make the selected marker punch through a map region the player has not actually explored.
                if (pin.m_iconElement != null)
                {
                    Color c = pin.m_iconElement.color;
                    pin.m_iconElement.color = new Color(SelectedPinColour.r, SelectedPinColour.g, SelectedPinColour.b, c.a);
                }
                if (pin.m_NamePinData != null && pin.m_NamePinData.PinNameText != null)
                {
                    Color c = pin.m_NamePinData.PinNameText.color;
                    pin.m_NamePinData.PinNameText.color = new Color(SelectedPinColour.r, SelectedPinColour.g, SelectedPinColour.b, c.a);
                }
            }
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

            // The preview block only exists when the player wants pictures - with ShowDestinationPreviews
            // off the details start at the top and the whole window is narrower (see SelectWindowRect).
            if (Configuration.showPreviews.Value)
            {
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
            }

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
            // Arrow keys page through the album, matching the on-screen arrows.
            Event ev = Event.current;
            if (ev != null && ev.type == EventType.KeyDown)
            {
                if (ev.keyCode == KeyCode.LeftArrow) { ev.Use(); CyclePreview(-1); return; }
                if (ev.keyCode == KeyCode.RightArrow) { ev.Use(); CyclePreview(1); return; }
            }

            TortalUITheme.DrawFill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.86f));

            // A snapshot still on its way from the server (mid-cycle) gets a 16:9 frame to arrive into.
            float aspect = _previewTex != null && _previewTex.height > 0
                ? _previewTex.width / (float)_previewTex.height
                : 16f / 9f;
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
            if (_previewTex != null)
            {
                GUI.DrawTexture(body, _previewTex, ScaleMode.ScaleToFit);
            }
            else
            {
                GUI.Label(body,
                          _selected.Details == DetailState.Pending ? "Reading portal..." :
                          _selected.Details == DetailState.Unavailable ? "Could not reach that portal." :
                          "No snapshot captured yet.",
                          TortalUITheme.Note);
            }
            TortalUITheme.DrawOutline(body, new Color(TortalUITheme.Gold.r, TortalUITheme.Gold.g, TortalUITheme.Gold.b, 0.8f), 1f);

            GUI.Label(TortalUITheme.FooterLine(win),
                      $"{PrettyBiome(_selected.Biome)}   ·   {(int)_selected.Pos.x}, {(int)_selected.Pos.y}, {(int)_selected.Pos.z}" +
                      "   ·   arrows flip through portals   ·   click anywhere to close",
                      TortalUITheme.Footer);

            // The page-turn arrows, parked in the side gutters outside the frame.
            float arrowW = TortalUITheme.S(52f);
            float arrowH = TortalUITheme.S(96f);
            Rect prev = new Rect(Mathf.Max(8f, win.x - arrowW - 12f), Mathf.Round((Screen.height - arrowH) * 0.5f), arrowW, arrowH);
            Rect next = new Rect(Mathf.Min(Screen.width - arrowW - 8f, win.xMax + 12f), prev.y, arrowW, arrowH);

            bool cycled = false;
            if (GUI.Button(prev, "<", TortalUITheme.Primary)) { CyclePreview(-1); cycled = true; }
            if (GUI.Button(next, ">", TortalUITheme.Primary)) { CyclePreview(1); cycled = true; }

            // Drawn last so it sits over the frame. GUIStyle.none keeps it genuinely invisible - the row style's
            // hover tint would wash the entire screen gold. IMGUI fires EVERY control under a click, so a click
            // on an arrow also lands here - the hover check is what keeps a page-turn from closing the album.
            bool overArrow = ev != null && (prev.Contains(ev.mousePosition) || next.Contains(ev.mousePosition));
            if (GUI.Button(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none, GUIStyle.none) && !cycled && !overArrow)
            {
                _previewExpanded = false;
            }
        }

        // Page to the neighbouring portal in the list order the player is already looking at (favorites
        // first, then alphabetical, honouring the current search filter), staying inside the lightbox. The
        // new portal's snapshot is fetched exactly as a row click would - the frame just waits for it.
        private void CyclePreview(int dir)
        {
            List<PortalEntry> visible = new List<PortalEntry>();
            string filter = _searchFilter == null ? "" : _searchFilter.ToLowerInvariant();
            foreach (PortalEntry e in _entries)
            {
                if (filter.Length > 0 && e.SearchKey.IndexOf(filter, StringComparison.Ordinal) < 0) continue;
                visible.Add(e);
            }
            if (visible.Count == 0) { _previewExpanded = false; return; }

            int idx = _selected != null ? visible.IndexOf(_selected) : -1;
            idx = idx < 0 ? 0 : (idx + dir + visible.Count) % visible.Count;

            PortalEntry chosen = visible[idx];
            _selected = chosen;
            _showPinPrompt = chosen.Locked && !Configuration.IsAdmin();
            _pinInput = "";
            ClearPreview();

            StopDetailFetch();
            chosen.Details = DetailState.Pending;
            _detailFetch = StartCoroutine(ResolveDetails(chosen));
            HighlightPin(chosen);
            // _previewExpanded stays true - that is the whole point.
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

        // Internal rather than private: TortalPinIcon loads the admin's custom pin PNG through the same shim.
        internal static void LoadImageViaReflection(Texture2D tex, byte[] data)
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

            // A ridden mount has to be identified BEFORE the teleport starts (the controller reference is
            // gone once dismounted) but only acted on if the teleport actually takes - TeleportTo refuses
            // during cooldowns, and dismounting a player whose trip never happens would just be rude.
            Character mount = null;
            Component mountController = null;
            if (Configuration.allowMountedTeleport.Value)
            {
                TamesPatch.FindMount(Player.m_localPlayer, out mount, out mountController);
            }

            if (!Player.m_localPlayer.TeleportTo(pos, rotation, true))
            {
                Configuration.LogDebug("TeleportTo refused (already teleporting or on cooldown).");
                return;
            }
            Game.instance.IncrementPlayerStat(PlayerStatType.PortalsUsed);

            if (mount != null)
            {
                // Out of the saddle, mount sent ahead, and a coroutine waits out the loading fade to sit the
                // player back down through the saddle's own Interact - see TamesPatch for the multiplayer
                // reasoning (ownership claims, control RPCs).
                TamesPatch.DismountForTeleport(Player.m_localPlayer);
                TamesPatch.SendMountAhead(mount, pos, rotation);
                StartCoroutine(TamesPatch.RemountWhenArrived(Player.m_localPlayer, mountController, mount));
            }
        }
    }
}
