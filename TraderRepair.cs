using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TortalPortal
{
    // Part three of the ground-snap fix, and the only part that can reach a trader who is ALREADY buried.
    //
    // SnapGuard makes sure a snap happens against finished terrain. It cannot help with the one case that
    // is invisible from inside a snap: terrain that is present and settled at the moment she is placed, and
    // is then EDITED afterwards by a TerrainComp that had not streamed in yet. The heightmap looks finished
    // because it is finished - it is simply about to change. Nothing can detect that prospectively.
    //
    // It also cannot help with the traders already standing in the ground on a live world. That position is
    // in her ZDO, in the world file, and it will be handed to every player who arrives until something
    // writes a better one.
    //
    // So this watches her for a short while after she appears and corrects her if she ends up under the
    // ground. Only ever DOWN-to-UP: a trader legitimately standing above the terrain (a platform, a
    // player-built floor) is left alone, because the raycast mask is "terrain" only and lifting her to it
    // would be the bug rather than the fix.
    //
    // Ownership: Snap only writes the corrected height to the ZDO if this peer owns her ZNetView, so a
    // correction by a non-owner would look right locally and be undone by the owner's next sync. Ownership
    // is claimed before correcting, which is safe precisely because the trigger is so narrow - she is
    // measurably under the ground, every peer computes the same answer, and the check stops the moment it
    // succeeds.
    [HarmonyPatch]
    public static class TraderRepair
    {
        // How far under the terrain she has to be before this is willing to call it wrong. A trader stands
        // on the ground, so anything approaching half a metre down is not a slope or a rounding difference.
        private const float BuriedThreshold = 0.5f;

        // How long she is watched, and how often. The window is generous because a late TerrainComp is
        // exactly the case this exists for - the whole point is that her ground can change seconds after
        // she is standing on it. It stops early the moment a correction is made.
        private const float WatchSeconds = 30f;
        private const float PollSeconds = 1f;

        private static readonly HashSet<Trader> Watched = new HashSet<Trader>();

        [HarmonyPatch(typeof(Trader), "Start")]
        [HarmonyPostfix]
        public static void Trader_Start_Postfix(Trader __instance)
        {
            try
            {
                if (!Configuration.repairSunkenTraders.Value) return;
                if (__instance == null || Plugin.Instance == null) return;
                if (__instance.GetComponent<SnapToGround>() == null) return; // not one of the three
                if (!Watched.Add(__instance)) return;

                Plugin.Instance.StartCoroutine(WatchRoutine(__instance));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not start watching a trader's footing (non-fatal). Reason: {ex}");
            }
        }

        private static IEnumerator WatchRoutine(Trader trader)
        {
            float deadline = Time.realtimeSinceStartup + WatchSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForSeconds(PollSeconds);

                if (trader == null) break;
                if (!Configuration.repairSunkenTraders.Value) break;
                if (TryRepair(trader)) break;
            }
            if (trader != null) Watched.Remove(trader);
        }

        // Returns true when a correction was made, which ends the watch.
        private static bool TryRepair(Trader trader)
        {
            try
            {
                if (ZoneSystem.instance == null) return false;

                Vector3 pos = trader.transform.position;

                // No terrain under her means the raycast below would miss and report her own height back,
                // which reads as "not buried" and would end the watch on a question that was never asked.
                if (!SnapGuard.GroundReady(pos)) return false;

                float ground = ZoneSystem.instance.GetGroundHeight(pos);
                SnapToGround snapper = trader.GetComponent<SnapToGround>();
                if (snapper == null) return true; // nothing to correct with; stop asking

                float wanted = ground + snapper.m_offset;
                if (wanted - pos.y < BuriedThreshold) return false; // standing on it, or above it - leave her

                ZNetView nview = trader.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid() && !nview.IsOwner()) nview.ClaimOwnership();

                // Vanilla's own placement, so the correction and the original are the same operation - and
                // Snap is what writes it into the ZDO, which is what makes it stick for everybody.
                snapper.Snap();

                Plugin.Log.LogInfo(
                    $"TortalPortal: {trader.name} was standing {wanted - pos.y:0.0}m under the ground at " +
                    $"({pos.x:0},{pos.z:0}) and has been put back on it.");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not check a trader's footing (non-fatal, she is left as she is). Reason: {ex}");
                return true;
            }
        }
    }
}
