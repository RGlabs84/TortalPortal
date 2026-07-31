using HarmonyLib;
using System;
using UnityEngine;

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

        // Valheim frees the mouse for its own windows by naming each one in UpdateMouseCapture - the map,
        // the inventory, the store, the barber. It has never heard of ours, so the configuration window
        // opened with the cursor still locked to the camera and invisible: the name and PIN fields were
        // right there and could not be clicked. (The destination window got away with it only because it
        // opens the large map, which is on that list.)
        //
        // Runs after the game has had its say, so this is the last word for as long as a window is up.
        [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
        [HarmonyPostfix]
        public static void GameCamera_UpdateMouseCapture_Postfix()
        {
            try
            {
                if (!PortalUIManager.WantsCursor()) return;

                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(GameCamera_UpdateMouseCapture_Postfix)} failed (non-fatal, cursor may stay locked). Reason: {ex}");
            }
        }
    }
}
