using System.Collections.Generic;
using UnityEngine;

namespace TortalPortal
{
    // "Where, near this point, can a portal stand" - the placement scanner behind both ends of a corpse gate.
    //
    // Pure data only, deliberately: nothing here touches ZoneSystem.GetGroundHeight, FindFloor, IsBlocked or any
    // Physics.* call. Those raycast against a live terrain collider, and on a dedicated server no collider
    // exists near a real player coordinate, so every one of them would silently return its no-hit fallback.
    // Ground comes from GroundProbe (the client's own heightmap recipe re-run headless, terraforming included)
    // and obstacles from ObstacleField (collider footprints off the prefab assets, plus generated locations).
    //
    // Search order, each stage only reached when the previous found nothing:
    //  1. Fully clear: honours the configured clearance against everything, on dry, level, contiguous ground.
    //     Rings scan outward; once a ring yields a hit, three more rings are scanned and the best-scored spot
    //     (flattest, most open, nearest) wins.
    //  2. Same again with the level-ground rules relaxed (steeper hillsides).
    //  3. Most open spot: nothing inside the search radius honours the clearance, so take the candidate with
    //     the largest edge-to-edge room that still stands on dry ground. Logged as a compromise so an admin can
    //     raise the search radius if it keeps happening.
    //  4. Water surface, only when the anchor itself is in water (an ocean death): float the gate.
    //  5. Last resort: the most open candidate on the ground regardless of slope.
    // A death inside a dungeon (interiors are stacked at y > 1000) is re-anchored to the dungeon's exterior
    // entrance location, so the gate opens outside the crypt rather than inside a wall.
    public static class PortalPlacement
    {
        // Half-width of the playable map along one axis, beyond which WorldGenerator returns the fixed ocean-edge
        // height.
        private const float WaterEdgeRadius = 10500f;

        // Anything above this is an interior (dungeons are assembled ~5000m up); no surface terrain reaches it.
        public const float InteriorHeight = 1000f;

        private const float RingStep = 1f;
        private const float ArcSpacing = 2f;
        private const int MinBearings = 8;
        private const int MaxBearings = 64;

        // After the first ring with a fully clear hit, how many further rings are still scored before settling.
        private const int ExtraRingsAfterHit = 3;

        // Cap on ground evaluations spent in the "most open spot" stage.
        private const int MaxCompromiseChecks = 400;

        // How far the search radius may grow, and how often, when not even the hard floor is met inside the
        // configured radius. Widening before settling for less is the whole point: a gate 80m away with room
        // to stand is a better answer than one wedged into a boulder field 10m from the bed.
        //
        // Kept to ONE escalation, and capped well below Lite's 200m, because each attempt rebuilds the whole
        // obstacle field and re-enumerates every ring, and both the candidate count and the obstacle count grow
        // with the radius - so an attempt costs roughly the square of it. This runs synchronously on the server's
        // main thread inside one tick, twice per death, and it only ever fires in the dense terrain where the
        // obstacle list is already at its worst. A second doubling bought a rarely-needed answer for several
        // times the cost of the first.
        private const float RadiusEscalationFactor = 2f;
        private const int MaxRadiusEscalations = 1;
        private const float MaxEscalatedRadius = 100f;

        // The hard floor when a caller does not name one.
        public const float DefaultMinRoom = 2f;

        // The portal pivot sits on the ground; a couple of centimetres of lift only avoids z-fighting on a
        // perfectly flat mesh.
        public const float GroundLift = 0.03f;

        // Base corners of a portal frame, in its own space: +-x across the frame, +-z through it.
        private const float FrameHalfWidth = 1.2f;
        private const float FrameHalfDepth = 0.5f;

        // Ground must sit at least this far above the water surface.
        private const float DryMargin = 0.3f;

        public enum Quality
        {
            Clear,          // configured clearance honoured against everything, level dry ground
            Reduced,        // a smaller clearance step was honoured - still real room, just less than asked for
            Compromise,     // nothing in range honoured even the hard floor; this is the most open dry spot found
            WaterSurface,   // anchor is in open water; gate floats on the surface
            LastResort,     // only steep or otherwise poor ground was available
            Unplaced,       // WorldGenerator not ready; fixed offset from the anchor
        }

        public readonly struct Placement
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Quality Quality;
            // Edge-to-edge room to spare beyond the configured clearance (negative = short by that much).
            public readonly float Margin;
            // Horizontal distance from the anchor actually scanned around (the entrance location for an interior death).
            public readonly float Distance;
            public readonly float NormalY;
            public readonly string Nearest;
            // True when the anchor was an interior point re-anchored to its exterior location.
            public readonly bool ReanchoredFromInterior;
            // Obstacle census the search ran against - for the log.
            public readonly string Obstacles;
            // Anchor's own height minus the modelled ground under it - a sanity readout of the ground model.
            public readonly float AnchorAboveGround;
            // The clearance requirement this spot actually honoured - the tier that won, or the hard floor it
            // failed to reach. Says how much of the configured preference the ground could really offer.
            public readonly float Honoured;

            public Placement(Vector3 position, Quaternion rotation, Quality quality, float margin, float distance, float normalY, string nearest, bool reanchored, string obstacles = "", float anchorAboveGround = 0f, float honoured = 0f)
            {
                Position = position;
                Rotation = rotation;
                Quality = quality;
                Margin = margin;
                Distance = distance;
                NormalY = normalY;
                Nearest = nearest;
                ReanchoredFromInterior = reanchored;
                Obstacles = obstacles;
                AnchorAboveGround = anchorAboveGround;
                Honoured = honoured;
            }

            // A gate that honoured a real clearance requirement, even a reduced one, is a gate in a spot a
            // player can walk up to. Only the fallbacks below that are worth warning about.
            public bool IsGood => Quality == Quality.Clear || Quality == Quality.Reduced;

            public string Describe()
            {
                string margin = Margin >= 999f ? "nothing in range" : $"{Margin:0.0}m of room" + (Nearest.Length > 0 ? $" to {Nearest}" : "") + (Honoured > 0f ? $", honoured {Honoured:0.0}m" : "");
                string extra = (ReanchoredFromInterior ? ", re-anchored from interior" : $", anchor {AnchorAboveGround:+0.00;-0.00}m above modelled ground") + (Obstacles.Length > 0 ? ", " + Obstacles : "");
                return $"{Quality} at {Position.x:F1},{Position.y:F1},{Position.z:F1} ({Distance:F1}m out, slope normal {NormalY:F2}, {margin}{extra})";
            }
        }

        // A spot on a ring, with its obstacle field probed ONCE. Margin is deliberately not stored: the
        // tiered search asks the same spot about several clearance requirements, and re-walking the obstacle
        // list per tier would multiply the cost of the scan by the number of tiers.
        private struct Candidate
        {
            public float X;
            public float Z;
            public float R;
            public float NormalEdge;
            public float SmallEdge;
            public string NormalName;
            public string SmallName;

            // Room to spare beyond `required` (negative = short by that much).
            public float MarginAt(float required, out string nearest)
            {
                return ObstacleField.MarginFrom(NormalEdge, SmallEdge, NormalName, SmallName, required, out nearest);
            }

            // The room this spot actually has, against no requirement at all.
            public float Room(out string nearest)
            {
                return ObstacleField.RoomFrom(NormalEdge, SmallEdge, NormalName, SmallName, out nearest);
            }
        }

        private struct GroundFit
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public float NormalY;
            public float Deviation;
            public int OpenSides;
        }

        // Rotation facing FROM the offset position back TOWARD the centre it was offset from - the gate faces
        // the grave, or the bed.
        public static Quaternion FacingTowards(Vector3 from, Vector3 towards)
        {
            Vector3 dir = towards - from;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return Quaternion.identity;
            return Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        // Finds where to stand a portal near anchor: between minRadius and maxRadius out, with clearance metres
        // edge-to-edge from every solid thing, on the real ground. Always returns a position; Quality says how
        // good it is.
        public static Placement Find(Vector3 anchor, float minRadius, float maxRadius, float clearance, ZDOID ignoreZdo, IReadOnlyList<Vector3> avoid = null, float minRoom = DefaultMinRoom)
        {
            minRadius = Mathf.Max(0.5f, minRadius);
            maxRadius = Mathf.Max(minRadius + RingStep, maxRadius);
            clearance = Mathf.Max(0f, clearance);
            minRoom = Mathf.Clamp(minRoom, 0f, clearance);

            if (!GroundProbe.Ready) return FixedOffset(anchor, minRadius);

            GroundProbe probe = new GroundProbe();

            // Interior death: scan around the exterior entrance instead, on the real ground there.
            bool reanchored = false;
            Vector3 scanAnchor = anchor;
            if (anchor.y > InteriorHeight)
            {
                reanchored = true;
                if (ObstacleField.TryFindLocationInZoneOf(anchor, out Vector3 exterior, out _)) scanAnchor = exterior;
                scanAnchor.y = probe.HeightAt(scanAnchor.x, scanAnchor.z);
            }

            float water = GroundProbe.WaterLevel;
            float anchorAbove = reanchored ? 0f : anchor.y - probe.HeightAt(anchor.x, anchor.z);
            List<float> tiers = Tiers(clearance, minRoom);

            // Stages 1 + 2: the tiered scan. The configured clearance is a PREFERENCE, tried first, then
            // progressively smaller requirements down to the hard floor, and only when not even the floor can
            // be met inside the radius is the radius widened and the whole ladder tried again. Ten metres of
            // edge-to-edge room from EVERYTHING does not exist in a Valheim forest, so treating it as a
            // requirement meant every gate fell through to the most-open-spot fallback below - which trades
            // distance for room so cheaply that bed-side gates ended up tens of metres from the bed.
            List<Candidate> candidates = new List<Candidate>();
            string census = "";
            float radius = maxRadius;
            for (int attempt = 0; ; attempt++)
            {
                ObstacleField field = ObstacleField.Build(scanAnchor, radius, clearance, ignoreZdo, avoid);
                census = $"{field.ZdoObstacles} objects + {field.LocationObstacles} locations in reach" + (field.UnreadablePrefabs > 0 ? $", {field.UnreadablePrefabs} with assumed footprints" : "");
                candidates = EnumerateCandidates(scanAnchor, minRadius, field.EffectiveSearchRadius, field);

                for (int t = 0; t < tiers.Count; t++)
                {
                    if (TryTier(probe, candidates, scanAnchor, water, tiers[t], out GroundFit fit, out Candidate won))
                    {
                        float room = won.Room(out string nearest);
                        Quality quality = t == 0 ? Quality.Clear : Quality.Reduced;
                        return new Placement(fit.Position, fit.Rotation, quality, room, won.R, fit.NormalY, nearest, reanchored, census, anchorAbove, tiers[t]);
                    }
                }

                if (attempt >= MaxRadiusEscalations) break;
                float widened = Mathf.Min(radius * RadiusEscalationFactor, MaxEscalatedRadius);
                if (widened <= radius + 0.001f) break;
                // The field can enforce a larger radius than asked for (a death inside a big generated location
                // has to escape its exterior first), so a nominal widening can leave the searched area
                // unchanged. Re-scanning an identical candidate set costs a full rebuild for nothing.
                if (field.EffectiveSearchRadius >= widened - 0.001f) break;
                radius = widened;
            }

            // 3: most open dry spot. Nothing in the widened radius clears even the hard floor, so every
            // candidate is in the running - not just the ones that failed a particular requirement, because
            // with a ladder of tiers there is no single margin left to filter on.
            List<Candidate> crowded = new List<Candidate>(candidates);
            // Most open first; a metre of room is worth roughly thirty metres of extra walk.
            crowded.Sort((a, b) => (b.Room(out _) - (b.R * 0.03f)).CompareTo(a.Room(out _) - (a.R * 0.03f)));
            for (int pass = 0; pass < 2; pass++)
            {
                bool strict = pass == 0;
                int checks = 0;
                for (int i = 0; i < crowded.Count && checks < MaxCompromiseChecks; i++, checks++)
                {
                    Candidate c = crowded[i];
                    if (TryFitGround(probe, c.X, c.Z, water, scanAnchor, strict, out GroundFit fit))
                    {
                        float room = c.Room(out string nearest);
                        // honoured: 0, not minRoom. Reaching this stage means nothing in the widened radius met
                        // the floor, so printing the floor beside "Compromise" would name the one requirement
                        // this spot is known to have failed.
                        return new Placement(fit.Position, fit.Rotation, Quality.Compromise, room, c.R, fit.NormalY, nearest, reanchored, census, anchorAbove, 0f);
                    }
                }
            }

            // 4: anchor in open water and no dry ground in range - float on the surface.
            float anchorGround = probe.HeightAt(scanAnchor.x, scanAnchor.z);
            if (anchorGround < water && candidates.Count > 0)
            {
                Candidate best = MostOpen(candidates);
                float bestRoom = best.Room(out string bestNearest);
                Vector3 pos = new Vector3(best.X, water + 0.1f, best.Z);
                return new Placement(pos, FacingTowards(pos, scanAnchor), Quality.WaterSurface, bestRoom, best.R, 1f, bestNearest, reanchored, census, anchorAbove);
            }

            // 5: whatever is most open, on the ground, slope be damned.
            if (candidates.Count > 0)
            {
                Candidate best = MostOpen(candidates);
                float bestRoom = best.Room(out string bestNearest);
                float h = Mathf.Max(probe.HeightAt(best.X, best.Z), water + 0.1f);
                Vector3 pos = new Vector3(best.X, h + GroundLift, best.Z);
                return new Placement(pos, FacingTowards(pos, scanAnchor), Quality.LastResort, bestRoom, best.R, probe.NormalYAt(best.X, best.Z), bestNearest, reanchored, census, anchorAbove);
            }

            Vector3 edge = scanAnchor + (Vector3.forward * minRadius);
            edge.y = Mathf.Max(probe.HeightAt(edge.x, edge.z), water + 0.1f) + GroundLift;
            return new Placement(edge, FacingTowards(edge, scanAnchor), Quality.LastResort, 0f, minRadius, 1f, "", reanchored, census, anchorAbove);
        }

        // The no-information fallback: a fixed step from the anchor at the anchor's own height. Only for when the
        // world is not ready or the scan itself faulted.
        public static Placement FixedOffset(Vector3 anchor, float radius)
        {
            Vector3 pos = anchor + (Vector3.forward * Mathf.Max(0.5f, radius));
            return new Placement(pos, FacingTowards(pos, anchor), Quality.Unplaced, 0f, radius, 1f, "", false);
        }

        private static List<Candidate> EnumerateCandidates(Vector3 anchor, float minRadius, float maxRadius, ObstacleField field)
        {
            List<Candidate> list = new List<Candidate>(1024);
            int ring = 0;
            for (float r = minRadius; r <= maxRadius + 0.001f; r += RingStep, ring++)
            {
                int bearings = Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * r / ArcSpacing), MinBearings, MaxBearings);
                float phase = (ring & 1) == 1 ? 0.5f : 0f;
                for (int i = 0; i < bearings; i++)
                {
                    float angle = (i + phase) * (2f * Mathf.PI / bearings);
                    float x = anchor.x + (Mathf.Cos(angle) * r);
                    float z = anchor.z + (Mathf.Sin(angle) * r);
                    if ((x * x) + (z * z) > WaterEdgeRadius * WaterEdgeRadius) continue;
                    field.Probe(x, z, out float normalEdge, out float smallEdge, out string normalName, out string smallName);
                    list.Add(new Candidate { X = x, Z = z, R = r, NormalEdge = normalEdge, SmallEdge = smallEdge, NormalName = normalName, SmallName = smallName });
                }
            }
            return list;
        }

        // The clearance requirements to try, largest first, always ending at the hard floor. Geometric rather
        // than a fixed step so the shape holds whether an admin asks for 3m of room or 30.
        private static List<float> Tiers(float clearance, float minRoom)
        {
            List<float> tiers = new List<float>(5);
            void Add(float value)
            {
                value = Mathf.Max(value, minRoom);
                if (tiers.Count == 0 || value < tiers[tiers.Count - 1] - 0.05f) tiers.Add(value);
            }
            Add(clearance);
            Add(clearance * 0.7f);
            Add(clearance * 0.5f);
            Add(clearance * 0.35f);
            Add(minRoom);
            return tiers;
        }

        // One rung of the clearance ladder, strict ground rules then relaxed. The CLOSEST ring holding a
        // candidate that honours `required` wins its neighbourhood, and the best-scored spot within
        // ExtraRingsAfterHit of it settles it - so a tier that can be met near the anchor is never traded away
        // for a roomier spot far from it.
        private static bool TryTier(GroundProbe probe, List<Candidate> candidates, Vector3 scanAnchor, float water, float required, out GroundFit bestFit, out Candidate bestCandidate)
        {
            bestFit = default;
            bestCandidate = default;
            for (int pass = 0; pass < 2; pass++)
            {
                bool strict = pass == 0;
                bool found = false;
                float bestScore = float.MinValue;
                float firstHitRing = -1f;
                for (int i = 0; i < candidates.Count; i++)
                {
                    Candidate c = candidates[i];
                    float margin = c.MarginAt(required, out _);
                    if (margin < 0f) continue;
                    if (firstHitRing >= 0f && c.R > firstHitRing + (ExtraRingsAfterHit * RingStep) + 0.001f) break;
                    if (!TryFitGround(probe, c.X, c.Z, water, scanAnchor, strict, out GroundFit fit)) continue;
                    if (firstHitRing < 0f) firstHitRing = c.R;
                    float score = (fit.NormalY * 20f) + (Mathf.Min(margin, 5f) * 0.6f) + (fit.OpenSides * 0.5f) - (c.R * 0.35f) - (fit.Deviation * 4f);
                    if (!found || score > bestScore)
                    {
                        found = true;
                        bestScore = score;
                        bestFit = fit;
                        bestCandidate = c;
                    }
                }
                if (found) return true;
            }
            return false;
        }

        private static Candidate MostOpen(List<Candidate> candidates)
        {
            Candidate best = candidates[0];
            float bestRoom = best.Room(out _);
            for (int i = 1; i < candidates.Count; i++)
            {
                float room = candidates[i].Room(out _);
                // Ties (typically "nothing in range" everywhere) resolve toward the anchor.
                if (room > bestRoom + 0.01f || (Mathf.Abs(room - bestRoom) <= 0.01f && candidates[i].R < best.R))
                {
                    best = candidates[i];
                    bestRoom = room;
                }
            }
            return best;
        }

        // Is this dry, level, contiguous ground a portal frame can sit on? Checks the pivot, the four base corners
        // of the frame (oriented to face the anchor) and a 2m ring of surroundings, so the frame neither floats
        // off a downhill edge, sinks into an uphill one, nor wedges against a terrain cliff or hole.
        private static bool TryFitGround(GroundProbe probe, float x, float z, float water, Vector3 faceTarget, bool strict, out GroundFit fit)
        {
            fit = default;
            float minNormalY = strict ? 0.87f : 0.72f;       // 30 / 44 degrees
            float maxDeviation = strict ? 0.45f : 0.9f;      // frame corner vs pivot height
            float surroundStep = strict ? 1.0f : 1.8f;       // surroundings vs pivot height
            int minOpenSides = strict ? 6 : 4;               // of 8, at 2m

            float h = probe.HeightAt(x, z);
            if (h < water + DryMargin) return false;
            Vector3 pos = new Vector3(x, h, z);
            if (GroundProbe.IsLava(pos)) return false;
            float normalY = probe.NormalYAt(x, z);
            if (normalY < minNormalY) return false;

            Quaternion rot = FacingTowards(pos, faceTarget);
            Vector3 right = rot * Vector3.right;
            Vector3 fwd = rot * Vector3.forward;
            float deviation = 0f;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    float cx = x + (right.x * FrameHalfWidth * sx) + (fwd.x * FrameHalfDepth * sz);
                    float cz = z + (right.z * FrameHalfWidth * sx) + (fwd.z * FrameHalfDepth * sz);
                    float hc = probe.HeightAt(cx, cz);
                    if (hc < water + (DryMargin * 0.5f)) return false;
                    deviation = Mathf.Max(deviation, Mathf.Abs(hc - h));
                }
            }
            if (deviation > maxDeviation) return false;

            int openSides = 0;
            for (int i = 0; i < 8; i++)
            {
                float a = i * (Mathf.PI / 4f);
                float hs = probe.HeightAt(x + (Mathf.Cos(a) * 2f), z + (Mathf.Sin(a) * 2f));
                if (Mathf.Abs(hs - h) <= surroundStep && hs >= water) openSides++;
            }
            if (openSides < minOpenSides) return false;

            fit = new GroundFit
            {
                Position = new Vector3(x, h + GroundLift, z),
                Rotation = rot,
                NormalY = normalY,
                Deviation = deviation,
                OpenSides = openSides,
            };
            return true;
        }
    }
}
