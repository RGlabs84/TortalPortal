using System;
using System.Collections.Generic;
using UnityEngine;

namespace TortalPortal
{
    // Where the ground is, with nothing loaded.
    //
    // The corpse run and the health scan both have to answer "how high is the terrain at this spot" for places
    // no player is standing near - a grave on the far side of the map, a bed in a zone the server has never
    // instantiated. There is no Heightmap and no collider there, so ZoneSystem.GetGroundHeight and every
    // Physics.* call would hand back their no-hit fallback and say nothing. This re-runs the client's own
    // heightmap recipe on the raw data instead, the way TortalPortal Lite's placement scanner does:
    //
    //  1. HeightmapBuilder.Build. A zone whose four corners share a biome samples GetBiomeHeight for that
    //     biome directly; a zone straddling biomes smoothstep-lerps the four corner biomes' heights across
    //     the 64m zone. Bare WorldGenerator.GetHeight - what tortal_scatter uses - returns the un-blended
    //     single-biome height, which at a biome border can sit metres above or below the real mesh.
    //  2. TerrainComp. Hoe, pickaxe and cultivator work lives in the zone's _TerrainCompiler ZDO as a
    //     gzip'd per-vertex (level, smooth) array, and the mesh vertex is clamp(base + level + smooth,
    //     base - 8, base + 8). Read straight off that ZDO here, so a gate raised beside a bed on a levelled
    //     base stands on the levelled floor and not on the slope that was there before the hoe.
    //
    // Vertices sit on the integer world grid; a point between them is bilinearly interpolated, which matches
    // the collision mesh to within a quad's diagonal twist - centimetres. One instance per search: every
    // vertex and zone it touches is memoised, so the couple of thousand probes a full placement search makes
    // cost one noise evaluation per distinct vertex rather than per probe.
    public sealed class GroundProbe
    {
        private const int ZoneSize = 64;
        private const int VertexPitch = ZoneSize + 1;

        // TerrainComp.ApplyToHeightmap's own clamp on how far terraforming may move a vertex.
        private const float MaxTerraformDelta = 8f;

        private sealed class ZoneData
        {
            public Heightmap.Biome B0, B1, B2, B3;
            public bool SingleBiome;
            // level + smooth per vertex (row-major, z * 65 + x), or null when the zone was never terraformed.
            public float[] Deltas;
        }

        private readonly Dictionary<long, ZoneData> _zones = new Dictionary<long, ZoneData>();
        private readonly Dictionary<long, float> _vertexHeights = new Dictionary<long, float>();

        public static bool Ready => WorldGenerator.instance != null;

        public static float WaterLevel => ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : ZoneSystem.c_WaterLevel;

        // The same test ZoneSystem runs before a heightmap exists - a pure WorldGenerator mask lookup.
        public static bool IsLava(Vector3 pos)
        {
            try { return ZoneSystem.IsLavaPreHeightmap(pos); }
            catch { return false; }
        }

        // Terrain-mesh height at a world (x, z).
        public float HeightAt(float x, float z)
        {
            int gx = Mathf.FloorToInt(x);
            int gz = Mathf.FloorToInt(z);
            float fx = x - gx;
            float fz = z - gz;
            float h00 = VertexHeight(gx, gz);
            float h10 = VertexHeight(gx + 1, gz);
            float h01 = VertexHeight(gx, gz + 1);
            float h11 = VertexHeight(gx + 1, gz + 1);
            return Mathf.Lerp(Mathf.Lerp(h00, h10, fx), Mathf.Lerp(h01, h11, fx), fz);
        }

        // Y component of the terrain normal at (x, z) by central differences over 2m - 1.0 is dead flat, 0.87
        // is a 30 degree slope.
        public float NormalYAt(float x, float z)
        {
            float dx = (HeightAt(x + 1f, z) - HeightAt(x - 1f, z)) * 0.5f;
            float dz = (HeightAt(x, z + 1f) - HeightAt(x, z - 1f)) * 0.5f;
            return 1f / Mathf.Sqrt(1f + (dx * dx) + (dz * dz));
        }

        private float VertexHeight(int gx, int gz)
        {
            long key = Pack(gx, gz);
            if (_vertexHeights.TryGetValue(key, out float cached)) return cached;

            // ZoneSystem.GetZone's own arithmetic: floor((p + 32) / 64). A vertex on a shared zone edge resolves
            // to the eastern/northern zone's column 0, which holds the same value.
            int zx = Mathf.FloorToInt((gx + 32) / (float)ZoneSize);
            int zz = Mathf.FloorToInt((gz + 32) / (float)ZoneSize);
            ZoneData zone = GetZone(zx, zz);
            int vx = gx - ((zx * ZoneSize) - (ZoneSize / 2));
            int vz = gz - ((zz * ZoneSize) - (ZoneSize / 2));

            WorldGenerator wg = WorldGenerator.instance;
            float h;
            if (zone.SingleBiome)
            {
                h = wg.GetBiomeHeight(zone.B0, gx, gz, out _);
            }
            else
            {
                float t2 = DUtils.SmoothStep(0f, 1f, vx / (float)ZoneSize);
                float t = DUtils.SmoothStep(0f, 1f, vz / (float)ZoneSize);
                float h0 = wg.GetBiomeHeight(zone.B0, gx, gz, out _);
                float h1 = wg.GetBiomeHeight(zone.B1, gx, gz, out _);
                float h2 = wg.GetBiomeHeight(zone.B2, gx, gz, out _);
                float h3 = wg.GetBiomeHeight(zone.B3, gx, gz, out _);
                h = DUtils.Lerp(DUtils.Lerp(h0, h1, t2), DUtils.Lerp(h2, h3, t2), t);
            }

            if (zone.Deltas != null)
            {
                float delta = zone.Deltas[(vz * VertexPitch) + vx];
                if (delta != 0f) h = Mathf.Clamp(h + delta, h - MaxTerraformDelta, h + MaxTerraformDelta);
            }

            _vertexHeights[key] = h;
            return h;
        }

        private ZoneData GetZone(int zx, int zz)
        {
            long key = Pack(zx, zz);
            if (_zones.TryGetValue(key, out ZoneData existing)) return existing;

            // HeightmapBuilder.Build samples the four corners of the zone's vertex grid - zone centre minus
            // half a width, then plus the full width - through GetBiome, not GetBiomeSector.
            WorldGenerator wg = WorldGenerator.instance;
            float x0 = (zx * ZoneSize) - (ZoneSize / 2);
            float z0 = (zz * ZoneSize) - (ZoneSize / 2);
            ZoneData zone = new ZoneData
            {
                B0 = wg.GetBiome(x0, z0),
                B1 = wg.GetBiome(x0 + ZoneSize, z0),
                B2 = wg.GetBiome(x0, z0 + ZoneSize),
                B3 = wg.GetBiome(x0 + ZoneSize, z0 + ZoneSize),
            };
            zone.SingleBiome = zone.B1 == zone.B0 && zone.B2 == zone.B0 && zone.B3 == zone.B0;
            zone.Deltas = LoadTerraformDeltas(zx, zz);
            _zones[key] = zone;
            return zone;
        }

        // The zone's _TerrainCompiler ZDO sits exactly at ZoneSystem.GetZonePos (x*64, 0, z*64) - it is
        // instantiated at the Heightmap's own transform. Identified by carrying an s_TCData byte array rather
        // than by prefab name, so a renamed or modded compiler prefab still resolves.
        private static float[] LoadTerraformDeltas(int zx, int zz)
        {
            if (ZDOMan.instance == null) return null;
            Vector3 zonePos = new Vector3(zx * ZoneSize, 0f, zz * ZoneSize);
            List<ZDO> near = ZdoQuery.FindNear(zonePos, ZoneSize);
            foreach (ZDO zdo in near)
            {
                byte[] data = zdo.GetByteArray(ZDOVars.s_TCData);
                if (data == null) continue;
                Vector3 p = zdo.GetPosition();
                if (Mathf.Abs(p.x - zonePos.x) > 2f || Mathf.Abs(p.z - zonePos.z) > 2f) continue;
                try
                {
                    return ParseTerrainCompData(data);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"TortalPortal: terrain data for zone ({zx},{zz}) could not be read, using the base terrain there. Reason: {ex.GetType().Name}: {ex.Message}");
                    return null;
                }
            }
            return null;
        }

        // TerrainComp.Load's own read order, paint section ignored.
        private static float[] ParseTerrainCompData(byte[] data)
        {
            ZPackage pkg = new ZPackage(Utils.Decompress(data));
            pkg.ReadInt();      // terrainCompVersion
            pkg.ReadInt();      // m_operations
            pkg.ReadVector3();  // m_lastOpPoint
            pkg.ReadSingle();   // m_lastOpRadius
            int count = pkg.ReadInt();
            if (count != VertexPitch * VertexPitch)
            {
                Plugin.Log.LogWarning($"TortalPortal: terrain data vertex count {count} is not {VertexPitch * VertexPitch}, ignoring it.");
                return null;
            }
            float[] deltas = new float[count];
            bool any = false;
            for (int i = 0; i < count; i++)
            {
                if (!pkg.ReadBool()) continue;
                float level = pkg.ReadSingle();
                float smooth = pkg.ReadSingle();
                deltas[i] = level + smooth;
                any |= deltas[i] != 0f;
            }
            return any ? deltas : null;
        }

        private static long Pack(int a, int b) => ((long)a << 32) | ((long)b & 0xFFFFFFFFL);
    }
}
