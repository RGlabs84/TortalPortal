using HarmonyLib;
using UnityEngine;
using System;

namespace TortalPortal
{
    [HarmonyPatch]
    public static class PortalBehaviorPatch
    {
        // The portal we have already auto-opened the menu for. This used to be a single shared bool, but every
        // loaded portal ticks UpdatePortal, so whichever other portal the player happened to be out of range of
        // cleared the flag each tick and the menu re-opened the instant it was dismissed. Tracking the instance
        // keeps "only auto-open once per approach" scoped to the portal actually being approached.
        private static TeleportWorld _autoOpenedFor;

        // The portal the player has just arrived through. Every loaded portal ticks UpdatePortal, so the
        // destination sees the player standing well inside its activation range the moment the trip finishes
        // and would throw the menu straight back up - which read as the menu never having closed at all.
        private static ZDOID _arrivedThrough = ZDOID.None;

        // Called by the UI just before it hands off to TeleportTo.
        public static void SuppressAutoOpenFor(ZDOID portalId)
        {
            _arrivedThrough = portalId;
        }

        [HarmonyPatch(typeof(TeleportWorld), "Awake")]
        [HarmonyPostfix]
        public static void TeleportWorld_Awake_Postfix(TeleportWorld __instance)
        {
            try
            {
                if (Configuration.enableCustomVFX.Value)
                {
                    StripVanillaPortalVFX(__instance);

                    if (__instance.gameObject.GetComponent<TortalPortalRuneVFX>() == null)
                    {
                        __instance.gameObject.AddComponent<TortalPortalRuneVFX>();
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(TeleportWorld_Awake_Postfix)} failed. Reason: {ex}");
            }
        }

        // EffectFade - which is what TeleportWorld.m_target_found is - caches GetComponentsInChildren<ParticleSystem>()
        // into m_particles during its own Awake, then writes ps.emission.enabled on every entry from SetActive.
        // Unity does not guarantee component Awake order, so destroying those systems here left EffectFade holding
        // destroyed references and the module setter threw "Do not create your own module instances, get them from
        // a ParticleSystem instance" out of SetActive on every tick - which aborted the UpdatePortal prefix before it
        // ever reached the menu code, so the portal menu never opened on approach.
        //
        // Deactivating instead of destroying keeps every reference valid, and an inactive GameObject renders nothing
        // no matter what EffectFade does to its emission module afterwards.
        private static void StripVanillaPortalVFX(TeleportWorld portal)
        {
            GameObject fadeRoot = portal.m_target_found != null ? portal.m_target_found.gameObject : null;

            foreach (ParticleSystem ps in portal.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;

                ps.Clear(true);
                var em = ps.emission;
                em.enabled = false;

                // Never deactivate the portal root (that would hide the portal) or the EffectFade host (that would
                // stop EffectFade updating) - silencing their emission is enough.
                if (ps.gameObject != portal.gameObject && ps.gameObject != fadeRoot)
                {
                    ps.gameObject.SetActive(false);
                }
            }

            if (portal.m_connected != null)
            {
                portal.m_connected.m_effectPrefabs = new EffectList.EffectData[0];
            }
        }

        [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.UpdatePortal))]
        [HarmonyPrefix]
        public static bool TeleportWorld_UpdatePortal_Prefix(TeleportWorld __instance, ref float ___m_colorAlpha, ref bool ___m_hadTarget)
        {
            try
            {
                var nview = __instance.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid() || __instance.m_proximityRoot == null) return true;

                Vector3 gate = __instance.m_proximityRoot.position;

                // Kept on the portal's own activation range: this drives the glow and the connection
                // effect, which should read the same for everyone regardless of anyone's menu preference.
                Player closestPlayer = Player.GetClosestPlayer(gate, __instance.m_activationRange);
                bool anyPlayerInRange = closestPlayer != null && !closestPlayer.IsTeleporting();

                // Measured against the local player directly rather than taken from GetClosestPlayer.
                // Standing at a portal with a friend a step nearer than you made that call return them, so
                // the window never opened for you at all.
                Player local = Player.m_localPlayer;
                bool localPlayerInRange = false;
                if (local != null && !local.IsTeleporting())
                {
                    float openRange = Mathf.Min(Configuration.menuOpenDistance.Value, __instance.m_activationRange);
                    // A little further out to close than to open, so hovering on the boundary does not
                    // strobe the window. How much further is MenuCloseMargin.
                    float range = PortalUIManager.IsUIVisible() && PortalUIManager.CurrentSourcePortal == __instance
                        ? openRange + Mathf.Max(0f, Configuration.menuCloseMargin.Value)
                        : openRange;

                    localPlayerInRange = Vector3.Distance(local.transform.position, gate) <= range;
                }

                bool canTeleport = false;
                if (localPlayerInRange)
                {
                    canTeleport = local.IsTeleportable() || __instance.m_allowAllItems || Configuration.IsAdmin() || Configuration.filterMode.Value == FilterMode.BypassAll;
                    if (!canTeleport) Configuration.LogDebug("Local player is in range but cannot teleport (likely carrying restricted items).");
                }

                UpdatePortalVisuals(__instance, anyPlayerInRange, canTeleport && localPlayerInRange, ref ___m_hadTarget);

                ZDOID portalId = nview.GetZDO().m_uid;

                if (localPlayerInRange && canTeleport)
                {
                    if (_arrivedThrough == portalId)
                    {
                        // Claim the portal as already handled instead of opening on it. Leaving its range
                        // clears _autoOpenedFor as usual, so walking away and coming back opens normally.
                        //
                        // Deliberately not cleared from the out-of-range branch below: a player mid-teleport
                        // counts as out of range, so that would hand the menu straight back. It is scoped to
                        // one ZDOID and the exit point is always well inside activation range, so the only way
                        // it can linger is a player who never arrives - at worst costing one auto-open there.
                        Configuration.LogDebug($"Arrived through portal ZDO {portalId}. Holding its menu closed until the player leaves and returns.");
                        _autoOpenedFor = __instance;
                        _arrivedThrough = ZDOID.None;
                    }
                    else if (_autoOpenedFor != __instance && !PortalUIManager.IsUIVisible())
                    {
                        Configuration.LogDebug($"Local player in range and can teleport. Showing UI for portal ZDO {portalId}.");
                        _autoOpenedFor = __instance;
                        PortalUIManager.ShowUI(__instance);
                    }
                }
                else if (!localPlayerInRange)
                {
                    if (_autoOpenedFor == __instance)
                    {
                        Configuration.LogDebug("Local player left range. Clearing auto-open state for this portal.");
                        _autoOpenedFor = null;
                    }
                    if (PortalUIManager.IsUIVisible() && PortalUIManager.CurrentSourcePortal == __instance)
                    {
                        Configuration.LogDebug("Local player left range while UI was open. Hiding UI.");
                        PortalUIManager.HideUI();
                    }
                }

                return false; // Skip vanilla target connections
            }
            catch (Exception ex)
            {
                if (_autoOpenedFor == __instance) _autoOpenedFor = null;
                Plugin.Log.LogError($"TortalPortal: {nameof(TeleportWorld_UpdatePortal_Prefix)} failed, falling back to vanilla portal update. Reason: {ex}");
                return true;
            }
        }

        // Purely cosmetic, and kept in its own try/catch on purpose: EffectFade.SetActive reaches into
        // ParticleSystem modules, so anything that invalidates those systems throws from here. Losing the glow
        // must never cost us the teleport menu, which is exactly what happened when this ran inline.
        private static void UpdatePortalVisuals(TeleportWorld portal, bool anyPlayerInRange, bool localCanTeleport, ref bool hadTarget)
        {
            try
            {
                // Connection effect fires for ANY player (multiplayer friendly), not just the local one.
                if (anyPlayerInRange && !hadTarget && portal.m_connected != null)
                {
                    portal.m_connected.Create(portal.transform.position, portal.transform.rotation);
                }

                // The glowing inner center lights up only if the LOCAL player can actually teleport.
                if (portal.m_target_found != null)
                {
                    portal.m_target_found.SetActive(localCanTeleport);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: portal visuals update failed (non-fatal, teleport menu unaffected). Reason: {ex}");
            }
            finally
            {
                hadTarget = anyPlayerInRange;
            }
        }

        [HarmonyPatch(typeof(TeleportWorld), "Update")]
        [HarmonyPrefix]
        public static bool TeleportWorld_Update_Prefix(TeleportWorld __instance, ref float ___m_colorAlpha, ref bool ___m_hadTarget)
        {
            try
            {
                ___m_colorAlpha = Mathf.MoveTowards(___m_colorAlpha, ___m_hadTarget ? 1f : 0f, Time.deltaTime);
                __instance.m_model.material.SetColor("_EmissionColor", Color.Lerp(__instance.m_colorUnconnected, __instance.m_colorTargetfound, ___m_colorAlpha));

                var vfx = __instance.GetComponent<TortalPortalRuneVFX>();
                if (vfx != null)
                {
                    // Ensure the effect matches vanilla target discovery activation state
                    vfx.Charge = ___m_hadTarget ? 1f : 0.2f; 
                }

                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(TeleportWorld_Update_Prefix)} failed, falling back to vanilla update. Reason: {ex}");
                return true;
            }
        }

        // Intercept vanilla teleport to do nothing if called manually, since we handle it directly
        [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
        [HarmonyPrefix]
        public static bool TeleportWorld_Teleport_Prefix()
        {
            try
            {
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(TeleportWorld_Teleport_Prefix)} failed, falling back to vanilla teleport. Reason: {ex}");
                return true;
            }
        }
    }
}
