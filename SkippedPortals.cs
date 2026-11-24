using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TortalPortal
{
    // Portal prefabs this mod leaves alone (SkipPortalPrefabs, synced).
    //
    // Other mods add portals of their own that work differently - a one-way gate, a portal into an instance
    // or a dungeon, a gate run by its own menu. They are still TeleportWorld pieces in Game.PortalPrefabHash, so
    // without this TortalPortal took every one of them over: its window opened on them, vanilla's tag pairing
    // was cut, and they turned up as destinations for everyone else.
    //
    // A skipped prefab is not ours at all. Interact, SetText, Update, UpdatePortal and Teleport fall straight
    // through to vanilla (and to whatever the owning mod patches in); the gate is never listed, never on the map,
    // never counted against a cap, never in the export or the health scan; it gets no runic gateway and no
    // damage immunity. Only the item filter still applies, because that is a rule about what may travel rather
    // than about any one gate, and it already defers to a portal that allows all items.
    //
    // The gates this mod raises itself - corpse gates, concealed gates - carry a Kind key and are never skipped,
    // whatever the list says, so skipping the prefab they are minted from cannot strand a corpse run.
    public static class SkippedPortals
    {
        private static string _parsedFrom;
        private static HashSet<string> _exact = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static HashSet<int> _exactHashes = new HashSet<int>();
        private static List<Regex> _patterns = new List<Regex>();

        // Prefab hash -> verdict, filled only once ZNetScene can name the hash, so an early miss is not cached.
        private static readonly Dictionary<int, bool> ByHash = new Dictionary<int, bool>();

        public static bool Any
        {
            get { Parse(); return _exact.Count > 0 || _patterns.Count > 0; }
        }

        // Called from the setting's SettingChanged (which ServerSync also fires when the server's value lands).
        public static void Invalidate()
        {
            _parsedFrom = null;
            ByHash.Clear();
        }

        // The configured list as read, for the log line on change.
        public static string Describe()
        {
            Parse();
            if (!Any) return "none";
            var parts = new List<string>(_exact);
            foreach (Regex r in _patterns) parts.Add(r.ToString());
            return string.Join(", ", parts);
        }

        private static void Parse()
        {
            string raw = Configuration.skipPortalPrefabs != null ? (Configuration.skipPortalPrefabs.Value ?? "") : "";
            if (raw == _parsedFrom) return;

            var exact = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hashes = new HashSet<int>();
            var patterns = new List<Regex>();

            foreach (string piece in raw.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = piece.Trim();
                if (name.EndsWith("(Clone)", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 7).Trim();
                if (name.Length == 0) continue;

                if (name.IndexOf('*') >= 0)
                {
                    // A plain glob: * is any run of characters, everything else literal. "MWL_*" or "*_portal".
                    string rx = "^" + Regex.Escape(name).Replace("\\*", ".*") + "$";
                    patterns.Add(new Regex(rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
                }
                else
                {
                    exact.Add(name);
                    // Exact-case hash, for the rare check that runs before ZNetScene can name a prefab.
                    hashes.Add(name.GetStableHashCode());
                }
            }

            _exact = exact;
            _exactHashes = hashes;
            _patterns = patterns;
            _parsedFrom = raw;
            ByHash.Clear();
        }

        public static bool IsSkippedName(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return false;
            Parse();
            if (_exact.Count == 0 && _patterns.Count == 0) return false;

            string name = prefabName;
            if (name.EndsWith("(Clone)", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 7);
            name = name.Trim();

            if (_exact.Contains(name)) return true;
            foreach (Regex r in _patterns)
            {
                if (r.IsMatch(name)) return true;
            }
            return false;
        }

        public static bool IsSkippedHash(int prefabHash)
        {
            Parse();
            if (_exact.Count == 0 && _patterns.Count == 0) return false;
            if (ByHash.TryGetValue(prefabHash, out bool cached)) return cached;

            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabHash) : null;
            if (prefab == null)
            {
                // Not nameable yet (or not a registered prefab): only an exact-case name can be recognised.
                return _exactHashes.Contains(prefabHash);
            }

            bool verdict = IsSkippedName(prefab.name);
            ByHash[prefabHash] = verdict;
            return verdict;
        }

        public static bool IsSkipped(ZDO zdo)
        {
            if (zdo == null || !zdo.IsValid()) return false;
            if (!Any) return false;
            // Ours, whatever prefab it was minted from.
            if (!string.IsNullOrEmpty(zdo.GetString(PortalKeys.Kind, ""))) return false;
            return IsSkippedHash(zdo.GetPrefab());
        }

        // A live portal (or anything on one). Reads the ZDO when there is one, and falls back to the object's
        // own name - TeleportWorld.Awake can run before the ZNetView has a ZDO to read.
        public static bool IsSkipped(Component onPortal)
        {
            if (onPortal == null) return false;
            if (!Any) return false;

            ZNetView nview = onPortal.GetComponent<ZNetView>();
            if (nview == null) nview = onPortal.GetComponentInParent<ZNetView>();
            if (nview != null && nview.IsValid()) return IsSkipped(nview.GetZDO());

            return IsSkippedName(Utils.GetPrefabName(nview != null ? nview.gameObject : onPortal.gameObject));
        }
    }
}
