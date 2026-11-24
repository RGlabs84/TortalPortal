using System.Collections.Generic;

namespace TortalPortal
{
    /// <summary>
    /// The 1.0 sector API in the shape this mod used before 1.0. Valheim 1.0 retyped sectors from
    /// Vector2i to Vector2s, folded FindSectorObjects' (int area, int distantArea) pair into a
    /// SimulationDistance struct, and deleted ZoneSystem's two public distance fields
    /// (m_activeArea / m_activeDistantArea). The live values now come from the synced
    /// SimulationDistance on ZNet, which defaults to (2, 2) and is player-configurable.
    ///
    /// classic: true matters. With it FindSectorObjects reproduces the pre-1.0 square-ring sweep -
    /// rings 1..near unfiltered, then near+1..near+far for the distant list. Without it the body
    /// loops 1..near+far and trims every ring to a disc through ZoneSystem.ZonesWithinRadius,
    /// which would silently change the block of zones the anchor feeds ZNetScene and the
    /// subscription feeds a sync list.
    /// </summary>
    internal static class ZoneCompat
    {
        public static void FindSector(Vector2s sector, int ring, int distantRing, List<ZDO> results)
            => ZDOMan.instance.FindSectorObjects(sector, new SimulationDistance(ring, distantRing, classic: true), results);

        // Near ring vanilla considers a player to be occupying - the pre-1.0 m_activeArea (1 in code,
        // 2 as serialized on real servers). Falls back to vanilla's own original constant when there is
        // no ZNet to ask, which is the same 2.
        public static int ActiveArea =>
            ZNet.instance != null ? ZNet.instance.GetSyncedSimulationDistance().NearSimulationDistance
                                  : SimulationDistance.OriginalNear;

        // Distant ring beyond the near one - the pre-1.0 m_activeDistantArea.
        public static int ActiveDistantArea =>
            ZNet.instance != null ? ZNet.instance.GetSyncedSimulationDistance().FarSimulationDistance
                                  : SimulationDistance.OriginalFar;
    }
}
