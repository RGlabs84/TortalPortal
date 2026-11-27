// Into a single-player world without the menus, and the player helpers every phase uses.
// Ported from Sailwright's tools/sailwright-autotest/src/WorldEntry.cs (FindWater dropped).
using System;
using System.Collections;
using Splatform;
using UnityEngine;

namespace TortalAutoTest
{
    public partial class AutoTest
    {
        static PlayerProfile EnsureCharacter()
        {
            var profile = new PlayerProfile(CharacterFile, FileHelpers.FileSource.Local);
            if (!profile.Load())
            {
                profile = new PlayerProfile(CharacterFile, FileHelpers.FileSource.Local);
                profile.SetName("Wardtester");
            }
            profile.m_firstSpawn = false;
            profile.Save();
            return profile;
        }

        IEnumerator PhaseA()
        {
            const string guid = "wubarrk.TortalPortal";
            bool loaded = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(guid);
            Check(loaded, "plugin loaded", loaded ? guid + " " + BepInEx.Bootstrap.Chainloader.PluginInfos[guid].Metadata.Version : "not in Chainloader.PluginInfos");
            foreach (var (type, method) in new[] { (typeof(Character), "RPC_Damage"), (typeof(Player), "UpdateTeleport"), (typeof(Humanoid), "StartAttack"), (typeof(Player), "OnSpawned") })
            {
                var m = HarmonyLib.AccessTools.Method(type, method);
                var info = m != null ? HarmonyLib.Harmony.GetPatchInfo(m) : null;
                int mine = 0;
                if (info != null)
                {
                    foreach (var p in info.Prefixes) if (p.owner == guid) mine++;
                    foreach (var p in info.Postfixes) if (p.owner == guid) mine++;
                }
                Check(mine > 0, $"patch {type.Name}.{method}", m == null ? "method not found" : $"{mine} TortalPortal patch(es)");
            }
            var cfg = TortalPortal.Configuration.corpseRunArrivalWardSeconds;
            Check(cfg != null && Math.Abs((float)cfg.DefaultValue - 20f) < 0.01f, "CorpseRunArrivalWardSeconds default 20",
                cfg == null ? "entry missing" : $"default {cfg.DefaultValue}, value {cfg.Value}");
            yield break;
        }

        IEnumerator EnterWorld()
        {
            inWorld = false;
            var profile = EnsureCharacter();
            Game.SetProfile(profile.GetFilename(), profile.m_fileSource);
            SaveSystem.ClearWorldListCache(true);

            global::World world;
            if (SaveSystem.TryGetSaveByName(WorldName, SaveDataType.World, out var save) && !save.IsDeleted)
            {
                world = global::World.LoadWorld(save);
                if (world.m_dataError != global::World.SaveDataError.None)
                {
                    Result("FAIL", "load world", $"{WorldName}: data error {world.m_dataError}");
                    yield break;
                }
            }
            else
            {
                world = new global::World(WorldName, WorldSeed) { m_fileSource = FileHelpers.FileSource.Local };
                SaveSystem.SetSaveNumber(0u);
                world.SaveWorldFWLData(DateTime.Now);
                Log($"world {WorldName} created (seed {WorldSeed})");
            }

            FejdStartup.instance.m_world = world;
            ZNet.m_onlineBackend = OnlineBackendType.Steamworks;
            ZNet.SetServer(true, false, false, world.m_name, "", world);
            ZNet.ResetServerHost();
            FejdStartup.instance.LoadMainScene();

            float t0 = Time.realtimeSinceStartup;
            yield return WaitFor(() => Player.m_localPlayer != null && ZoneSystem.instance != null && ZoneSystem.instance.IsActiveAreaLoaded()
                                       && !Player.m_localPlayer.IsTeleporting(), 900f);
            if (!waitOk)
            {
                Result("FAIL", "spawn", "no player in a loaded world within 900 s");
                yield break;
            }
            Result("PASS", "spawn", $"player in the world after {Time.realtimeSinceStartup - t0:0} s");
            yield return Sec(8f);

            Player p = Player.m_localPlayer;
            // God mode OFF: the ward is about damage, and god mode would hide it. Ghost mode keeps the wildlife off us.
            p.SetGodMode(false);
            p.SetGhostMode(true);
            inWorld = Player.m_localPlayer != null;
        }

        // ------------------------------------------------------------------------------------------ helpers

        internal static string V(Vector3 v) => $"({v.x.ToString("0.0", IC)}, {v.y.ToString("0.0", IC)}, {v.z.ToString("0.0", IC)})";

        static float Ground(Vector3 p) => ZoneSystem.instance.GetGroundHeight(p);

        static Vector3 OnGround(Vector3 p) => new Vector3(p.x, Ground(p), p.z);

        static void Face(Vector3 dir)
        {
            Player p = Player.m_localPlayer;
            if (p == null || dir.sqrMagnitude < 1e-6f) return;
            var flat = new Vector3(dir.x, 0f, dir.z).normalized;
            p.m_lookYaw = Quaternion.LookRotation(flat);
            p.m_lookPitch = 0f;
            p.SetLookDir(flat);
            p.transform.rotation = Quaternion.LookRotation(flat);
        }

        /// <summary>Puts the player at a point standing still, facing a direction (no teleport: same zone).</summary>
        static IEnumerator PlaceAt(Vector3 at, Vector3 facing)
        {
            Player p = Player.m_localPlayer;
            int solid = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
            at = Physics.Raycast(new Vector3(at.x, Ground(at) + 4f, at.z), Vector3.down, out RaycastHit hit, 8f, solid, QueryTriggerInteraction.Ignore)
                ? hit.point + Vector3.up * 0.2f
                : OnGround(at) + Vector3.up * 0.2f;
            p.transform.position = at;
            p.m_body.position = at;
            p.m_body.linearVelocity = Vector3.zero;
            p.m_currentVel = Vector3.zero;
            Face(facing);
            yield return Sec(0.6f);
            yield return WaitFor(() => p.IsOnGround(), 3f);
            Face(facing);
        }
    }
}
