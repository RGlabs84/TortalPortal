using HarmonyLib;
using System;

namespace TortalPortal
{
    [HarmonyPatch]
    public static class InputFocusPatch
    {
        // Valheim gates essentially all gameplay input on "is the player typing": Player.TakeInput, the chat
        // key, the inventory toggle and the map's own zoom, pan and pin keys all consult Chat.HasFocus().
        //
        // Our windows are IMGUI, which the game knows nothing about, so clicking into the search box left
        // every keystroke doing both jobs - naming a portal walked you into the sea. Reporting focus through
        // the gate the game already uses stops all of it at once, and costs nothing while our fields are
        // unfocused.
        [HarmonyPatch(typeof(Chat), nameof(Chat.HasFocus))]
        [HarmonyPostfix]
        public static void Chat_HasFocus_Postfix(ref bool __result)
        {
            try
            {
                if (PortalUIManager.IsTextInputActive()) __result = true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(Chat_HasFocus_Postfix)} failed (non-fatal, keyboard may leak into gameplay while typing). Reason: {ex}");
            }
        }
    }
}
