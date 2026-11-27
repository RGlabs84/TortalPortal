// The grave ward phases. Single-player: the host is the server, so the corpse run raises the gates in this process.
//   D  die ~250 m from the spawn with something in the pack (a tombstone), respawn, find the bed-side corpse gate
//   G  walk into it: the trip lands, the ward is up, a hit is refused, still up near the end, gone after, hits land
//   S  back to the gate and through again: a swing ends the ward at once
//   Z  CorpseRunArrivalWardSeconds = 0: no ward at all
using System.Collections;
using System.Linq;
using TortalPortal;
using UnityEngine;

namespace TortalAutoTest
{
    public partial class AutoTest
    {
        static TeleportWorld originGate;
        static Vector3 gravePos;

        static float WardSeconds => Configuration.corpseRunArrivalWardSeconds.Value;
        static float WardLeft => CorpseWard._until - Time.time;

        static TeleportWorld FindMyOriginGate()
        {
            foreach (TeleportWorld tw in Object.FindObjectsOfType<TeleportWorld>())
            {
                ZNetView nv = tw.GetComponent<ZNetView>();
                if (nv == null || !nv.IsValid()) continue;
                ZDO zdo = nv.GetZDO();
                if (CorpseRun.IsOrigin(zdo) && CorpseRun.IsMine(zdo)) return tw;
            }
            return null;
        }

        /// <summary>A small, attacker-less blunt hit on the local player; returns the health lost.</summary>
        static IEnumerator Hit(float blunt)
        {
            Player p = Player.m_localPlayer;
            float before = p.GetHealth();
            var hit = new HitData();
            hit.m_damage.m_blunt = blunt;
            hit.m_point = p.transform.position + Vector3.up;
            hit.m_dir = Vector3.forward;
            p.Damage(hit);
            yield return Sec(0.6f);
            lastLoss = before - p.GetHealth();
            Log($"hit {blunt} blunt: health {before:0.0} -> {p.GetHealth():0.0} ({Flags(p)})");
        }
        static float lastLoss;

        static string Flags(Player p) =>
            $"dead {p.IsDead()}, teleporting {p.IsTeleporting()}, cutscene {p.InCutscene()}, intro {p.InIntro()}, cinematic {CinematicsManager.IsPlaying()}, " +
            $"debugfly {p.IsDebugFlying()}, god {p.InGodMode()}, ward {CorpseWard.Active}, dmgrate {Game.m_localDamgeTakenRate}";

        static void TopUp()
        {
            Player p = Player.m_localPlayer;
            if (p != null) p.SetHealth(p.GetMaxHealth());
        }

        // ------------------------------------------------------------------------------------------ D

        IEnumerator PhaseD()
        {
            Player p = Player.m_localPlayer;
            Vector3 spawn = p.transform.position;
            float water = ZoneSystem.instance.m_waterLevel;
            Vector3 away = Vector3.zero;
            bool found = false;
            for (int k = 0; k < 36 && !found; k++)
            {
                float a = k * Mathf.PI / 18f;
                Vector3 c = spawn + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 250f;
                float h = WorldGenerator.instance.GetHeight(c.x, c.z);
                if (h > water + 2f && h < water + 30f) { away = new Vector3(c.x, h + 1f, c.z); found = true; }
            }
            if (!found) { Result("FAIL", "a dry place 250 m out", $"none around {V(spawn)}"); yield break; }

            p.TeleportTo(away, Quaternion.identity, true);
            yield return WaitFor(() => p.IsTeleporting(), 5f);
            yield return WaitFor(() => !p.IsTeleporting() && ZoneSystem.instance.IsActiveAreaLoaded(), 120f);
            yield return Sec(3f);
            Result("INFO", "away from the spawn", $"spawn {V(spawn)}, now {V(p.transform.position)} ({Vector3.Distance(spawn, p.transform.position):0} m)");

            GameObject wood = ObjectDB.instance.GetItemPrefab("Wood");
            p.GetInventory().AddItem(wood, 20);
            Check(p.GetInventory().CountItems("$item_wood") >= 20, "something in the pack", $"{p.GetInventory().NrOfItems()} item stacks");

            TopUp();
            yield return Hit(5f);
            Check(lastLoss > 0.5f, "control: a hit lands before any ward", $"health lost {lastLoss:0.00}; {Flags(p)}");
            TopUp();

            Player dead = p;
            var kill = new HitData();
            kill.m_damage.m_damage = 99999f;
            kill.m_point = p.transform.position;
            p.Damage(kill);
            yield return WaitFor(() => dead == null || dead.IsDead() || Player.m_localPlayer != dead, 10f);
            if (!waitOk)
            {
                Log($"a 99999 hit did not kill ({Flags(p)}); setting health to 0 instead");
                p.SetHealth(0f);
                yield return WaitFor(() => dead == null || dead.IsDead() || Player.m_localPlayer != dead, 10f);
            }
            Check(waitOk, "died", waitOk ? "the player died" : $"still alive after a 99999 hit and SetHealth(0) ({Flags(p)})");
            if (!waitOk) yield break;
            gravePos = dead != null ? dead.transform.position : away;

            float t0 = Time.realtimeSinceStartup;
            yield return WaitFor(() => Player.m_localPlayer != null && Player.m_localPlayer != dead && !Player.m_localPlayer.IsDead()
                                       && ZoneSystem.instance.IsActiveAreaLoaded(), 180f);
            Check(waitOk, "respawned", waitOk ? $"after {Time.realtimeSinceStartup - t0:0} s at {V(Player.m_localPlayer.transform.position)}" : "no respawn in 180 s");
            if (!waitOk) yield break;
            p = Player.m_localPlayer;
            p.SetGodMode(false);
            p.SetGhostMode(true);

            yield return WaitFor(() => FindMyOriginGate() != null, 90f);
            originGate = FindMyOriginGate();
            Check(originGate != null, "corpse gate by the spawn",
                originGate != null ? $"origin gate at {V(originGate.transform.position)}, {Vector3.Distance(originGate.transform.position, p.transform.position):0.0} m from the player" : "no origin gate of mine within 90 s");
            if (originGate != null) yield return Shot("d-corpse-gate", originGate.transform.position, keepHud: true);
        }

        // ------------------------------------------------------------------------------------------ the trip

        static bool tripOk;
        static float landedAt;

        /// <summary>Into the bed-side gate's range (unless a trip is already under way), through, and onto the ground.</summary>
        static IEnumerator Trip(string label)
        {
            tripOk = false;
            Player p = Player.m_localPlayer;
            if (originGate == null) originGate = FindMyOriginGate();
            if (originGate == null) { Result("FAIL", label + ": gate", "no origin gate of mine loaded"); yield break; }

            if (!p.IsTeleporting() && !CorpseWard._pending)
            {
                Vector3 g = originGate.transform.position;
                Vector3 root = originGate.m_proximityRoot != null ? originGate.m_proximityRoot.position : g;
                Vector3 at = new Vector3(root.x, g.y, root.z) + originGate.transform.forward * 0.4f;
                yield return PlaceAt(at, g - at);
                Vector3 flat = p.transform.position - root; flat.y = 0f;
                Log($"{label}: placed {V(p.transform.position)}, {flat.magnitude:0.00} m flat from the trigger point, rise {p.transform.position.y - g.y:0.00}, " +
                    $"latch {(PortalBehaviorPatch._autoOpenedFor == originGate ? "this gate" : "clear")}, cooldown {p.m_teleportCooldown:0.0}");
            }
            yield return WaitFor(() => p.IsTeleporting(), 10f);
            if (!waitOk)
            {
                Vector3 root = originGate.m_proximityRoot != null ? originGate.m_proximityRoot.position : originGate.transform.position;
                Vector3 flat = p.transform.position - root; flat.y = 0f;
                Log($"{label}: no trip; now {V(p.transform.position)}, {flat.magnitude:0.00} m flat, rise {p.transform.position.y - originGate.transform.position.y:0.00}, " +
                    $"latch {(PortalBehaviorPatch._autoOpenedFor == originGate ? "this gate" : "clear")}, cooldown {p.m_teleportCooldown:0.0}, {Flags(p)}");
            }
            Check(waitOk, label + ": trip starts", waitOk ? "the gate sent the player through" : "standing at the gate, no trip within 10 s");
            if (!waitOk) yield break;
            yield return WaitFor(() => !p.IsTeleporting(), 90f);
            if (!waitOk) { Result("FAIL", label + ": trip lands", "still teleporting after 90 s"); yield break; }
            landedAt = Time.time;
            yield return null;   // one UpdateTeleport after landing
            Check(Vector3.Distance(p.transform.position, gravePos) < 30f, label + ": trip lands at the grave",
                $"at {V(p.transform.position)}, {Vector3.Distance(p.transform.position, gravePos):0.0} m from where the player died");
            tripOk = true;
        }

        /// <summary>Back near the bed-side gate but out of its range, so the next approach counts as a new one.</summary>
        static IEnumerator BackToGate()
        {
            Player p = Player.m_localPlayer;
            yield return WaitFor(() => p.m_teleportCooldown > 2.1f, 5f);
            Vector3 g = originGate != null ? originGate.transform.position : Vector3.zero;
            Vector3 at = g + Vector3.right * 15f;
            at.y = WorldGenerator.instance.GetHeight(at.x, at.z) + 1f;
            p.TeleportTo(at, Quaternion.identity, true);
            yield return WaitFor(() => p.IsTeleporting(), 5f);
            yield return WaitFor(() => !p.IsTeleporting() && ZoneSystem.instance.IsActiveAreaLoaded(), 120f);
            yield return Sec(2f);
            originGate = FindMyOriginGate();
            TopUp();
            yield return WaitFor(() => p.m_teleportCooldown > 2.1f, 5f);
        }

        // ------------------------------------------------------------------------------------------ G

        IEnumerator PhaseG()
        {
            Configuration.enableDebugMode.Value = true;
            int msgMark = MessageMark();
            yield return Trip("G");
            if (!tripOk) yield break;
            Player p = Player.m_localPlayer;

            Check(CorpseWard.Active, "ward up on landing", $"active {CorpseWard.Active}, {WardLeft:0.0} s left of {WardSeconds:0}");
            yield return Shot("g-ward-banner", null, keepHud: true);
            bool banner = MessagesSince(msgMark).Any(m => m.StartsWith("Grave ward: 20"));
            Check(banner, "banner shown", string.Join(" | ", MessagesSince(msgMark)));

            TopUp();
            yield return Hit(10f);
            Check(lastLoss < 0.01f, "hit refused under the ward", $"health lost {lastLoss:0.00}");

            // still up 2 s before the end
            yield return WaitFor(() => Time.time - landedAt >= WardSeconds - 2f, WardSeconds + 5f);
            Check(CorpseWard.Active, "ward still up 2 s before the end", $"{Time.time - landedAt:0.0} s after landing, {WardLeft:0.0} s left");

            int fadeMark = MessageMark();
            yield return WaitFor(() => Time.time - landedAt >= WardSeconds + 1f, 10f);
            Check(!CorpseWard.Active, "ward gone after its time", $"{Time.time - landedAt:0.0} s after landing, active {CorpseWard.Active}");
            bool faded = MessagesSince(fadeMark).Any(m => m.Contains("grave ward fades"));
            Check(faded, "fade line shown", string.Join(" | ", MessagesSince(fadeMark)));
            yield return Shot("g-ward-faded", null, keepHud: true);

            TopUp();
            yield return Hit(10f);
            Check(lastLoss > 0.5f, "hits land after the ward", $"health lost {lastLoss:0.00}");
            TopUp();
        }

        // ------------------------------------------------------------------------------------------ S

        IEnumerator PhaseS()
        {
            yield return BackToGate();
            yield return Trip("S");
            if (!tripOk) yield break;
            Player p = Player.m_localPlayer;
            Check(CorpseWard.Active, "ward up on landing", $"{WardLeft:0.0} s left");

            yield return Sec(1f);
            int fadeMark = MessageMark();
            bool swung = false;
            for (int i = 0; i < 20 && !swung; i++)
            {
                swung = p.StartAttack(null, false);
                if (!swung) yield return Sec(0.25f);
            }
            if (!swung) { Result("SKIP", "a swing ends the ward", "StartAttack never accepted (unarmed) in 5 s: not assessed"); yield break; }
            yield return null;
            Check(!CorpseWard.Active, "a swing ends the ward", $"{Time.time - landedAt:0.0} s after landing, active {CorpseWard.Active}");
            yield return Sec(0.5f);
            Check(MessagesSince(fadeMark).Any(m => m.Contains("grave ward fades")), "fade line on the swing", string.Join(" | ", MessagesSince(fadeMark)));

            yield return Sec(1.5f);
            TopUp();
            yield return Hit(10f);
            Check(lastLoss > 0.5f, "hits land after the swing", $"health lost {lastLoss:0.00}");
            TopUp();
        }

        // ------------------------------------------------------------------------------------------ Z

        IEnumerator PhaseZ()
        {
            float keep = Configuration.corpseRunArrivalWardSeconds.Value;
            Configuration.corpseRunArrivalWardSeconds.Value = 0f;
            try
            {
                Check(Configuration.corpseRunArrivalWardSeconds.Value == 0f, "setting takes 0", $"value {Configuration.corpseRunArrivalWardSeconds.Value}");
            }
            catch { }
            yield return BackToGate();
            yield return Trip("Z");
            if (tripOk)
            {
                yield return Sec(0.5f);
                Check(!CorpseWard.Active, "no ward when set to 0", $"active {CorpseWard.Active}");
                TopUp();
                yield return Hit(10f);
                Check(lastLoss > 0.5f, "hits land at once when set to 0", $"health lost {lastLoss:0.00}");
                TopUp();
            }
            Configuration.corpseRunArrivalWardSeconds.Value = keep;
        }
    }
}
