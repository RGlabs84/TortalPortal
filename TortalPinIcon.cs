using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TortalPortal
{
    // The map-marker art for the two non-vanilla PortalPinIcon choices, and the custom icon library behind
    // per-portal markers.
    //
    // Rune is generated at runtime like every other piece of art in this mod: one of the Elder Futhark glyphs
    // from TortalRuneTexture, sat on a dark disc inside a bright ring so it reads as a marker rather than a
    // loose squiggle at map scale.
    //
    // Everything else is a PNG on disk. Up to 1.2.1 there was exactly one - TortalPortalPin.png beside the
    // plugin - and every portal in the world wore it, which meant a modpack could theme its markers but not
    // tell them apart. Now any number of PNGs can sit in a TortalPortalPins folder beside the plugin, and each
    // portal names the one it wants (see ZdoKey). The single-file layout still works exactly as it did, so an
    // install that predates this never notices the change.
    //
    // Files are found once per session and read on first use, not at startup: a modpack shipping forty icons
    // should not pay for thirty-nine nobody looks at.
    public static class TortalPinIcon
    {
        // Where a portal's own icon choice lives on its ZDO - the file's name without the extension. Empty or
        // absent means "whatever this player's PortalPinIcon setting says", which is every portal raised
        // before this feature existed.
        public const string ZdoKey = "TortalPinIcon";

        private const string LibraryFolder = "TortalPortalPins";
        private const string LegacyFileName = "TortalPortalPin.png";

        // Vanilla draws every pin into a square box - 48 px on the great map, 32 px on the minimap (Minimap
        // m_pinSizeLarge / m_pinSizeSmall), set on the marker's RectTransform on both axes. So a picture of any
        // size lands in that same box and cannot spill across the map. What it CAN do is cost: LoadImage sizes
        // the texture to the file's own dimensions, so a 4096-pixel PNG holds 64 MB of video memory to draw
        // something 48 pixels across. Past this we say so in the log once.
        private const int SensibleMaxDimension = 256;

        private static Sprite _rune;
        private static Color _runeColour;

        // Name -> file, filled by the one-off scan. Name -> sprite, filled on demand; a null VALUE is a
        // remembered failure, so a missing or broken file is reported once rather than retried every frame the
        // map is open.
        private static readonly Dictionary<string, string> _paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Sprite> _loaded = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> _names = new List<string>();
        private static bool _scanned;
        private static bool _warnedNoCustom;

        // Every custom icon this client can draw, sorted, for the picker in the portal configuration window.
        // Empty when the player has installed no PNGs at all, which is the signal to hide the picker entirely
        // rather than offer a control with nothing in it.
        public static List<string> Names
        {
            get { EnsureScanned(); return _names; }
        }

        // Never returns null: Custom falls back to the rune when no PNG is there, and the rune is generated
        // code that cannot fail to exist. Vanilla icons never reach this - the caller maps those to
        // Minimap.PinType.
        public static Sprite Get(PortalPinIcon which)
        {
            if (which == PortalPinIcon.Custom)
            {
                string name = DefaultCustomName();
                if (string.IsNullOrEmpty(name))
                {
                    // Said once per session rather than every time the map opens. Without it, choosing Custom
                    // with no PNG anywhere silently draws the rune and reads as the setting being broken.
                    if (!_warnedNoCustom)
                    {
                        _warnedNoCustom = true;
                        Plugin.Log.LogInfo($"TortalPortal: PortalPinIcon is Custom, but there is no {LegacyFileName} beside the plugin and no PNG in the {LibraryFolder} folder - using the Rune pin instead.");
                    }
                }
                else
                {
                    Sprite custom = GetByName(name);
                    if (custom != null) return custom;
                }
            }
            return GetRune();
        }

        // A named icon, or null when this client has no such file - which is a normal state, not an error: the
        // portal's owner may have picked an icon out of a modpack the player looking at the map does not run.
        // The caller falls back to the player's own PortalPinIcon setting, so the marker is always drawn.
        public static Sprite GetByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            EnsureScanned();

            if (_loaded.TryGetValue(name, out Sprite cached)) return cached;

            if (!_paths.TryGetValue(name, out string path))
            {
                _loaded[name] = null;
                Plugin.Log.LogInfo($"TortalPortal: a portal asks for the map icon '{name}', which is not installed here - using the default marker for it. Drop {name}.png in the {LibraryFolder} folder beside the plugin to see it.");
                return null;
            }

            Sprite sprite = LoadFrom(path, name);
            _loaded[name] = sprite;
            return sprite;
        }

        // What PortalPinIcon.Custom means now that there can be more than one file. The legacy single file
        // wins when it is there, so an install that has used Custom for a year keeps the icon it has always
        // had; otherwise the first icon in the library stands in, so choosing Custom with a library installed
        // does something sensible instead of nothing.
        private static string DefaultCustomName()
        {
            EnsureScanned();
            string legacy = Path.GetFileNameWithoutExtension(LegacyFileName);
            if (_paths.ContainsKey(legacy)) return legacy;
            return _names.Count > 0 ? _names[0] : null;
        }

        // Filenames only - nothing is read from disk here. Runs once per session: adding a PNG while the game
        // is running needs a restart, which is worth one line in the log at startup rather than a directory
        // watch running for the lifetime of the process.
        private static void EnsureScanned()
        {
            if (_scanned) return;
            _scanned = true;

            try
            {
                string dir = Path.GetDirectoryName(Plugin.Instance.Info.Location) ?? "";

                // The legacy file is registered first so it wins the name if a library file shares it.
                string legacy = Path.Combine(dir, LegacyFileName);
                if (File.Exists(legacy)) Register(legacy);

                string library = Path.Combine(dir, LibraryFolder);
                if (Directory.Exists(library))
                {
                    foreach (string file in Directory.GetFiles(library, "*.png", SearchOption.TopDirectoryOnly))
                    {
                        Register(file);
                    }
                }

                _names.Sort(StringComparer.OrdinalIgnoreCase);

                if (_names.Count > 0)
                {
                    Plugin.Log.LogInfo($"TortalPortal: {_names.Count} custom map icon(s) available - {string.Join(", ", _names.ToArray())}.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not scan for custom map icons (non-fatal, the rune pin is used instead). Reason: {ex}");
            }
        }

        private static void Register(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(name) || _paths.ContainsKey(name)) return;
            _paths[name] = path;
            _names.Add(name);
        }

        private static Sprite LoadFrom(string path, string name)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = UnityEngine.FilterMode.Bilinear,
                    // Same lesson as the UI theme: without this Unity collects the texture on the next scene
                    // load and the map starts drawing a destroyed sprite.
                    hideFlags = HideFlags.HideAndDontSave
                };

                // Non-readable: the pixels are never read back, only drawn, so there is no reason to keep a
                // second copy of the picture in system memory alongside the one on the card.
                PortalUIManager.LoadImageViaReflection(tex, bytes, markNonReadable: true);

                if (tex.width > SensibleMaxDimension || tex.height > SensibleMaxDimension)
                {
                    float mb = tex.width * (float)tex.height * 4f / (1024f * 1024f);
                    Plugin.Log.LogWarning($"TortalPortal: map icon '{name}' is {tex.width}x{tex.height}, which costs about {mb:0.#} MB of video memory to draw a marker 48 pixels across. It will look no different at {SensibleMaxDimension}x{SensibleMaxDimension} or smaller.");
                }

                if (tex.width != tex.height)
                {
                    Plugin.Log.LogInfo($"TortalPortal: map icon '{name}' is {tex.width}x{tex.height} rather than square. Vanilla's marker box is square, so it is letterboxed to fit rather than stretched - a square image fills the marker best.");
                }

                Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                Plugin.Log.LogInfo($"TortalPortal: loaded map icon '{name}' from {path} ({tex.width}x{tex.height}).");
                return sprite;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not load the map icon '{name}' from {path} (non-fatal, the default marker is used). Reason: {ex}");
                return null;
            }
        }

        private static Sprite GetRune()
        {
            Color wanted = Configuration.pinColour != null ? Configuration.pinColour.Value : new Color(0.30f, 1.00f, 0.55f, 1f);

            // Rebuilt when the colour dial moves, cached the rest of the time. The old sprite and its texture
            // are destroyed rather than dropped - both are HideAndDontSave and would live forever otherwise.
            if (_rune != null && wanted == _runeColour) return _rune;
            if (_rune != null)
            {
                Texture2D old = _rune.texture;
                UnityEngine.Object.Destroy(_rune);
                if (old != null) UnityEngine.Object.Destroy(old);
            }

            _runeColour = wanted;
            Texture2D tex = BuildRunePin(new Color(wanted.r, wanted.g, wanted.b, 1f));
            _rune = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            return _rune;
        }

        // The rune texture is a glyph over transparency; alone on the map it reads as debris. Composite it
        // over a dark backing disc and put a ring round it, and it reads as a deliberate marker at any zoom.
        private static Texture2D BuildRunePin(Color colour)
        {
            // Glyph 7 rather than the colour-hashed pick: the marker should be the SAME rune on every client
            // and every session, so two players comparing maps are looking at the same symbol.
            Texture2D rune = TortalRuneTexture.GenerateRune(colour, 7);
            int s = rune.width;
            Color[] px = rune.GetPixels();

            float mid = (s - 1) * 0.5f;
            float discR = s * 0.42f;
            float ringR = s * 0.45f;
            float ringW = s * 0.035f;

            Color disc = new Color(0.02f, 0.07f, 0.04f, 0.80f);
            Color ring = new Color(colour.r, colour.g, colour.b, 1f);

            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    int i = y * s + x;
                    float dx = x - mid, dy = y - mid;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    // Backing disc UNDER the glyph, so the rune stays the brightest thing in the marker.
                    float discA = Mathf.Clamp01(discR - d) * disc.a;
                    Color under = new Color(disc.r, disc.g, disc.b, discA);
                    Color over = px[i];
                    float outA = over.a + under.a * (1f - over.a);
                    Color c = outA > 0f
                        ? new Color((over.r * over.a + under.r * under.a * (1f - over.a)) / outA,
                                    (over.g * over.a + under.g * under.a * (1f - over.a)) / outA,
                                    (over.b * over.a + under.b * under.a * (1f - over.a)) / outA,
                                    outA)
                        : Color.clear;

                    // Ring on top of everything.
                    float ringA = Mathf.Clamp01(ringW + 0.5f - Mathf.Abs(d - ringR));
                    if (ringA > 0f)
                    {
                        float a = ringA + c.a * (1f - ringA);
                        c = new Color((ring.r * ringA + c.r * c.a * (1f - ringA)) / Mathf.Max(a, 0.0001f),
                                      (ring.g * ringA + c.g * c.a * (1f - ringA)) / Mathf.Max(a, 0.0001f),
                                      (ring.b * ringA + c.b * c.a * (1f - ringA)) / Mathf.Max(a, 0.0001f),
                                      a);
                    }

                    px[i] = c;
                }
            }

            Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = UnityEngine.FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            tex.SetPixels(px);
            tex.Apply(false);

            // The intermediate rune texture is not kept - everything it had to say is composited in.
            UnityEngine.Object.Destroy(rune);
            return tex;
        }
    }
}
