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
        // the inventory, the store, the barber. It has never heard of ours, so a window of ours opens with
        // the cursor still locked to the camera and invisible: the fields are right there and cannot be
        // clicked. (The destination window got away with it only because it opens the large map, which IS
        // on that list.)
        //
        // A PREFIX that replaces the method, not a postfix that corrects it afterwards. Correcting it
        // afterwards still let vanilla run `Cursor.lockState = Locked` first, and Unity SNAPS the cursor to
        // the centre of the screen the moment it is locked - so every single frame the pointer was yanked
        // to the middle and then released, which is unusable and reads exactly like the mouse being
        // grabbed. The configuration window took the worst of it because it closes the map, leaving
        // Minimap.IsOpen() false and vanilla's lock branch firing continuously underneath it.
        [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
        [HarmonyPrefix]
        public static bool GameCamera_UpdateMouseCapture_Prefix()
        {
            try
            {
                if (!PortalUIManager.WantsCursor()) return true;

                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(GameCamera_UpdateMouseCapture_Prefix)} failed (non-fatal, falling back to vanilla cursor handling). Reason: {ex}");
                return true;
            }
        }
    }
}
