using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TortalPortal
{
    // The portal network's health: a standing scan, a bounded repair pass, and snapshots to undo it with.
    //
    // Ported in shape from TortalPortal Lite - scan on a timer, dry-run by default, --apply writes and only
    // after a snapshot, restore by position - but the findings are this mod's own. Lite's scan looks for what
    // vanilla's tag pairing leaves broken; this mod never pairs by tag, so those findings would be noise here.
    // What CAN go wrong with a portal under this mod is different and every bit as real:
    //
    //   Buried          - a portal standing below the terrain, the way the traders were. Raise-ground work,
    //                     a scatter placed against a height that was not there, a piece that sank. Lifted to
    //                     the ground on repair; never lowered.
    //   OversizedPhoto  - a destination photograph over the wire budget. A ZDO crosses the wire whole, Steam
    //                     refuses a message over 512 KiB, and Valheim's socket blocks on the refusal forever:
    //                     a picture from before the 1.4.3 budget can boot-loop every client that walks near
    //                     the gate. Stripped on repair, saved beside the snapshot first, re-photographed later.
    //   UnknownIcon     - a portal asking for a map icon the server does not offer. Cleared on repair.
    //   StrayCorpseGate - a corpse gate no running record accounts for. Removed on repair.
    //   OverCap         - a player holding more portals than the cap. Reported only; the gates are theirs.
    //   Interior        - a portal at dungeon height. Reported only.
    //   Scattered       - tortal_scatter litter still standing. Reported only; tortal_scatterclear removes it.
    //   Concealed       - seeded gates nobody has been told about, and how many have been found. Reported only;
    //                     tortal_concealedclear removes them. Held apart from every other check because a
    //                     concealed gate is exempt from the rules public portals are measured against.
    //   Unowned, DuplicateName, LockedUnowned - informational.
    //
    // Everything runs on the server, which is the only peer that holds the whole world; from a client console
    // the commands are routed there, admin-gated against the server's own adminlist, and the answer comes back.
    [HarmonyPatch]
    public static class PortalHealth
    {
        public enum Severity { Info, Warning, Error }

        public sealed class Finding
        {
            public Severity Severity;
            public string Category;
            public string Detail;
            public List<ZDOID> Portals = new List<ZDOID>();
        }

        private sealed class RepairAction
        {
            public string Description;
            public ZDOID Portal;
            public bool StripsPhoto;
            public Action Apply;
        }

        // Findings from the last scan, newest first by severity.
        public static IReadOnlyList<Finding> Findings => _findings;
        private static List<Finding> _findings = new List<Finding>();
        private static DateTime _lastScanUtc = DateTime.MinValue;

        private static Coroutine _routine;
        private static bool _saveHooked;
        private static bool _registered;

        private const string OpsRpc = "TortalPortal_Ops";
        private const string OpsReplyRpc = "TortalPortal_OpsReply";

        // A portal this far under the modelled ground is buried. Generous, because the ground model is the
        // heightmap plus terraforming and not the location pieces a gate may stand on.
        private const float BuriedMeters = 1.0f;

        // The same budget the photographer enforces - see PlacementPatch.SnapshotWireBudgetBytes.
        private const int PhotoWireBudgetBytes = 440 * 1024;

        private static string SnapshotDir => Path.Combine(Paths.ConfigPath, "TortalPortal", "snapshots");

        // ---- lifecycle ---------------------------------------------------------------------------------------

        public static void OnZNetStart()
        {
            if (ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.Register<string>(OpsRpc, RPC_Ops);
                ZRoutedRpc.instance.Register<string>(OpsReplyRpc, RPC_OpsReply);
            }

            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

            if (_routine != null) Plugin.Instance.StopCoroutine(_routine);
            _routine = Plugin.Instance.StartCoroutine(ServerRoutine());

            if (!_saveHooked)
            {
                try
                {
                    ZNet.WorldSaveStarted = (Action)Delegate.Combine(ZNet.WorldSaveStarted, new Action(OnWorldSaveStarted));
                    _saveHooked = true;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"TortalPortal: could not hook the world save for pre-save snapshots (non-fatal). Reason: {ex.Message}");
                }
            }
        }

        public static void Reset()
        {
            if (_routine != null && Plugin.Instance != null) Plugin.Instance.StopCoroutine(_routine);
            _routine = null;
            _findings = new List<Finding>();
            _lastScanUtc = DateTime.MinValue;
            if (_saveHooked)
            {
                try { ZNet.WorldSaveStarted = (Action)Delegate.Remove(ZNet.WorldSaveStarted, new Action(OnWorldSaveStarted)); }
                catch { }
                _saveHooked = false;
            }
        }

        private static IEnumerator ServerRoutine()
        {
            while (ZDOMan.instance == null || Game.instance == null) yield return null;
            // The world's ZDOs stream in after ZNet.Start, and the corpse run needs a moment to reap its
            // leftovers before a scan reports them.
            yield return new WaitForSeconds(20f);

            while (true)
            {
                RunScheduled();
                yield return new WaitForSeconds(Mathf.Clamp(Configuration.healthScanIntervalSeconds.Value, 10f, 3600f));
            }
            // ReSharper disable once IteratorNeverReturns
        }

        private static void RunScheduled()
        {
            try
            {
                Scan();
                LogSummary("scheduled scan");
                if (Configuration.autoRepair.Value)
                {
                    bool dryRun = Configuration.repairDryRunDefault.Value;
                    string result = Run(apply: !dryRun);
                    Plugin.Log.LogInfo($"TortalPortal: automatic repair pass ({(dryRun ? "report only" : "applying")}): {result}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: the scheduled health scan failed (non-fatal). Reason: {ex}");
            }
        }

        private static void OnWorldSaveStarted()
        {
            try
            {
                if (Configuration.snapshotOnWorldSave.Value) TakeSnapshot("pre-save-" + Stamp(), null);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: the pre-save snapshot failed (non-fatal). Reason: {ex}");
            }
        }

        private static string Stamp() => DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        // ---- the scan ----------------------------------------------------------------------------------------

        public static void Scan()
        {
            List<Finding> findings = new List<Finding>();
            if (ZDOMan.instance == null)
            {
                _findings = findings;
                return;
            }

            List<ZDO> portals = PlacementPatch.GetAllPortals(includeCorpseGates: true, includeConcealed: true);
            GroundProbe probe = GroundProbe.Ready ? new GroundProbe() : null;
            List<ZoneSystem.LocationInstance> locations = LocationList();
            List<string> icons = TortalPinLibrary.Names();
            int cap = Configuration.maxPortalsPerPlayer.Value;

            Dictionary<long, List<ZDOID>> byCreator = new Dictionary<long, List<ZDOID>>();
            Dictionary<string, List<ZDOID>> byName = new Dictionary<string, List<ZDOID>>(StringComparer.Ordinal);
            List<ZDOID> unowned = new List<ZDOID>();
            List<ZDOID> scattered = new List<ZDOID>();
            List<ZDOID> concealed = new List<ZDOID>();
            int concealedFound = 0;

            foreach (ZDO zdo in portals)
            {
                if (zdo == null || !zdo.IsValid()) continue;
                Vector3 pos = zdo.GetPosition();
                string where = Where(zdo);

                if (CorpseRun.IsCorpseGate(zdo))
                {
                    if (!CorpseRun.IsTracked(zdo.m_uid))
                    {
                        findings.Add(Make(Severity.Warning, "StrayCorpseGate", $"corpse gate at {where} is not one this server run raised (a leftover from a previous run){(Configuration.enableCorpseRun.Value ? "; the corpse run reaps these on its own" : "")}.", zdo.m_uid));
                    }
                    continue;
                }

                // A concealed gate records no builder, carries no public name and is in nobody's index by
                // design, so every check below it would report a rule it is deliberately exempt from - unowned,
                // duplicate-named, locked-and-unowned, all of them. Only the buried test is worth keeping, and
                // it is worth MORE here than anywhere: the whole point of one of these is to be walked up to,
                // and a gate sunk in a hillside cannot be.
                if (ConcealedPortals.IsConcealed(zdo))
                {
                    concealed.Add(zdo.m_uid);
                    if (zdo.GetBool(PortalKeys.FoundAll, false) || (zdo.GetString(PortalKeys.Found, "") ?? "").Length > 0) concealedFound++;

                    if (probe != null && pos.y <= PortalPlacement.InteriorHeight && !InsideLocation(pos, locations))
                    {
                        float concealedGround = probe.HeightAt(pos.x, pos.z);
                        if (pos.y < concealedGround - BuriedMeters)
                        {
                            findings.Add(Make(Severity.Warning, "Buried", $"concealed gate at {where} is {concealedGround - pos.y:0.0}m below the terrain - nobody can find what they cannot see.", zdo.m_uid));
                        }
                    }
                    continue;
                }

                long creator = zdo.GetLong(ZDOVars.s_creator, 0L);
                if (creator == 0L) unowned.Add(zdo.m_uid);
                else
                {
                    if (!byCreator.TryGetValue(creator, out List<ZDOID> owned)) byCreator[creator] = owned = new List<ZDOID>();
                    owned.Add(zdo.m_uid);
                }

                string name = zdo.GetString(ZDOVars.s_tag, "") ?? "";
                if (name.Length > 0)
                {
                    if (!byName.TryGetValue(name, out List<ZDOID> same)) byName[name] = same = new List<ZDOID>();
                    same.Add(zdo.m_uid);
                }

                if (zdo.GetBool("TortalScattered", false)) scattered.Add(zdo.m_uid);

                if (pos.y > PortalPlacement.InteriorHeight)
                {
                    findings.Add(Make(Severity.Warning, "Interior", $"portal '{name}' at {where} stands at dungeon height - a trip to it lands inside an instance.", zdo.m_uid));
                }
                else if (probe != null && !InsideLocation(pos, locations))
                {
                    float ground = probe.HeightAt(pos.x, pos.z);
                    if (pos.y < ground - BuriedMeters)
                    {
                        findings.Add(Make(Severity.Warning, "Buried", $"portal '{name}' at {where} is {ground - pos.y:0.0}m below the terrain.", zdo.m_uid));
                    }
                }

                byte[] photo = zdo.GetByteArray("PortalSnapshot");
                if (photo != null && photo.Length > PhotoWireBudgetBytes)
                {
                    findings.Add(Make(Severity.Error, "OversizedPhoto", $"portal '{name}' at {where} carries a {photo.Length / 1024}KB photograph, over the {PhotoWireBudgetBytes / 1024}KB a ZDO can be sent in - a client walking near it can be disconnected in a loop.", zdo.m_uid));
                }

                string icon = zdo.GetString(TortalPinIcon.ZdoKey, "") ?? "";
                if (icon.Length > 0 && icons.Count > 0 && !icons.Contains(icon, StringComparer.OrdinalIgnoreCase))
                {
                    findings.Add(Make(Severity.Info, "UnknownIcon", $"portal '{name}' at {where} asks for the map icon '{icon}', which this server does not offer - clients show the default marker.", zdo.m_uid));
                }

                if (!string.IsNullOrEmpty(zdo.GetString("PortalPIN", "")) && creator == 0L)
                {
                    findings.Add(Make(Severity.Info, "LockedUnowned", $"portal '{name}' at {where} is PIN-locked and records no builder - only an admin can change it.", zdo.m_uid));
                }
            }

            if (cap > 0)
            {
                foreach (KeyValuePair<long, List<ZDOID>> pair in byCreator)
                {
                    if (pair.Value.Count <= cap) continue;
                    string who = PlayerNames.Get(pair.Key) ?? pair.Key.ToString(CultureInfo.InvariantCulture);
                    Finding f = Make(Severity.Warning, "OverCap", $"{who} holds {pair.Value.Count} portals against a cap of {cap} - reported only, their gates are not touched.", ZDOID.None);
                    f.Portals.Clear();
                    f.Portals.AddRange(pair.Value);
                    findings.Add(f);
                }
            }

            foreach (KeyValuePair<string, List<ZDOID>> pair in byName)
            {
                if (pair.Value.Count < 2) continue;
                Finding f = Make(Severity.Info, "DuplicateName", $"{pair.Value.Count} portals share the name '{pair.Key}'.", ZDOID.None);
                f.Portals.Clear();
                f.Portals.AddRange(pair.Value);
                findings.Add(f);
            }

            if (unowned.Count > 0)
            {
                Finding f = Make(Severity.Info, "Unowned", $"{unowned.Count} portal(s) record no builder (admin-spawned, or older than the field).", ZDOID.None);
                f.Portals.Clear();
                f.Portals.AddRange(unowned);
                findings.Add(f);
            }

            if (scattered.Count > 0)
            {
                Finding f = Make(Severity.Info, "Scattered", $"{scattered.Count} portal(s) raised by tortal_scatter are still standing - tortal_scatterclear removes them.", ZDOID.None);
                f.Portals.Clear();
                f.Portals.AddRange(scattered);
                findings.Add(f);
            }

            if (concealed.Count > 0)
            {
                Finding f = Make(Severity.Info, "Concealed", $"{concealed.Count} concealed gate(s) standing, {concealedFound} of them found by somebody. They are withheld from the portal index on purpose - tortal_concealed reports who has found what, tortal_concealedclear removes them.", ZDOID.None);
                f.Portals.Clear();
                f.Portals.AddRange(concealed);
                findings.Add(f);
            }

            findings.Sort((a, b) => b.Severity.CompareTo(a.Severity));
            _findings = findings;
            _lastScanUtc = DateTime.UtcNow;
        }

        private static Finding Make(Severity severity, string category, string detail, ZDOID portal)
        {
            Finding f = new Finding { Severity = severity, Category = category, Detail = detail };
            if (portal != ZDOID.None) f.Portals.Add(portal);
            return f;
        }

        private static string Where(ZDO zdo)
        {
            Vector3 p = zdo.GetPosition();
            return $"({p.x:0},{p.y:0},{p.z:0})";
        }

        private static List<ZoneSystem.LocationInstance> LocationList()
        {
            List<ZoneSystem.LocationInstance> list = new List<ZoneSystem.LocationInstance>();
            try
            {
                if (ZoneSystem.instance != null) list.AddRange(ZoneSystem.instance.m_locationInstances.Values);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"TortalPortal: the location list is unavailable to the health scan (non-fatal). Reason: {ex.Message}");
            }
            return list;
        }

        // A generated location levels the ground under itself with terrain modifiers the ground model does not
        // see, so a portal standing inside one is never judged against that model.
        private static bool InsideLocation(Vector3 pos, List<ZoneSystem.LocationInstance> locations)
        {
            foreach (ZoneSystem.LocationInstance inst in locations)
            {
                float r = ObstacleField.ExteriorRadiusOf(inst) + 5f;
                float dx = inst.m_position.x - pos.x;
                float dz = inst.m_position.z - pos.z;
                if ((dx * dx) + (dz * dz) <= r * r) return true;
            }
            return false;
        }

        private static void LogSummary(string what)
        {
            int errors = 0, warnings = 0, infos = 0;
            foreach (Finding f in _findings)
            {
                if (f.Severity == Severity.Error) errors++;
                else if (f.Severity == Severity.Warning) warnings++;
                else infos++;
            }
            if (errors == 0 && warnings == 0)
            {
                Plugin.Log.LogInfo($"TortalPortal: portal health {what}: nothing wrong ({infos} note(s)).");
                return;
            }
            Plugin.Log.LogWarning($"TortalPortal: portal health {what}: {errors} error(s), {warnings} warning(s), {infos} note(s) - tortal_health lists them.");
            foreach (Finding f in _findings)
            {
                if (f.Severity != Severity.Info) Plugin.Log.LogWarning($"TortalPortal:   [{f.Severity}] {f.Category}: {f.Detail}");
            }
        }

        public static string Report()
        {
            if (_lastScanUtc == DateTime.MinValue) return "tortal_health: no scan has run yet.";
            StringBuilder sb = new StringBuilder();
            sb.Append($"tortal_health: {_findings.Count} finding(s) as of {_lastScanUtc:HH:mm:ss} UTC");
            foreach (Finding f in _findings) sb.Append('\n').Append($"[{f.Severity}] {f.Category}: {f.Detail}");
            if (_findings.Count == 0) sb.Append(" - nothing wrong.");
            return sb.ToString();
        }

        // ---- repair ------------------------------------------------------------------------------------------

        // apply=false is always a dry run whatever the config says - an explicit request to write requires an
        // explicit --apply.
        public static string Run(bool apply)
        {
            if (ZDOMan.instance == null) return "tortal_repair: no world loaded.";

            List<RepairAction> actions = PlanActions();
            if (actions.Count == 0) return "tortal_repair: nothing to fix.";

            int budget = Mathf.Max(1, Configuration.repairMaxActionsPerRun.Value);
            List<RepairAction> chosen = actions.Take(budget).ToList();

            if (!apply)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append($"tortal_repair: DRY RUN - {chosen.Count} action(s){(actions.Count > chosen.Count ? $" of {actions.Count}" : "")}:");
                foreach (RepairAction a in chosen) sb.Append('\n').Append("  ").Append(a.Description);
                sb.Append("\nRe-run with --apply to write them (a snapshot is taken first).");
                return sb.ToString();
            }

            HashSet<ZDOID> photosToKeep = new HashSet<ZDOID>();
            foreach (RepairAction a in chosen)
            {
                if (a.StripsPhoto) photosToKeep.Add(a.Portal);
            }
            string snapshotName = "pre-repair-" + Stamp();
            string snapshotResult = TakeSnapshot(snapshotName, photosToKeep);
            if (!snapshotResult.StartsWith("ok", StringComparison.Ordinal))
            {
                return $"tortal_repair: refused - the pre-repair snapshot failed ({snapshotResult}). Nothing was written.";
            }

            int applied = 0;
            foreach (RepairAction a in chosen)
            {
                try
                {
                    a.Apply();
                    applied++;
                    Plugin.Log.LogInfo($"TortalPortal: repair applied - {a.Description}");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"TortalPortal: repair action failed ({a.Description}). Reason: {ex.GetType().Name}: {ex.Message}");
                }
            }

            // The world changed; the next report should say so.
            Scan();
            return $"tortal_repair: applied {applied}/{chosen.Count}, snapshot '{snapshotName}' saved first - tortal_restore {snapshotName} undoes it.";
        }

        private static List<RepairAction> PlanActions()
        {
            List<RepairAction> actions = new List<RepairAction>();
            foreach (Finding f in _findings)
            {
                foreach (ZDOID id in f.Portals)
                {
                    ZDO zdo = ZDOMan.instance.GetZDO(id);
                    if (zdo == null || !zdo.IsValid()) continue;
                    string name = zdo.GetString(ZDOVars.s_tag, "");
                    string where = Where(zdo);

                    switch (f.Category)
                    {
                        case "Buried":
                            actions.Add(new RepairAction
                            {
                                Portal = id,
                                Description = $"lift '{name}' at {where} to the ground",
                                Apply = () => LiftToGround(id),
                            });
                            break;

                        case "OversizedPhoto":
                            actions.Add(new RepairAction
                            {
                                Portal = id,
                                StripsPhoto = true,
                                Description = $"strip the oversized photograph from '{name}' at {where} (kept beside the snapshot; re-photographed on a later pass)",
                                Apply = () => ServerPeers.ClaimAndWrite(zdo, z => z.Set("PortalSnapshot", new byte[0])),
                            });
                            break;

                        case "UnknownIcon":
                            actions.Add(new RepairAction
                            {
                                Portal = id,
                                Description = $"clear the unknown icon '{zdo.GetString(TortalPinIcon.ZdoKey, "")}' from '{name}' at {where}",
                                Apply = () => ServerPeers.ClaimAndWrite(zdo, z => z.Set(TortalPinIcon.ZdoKey, "")),
                            });
                            break;

                        case "StrayCorpseGate":
                            actions.Add(new RepairAction
                            {
                                Portal = id,
                                Description = $"remove the stray corpse gate at {where}",
                                Apply = () => PortalMint.Destroy(zdo),
                            });
                            break;
                    }
                }
            }
            return actions;
        }

        // Up to the ground, never down onto it - the same rule the trader repair follows.
        private static void LiftToGround(ZDOID id)
        {
            ZDO zdo = ZDOMan.instance.GetZDO(id);
            if (zdo == null || !zdo.IsValid() || !GroundProbe.Ready) return;
            Vector3 pos = zdo.GetPosition();
            float ground = new GroundProbe().HeightAt(pos.x, pos.z) + PortalPlacement.GroundLift;
            if (ground <= pos.y) return;
            ServerPeers.ClaimAndWrite(zdo, z => z.SetPosition(new Vector3(pos.x, ground, pos.z)));
        }

        // ---- snapshots ---------------------------------------------------------------------------------------

        public sealed class SnapshotEntry
        {
            // "x:z" at half-metre resolution. Snapshots resolve by position, never by ZDOID, because ZDOIDs are
            // re-minted on every world load - and by x/z alone, so a portal the repair has lifted still matches.
            public string Id = "";
            public float X, Y, Z;
            public float RotY;
            public int Prefab;
            public string Name = "";
            public long Creator;
            public string CreatorName = "";
            public string Pin = "";
            public string Icon = "";
            public bool HasPhoto;
            public int PhotoBytes;
            // The photograph's file beside the snapshot, only when a repair was about to strip it.
            public string PhotoFile = "";
        }

        public sealed class SnapshotFile
        {
            public int SchemaVersion = 1;
            public string CreatedUtc = "";
            public string Source = "";
            public string WorldName = "";
            public long WorldUid;
            public List<SnapshotEntry> Portals = new List<SnapshotEntry>();
        }

        public static string PositionId(Vector3 p)
        {
            return $"{Mathf.Round(p.x * 2f) / 2f:0.0}:{Mathf.Round(p.z * 2f) / 2f:0.0}";
        }

        // Returns "ok ..." on success so callers can tell success from a message.
        public static string TakeSnapshot(string name, HashSet<ZDOID> savePhotosFor)
        {
            if (ZDOMan.instance == null) return "no world loaded";
            try
            {
                string safeName = Sanitize(string.IsNullOrEmpty(name) ? "manual-" + Stamp() : name);
                SnapshotFile file = new SnapshotFile
                {
                    CreatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    Source = $"{Plugin.PluginName} {Plugin.PluginVersion}",
                    WorldName = ZNet.instance != null ? ZNet.instance.GetWorldName() : "",
                    WorldUid = ZNet.instance != null ? ZNet.instance.GetWorldUID() : 0L,
                };

                Directory.CreateDirectory(SnapshotDir);
                int photos = 0;
                foreach (ZDO zdo in PlacementPatch.GetAllPortals())
                {
                    if (zdo == null || !zdo.IsValid()) continue;
                    Vector3 pos = zdo.GetPosition();
                    byte[] photo = zdo.GetByteArray("PortalSnapshot");
                    SnapshotEntry e = new SnapshotEntry
                    {
                        Id = PositionId(pos),
                        X = pos.x, Y = pos.y, Z = pos.z,
                        RotY = zdo.GetRotation().eulerAngles.y,
                        Prefab = zdo.GetPrefab(),
                        Name = zdo.GetString(ZDOVars.s_tag, "") ?? "",
                        Creator = zdo.GetLong(ZDOVars.s_creator, 0L),
                        CreatorName = zdo.GetString(ZDOVars.s_creatorName, "") ?? "",
                        Pin = zdo.GetString("PortalPIN", "") ?? "",
                        Icon = zdo.GetString(TortalPinIcon.ZdoKey, "") ?? "",
                        HasPhoto = photo != null && photo.Length > 0,
                        PhotoBytes = photo != null ? photo.Length : 0,
                    };
                    if (e.HasPhoto && savePhotosFor != null && savePhotosFor.Contains(zdo.m_uid))
                    {
                        string photoFile = $"{safeName}.{e.Id.Replace(':', '_')}.jpg";
                        File.WriteAllBytes(Path.Combine(SnapshotDir, photoFile), photo);
                        e.PhotoFile = photoFile;
                        photos++;
                    }
                    file.Portals.Add(e);
                }

                string json = JsonConvert.SerializeObject(file, Formatting.Indented);
                string path = Path.Combine(SnapshotDir, safeName + ".json");
                WriteAtomic(path, json);
                PruneAutoSnapshots();
                Plugin.Log.LogInfo($"TortalPortal: snapshot '{safeName}' written ({file.Portals.Count} portal(s){(photos > 0 ? $", {photos} photograph(s) kept beside it" : "")}).");
                return $"ok - snapshot '{safeName}' saved ({file.Portals.Count} portal(s)).";
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: taking a snapshot failed. Reason: {ex}");
                return $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        private static void WriteAtomic(string path, string content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content);
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); }
                catch (PlatformNotSupportedException)
                {
                    File.Delete(path);
                    File.Move(tmp, path);
                }
            }
            else File.Move(tmp, path);
        }

        private static void PruneAutoSnapshots()
        {
            try
            {
                int retain = Mathf.Max(1, Configuration.snapshotRetain.Value);
                if (!Directory.Exists(SnapshotDir)) return;
                List<string> auto = Directory.GetFiles(SnapshotDir, "*.json")
                    .Where(p => Path.GetFileName(p).StartsWith("pre-save-", StringComparison.Ordinal) || Path.GetFileName(p).StartsWith("pre-repair-", StringComparison.Ordinal))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ToList();
                foreach (string stale in auto.Skip(retain))
                {
                    try
                    {
                        string stem = Path.GetFileNameWithoutExtension(stale);
                        File.Delete(stale);
                        foreach (string photo in Directory.GetFiles(SnapshotDir, stem + ".*.jpg")) File.Delete(photo);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"TortalPortal: pruning old snapshots failed (non-fatal). Reason: {ex.Message}");
            }
        }

        public static string[] ListSnapshots()
        {
            try
            {
                if (!Directory.Exists(SnapshotDir)) return Array.Empty<string>();
                return Directory.GetFiles(SnapshotDir, "*.json").Select(Path.GetFileNameWithoutExtension).OrderByDescending(n => n, StringComparer.Ordinal).ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        public static string Restore(string name, bool recreate)
        {
            if (ZDOMan.instance == null) return "tortal_restore: no world loaded.";
            if (string.IsNullOrEmpty(name)) return "tortal_restore: which snapshot? tortal_snapshots lists them.";
            string path = Path.Combine(SnapshotDir, Sanitize(name) + ".json");
            if (!File.Exists(path)) return $"tortal_restore: no snapshot named '{name}' in {SnapshotDir}.";

            try
            {
                SnapshotFile file = JsonConvert.DeserializeObject<SnapshotFile>(File.ReadAllText(path));
                if (file == null) return "tortal_restore: the snapshot file could not be parsed.";
                if (ZNet.instance != null && file.WorldUid != 0 && file.WorldUid != ZNet.instance.GetWorldUID())
                {
                    return $"tortal_restore: refused - that snapshot was taken on a different world (uid {file.WorldUid}, this one is {ZNet.instance.GetWorldUID()}).";
                }

                Dictionary<string, ZDO> byId = new Dictionary<string, ZDO>(StringComparer.Ordinal);
                foreach (ZDO zdo in PlacementPatch.GetAllPortals())
                {
                    if (zdo != null && zdo.IsValid()) byId[PositionId(zdo.GetPosition())] = zdo;
                }

                int rewritten = 0, unchanged = 0, missing = 0, recreated = 0, photos = 0;
                foreach (SnapshotEntry e in file.Portals)
                {
                    if (!byId.TryGetValue(e.Id, out ZDO zdo))
                    {
                        if (!recreate)
                        {
                            missing++;
                            continue;
                        }
                        // An ordinary portal again: no kind, no server author, removable by its builder.
                        zdo = PortalMint.Create(new Vector3(e.X, e.Y, e.Z), Quaternion.Euler(0f, e.RotY, 0f), e.Name, "", 0L, immune: false, lockRemoval: false);
                        if (zdo == null)
                        {
                            missing++;
                            continue;
                        }
                        byId[e.Id] = zdo;
                        recreated++;
                    }

                    bool changed = false;
                    Vector3 pos = zdo.GetPosition();
                    Vector3 want = new Vector3(e.X, e.Y, e.Z);
                    ServerPeers.ClaimAndWrite(zdo, z =>
                    {
                        if ((z.GetString(ZDOVars.s_tag, "") ?? "") != e.Name) { z.Set(ZDOVars.s_tag, e.Name); changed = true; }
                        if (z.GetLong(ZDOVars.s_creator, 0L) != e.Creator) { z.Set(ZDOVars.s_creator, e.Creator); changed = true; }
                        if ((z.GetString(ZDOVars.s_creatorName, "") ?? "") != e.CreatorName) { z.Set(ZDOVars.s_creatorName, e.CreatorName); changed = true; }
                        if ((z.GetString("PortalPIN", "") ?? "") != e.Pin) { z.Set("PortalPIN", e.Pin); changed = true; }
                        if ((z.GetString(TortalPinIcon.ZdoKey, "") ?? "") != e.Icon) { z.Set(TortalPinIcon.ZdoKey, e.Icon); changed = true; }
                        if ((pos - want).sqrMagnitude > 0.01f) { z.SetPosition(want); changed = true; }

                        if (!string.IsNullOrEmpty(e.PhotoFile))
                        {
                            byte[] current = z.GetByteArray("PortalSnapshot");
                            string photoPath = Path.Combine(SnapshotDir, e.PhotoFile);
                            if ((current == null || current.Length == 0) && File.Exists(photoPath))
                            {
                                z.Set("PortalSnapshot", File.ReadAllBytes(photoPath));
                                photos++;
                                changed = true;
                            }
                        }
                    });
                    if (changed) rewritten++;
                    else unchanged++;
                }

                Scan();
                return $"tortal_restore '{name}': {rewritten} portal(s) rewritten, {unchanged} already matched, {missing} missing{(recreate ? "" : " (not recreated; add --recreate)")}, {recreated} recreated{(photos > 0 ? $", {photos} photograph(s) put back" : "")}.";
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: restore failed. Reason: {ex}");
                return $"tortal_restore: failed - {ex.GetType().Name}: {ex.Message}";
            }
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        // ---- commands ------------------------------------------------------------------------------------------

        // One entry point for every verb, whether typed at the server's own console or routed from a client's.
        public static string Execute(string line)
        {
            try
            {
                string[] parts = (line ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return "tortal: nothing to do.";
                string verb = parts[0].ToLowerInvariant();
                bool apply = parts.Skip(1).Any(p => p.Equals("--apply", StringComparison.OrdinalIgnoreCase));
                bool recreate = parts.Skip(1).Any(p => p.Equals("--recreate", StringComparison.OrdinalIgnoreCase));
                string firstArg = parts.Skip(1).FirstOrDefault(p => !p.StartsWith("--", StringComparison.Ordinal));

                switch (verb)
                {
                    case "tortal_health":
                        Scan();
                        LogSummary("on-demand scan");
                        return Report();
                    case "tortal_repair":
                        if (_lastScanUtc == DateTime.MinValue) Scan();
                        return Run(apply);
                    case "tortal_snapshot":
                    {
                        string r = TakeSnapshot(firstArg, null);
                        return r.StartsWith("ok", StringComparison.Ordinal) ? "tortal_snapshot: " + r.Substring(5) : "tortal_snapshot: failed - " + r;
                    }
                    case "tortal_snapshots":
                    {
                        string[] names = ListSnapshots();
                        return names.Length == 0 ? $"tortal_snapshots: none yet (they go in {SnapshotDir})." : $"tortal_snapshots: {names.Length} in {SnapshotDir}\n" + string.Join("\n", names);
                    }
                    case "tortal_restore":
                        return Restore(firstArg, recreate);
                    default:
                        return $"tortal: unknown verb '{verb}'.";
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: '{line}' failed. Reason: {ex}");
                return $"{line}: failed - see the server log.";
            }
        }

        private static void RPC_Ops(long sender, string line)
        {
            try
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                if (!ServerPeers.IsAdminPeer(sender, out string host))
                {
                    Plugin.Log.LogWarning($"TortalPortal: refused '{line}' from {host} - not on the adminlist.");
                    ZRoutedRpc.instance.InvokeRoutedRPC(sender, OpsReplyRpc, $"{line}: the server refused - you are not on its adminlist.");
                    return;
                }
                Plugin.Log.LogInfo($"TortalPortal: '{line}' via {host}.");
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, OpsReplyRpc, Execute(line));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(RPC_Ops)} failed (non-fatal). Reason: {ex}");
            }
        }

        private static void RPC_OpsReply(long sender, string text)
        {
            try
            {
                if (Console.instance != null) Console.instance.Print(text);
                Plugin.Log.LogInfo($"TortalPortal: {text}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(RPC_OpsReply)} failed (non-fatal). Reason: {ex}");
            }
        }

        [HarmonyPatch(typeof(Terminal), "InitTerminal")]
        [HarmonyPostfix]
        public static void Terminal_InitTerminal_Postfix()
        {
            try
            {
                if (_registered) return;
                _registered = true;

                Register("tortal_health", "scans every portal in the world for trouble - buried gates, oversized photographs, unknown icons, players over the cap, stray corpse gates - and lists what it found.");
                Register("tortal_repair", "[--apply] - fixes what tortal_health found. Without --apply it only reports what it would do; with --apply it writes, after taking a snapshot.");
                Register("tortal_snapshot", "[name] - saves every portal's name, PIN, icon, builder and position to BepInEx/config/TortalPortal/snapshots.");
                Register("tortal_snapshots", "lists the saved snapshots.");
                Register("tortal_restore", "<name> [--recreate] - puts every portal back the way the snapshot has it; --recreate also raises portals that have since vanished.");

                Plugin.Log.LogInfo("TortalPortal: console commands tortal_health / tortal_repair / tortal_snapshot / tortal_snapshots / tortal_restore registered.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not register the portal health console commands. Reason: {ex}");
            }
        }

        private static void Register(string command, string description)
        {
            new Terminal.ConsoleCommand(command, description,
                delegate (Terminal.ConsoleEventArgs args) { RunCommand(args); },
                isCheat: false, isNetwork: false, onlyServer: false);
        }

        private static void RunCommand(Terminal.ConsoleEventArgs args)
        {
            try
            {
                string line = args != null ? (args.FullLine ?? "") : "";
                if (ZNet.instance == null || ZDOMan.instance == null)
                {
                    Say(args, "tortal: no world loaded.");
                    return;
                }

                if (ZNet.instance.IsServer())
                {
                    Say(args, Execute(line));
                    return;
                }

                // A client only holds the portals loaded around it; the server holds them all. Ask it, and the
                // answer prints here when it comes back.
                Say(args, "Asking the server...");
                ZRoutedRpc.instance?.InvokeRoutedRPC(OpsRpc, line);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: the console command failed. Reason: {ex}");
                Say(args, "tortal: failed - see the log.");
            }
        }

        private static void Say(Terminal.ConsoleEventArgs args, string msg)
        {
            // A dedicated server has a Terminal but nothing to print to, so everything is mirrored to the log.
            try { if (args != null && args.Context != null) args.Context.AddString(msg); } catch { }
            Plugin.Log.LogInfo($"TortalPortal: {msg}");
        }
    }
}
