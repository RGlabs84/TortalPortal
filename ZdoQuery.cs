using System;
using System.Collections.Generic;
using UnityEngine;

namespace TortalPortal
{
    // Nearby-object lookups against ZDOMan's own sector data, and a resumable whole-map sweep - never
    // Physics.OverlapSphere. The server holds no collider for anything outside the single point
    // Game.FixedUpdate pins its reference position to, so a physics query there answers nothing; the sector
    // lists hold every ZDO in the world whether or not anything is instantiated.
    //
    // Two shapes:
    //  - FindNear: anchored to one known position. A handful of sectors, cheap enough to run per portal.
    //  - PrefabSetSweeper: "every tombstone on the map" - budgeted and resumable so a full pass never costs
    //    a frame spike, and it never completes so much as cycles, which is what a background sweep wants.
    public static class ZdoQuery
    {
        // Every ZDO within roughly radius metres of worldPos. FindSectorObjects only narrows to whole 64m
        // sectors (the ring count is rounded up and ring 0 still covers the containing sector), so this always
        // post-filters to the real distance.
        public static List<ZDO> FindNear(Vector3 worldPos, float radius, List<ZDO> reuseBuffer = null)
        {
            List<ZDO> result = reuseBuffer ?? new List<ZDO>();
            result.Clear();
            if (ZDOMan.instance == null || ZoneSystem.instance == null) return result;

            int ring = Mathf.Max(1, Mathf.CeilToInt(radius / ZoneSystem.c_ZoneSize));
            List<ZDO> raw = new List<ZDO>();
            try
            {
                ZoneCompat.FindSector(ZoneSystem.GetZone(worldPos), ring, 0, raw);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"TortalPortal: sector lookup around {worldPos} failed (non-fatal, treated as empty). Reason: {ex.Message}");
                return result;
            }

            float radiusSqr = radius * radius;
            foreach (ZDO zdo in raw)
            {
                if (zdo == null || !zdo.IsValid()) continue;
                if ((zdo.GetPosition() - worldPos).sqrMagnitude <= radiusSqr) result.Add(zdo);
            }
            return result;
        }

        // One resumable walk of ZDOMan's whole sector array, matching every ZDO against a SET of prefab hashes
        // in a single pass. Reads the publicised ZDOMan.m_objectsBySector directly.
        public sealed class PrefabSetSweeper
        {
            private readonly HashSet<int> _prefabHashes;
            private int _sectorCursor;

            public PrefabSetSweeper(IEnumerable<int> prefabHashes)
            {
                _prefabHashes = new HashSet<int>(prefabHashes);
            }

            // Walks up to sectorBudget NON-EMPTY sectors from where the last call stopped, appending matches to
            // results (not cleared first). Returns true when the cursor ran off the end of the array - a full
            // pass completed - and has been reset for the next call.
            public bool Advance(int sectorBudget, List<ZDO> results)
            {
                if (_prefabHashes.Count == 0 || ZDOMan.instance == null) return false;
                List<ZDO>[] sectors = ZDOMan.instance.m_objectsBySector;
                if (sectors == null) return false;

                int visited = 0;
                while (_sectorCursor < sectors.Length)
                {
                    List<ZDO> sector = sectors[_sectorCursor];
                    _sectorCursor++;
                    if (sector == null) continue;
                    for (int i = 0; i < sector.Count; i++)
                    {
                        ZDO zdo = sector[i];
                        if (zdo != null && zdo.IsValid() && _prefabHashes.Contains(zdo.GetPrefab())) results.Add(zdo);
                    }
                    if (++visited >= sectorBudget) return false;
                }
                _sectorCursor = 0;
                return true;
            }
        }
    }
}
