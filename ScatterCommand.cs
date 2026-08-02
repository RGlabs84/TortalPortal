using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TortalPortal
{
    // Test scaffolding: fills a world with portals so the destination list, the map markers, the filter box,
    // the favorites strip and the PIN flow can all be exercised without spending an evening building gates.
    //
    // Portals are raised by instantiating the real prefab rather than hand-rolling a ZDO. The prefab's own
    // ZNetView.Awake is what sets persistence, object type, prefab hash and rotation correctly, and a
    // hand-built ZDO that gets any of those wrong is a portal that vanishes on world reload or never
    // replicates. The instance is culled by ZNetScene moments later because it is nowhere near the player -
    // and that is fine, because RemoveObjects keeps a PERSISTENT ZDO and destroys only the GameObject. The
    // ZDO is the portal as far as the rest of the world is concerned.
    //
    // Everything raised this way carries a marker key so tortal_scatterclear can find its own litter again
    // and nobody's real portals are ever at risk.
    [HarmonyPatch]
    public static class ScatterCommand
    {
        private const string ScatterKey = "TortalScattered";
        private const string ScatterPin = "1234";

        // One in this many gets a PIN, so the locked-portal paths (the inline row, the quick PIN window,
        // the wrong-PIN message) all have something to be tested against.
        private const int LockEvery = 5;

        // Far enough apart that no two land in the same zone, so they read as separate places on the map.
        private const float MinSeparation = 64f;

        private const int MaxPerCommand = 500;
        private const int PlacementAttempts = 40;

        // Instantiating a portal builds a whole rune gateway on it through our own Awake patch. Doing four
        // hundred of those in one frame is a visible freeze, so the work is spread across frames and the
        // culling gets to keep pace with it.
        private const int PerFrame = 4;

        private static bool _registered;
        private static Coroutine _running;

        [HarmonyPatch(typeof(Terminal), "InitTerminal")]
        [HarmonyPostfix]
        public static void Terminal_InitTerminal_Postfix()
        {
            try
            {
                if (_registered) return;
                _registered = true;

                new Terminal.ConsoleCommand("tortal_scatter",
                    "[count] [radius] - raises test portals at random spots across the world (default 25 within 2000m). Every " + LockEvery + "th is PIN-locked with " + ScatterPin + ".",
                    delegate (Terminal.ConsoleEventArgs args) { RunScatter(args); },
                    isCheat: true, isNetwork: false, onlyServer: false);

                new Terminal.ConsoleCommand("tortal_scatterclear",
                    "removes every portal raised by tortal_scatter, leaving real ones alone.",
                    delegate (Terminal.ConsoleEventArgs args) { RunClear(args); },
                    isCheat: true, isNetwork: false, onlyServer: false);

                Plugin.Log.LogInfo("TortalPortal: console commands tortal_scatter / tortal_scatterclear registered.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not register the scatter console commands. Reason: {ex}");
            }
        }

        private static void Say(Terminal.ConsoleEventArgs args, string msg)
        {
            // A dedicated server has a Terminal but nothing to print to, so everything is mirrored to the log.
            try { if (args != null && args.Context != null) args.Context.AddString(msg); } catch { }
            Plugin.Log.LogInfo($"TortalPortal: {msg}");
        }

        private static void RunScatter(Terminal.ConsoleEventArgs args)
        {
            try
            {
                if (ZNetScene.instance == null || ZDOMan.instance == null || WorldGenerator.instance == null)
                {
                    Say(args, "tortal_scatter: no world loaded.");
                    return;
                }
                if (_running != null)
                {
                    Say(args, "tortal_scatter: already scattering - let it finish first.");
                    return;
                }

                int count = 25;
                float radius = 2000f;
                if (args.Length > 1) int.TryParse(args[1], out count);
                if (args.Length > 2) float.TryParse(args[2], out radius);

                count = Mathf.Clamp(count, 1, MaxPerCommand);
                radius = Mathf.Clamp(radius, 50f, 10000f);

                GameObject prefab = ZNetScene.instance.GetPrefab("portal_wood");
                if (prefab == null)
                {
                    Say(args, "tortal_scatter: could not find the portal_wood prefab.");
                    return;
                }

                Say(args, $"tortal_scatter: raising {count} portals within {radius:0}m...");
                _running = Plugin.Instance.StartCoroutine(ScatterRoutine(args, prefab, count, radius));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: tortal_scatter failed. Reason: {ex}");
                Say(args, "tortal_scatter: failed - see the log.");
            }
        }

        private static IEnumerator ScatterRoutine(Terminal.ConsoleEventArgs args, GameObject prefab, int count, float radius)
        {
            List<Vector3> placed = new List<Vector3>();
            string creator = "Scattered";
            try
            {
                if (Player.m_localPlayer != null) creator = Player.m_localPlayer.GetPlayerName();
            }
            catch { }

            int made = 0, locked = 0, skipped = 0;

            for (int i = 0; i < count; i++)
            {
                Vector3 pos;
                if (!FindSpot(radius, placed, out pos))
                {
                    skipped++;
                    continue;
                }

                if (Raise(prefab, pos, made + 1, creator, (made + 1) % LockEvery == 0))
                {
                    placed.Add(pos);
                    made++;
                    if ((made) % LockEvery == 0) locked++;
                }
                else skipped++;

                if (made % PerFrame == 0) yield return null;
            }

            _running = null;
            Say(args, $"tortal_scatter: raised {made} portal(s), {locked} of them PIN-locked with {ScatterPin}" +
                      (skipped > 0 ? $" ({skipped} spot(s) rejected as water or too crowded)." : "."));
            Say(args, "The destination list refreshes on the server's next index publish - within PortalIndexRefreshSeconds.");
        }

        // A random spot on dry land, far enough from the ones already used.
        //
        // Height comes from WorldGenerator, which is pure maths and works anywhere in the world. The obvious
        // alternative, ZoneSystem.GetGroundHeight, is a physics raycast against a loaded Heightmap - and
        // nothing is loaded out where these are going, so it would silently hand back the Y we passed in and
        // bury every portal at sea level.
        private static bool FindSpot(float radius, List<Vector3> placed, out Vector3 pos)
        {
            pos = Vector3.zero;
            float water = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;

            for (int attempt = 0; attempt < PlacementAttempts; attempt++)
            {
                // sqrt on the radius keeps the spread even across the disc instead of crowding the middle.
                float ang = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float r = Mathf.Sqrt(UnityEngine.Random.value) * radius;
                float x = Mathf.Cos(ang) * r;
                float z = Mathf.Sin(ang) * r;

                float y = WorldGenerator.instance.GetHeight(x, z);
                if (y <= water + 1f) continue; // in the sea

                Vector3 candidate = new Vector3(x, y + 0.05f, z);

                bool crowded = false;
                foreach (Vector3 p in placed)
                {
                    if (Vector3.Distance(p, candidate) < MinSeparation) { crowded = true; break; }
                }
                if (crowded) continue;

                pos = candidate;
                return true;
            }
            return false;
        }

        private static bool Raise(GameObject prefab, Vector3 pos, int number, string creator, bool locked)
        {
            GameObject go = null;
            try
            {
                Quaternion rot = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                go = UnityEngine.Object.Instantiate(prefab, pos, rot);

                ZNetView nview = go.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid())
                {
                    if (go != null) UnityEngine.Object.Destroy(go);
                    return false;
                }

                ZDO zdo = nview.GetZDO();

                Heightmap.Biome biome = Heightmap.Biome.None;
                try { biome = WorldGenerator.instance.GetBiome(pos.x, pos.z); } catch { }

                // Named by biome so the list, the filter box and the biome column all have something real to
                // chew on, and numbered so the ordering is obvious at a glance.
                zdo.Set(ZDOVars.s_tag, $"{biome} Gate {number:000}");
                zdo.Set(ZDOVars.s_creatorName, creator);
                if (locked) zdo.Set("PortalPIN", ScatterPin);

                // The marker that makes this reversible. Without it, clearing up afterwards would mean
                // guessing which portals were test litter and which someone had actually built.
                zdo.Set(ScatterKey, true);

                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not raise a scattered portal at {pos}. Reason: {ex}");
                if (go != null) UnityEngine.Object.Destroy(go);
                return false;
            }
        }

        private static void RunClear(Terminal.ConsoleEventArgs args)
        {
            try
            {
                if (ZDOMan.instance == null)
                {
                    Say(args, "tortal_scatterclear: no world loaded.");
                    return;
                }

                bool authoritative = ZNet.instance != null && ZNet.instance.IsServer();

                int removed = 0;
                foreach (ZDO zdo in PlacementPatch.GetAllPortals())
                {
                    if (zdo == null || !zdo.IsValid()) continue;
                    if (!zdo.GetBool(ScatterKey, false)) continue;

                    try
                    {
                        // If the portal happens to be loaded, let ZNetScene take it down - that removes the
                        // instance and the ZDO together. Otherwise the ZDO has to be claimed first, because
                        // DestroyZDO is a silent no-op on something this peer does not own.
                        ZNetView live = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(zdo) : null;
                        if (live != null && live.IsValid())
                        {
                            ZNetScene.instance.Destroy(live.gameObject);
                        }
                        else
                        {
                            zdo.SetOwner(ZDOMan.GetSessionID());
                            ZDOMan.instance.DestroyZDO(zdo);
                        }
                        removed++;
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogError($"TortalPortal: could not remove scattered portal {zdo.m_uid}. Reason: {ex}");
                    }
                }

                Say(args, $"tortal_scatterclear: removed {removed} scattered portal(s).");
                if (!authoritative)
                {
                    Say(args, "Run this on the host - a client only knows about the portals loaded around it, so the rest are still out there.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: tortal_scatterclear failed. Reason: {ex}");
                Say(args, "tortal_scatterclear: failed - see the log.");
            }
        }
    }
}
