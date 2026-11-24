using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TortalPortal
{
    // Self-healing portals.
    //
    // A troll that wanders past a base does not care that the gate it is swinging at is the only way home,
    // and a portal is exactly the kind of piece that gets destroyed when nobody is there to defend it. This
    // takes portals off the table: monsters stop choosing them as targets, any hit that does land is refused
    // at the one place every kind of damage funnels through, and a portal found below full health - one that
    // was hurt before this build, or by a peer that is not running it - is healed the next time it is looked
    // at. Corpse gates are always protected; player-built portals are protected while SelfHealingPortals is on.
    //
    // Damage is refused in ApplyDamage rather than in Damage or RPC_Damage because it is the funnel: hits,
    // fire, weather, structural collapse and the ash storms all end up there, and it runs on the peer that owns
    // the ZDO, which is the only peer whose verdict counts. Removal with the hammer is untouched - that is
    // WearNTear.Remove, a different path, and a player's own portal stays theirs to take down.
    [HarmonyPatch]
    public static class PortalGuard
    {
        // What an immune gate's health is pinned at. Well beyond anything a hit can subtract, and LoadFields
        // writes the same number into the client's WearNTear so the health bar never shows a scratch.
        public const float ImmuneHealth = 1000000000f;

        // How often a loaded portal is looked at for missing health.
        private const float HealIntervalSeconds = 2f;

        // Gates whose ZDO must not be deleted by anybody but this mod: every corpse gate, and every concealed
        // gate the world was seeded with.
        //
        // Deliberately an in-memory set of ZDOIDs and NOT the PortalKeys.Immune flag on the ZDO, though that
        // flag is right there and would be less code. Every corpse gate carries it, and so this mod's own three
        // removal paths - the stray reaper, a gate coming down when its grave is emptied, and the health scan's
        // "remove a stray corpse gate" repair - all funnel through PortalMint.Destroy and would be refused by
        // their own veto. A gate the mod is deliberately taking down is released from the set first, so the
        // veto only ever refuses a destroy this mod did not ask for.
        private static readonly HashSet<ZDOID> Locked = new HashSet<ZDOID>();
        private static long _refusedRemovals;

        public static int LockedCount => Locked.Count;

        public static void Lock(ZDOID uid)
        {
            if (uid != ZDOID.None) Locked.Add(uid);
        }

        public static void Unlock(ZDOID uid)
        {
            if (uid != ZDOID.None) Locked.Remove(uid);
        }

        public static bool IsLocked(ZDOID uid)
        {
            return Locked.Count > 0 && Locked.Contains(uid);
        }

        // Counted and reported here so the veto in PortalLockdown stays a single decision with no state.
        public static void CountRefusedRemoval(ZDOID uid)
        {
            _refusedRemovals++;
            if (_refusedRemovals <= 5L || _refusedRemovals % 50L == 0L)
            {
                Plugin.Log.LogWarning($"TortalPortal: refused a removal of server-raised gate {uid} that this mod did not ask for ({_refusedRemovals} so far this session).");
            }
        }

        // A world change means every ZDOID is re-minted; nothing in the old set means anything.
        public static void ResetLocks()
        {
            if (Locked.Count > 0) Plugin.Log.LogInfo($"TortalPortal: released {Locked.Count} server-raised gate removal lock(s) and refused {_refusedRemovals} removal(s) this session.");
            Locked.Clear();
            _refusedRemovals = 0L;
        }

        private static readonly Dictionary<TeleportWorld, float> NextHealCheck = new Dictionary<TeleportWorld, float>();
        private static float _nextPrune;

        // ---- policy ----------------------------------------------------------------------------------------

        // Is this portal one that shrugs off damage: a corpse gate, or any portal while the dial is on.
        public static bool IsProtected(ZDO zdo)
        {
            if (zdo == null || !zdo.IsValid()) return false;
            if (zdo.GetBool(PortalKeys.Immune, false)) return true;
            // Self-healing is TortalPortal's rule for TortalPortal's gates; a skipped gate is as mortal as its
            // own mod made it. (A gate this mod raised carries Immune above and never reaches here.)
            if (SkippedPortals.IsSkipped(zdo)) return false;
            return Configuration.selfHealingPortals != null && Configuration.selfHealingPortals.Value;
        }

        public static bool IsProtected(Component onPortal)
        {
            if (onPortal == null) return false;
            ZNetView nview = onPortal.GetComponent<ZNetView>();
            return nview != null && nview.IsValid() && IsProtected(nview.GetZDO());
        }

        // ---- targeting -------------------------------------------------------------------------------------

        // Monsters choose what to attack by reading Piece.m_randomTarget and m_primaryTarget off the live
        // piece each time they look, so flipping the flags on the instance is enough - and flipping them back
        // restores the prefab's own answer when the dial is turned off.
        public static void ApplyTargeting(TeleportWorld portal)
        {
            try
            {
                if (portal == null) return;
                Piece piece = portal.GetComponent<Piece>();
                if (piece == null) return;

                if (IsProtected(portal))
                {
                    piece.m_randomTarget = false;
                    piece.m_primaryTarget = false;
                    return;
                }

                GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(portal.name.Replace("(Clone)", "")) : null;
                Piece original = prefab != null ? prefab.GetComponent<Piece>() : null;
                if (original != null)
                {
                    piece.m_randomTarget = original.m_randomTarget;
                    piece.m_primaryTarget = original.m_primaryTarget;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"TortalPortal: could not set a portal's monster-target flags (non-fatal). Reason: {ex.Message}");
            }
        }

        // Every loaded portal, brought into line with the dial. Wired to SelfHealingPortals.SettingChanged.
        public static void RefreshLoadedPortals()
        {
            try
            {
                foreach (TeleportWorld portal in UnityEngine.Object.FindObjectsOfType<TeleportWorld>())
                {
                    if (portal != null) ApplyTargeting(portal);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"TortalPortal: could not refresh portal targeting after the setting changed (non-fatal, applies on reload). Reason: {ex.Message}");
            }
        }

        // ---- healing ---------------------------------------------------------------------------------------

        // Called from the per-frame portal update. Cheap: a dictionary lookup per portal per frame, and the
        // actual check every couple of seconds. Only the owner of the ZDO may heal it, which is the same rule
        // vanilla's own repair follows; a client that does not own the gate simply waits for one that does.
        public static void Tick(TeleportWorld portal)
        {
            if (portal == null) return;
            float now = Time.time;

            if (now >= _nextPrune)
            {
                _nextPrune = now + 30f;
                PruneDead();
            }

            if (NextHealCheck.TryGetValue(portal, out float next) && now < next) return;
            NextHealCheck[portal] = now + HealIntervalSeconds;

            try
            {
                if (!IsProtected(portal)) return;
                WearNTear wnt = portal.GetComponent<WearNTear>();
                if (wnt == null || wnt.m_nview == null || !wnt.m_nview.IsValid() || !wnt.m_nview.IsOwner()) return;

                ZDO zdo = wnt.m_nview.GetZDO();
                float health = zdo.GetFloat(ZDOVars.s_health, wnt.m_health);
                if (health >= wnt.m_health) return;

                // Vanilla's own repair path: the owner writes full health and tells everybody, so the visual
                // damage state clears on every client at once.
                wnt.Repair();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"TortalPortal: healing a portal failed (non-fatal). Reason: {ex.Message}");
            }
        }

        private static void PruneDead()
        {
            List<TeleportWorld> dead = null;
            foreach (KeyValuePair<TeleportWorld, float> pair in NextHealCheck)
            {
                if (pair.Key == null) (dead ?? (dead = new List<TeleportWorld>())).Add(pair.Key);
            }
            if (dead == null) return;
            foreach (TeleportWorld key in dead) NextHealCheck.Remove(key);
        }

        // ---- the refusal -----------------------------------------------------------------------------------

        // The hit itself, on the owner, before the damage number is drawn: a refused hit should not float a
        // number over the gate as if it had landed.
        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.RPC_Damage))]
        [HarmonyPrefix]
        public static bool WearNTear_RPC_Damage_Prefix(WearNTear __instance)
        {
            try
            {
                if (__instance == null || __instance.GetComponent<TeleportWorld>() == null) return true;
                return !IsProtected(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(WearNTear_RPC_Damage_Prefix)} failed, letting the hit through. Reason: {ex}");
                return true;
            }
        }

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.ApplyDamage))]
        [HarmonyPrefix]
        public static bool WearNTear_ApplyDamage_Prefix(WearNTear __instance, ref bool __result)
        {
            try
            {
                if (__instance == null || __instance.GetComponent<TeleportWorld>() == null) return true;
                if (!IsProtected(__instance)) return true;

                // Refused, and reported as "not destroyed" to whoever asked.
                __result = false;
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(WearNTear_ApplyDamage_Prefix)} failed, letting the damage through. Reason: {ex}");
                return true;
            }
        }
    }
}
