using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using UnityEngine;

namespace TortalPortal
{
    // The player's starred portals. Purely client-side - which portals somebody likes is nobody else's
    // business, so nothing here crosses the wire and multiplayer needs no special handling: every player
    // keeps their own file on their own machine.
    //
    // Keys are world-scoped, so a star on "Home" in one world can never light up a stranger's portal in
    // another.
    //
    // WHY POSITION AND NOT ZDOID. Keying by ZDOID was the obvious choice and it is wrong, in a way that
    // only shows up a session later: ZDOMan renumbers every ZDO as it reads the world off disk.
    //
    //     public void Load(ZPackage pkg, int version)
    //     {
    //         m_uid.SetID(++ZDOID.m_loadID);   // <- a brand new id, every world load, for every object
    //
    // So a ZDOID is stable for as long as the world stays loaded and meaningless the moment it is not.
    // Stars worked perfectly all session and were gone the next time you logged in, with the file on
    // disk intact and every key in it pointing at nothing.
    //
    // A portal's POSITION is the thing that genuinely does not change: it is written into the ZDO,
    // restored bit-for-bit on load, and a portal cannot be moved. Renaming keeps the star, which is
    // right. Demolishing and rebuilding elsewhere loses it, which is also right - that is a different
    // portal. Rebuilding on the exact same spot inherits it, which is the friendliest reading of an
    // ambiguous case.
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

        // One decimal place. The position round-trips through the save exactly, so this is not correcting
        // for drift - it is making the key legible and immune to any future change in how it is stored.
        // Portals are wider than a metre, so no two can round onto the same key.
        private static string Key(Vector3 pos)
        {
            return WorldKey() + "|" +
                   pos.x.ToString("F1", CultureInfo.InvariantCulture) + "," +
                   pos.y.ToString("F1", CultureInfo.InvariantCulture) + "," +
                   pos.z.ToString("F1", CultureInfo.InvariantCulture);
        }

        public static bool IsFavorite(Vector3 pos)
        {
            EnsureLoaded();
            return Keys.Contains(Key(pos));
        }

        public static void Toggle(Vector3 pos)
        {
            EnsureLoaded();
            string k = Key(pos);
            if (!Keys.Add(k)) Keys.Remove(k);
            Save();
        }

        // Old keys are "world|userID:id". There is no migrating them: the ids they name were reassigned
        // the first time the world loaded after they were written, so there is nothing left to look the
        // position up BY. They are dropped, once, with a line saying so - silently discarding somebody's
        // stars is worse than telling them.
        private static bool IsLegacyKey(string key)
        {
            int bar = key.LastIndexOf('|');
            return bar >= 0 && key.IndexOf(':', bar) >= 0;
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                if (!File.Exists(FilePath)) return;

                int legacy = 0;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0) continue;
                    if (IsLegacyKey(trimmed)) { legacy++; continue; }
                    Keys.Add(trimmed);
                }

                if (legacy > 0)
                {
                    Plugin.Log.LogWarning($"TortalPortal: dropped {legacy} favorite(s) saved in the old format. They were keyed by object id, which Valheim reassigns every time a world loads - which is why stars kept vanishing between sessions. Favorites are keyed by position now and will stick. Star them once more and it is the last time.");
                    Save(); // rewrite the file clean rather than warning again every session
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
