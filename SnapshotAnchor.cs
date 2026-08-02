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
        // 1 ring = a 3x3 block of zones, 192m across; 2 = 5x5. A synced dial now (AnchorZoneRing), read
        // through Configuration's clamped accessor - ring 3 would be 49 zones and a guaranteed hitch, so
        // the clamp is the game-breaking guard. Shared with the server's SectorSubscription, which must
        // send exactly the sectors this class will try to instantiate; the sync is what keeps the two
        // ends agreeing.
        public static int Ring => Configuration.AnchorRingSafe;

        private static bool _active;
        private static Vector3 _position;

        // The anchor's own horizon. Vanilla draws far mountains and coastlines with TerrainLod - a 2400m
        // carpet of low-res heightmap meshes that follows the PLAYER's camera - so a snapshot taken 5km
        // away sees past its little 3x3 of real zones into black void (painfully obvious from any
        // mountaintop). A second, temporary TerrainLod pinned to the anchor gives the photograph the same
        // horizon the player would see standing there, without ever touching the player's own. Built
        // fresh rather than cloned: the lod meshes are CHILDREN of the component's GameObject, so
        // Instantiate would bring nine orphaned copies along.
        private static TerrainLod _distantLod;

        public static bool IsActive => _active;

        // True once the anchor's horizon meshes have built (they bake on the HeightmapBuilder's worker
        // thread), or when there is no rig at all - absence degrades to the old void horizon, not a stall.
        public static bool DistantTerrainReady =>
            _distantLod == null || _distantLod.m_heightmapState == TerrainLod.HeightmapState.Done;

        public static void Set(Vector3 position)
        {
            _position = position;
            _active = true;
            CreateDistantLod(position);
        }

        public static void Clear()
        {
            _active = false;
            if (_distantLod != null)
            {
                try { UnityEngine.Object.Destroy(_distantLod.gameObject); }
                catch (Exception ex) { Plugin.Log.LogError($"TortalPortal: tearing down the anchor's horizon failed (non-fatal). Reason: {ex}"); }
                _distantLod = null;
            }
        }

        private static void CreateDistantLod(Vector3 position)
        {
            try
            {
                // The scene's own rig donates its settings and material. Absent (menus, odd scenes) the
                // photo simply keeps the void horizon it had before this existed.
                TerrainLod original = null;
                foreach (TerrainLod t in UnityEngine.Object.FindObjectsOfType<TerrainLod>())
                {
                    if (t != _distantLod) { original = t; break; }
                }
                if (original == null) return;

                // Inactive while configuring: OnEnable is what builds the child meshes, and it must run
                // with the copied settings (a default material of null would build nine invisible meshes).
                GameObject go = new GameObject("TortalPortal_AnchorTerrainLod");
                go.SetActive(false);
                TerrainLod lod = go.AddComponent<TerrainLod>();
                lod.m_terrainSize = original.m_terrainSize;
                lod.m_regionsPerAxis = original.m_regionsPerAxis;
                lod.m_vertexDistance = original.m_vertexDistance;
                lod.m_material = original.m_material;
                // Never re-follow the player's camera; this rig belongs to the anchor alone.
                lod.m_updateStepDistance = float.MaxValue;
                go.SetActive(true);

                // Aim it at the anchor and demand one rebuild - the same grid-snap vanilla applies. Its
                // Update then queues the bakes and flips m_heightmapState to Done when the carpet is laid.
                float vd = Mathf.Max(1f, Mathf.Round(lod.m_vertexDistance));
                lod.m_lastPoint = new Vector3(Mathf.Round(position.x / vd) * vd, 0f, Mathf.Round(position.z / vd) * vd);
                foreach (TerrainLod.HeightmapWithOffset h in lod.m_heightmaps)
                {
                    h.m_state = TerrainLod.HeightmapState.NeedsRebuild;
                }
                lod.m_heightmapState = TerrainLod.HeightmapState.NeedsRebuild;

                _distantLod = lod;
            }
            catch (Exception ex)
            {
                // The shot can still be taken - it just gets the old empty horizon.
                _distantLod = null;
                Plugin.Log.LogError($"TortalPortal: building the anchor's horizon failed (non-fatal, photo will lack distant terrain). Reason: {ex}");
            }
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
                int ring = Ring;
                Vector2i center = ZoneSystem.GetZone(_position);
                for (int y = center.y - ring; y <= center.y + ring; y++)
                {
                    for (int x = center.x - ring; x <= center.x + ring; x++)
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
                int overlap = ZoneSystem.instance.m_activeArea + Ring;
                if (Mathf.Abs(anchorZone.x - refZone.x) <= overlap &&
                    Mathf.Abs(anchorZone.y - refZone.y) <= overlap)
                {
                    return;
                }

                ZDOMan.instance.FindSectorObjects(anchorZone, Ring, 0, currentNearObjects);
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
