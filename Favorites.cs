using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace TortalPortal
{
    // The player's starred portals. Purely client-side - which portals somebody likes is nobody else's
    // business, so nothing here crosses the wire and multiplayer needs no special handling: every player
    // keeps their own file on their own machine.
    //
    // Keys are world-scoped: the same file serves every world and server the player joins, and a star on
    // "Home" in one world can never light up a stranger's portal that happens to share a ZDOID in another.
    // Portals are keyed by ZDOID, which is stable for as long as the portal stands - renaming keeps the
    // star, demolishing and rebuilding does not, which is the honest answer since it IS a different portal.
    public static class Favorites
    {
        private static readonly HashSet<string> Keys = new HashSet<string>(StringComparer.Ordinal);
        private static bool _loaded;

        private static string FilePath => Path.Combine(Paths.ConfigPath, "wubarrk.TortalPortal.favorites.txt");

        // The world name rather than anything session-bound, so favorites survive reconnects and server
        // restarts. Empty during the brief window before ZNet exists, in which case nothing matches - the
        // destination window cannot be open then anyway.
        private static string WorldKey()
        {
            try
            {
                return ZNet.instance != null ? (ZNet.instance.GetWorldName() ?? "") : "";
            }
            catch
            {
                return "";
            }
        }

        private static string Key(ZDOID id) => WorldKey() + "|" + id;

        public static bool IsFavorite(ZDOID id)
        {
            EnsureLoaded();
            return Keys.Contains(Key(id));
        }

        public static void Toggle(ZDOID id)
        {
            EnsureLoaded();
            string k = Key(id);
            if (!Keys.Add(k)) Keys.Remove(k);
            Save();
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length > 0) Keys.Add(trimmed);
                }
                Configuration.LogDebug($"Loaded {Keys.Count} favorite(s) from {Path.GetFileName(FilePath)}.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not read the favorites file (non-fatal, starting empty). Reason: {ex}");
            }
        }

        private static void Save()
        {
            try
            {
                string[] lines = new string[Keys.Count];
                Keys.CopyTo(lines);
                File.WriteAllLines(FilePath, lines);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not save the favorites file (non-fatal, the star still applies this session). Reason: {ex}");
            }
        }
    }
}
