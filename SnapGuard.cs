using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace TortalPortal
{
    // Stops Haldor, Hildir and the Bog Witch being set into the ground under their own huts.
    //
    // THE VANILLA MECHANISM, because none of this makes sense without it.
    //
    // SnapToGround is how a handful of objects find the ground, and there are exactly four prefabs in the
    // game carrying it: the three traders, and dvergrprops_wood_stakewall. It keeps ONE global static list
    // and drains it whole:
    //
    //     Awake()      adds this snapper to m_allSnappers
    //     Snap()       raycasts down from y=6000 against the "terrain" layer ONLY - never buildings - and
    //                  sets y to what it hit. If this peer owns the ZNetView it also writes that position
    //                  into the ZDO, where it persists in the world file and replicates to everyone.
    //     SnappAll()   Heightmap.ForceGenerateAll(), then Snap()s EVERY registered snapper and CLEARS the
    //                  list.
    //
    // A trader gets snapped twice. Trader.Start calls Snap() DIRECTLY, one frame after she is instantiated
    // - bypassing ForceGenerateAll entirely - and at that moment her zone is almost always still streaming,
    // so that first snap is very often wrong. Snap does not deregister her, so her correction is the next
    // SnappAll from anywhere in the world: that one runs ForceGenerateAll first, and in an unmodded game it
    // is fired by her own camp's SpawnLocation, with the ground beneath her freshly built. Then the list is
    // cleared and she never gets another.
    //
    // So the failure is not one bad snap. It is a bad first snap whose correction is spent on a drain that
    // fires while the ground is still not right - after which she is out of the list, and the wrong height
    // is in her ZDO.
    //
    // WHAT THIS FIXES, in three parts. The mask being "terrain" alone is what makes the first two safe:
    // with no heightmap the raycast simply misses and Snap leaves the object exactly where it was, so
    // "not ready" is never worse than "not snapped".
    //
    //   1. Snap prefix   - a heightmap with a rebuild still queued IS the pre-edit surface, which is
    //                      precisely the wrong answer. Finish the rebuild first, then let vanilla snap.
    //                      This alone repairs most of Trader.Start's bad first snap.
    //   2. SnappAll      - snap the ones whose ground is actually there, and leave the rest REGISTERED
    //      prefix         rather than clearing the list out from under them. Their turn still comes.
    //   3. TraderRepair  - the case neither of the above can see: terrain that arrives correct and settled,
    //                      and is then edited by a TerrainComp that had not streamed in yet. Nothing can
    //                      detect that prospectively, so it is watched for and corrected afterwards.
    //
    // Note this is no longer scoped to our own remote loading. It was, briefly, and that was the weaker
    // idea: "is this snapper inside the block my anchor just built" is only ever a proxy for "is the ground
    // under this snapper actually finished", and the second question can simply be asked directly. Asking
    // it directly also fixes the vanilla race, and every other mod's version of it, rather than only ours.
    [HarmonyPatch]
    public static class SnapGuard
    {
        // How long a snapper may be held back before it is snapped regardless. Vanilla guarantees every
        // snapper is placed by the next drain; deferring without a ceiling would quietly turn that into
        // "maybe never" for anything whose zone never loads. Ten seconds is far longer than a zone takes to
        // settle and far shorter than a player notices.
        private const float DeferGraceSeconds = 10f;

        // Reflection, because m_allSnappers and m_inList are private and there is no public way to take one
        // snapper out of that list. Resolved once; either being null stands the whole guard down.
        private static readonly FieldInfo AllSnappersField = AccessTools.Field(typeof(SnapToGround), "m_allSnappers");
        private static readonly FieldInfo InListField = AccessTools.Field(typeof(SnapToGround), "m_inList");

        private static readonly Dictionary<SnapToGround, float> DeferredSince = new Dictionary<SnapToGround, float>();
        private static readonly List<SnapToGround> DeferPurge = new List<SnapToGround>();

        private static bool _disabled;
        private static bool _disabledAnnounced;

        // Is there terrain under this point that is finished being built? Returns false only when there is
        // no heightmap at all - a queued rebuild is FINISHED here rather than refused, because the data for
        // it has already arrived and completing it is exactly what makes the answer correct.
        internal static bool GroundReady(Vector3 pos)
        {
            Heightmap hmap = Heightmap.FindHeightmap(pos);
            if (hmap == null) return false;
            if (hmap.HaveQueuedRebuild()) hmap.Regenerate();
            return true;
        }

        // Part 1. Trader.Start snaps directly, so this is the only place that call can be reached. Vanilla
        // still does the snapping - all this does is make sure the collider it raycasts is the finished
        // surface and not the one from before this zone's terrain edits were applied.
        [HarmonyPatch(typeof(SnapToGround), nameof(SnapToGround.Snap))]
        [HarmonyPrefix]
        public static void SnapToGround_Snap_Prefix(SnapToGround __instance)
        {
            if (_disabled) return;
            try
            {
                Heightmap hmap = Heightmap.FindHeightmap(__instance.transform.position);
                if (hmap != null && hmap.HaveQueuedRebuild()) hmap.Regenerate();
            }
            catch (Exception ex)
            {
                StandDown(ex.ToString());
            }
        }

        // Part 2.
        [HarmonyPatch(typeof(SnapToGround), nameof(SnapToGround.SnappAll))]
        [HarmonyPrefix]
        public static bool SnapToGround_SnappAll_Prefix()
        {
            if (_disabled) return true;
            try
            {
                if (AllSnappersField == null || InListField == null) return StandDown("SnapToGround changed shape");
                if (!(AllSnappersField.GetValue(null) is List<SnapToGround> snappers)) return StandDown("m_allSnappers was not a list");
                if (snappers.Count == 0) { DeferredSince.Clear(); return false; }

                float now = Time.realtimeSinceStartup;
                int snapped = 0;
                int deferred = 0;
                int forced = 0;

                // Backwards, because the ones that are snapped come out of the list as we go.
                for (int i = snappers.Count - 1; i >= 0; i--)
                {
                    SnapToGround snapper = snappers[i];
                    if (snapper == null) continue; // destroyed without OnDestroy; leave vanilla's own handling to it

                    bool ready = GroundReady(snapper.transform.position);

                    if (!ready)
                    {
                        // Held back - but never indefinitely. Past the grace period it is snapped anyway,
                        // which with no heightmap under it is a raycast that misses and leaves it exactly
                        // where it was: no worse than vanilla, and it stops the list growing forever.
                        if (!DeferredSince.TryGetValue(snapper, out float since))
                        {
                            DeferredSince[snapper] = now;
                            deferred++;
                            continue;
                        }
                        if (now - since < DeferGraceSeconds) { deferred++; continue; }
                        forced++;
                    }

                    snapper.Snap();
                    InListField.SetValue(snapper, false);
                    snappers.RemoveAt(i);
                    DeferredSince.Remove(snapper);
                    snapped++;
                }

                PurgeDeadDeferrals();

                if (deferred > 0 || forced > 0)
                {
                    Configuration.LogDebug(
                        $"TortalPortal: ground-snap pass placed {snapped} object(s), held {deferred} back for ground that " +
                        $"is not built yet{(forced > 0 ? $", and released {forced} past the {DeferGraceSeconds:0}s grace" : "")}.");
                }
                return false;
            }
            catch (Exception ex)
            {
                return StandDown(ex.ToString());
            }
        }

        // Destroyed snappers never come back to be snapped, and their OnDestroy cannot reach this
        // dictionary. Swept on the same pass rather than on a timer - the list is tiny and this is the only
        // place it is walked.
        private static void PurgeDeadDeferrals()
        {
            if (DeferredSince.Count == 0) return;
            DeferPurge.Clear();
            foreach (KeyValuePair<SnapToGround, float> kv in DeferredSince)
            {
                if (kv.Key == null) DeferPurge.Add(kv.Key);
            }
            foreach (SnapToGround dead in DeferPurge) DeferredSince.Remove(dead);
            DeferPurge.Clear();
        }

        // Fail OPEN. Vanilla SnappAll is what shipped before this class existed, and it restores an
        // occasional sunken trader - which is a great deal better than a snapper that is never placed on
        // the ground at all because the guard threw on the way past.
        private static bool StandDown(string reason)
        {
            _disabled = true;
            DeferredSince.Clear();
            if (!_disabledAnnounced)
            {
                _disabledAnnounced = true;
                Plugin.Log.LogWarning(
                    "TortalPortal: the ground-snap guard has stood down and SnappAll is back to vanilla for the rest " +
                    $"of this session - traders may occasionally sink into the ground. Reason: {reason}");
            }
            return true;
        }
    }
}
