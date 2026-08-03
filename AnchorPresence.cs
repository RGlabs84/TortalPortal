using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TortalPortal
{
    // Phase five of the portal anchor: put somebody at the far end.
    //
    // Everything before this made the destination LOOK right - the ground, the trees, the horizon, the
    // sky, the weather. It still looked like a place nobody lived, and the reason is that Valheim only
    // makes wildlife where a player is standing. Not "near a loaded zone" - near a PLAYER. Reading
    // SpawnSystem, the whole of that gate is four lines:
    //
    //   UpdateSpawning()      requires m_nview.IsOwner() and Player.m_localPlayer != null
    //   GetPlayersInZone()    walks Player.GetAllPlayers() and keeps those InsideZone (+-32m)
    //   ...and returns early if that list is empty
    //   FindBaseSpawnPoint()  throws a dart 40-80m around one of those players
    //
    // Nothing downstream cares about players at all: IsSpawnPointGood is pure geometry, biome, tilt
    // and altitude. So a presence in the zone IS the feature, and the honest way to provide one is to
    // tell those two methods the truth about where the camera is - not to fabricate a Player object,
    // which would need a ZNetView and would replicate to every other client as a ghost standing in
    // the Ashlands.
    //
    // WHAT THIS COSTS, stated plainly because it is not a rendering change like everything above it:
    // these are REAL creatures. They are written to the world, they are simulated by whoever is
    // looking, and they outlive the feed that made them. A player browsing an album of eight gates
    // seeds eight neighbourhoods. That is why it is off unless a server turns it on.
    [HarmonyPatch]
    public static class AnchorPresence
    {
        // Zones out from the anchor that count as "somebody is here". Deliberately NOT AnchorZoneRing:
        // that dial goes to 2, which is 25 zones, and every one of them would be stocked with monsters
        // by somebody glancing at a photograph. 1 matches vanilla's own m_activeArea - the ring the
        // game considers a player to be occupying - and 3x3 zones is already wider than the camera sees.
        private const int SpawnRing = 1;

        private static bool _active;
        private static Vector3 _pos;

        public static bool IsActive => _active;
        public static Vector3 Position => _pos;

        public static void Set(Vector3 anchorPos)
        {
            _pos = anchorPos;
            _active = true;
        }

        public static void Clear()
        {
            _active = false;
        }

        // Does this spawner belong to the anchor rather than to the player's own surroundings? Checked
        // on every gate below, so a spawner anywhere else in the world behaves exactly as vanilla.
        private static bool Applies(SpawnSystem system)
        {
            if (!_active || system == null) return false;
            if (!Configuration.anchoredSpawning.Value) return false;
            if (ZoneSystem.instance == null || Player.m_localPlayer == null) return false;

            Vector2i spawnZone = ZoneSystem.GetZone(system.transform.position);
            Vector2i anchorZone = ZoneSystem.GetZone(_pos);
            return Mathf.Abs(spawnZone.x - anchorZone.x) <= SpawnRing &&
                   Mathf.Abs(spawnZone.y - anchorZone.y) <= SpawnRing;
        }

        // Gate 1: ownership. A zone's spawner only runs for whoever owns its control object, and out at
        // the anchor that is the server (or nobody). Claiming it is what makes this client responsible
        // for simulating the place it is looking at - which is the correct answer, since it is the only
        // participant that wants the place populated at all.
        [HarmonyPatch(typeof(SpawnSystem), "UpdateSpawning")]
        [HarmonyPrefix]
        private static void ClaimAnchoredZone(SpawnSystem __instance)
        {
            try
            {
                if (!Applies(__instance)) return;

                ZNetView nview = __instance.m_nview;
                if (nview == null || !nview.IsValid() || nview.IsOwner()) return;
                nview.ClaimOwnership();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: claiming the anchored zone's spawner failed (non-fatal, the destination stays empty). Reason: {ex}");
            }
        }

        // Gate 2: presence. The list is empty out there and an empty list is an early return, so the
        // spawner never even looks. Reporting the local player only when the list came back EMPTY
        // matters: if a real player is standing at the destination, vanilla's answer is already right
        // and the anchor has no business editing it.
        [HarmonyPatch(typeof(SpawnSystem), "GetPlayersInZone")]
        [HarmonyPostfix]
        private static void ReportAnchorAsOccupied(SpawnSystem __instance, List<Player> players)
        {
            try
            {
                if (players == null || players.Count > 0) return;
                if (!Applies(__instance)) return;
                players.Add(Player.m_localPlayer);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: reporting the anchor as occupied failed (non-fatal, the destination stays empty). Reason: {ex}");
            }
        }

        // Gate 3: WHERE. Gate 2 hands the spawner the local player, whose transform is wherever the
        // viewer actually stands - so vanilla would throw its dart around a player on the other side of
        // the world and IsSpawnPointGood would reject every one of them for being the wrong biome.
        // This is vanilla's own loop, verbatim, with the anchor as the centre instead.
        [HarmonyPatch(typeof(SpawnSystem), "FindBaseSpawnPoint")]
        [HarmonyPrefix]
        private static bool AnchoredSpawnPoint(SpawnSystem __instance, SpawnSystem.SpawnData spawn,
                                               out Vector3 spawnCenter, out Player targetPlayer,
                                               ref bool __result)
        {
            spawnCenter = Vector3.zero;
            targetPlayer = null;
            try
            {
                if (!Applies(__instance)) return true; // vanilla, untouched

                float minInclusive = spawn.m_spawnRadiusMin > 0f ? spawn.m_spawnRadiusMin : 40f;
                float maxInclusive = spawn.m_spawnRadiusMax > 0f ? spawn.m_spawnRadiusMax : 80f;

                // Twenty attempts, as vanilla does. Failing all twenty is a normal outcome - it means
                // this creature has nowhere sensible to stand here - and returning false is how the
                // spawner is told to give up on this one and move on.
                for (int i = 0; i < 20; i++)
                {
                    Vector3 dir = Quaternion.Euler(0f, UnityEngine.Random.Range(0, 360), 0f) * Vector3.forward;
                    Vector3 point = _pos + dir * UnityEngine.Random.Range(minInclusive, maxInclusive);
                    if (__instance.IsSpawnPointGood(spawn, ref point))
                    {
                        spawnCenter = point;
                        // Discarded by the only caller, but it must not be left null in case that
                        // ever stops being true.
                        targetPlayer = Player.m_localPlayer;
                        __result = true;
                        return false;
                    }
                }

                __result = false;
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: picking a spawn point at the anchor failed (non-fatal, falling back to vanilla). Reason: {ex}");
                return true;
            }
        }
    }
}
