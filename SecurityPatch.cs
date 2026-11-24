using HarmonyLib;
using System;

namespace TortalPortal
{
    [HarmonyPatch]
    public static class SecurityPatch
    {
        [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Interact))]
        [HarmonyPrefix]
        public static bool TeleportWorld_Interact_Prefix(TeleportWorld __instance, Humanoid human, bool hold, bool alt, ref bool __result)
        {
            try
            {
                if (hold)
                {
                    if (SkippedPortals.IsSkipped(__instance)) return true;
                    __result = false;
                    return false;
                }

                Configuration.LogDebug("TeleportWorld_Interact_Prefix called.");

                var nview = __instance.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) return true;

                // A gate on SkipPortalPrefabs is not ours: vanilla (and the mod that owns it) handles E, hold
                // included, so this check sits before anything else of ours decides.
                if (SkippedPortals.IsSkipped(nview.GetZDO())) return true;

                // A corpse gate is not anyone's to configure. E at the bed side sends the owner through - the
                // manual trigger for a player who stepped back from the approach and returned - and does
                // nothing for anybody else or at the grave side.
                if (CorpseRun.IsCorpseGate(nview.GetZDO()))
                {
                    __result = CorpseRun.TravelToGrave(__instance);
                    return false;
                }

                // A concealed gate this client has never been handed. E on it IS the discovery: the claim goes
                // to the server, which checks that the player is genuinely standing here before it records
                // anything, and the destination window opens on the gate when the reveal comes back. Once found
                // it falls through to everything below and behaves like any other portal - which, for a gate
                // recording no builder, means only an admin can rename or lock it.
                if (ConcealedPortals.IsUndiscoveredHere(nview.GetZDO()))
                {
                    __result = ConcealedPortals.Claim(__instance, nview.GetZDO());
                    return false;
                }

                if (CanConfigure(__instance))
                {
                    Configuration.LogDebug("Access granted for portal configuration. Showing Config UI.");
                    PortalUIManager.ShowConfigUI(__instance);
                    __result = true;
                    return false;
                }

                Configuration.LogDebug("Access denied for portal configuration - not the creator and not an admin.");

                __result = true; // Let vanilla handle normal tags if they want, or we can just block it
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(TeleportWorld_Interact_Prefix)} failed, falling back to vanilla interact. Reason: {ex}");
                return true;
            }
        }

        // Who is allowed to rename a portal or set its PIN: whoever built it, plus server admins.
        //
        // Shared with the destination window's "Name & PIN" button so the two routes into the configuration
        // window can never disagree about that. Fails closed - a portal we cannot read is not one we should
        // be offering to reconfigure.
        public static bool CanConfigure(TeleportWorld portal)
        {
            try
            {
                if (portal == null || Game.instance == null) return false;

                ZNetView nview = portal.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) return false;

                long creatorID = nview.GetZDO().GetLong(ZDOVars.s_creator);
                long playerID = Game.instance.GetPlayerProfile().GetPlayerID();
                return creatorID == playerID || Configuration.IsAdmin();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(CanConfigure)} failed, treating the portal as not configurable. Reason: {ex}");
                return false;
            }
        }

        [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.SetText))]
        [HarmonyPrefix]
        public static bool TeleportWorld_SetText_Prefix(TeleportWorld __instance, string text)
        {
            try
            {
                // A skipped gate keeps vanilla's tag: whatever pairs it (vanilla or its own mod) reads the tag.
                if (SkippedPortals.IsSkipped(__instance)) return true;

                var nview = __instance.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid() && nview.IsOwner())
                {
                    nview.GetZDO().Set("PortalPIN", text);
                    Player.m_localPlayer.Message(MessageHud.MessageType.Center, string.IsNullOrEmpty(text) ? "Portal lock removed." : "Portal PIN set.");
                }
                return false; // Skip vanilla tag setting
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(TeleportWorld_SetText_Prefix)} failed, falling back to vanilla SetText. Reason: {ex}");
                return true;
            }
        }
    }
}
