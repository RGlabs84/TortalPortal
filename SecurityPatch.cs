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
                    __result = false;
                    return false;
                }

                Configuration.LogDebug("TeleportWorld_Interact_Prefix called.");

                var nview = __instance.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) return true;

                long creatorID = nview.GetZDO().GetLong(ZDOVars.s_creator);
                long playerID = Game.instance.GetPlayerProfile().GetPlayerID();

                if (creatorID == playerID || Configuration.IsAdmin())
                {
                    Configuration.LogDebug("Access granted for portal configuration. Showing Config UI.");
                    // Owner or Admin can set PIN and Name
                    PortalUIManager.ShowConfigUI(__instance);
                    __result = true;
                    return false;
                }

                Configuration.LogDebug($"Access denied. CreatorID: {creatorID}, PlayerID: {playerID}, IsAdmin: {Configuration.IsAdmin()}");

                __result = true; // Let vanilla handle normal tags if they want, or we can just block it
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(TeleportWorld_Interact_Prefix)} failed, falling back to vanilla interact. Reason: {ex}");
                return true;
            }
        }

        [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.SetText))]
        [HarmonyPrefix]
        public static bool TeleportWorld_SetText_Prefix(TeleportWorld __instance, string text)
        {
            try
            {
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
