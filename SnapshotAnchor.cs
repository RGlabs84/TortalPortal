using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TortalPortal
{
    // Lets the snapshot camera photograph a portal NOBODY is standing near, by briefly loading the world
    // around it on the hosting player's own client - phase one of the portal anchor.
    //
    // Why this takes two hooks and not one: the world only exists around players. Terrain is built by
    // ZoneSystem for zones near the reference position, and objects (the portal itself, trees, buildings)
    // are instantiated by ZNetScene from ZDOs in sectors near that same position - everything else is
    // destroyed again every tick by RemoveObjects. An anchor therefore has to do both halves:
    //
    //   1. Terrain: the snapshot coroutine calls PokeZones() while it waits. PokeLocalZone is vanilla's
    //      own primitive - it resets the 4s TTL on zones that exist and builds the ones that do not - so
    //      the anchored zones live exactly as long as they are poked and expire on their own afterwards.
    //   2. Objects: the CreateObjects prefix below appends the anchor's sector ZDOs to the frame's list.
    //      The SAME list instance flows on into RemoveObjects, so one append both spawns the anchored
    //      objects and earmarks them as keep - the moment the anchor clears, the very next tick sweeps
    //      them away through the ordinary vanilla path, and the zones TTL out 4 seconds later.
    //
    // Host only in practice: a client of a dedicated server is never sent ZDOs for distant sectors, so it
    // has nothing to anchor to - the pass in PlacementPatch checks IsServer() and this class never
    // activates there. Phase two (a server-side sector subscription) is what would lift that.
    [HarmonyPatch]
    public static class SnapshotAnchor
    {
        // 1 ring = a 3x3 block of zones, 192m across. Vanilla's active area is wider, but the shot is
        // taken at eye level two metres from the gate and the fog curtains off the edge long before 96m,
        // so the wider block would cost load time and buy nothing visible.
        private const int AnchorRing = 1;

        private static bool _active;
        private static Vector3 _position;

        public static bool IsActive => _active;

        public static void Set(Vector3 position)
        {
            _position = position;
            _active = true;
        }

        public static void Clear()
        {
            _active = false;
        }

        // Called by the waiting snapshot coroutine every tick, mirroring what vanilla's CreateLocalZones
        // does around the player. At most one NEW zone is built per call: SpawnZone is synchronous and a
        // fresh zone can cost tens of milliseconds, so the 3x3 is spread across ticks instead of landing
        // on a single frame. Pokes to zones that already exist are a dictionary lookup and a TTL reset.
        public static void PokeZones()
        {
            if (!_active || ZoneSystem.instance == null) return;
            try
            {
                Vector2i center = ZoneSystem.GetZone(_position);
                for (int y = center.y - AnchorRing; y <= center.y + AnchorRing; y++)
                {
                    for (int x = center.x - AnchorRing; x <= center.x + AnchorRing; x++)
                    {
                        if (ZoneSystem.instance.PokeLocalZone(new Vector2i(x, y))) return;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: anchoring zones for a remote snapshot failed (non-fatal). Reason: {ex}");
                _active = false;
            }
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.CreateObjects))]
        [HarmonyPrefix]
        public static void ZNetScene_CreateObjects_Prefix(List<ZDO> currentNearObjects)
        {
            if (!_active) return;
            try
            {
                if (ZDOMan.instance == null || ZoneSystem.instance == null || ZNet.instance == null) return;

                // When the anchor sits close enough to the player that the sector lists could overlap,
                // vanilla is already loading it - appending would put the same ZDOs in the list twice.
                Vector2i anchorZone = ZoneSystem.GetZone(_position);
                Vector2i refZone = ZoneSystem.GetZone(ZNet.instance.GetReferencePosition());
                int overlap = ZoneSystem.instance.m_activeArea + AnchorRing;
                if (Mathf.Abs(anchorZone.x - refZone.x) <= overlap &&
                    Mathf.Abs(anchorZone.y - refZone.y) <= overlap)
                {
                    return;
                }

                ZDOMan.instance.FindSectorObjects(anchorZone, AnchorRing, 0, currentNearObjects);
            }
            catch (Exception ex)
            {
                // Fail closed: with the anchor down, the next RemoveObjects tick cleans up anything this
                // patch had spawned, and the world is back to vanilla.
                _active = false;
                Plugin.Log.LogError($"TortalPortal: feeding anchored objects to ZNetScene failed (non-fatal). Reason: {ex}");
            }
        }
    }
}
