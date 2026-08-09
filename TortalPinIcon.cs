using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TortalPortal
{
    // The map-marker art: the generated rune, the player's own PNGs, and the icons the server hands down.
    //
    // Rune is generated at runtime like every other piece of art in this mod: one of the Elder Futhark glyphs
    // from TortalRuneTexture, sat on a dark disc inside a bright ring so it reads as a marker rather than a
    // loose squiggle at map scale.
    //
    // Everything else is a PNG. Where that PNG comes from is the whole of this class's job, and there are
    // three places it can be:
    //
    //   1. A local file whose bytes MATCH what the server says the icon is. Same picture, already on disk -
    //      draw it and download nothing. This is what stops the icons that ship with the mod from being
    //      re-downloaded on every server: the server was handed the same package, so it offers them, and
    //      every client already has the identical bytes.
    //   2. The server's copy, cached under the config folder by content hash. The server WINS a genuine
    //      collision - same name, different picture - because the alternative is two players looking at the
    //      same portal and seeing different things, which is the exact problem the feature exists to solve.
    //   3. A local file the server has never heard of. Yours alone, drawn only for you.
    //
    // The player's personal fallback for unmarked portals - PortalPinIcon.Custom - is deliberately NOT part
    // of that ladder. It is one specific file, TortalPortalPin.png, and no server can change it.
    public static class TortalPinIcon
    {
        // Where a portal's own icon choice lives on its ZDO - the file's name without the extension. Empty or
        // absent means "whatever this player's PortalPinIcon setting says", which is every portal raised
        // before this feature existed.
        //
        // NOTE this is the ICON's name, and has nothing to do with the portal's name, which is vanilla's
        // ZDOVars.s_tag. A gate called "Bonemass Camp" can wear troll_sign; a gate called "banana" gets no
        // banana. They are set on adjacent but entirely independent lines when the config window saves.
        public const string ZdoKey = "TortalPinIcon";

        internal const string LibraryFolder = "TortalPortalPins";
        internal const string LegacyFileName = "TortalPortalPin.png";

        // Vanilla draws every pin into a square box - 48 px on the great map, 32 px on the minimap (Minimap
        // m_pinSizeLarge / m_pinSizeSmall), set on the marker's RectTransform on both axes. So a picture of any
        // size lands in that same box and cannot spill across the map. What it CAN do is cost: LoadImage sizes
        // the texture to the file's own dimensions, so a 4096-pixel PNG holds 64 MB of video memory to draw
        // something 48 pixels across. Past this we say so in the log once.
        private const int SensibleMaxDimension = 256;

        private static Sprite _rune;
        private static Color _runeColour;

        // Name -> local file, filled by the one-off scan of filenames.
        private static readonly Dictionary<string, string> Paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Name -> hash of that local file, computed on demand. The scan deliberately does not do this: a
        // modpack shipping forty icons should not read forty files at startup to answer questions about two.
        private static readonly Dictionary<string, string> LocalHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Name -> resolved sprite. A null VALUE is a remembered "not available here", so a missing file or an
        // icon still in flight is reported once rather than retried every frame the map is open.
        private static readonly Dictionary<string, Sprite> Loaded = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        // Sprites that a re-resolve replaced. They are NOT destroyed at that moment: a map marker may still be
        // holding one, and drawing a destroyed sprite is the exact failure the HideAndDontSave flags exist to
        // prevent. They are destroyed at world teardown instead, when nothing can be holding them.
        private static readonly List<Sprite> Retired = new List<Sprite>();

        private static readonly List<string> LocalNames = new List<string>();
        private static readonly HashSet<string> WarnedMismatch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> WarnedMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static bool _scanned;
        private static bool _warnedNoCustom;

        // Every icon this client could draw right now: its own files plus everything the server offers.
        // Sorted, and used to build the picker in the portal configuration window.
        public static List<string> Names
        {
            get
            {
                EnsureScanned();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                List<string> all = new List<string>();
                foreach (string n in LocalNames)
                {
                    if (seen.Add(n)) all.Add(n);
                }
                foreach (string n in TortalPinLibrary.Names())
                {
                    if (seen.Add(n)) all.Add(n);
                }
                all.Sort(StringComparer.OrdinalIgnoreCase);
                return all;
            }
        }

        // The filename is the id and never changes; this is only how it reads in the window. Deliberately NOT
        // done in the scan: the name registered there is written to the portal's ZDO and is the catalogue's
        // key, so substituting characters in it would make every lookup miss.
        public static string Pretty(string name)
        {
            return string.IsNullOrEmpty(name) ? name : name.Replace('_', ' ');
        }

        // ---- resolution ------------------------------------------------------------------------------------

        // Never returns null: Custom falls back to the rune when its file is not there, and the rune is
        // generated code that cannot fail to exist. Vanilla icons never reach this - the caller maps those to
        // Minimap.PinType.
        public static Sprite Get(PortalPinIcon which)
        {
            if (which == PortalPinIcon.Custom)
            {
                // The player NAMES their default. Anything else falls over as soon as the library is real:
                // a hard-coded filename cannot point at an icon the server sent (there is no file to name),
                // and "whichever sorts first" is an arbitrary pick nobody made - shipping eight icons would
                // have silently handed everyone a boar with meat for eyes.
                string wanted = Configuration.defaultPinIcon != null ? (Configuration.defaultPinIcon.Value ?? "").Trim() : "";
                if (!string.IsNullOrEmpty(wanted))
                {
                    Sprite named = GetByName(wanted);
                    if (named != null) return named;

                    if (!_warnedNoCustom)
                    {
                        _warnedNoCustom = true;
                        Plugin.Log.LogWarning($"TortalPortal: DefaultPinIcon is set to '{wanted}', which is not installed here and is not offered by the server - using the Rune pin instead. Installed icons: {(Names.Count > 0 ? string.Join(", ", Names.ToArray()) : "none")}.");
                    }
                    return GetRune();
                }

                // Nothing named: the single-file layout that predates the library still works untouched.
                Sprite legacy = LoadLegacy();
                if (legacy != null) return legacy;

                if (!_warnedNoCustom)
                {
                    _warnedNoCustom = true;
                    Plugin.Log.LogInfo($"TortalPortal: PortalPinIcon is Custom but DefaultPinIcon names nothing and there is no {LegacyFileName} beside the plugin - using the Rune pin instead. Set DefaultPinIcon to any installed icon's name{(Names.Count > 0 ? " (available: " + string.Join(", ", Names.ToArray()) + ")" : "")}.");
                }
            }
            return GetRune();
        }

        // A named icon, or null when it cannot be drawn here yet - which is a normal state, not an error. The
        // caller falls back to the player's own PortalPinIcon setting, so a marker is always drawn.
        public static Sprite GetByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            EnsureScanned();

            if (Loaded.TryGetValue(name, out Sprite cached)) return cached;

            Sprite sprite = Resolve(name);
            Loaded[name] = sprite;
            return sprite;
        }

        private static Sprite Resolve(string name)
        {
            TortalPinLibrary.Entry offered = TortalPinLibrary.Find(name);
            bool haveLocal = Paths.TryGetValue(name, out string localPath);

            if (offered != null)
            {
                // Tier 1 - the same picture is already on disk. Nothing to download, nothing to decide.
                if (haveLocal && string.Equals(LocalHashFor(name), offered.Hash, StringComparison.OrdinalIgnoreCase))
                {
                    return LoadFrom(localPath, name);
                }

                // Tier 2 - the server's copy, once it has arrived.
                if (TortalPinLibrary.HasBlob(offered.Hash))
                {
                    if (haveLocal && WarnedMismatch.Add(name))
                    {
                        Plugin.Log.LogInfo($"TortalPortal: your own '{name}.png' is a different picture from the server's. Drawing the server's, so this portal looks the same to everyone here. Your file is untouched.");
                    }
                    return LoadFrom(TortalPinLibrary.BlobPath(offered.Hash), name);
                }

                // Offered but not here yet. A local file of the same name stands in meanwhile - a picture of
                // roughly the right meaning beats a generic marker - and the download swaps it in when it
                // lands, because completing one moves TortalPinLibrary.Revision.
                if (haveLocal) return LoadFrom(localPath, name);
                return null;
            }

            // Tier 3 - the server has never heard of it. Yours alone.
            if (haveLocal) return LoadFrom(localPath, name);

            if (WarnedMissing.Add(name))
            {
                Plugin.Log.LogInfo($"TortalPortal: a portal asks for the map icon '{name}', which is not installed here and is not offered by the server - using the default marker for it. Drop {name}.png in the {LibraryFolder} folder beside the plugin to see it.");
            }
            return null;
        }

        // Called when the catalogue changes, when a download completes, and at world teardown. Everything
        // resolved against the server has to be worked out again; the sprites already handed to map markers
        // are retired rather than destroyed.
        public static void ForgetServerIcons()
        {
            foreach (Sprite s in Loaded.Values)
            {
                if (s != null) Retired.Add(s);
            }
            Loaded.Clear();
            WarnedMismatch.Clear();
            WarnedMissing.Clear();
        }

        // Leaving a world. Statics here are per-server state - which icons exist, which the server overrode -
        // and without this they would follow the player to the next server and into single player.
        public static void Reset()
        {
            try
            {
                ForgetServerIcons();
                foreach (Sprite s in Retired)
                {
                    if (s == null) continue;
                    Texture2D tex = s.texture;
                    UnityEngine.Object.Destroy(s);
                    if (tex != null) UnityEngine.Object.Destroy(tex);
                }
                Retired.Clear();

                Paths.Clear();
                LocalHashes.Clear();
                LocalNames.Clear();
                _scanned = false;
                _warnedNoCustom = false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: resetting the map icon library failed (non-fatal). Reason: {ex}");
            }
        }

        // ---- local files -----------------------------------------------------------------------------------

        // True when this client's own file for that name is byte-identical to what the server is offering.
        public static bool LocalHashMatches(string name, string hash)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(hash)) return false;
            EnsureScanned();
            string mine = LocalHashFor(name);
            return mine != null && string.Equals(mine, hash, StringComparison.OrdinalIgnoreCase);
        }

        private static string LocalHashFor(string name)
        {
            if (LocalHashes.TryGetValue(name, out string cached)) return cached;
            if (!Paths.TryGetValue(name, out string path)) return null;

            string hash = null;
            try
            {
                hash = TortalPinLibrary.HashOf(File.ReadAllBytes(path));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"TortalPortal: could not read '{path}' to compare it with the server's copy. Reason: {ex.Message}");
            }

            LocalHashes[name] = hash;
            return hash;
        }

        // Filenames only - nothing is read from disk here. Runs once per session: adding a PNG by hand while
        // the game is running needs a restart, which is worth one line in the log rather than a directory
        // watch running for the lifetime of the process. Icons arriving FROM THE SERVER need no restart.
        private static void EnsureScanned()
        {
            if (_scanned) return;
            _scanned = true;

            try
            {
                string dir = Path.GetDirectoryName(Plugin.Instance.Info.Location) ?? "";

                string library = Path.Combine(dir, LibraryFolder);
                if (Directory.Exists(library))
                {
                    foreach (string file in Directory.GetFiles(library, "*.png", SearchOption.TopDirectoryOnly))
                    {
                        Register(file);
                    }
                }

                LocalNames.Sort(StringComparer.OrdinalIgnoreCase);

                if (LocalNames.Count > 0)
                {
                    Plugin.Log.LogInfo($"TortalPortal: {LocalNames.Count} map icon(s) on this machine - {string.Join(", ", LocalNames.ToArray())}.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not scan for map icons (non-fatal, the rune pin is used instead). Reason: {ex}");
            }
        }

        private static void Register(string path)
        {
            // The filename IS the id - written to the portal's ZDO, and the catalogue's key. Nothing is
            // substituted or prettified here; see Pretty() for how it reads in the window.
            string name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(name) || Paths.ContainsKey(name)) return;
            Paths[name] = path;
            LocalNames.Add(name);
        }

        // The single-file layout that predates the folder. Kept entirely separate from the library so it can
        // never be confused with a shared icon, and so PortalPinIcon.Custom means exactly one thing.
        private static Sprite LoadLegacy()
        {
            const string key = " legacy";
            if (Loaded.TryGetValue(key, out Sprite cached)) return cached;

            Sprite sprite = null;
            try
            {
                string dir = Path.GetDirectoryName(Plugin.Instance.Info.Location) ?? "";
                string path = Path.Combine(dir, LegacyFileName);
                if (File.Exists(path)) sprite = LoadFrom(path, Path.GetFileNameWithoutExtension(LegacyFileName));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not load {LegacyFileName} (non-fatal, the rune pin is used). Reason: {ex}");
            }

            Loaded[key] = sprite;
            return sprite;
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
                Configuration.LogDebug($"Loaded map icon '{name}' from {path} ({tex.width}x{tex.height}).");
                return sprite;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not load the map icon '{name}' from {path} (non-fatal, the default marker is used). Reason: {ex}");
                return null;
            }
        }

        // ---- the rune --------------------------------------------------------------------------------------

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
