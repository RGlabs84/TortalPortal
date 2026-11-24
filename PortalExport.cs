using BepInEx;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TortalPortal
{
    // A live answer to "who is at their portal limit", written where an admin - or BarrkBOT over the same
    // FTP tree it already pulls mod configs from - can read it without being in the game.
    //
    // Retroactive by construction. The count comes from PlacementPatch.GetAllPortals(), which on the server
    // is every portal ZDO in the world, so a world that has been running for a year reports its whole history
    // on the first write. Nothing has to have been placed while this mod was installed.
    //
    // Keyed by player ID rather than by display name, which is where this parts company with Fatty's
    // barrkbot_feasts.json. It has to be: a portal records its owner as a number, names arrive later and only
    // once that player logs in, two players can pick the same name, and one player can change theirs. The ID
    // is the identity that the world itself agrees with. "name" rides inside each row for readers that want
    // to show something human, and is null - never a placeholder - while the owner is still a stranger.
    public static class PortalExport
    {
        private static string ExportDir => Path.Combine(Paths.ConfigPath, "TortalPortal", "portals");
        private static string ExportPath => Path.Combine(ExportDir, "barrkbot_portals.json");

        private static Coroutine _routine;

        public static void OnZNetStart()
        {
            try
            {
                Reset();
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

                // Checked before anything touches the disk, so "off" leaves no folder and no stale file
                // rather than quietly leaving yesterday's counts lying around looking current.
                if (!Configuration.enablePortalExport.Value) return;

                _routine = Plugin.Instance.StartCoroutine(ExportRoutine());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not start the portal export (non-fatal). Reason: {ex}");
            }
        }

        // These coroutines live on the plugin object, which survives a world change; ZNet.Start does not.
        public static void Reset()
        {
            if (_routine != null && Plugin.Instance != null) Plugin.Instance.StopCoroutine(_routine);
            _routine = null;
        }

        private static IEnumerator ExportRoutine()
        {
            // The same wait PortalRegistry takes, for the same reason: the world's ZDOs stream in after
            // ZNet.Start, and a file written before they land would report an empty world as fact.
            while (ZDOMan.instance == null || Game.instance == null) yield return null;
            yield return new WaitForSeconds(5f);

            while (true)
            {
                Write();
                yield return new WaitForSeconds(Mathf.Max(15f, Configuration.portalExportIntervalSeconds.Value));
            }
            // ReSharper disable once IteratorNeverReturns
        }

        // Kept out of the coroutine so the risky work can sit in a try/catch - C# forbids "yield return"
        // inside a try block with a catch clause.
        private static void Write()
        {
            try
            {
                if (ZDOMan.instance == null) return;

                List<ZDO> portals = PlacementPatch.GetAllPortals();

                Dictionary<long, int> counts = new Dictionary<long, int>();
                int unattributed = 0;

                foreach (ZDO zdo in portals)
                {
                    long creator = zdo.GetLong(ZDOVars.s_creator);

                    // No creator recorded: admin-spawned, or older than the field. Counted as a world total
                    // but never attributed to player zero, who does not exist.
                    if (creator == 0) { unattributed++; continue; }

                    counts.TryGetValue(creator, out int n);
                    counts[creator] = n + 1;
                }

                HashSet<long> online = PlayerNames.OnlinePlayerIDs();

                // Somebody logged in who owns no portals still gets a row. "We have no record of them" and
                // "they have none" are different answers, and a reader has to be able to tell them apart.
                foreach (long id in online)
                {
                    if (!counts.ContainsKey(id)) counts[id] = 0;
                }

                int cap = Configuration.maxPortalsPerPlayer.Value;
                bool capped = cap > 0;

                Dictionary<string, object> players = new Dictionary<string, object>();
                foreach (KeyValuePair<long, int> pair in counts.OrderByDescending(p => p.Value))
                {
                    players[pair.Key.ToString(CultureInfo.InvariantCulture)] = new Dictionary<string, object>
                    {
                        ["player_id"] = pair.Key,
                        ["name"] = PlayerNames.Get(pair.Key),
                        ["online"] = online.Contains(pair.Key),
                        ["portal_count"] = pair.Value,

                        // Emitted rather than left for the reader to derive. A field both sides calculate is
                        // a field that eventually disagrees.
                        ["at_cap"] = capped && pair.Value >= cap,
                        ["over_cap"] = capped && pair.Value > cap
                    };
                }

                Dictionary<string, object> doc = new Dictionary<string, object>
                {
                    ["generated_at"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    ["source"] = $"{Plugin.PluginName} {Plugin.PluginVersion}",

                    // Null, not zero. Zero is what the setting uses internally to mean "no limit", and a
                    // reader seeing "cap": 0 would reasonably conclude nobody may build anything.
                    ["cap"] = capped ? (object)cap : null,
                    ["cap_enabled"] = capped,
                    ["total_portals"] = portals.Count,
                    ["unattributed_portals"] = unattributed,
                    ["players"] = players
                };

                Save(JsonConvert.SerializeObject(doc, Formatting.Indented));
                Configuration.LogDebug($"Portal export written: {counts.Count} players, {portals.Count} portals.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: the portal export could not be built (non-fatal, the previous file stands). Reason: {ex}");
            }
        }

        private static void Save(string json)
        {
            Directory.CreateDirectory(ExportDir);

            string tmp = ExportPath + ".tmp";
            File.WriteAllText(tmp, json);

            // Written aside and swapped in, never written in place. Anything pulling this file on its own
            // schedule - BarrkBOT over FTP, an admin's script - would otherwise eventually catch it
            // half-written and parse a truncated document as the truth.
            if (File.Exists(ExportPath)) File.Replace(tmp, ExportPath, null);
            else File.Move(tmp, ExportPath);
        }
    }
}
