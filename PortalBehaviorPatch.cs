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
                ApplyVFXMode(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(TeleportWorld_Awake_Postfix)} failed. Reason: {ex}");
            }
        }

        // Bring one portal into line with whatever EnableCustomVFX currently says. Called from Awake for a
        // portal loading in, and from the config listener for every portal already standing.
        public static void ApplyVFXMode(TeleportWorld portal)
        {
            if (portal == null || portal.gameObject == null) return;

            TortalVanillaVFXState state = portal.gameObject.GetComponent<TortalVanillaVFXState>();

            if (Configuration.enableCustomVFX.Value)
            {
                if (state == null) state = portal.gameObject.AddComponent<TortalVanillaVFXState>();
                state.Strip(portal);

                if (portal.gameObject.GetComponent<TortalPortalRuneVFX>() == null)
                {
                    portal.gameObject.AddComponent<TortalPortalRuneVFX>();
                }
            }
            else
            {
                TortalPortalRuneVFX vfx = portal.gameObject.GetComponent<TortalPortalRuneVFX>();
                // Destroy tears down every emitter it built through its own OnDestroy, so the gateway leaves
                // nothing behind - without that this would strand a dozen orphaned child objects per portal.
                if (vfx != null) UnityEngine.Object.Destroy(vfx);

                if (state != null) state.Restore(portal);
            }
        }

        // Every portal currently loaded. Only ever runs off a config change, so the cost of the scan is paid
        // once per dial turn rather than per frame.
        public static void RefreshLoadedPortals(bool rebuildGateway)
        {
            try
            {
                foreach (TeleportWorld portal in UnityEngine.Object.FindObjectsOfType<TeleportWorld>())
                {
                    if (portal == null) continue;

                    // Colour and orb height are baked when the gateway is built, so changing them means
                    // building it again. Density and brightness are read live every frame and do not.
                    if (rebuildGateway && Configuration.enableCustomVFX.Value)
                    {
                        TortalPortalRuneVFX existing = portal.gameObject.GetComponent<TortalPortalRuneVFX>();
                        if (existing != null) existing.Rebuild();
                        else portal.gameObject.AddComponent<TortalPortalRuneVFX>();
                        continue;
                    }

                    ApplyVFXMode(portal);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: refreshing portal visuals after a settings change failed (non-fatal). Reason: {ex}");
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

                    // HORIZONTAL distance only. m_proximityRoot sits above the ground while a player's
                    // transform is down at their feet, so a full 3D measure always carried a vertical
                    // component of roughly a metre - which is why no MenuOpenDistance below 1 could ever
                    // be satisfied, however close you stood. Flattening it also makes the setting mean
                    // what a player reads it to mean: how far away across the ground.
                    Vector3 toPlayer = local.transform.position - gate;
                    toPlayer.y = 0f;
                    localPlayerInRange = toPlayer.magnitude <= range;
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

                // The last vanilla visual: the "target found" flare. With the custom VFX on it is replaced
                // outright by the rune swirl and stays dark; with the custom VFX off, vanilla behaviour -
                // lit only when the LOCAL player can actually teleport.
                if (portal.m_target_found != null)
                {
                    portal.m_target_found.SetActive(!Configuration.enableCustomVFX.Value && localCanTeleport);
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
