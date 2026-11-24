using HarmonyLib;
using System;
using UnityEngine;

namespace TortalPortal
{
    // The destination window is IMGUI and the large map under it is uGUI, and the two input systems have
    // never heard of each other. Unpatched, every click on the window ALSO landed on the map behind it -
    // toggling pins, starting drags, opening vanilla's pin-name box - and the scroll wheel zoomed the map
    // while it scrolled the portal list. This class is the missing introduction: while the cursor is inside
    // one of our windows, the map's mouse handlers stand down.
    //
    // It is also where click-n-go lives, because it is the same patch site: a click on the open map that
    // lands on one of OUR portal markers selects that portal, and a second click on the selected marker
    // travels. Vanilla's pin handling is skipped for those clicks so the marker does not get a checkmark
    // slapped on it in passing.
    [HarmonyPatch]
    public static class MapInputPatch
    {
        // ---- keep map mouse input out from under our windows ------------------------------------------------

        [HarmonyPatch(typeof(Minimap), "OnMapLeftDown")]
        [HarmonyPrefix]
        public static bool Minimap_OnMapLeftDown_Prefix()
        {
            try
            {
                // Skipping the down-handler kills the click, the double-click and the drag in one place -
                // all three are derived from m_leftDownTime, which never gets set.
                return !PortalUIManager.IsPointerOverWindow();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(Minimap_OnMapLeftDown_Prefix)} failed (non-fatal, click may fall through to the map). Reason: {ex}");
                return true;
            }
        }

        [HarmonyPatch(typeof(Minimap), "OnMapLeftUp")]
        [HarmonyPrefix]
        public static bool Minimap_OnMapLeftUp_Prefix(Minimap __instance)
        {
            try
            {
                if (!PortalUIManager.IsPointerOverWindow()) return true;

                // The press may have started out on the map and been released over the window; letting the
                // state linger would turn the NEXT press into an instant double-click.
                __instance.m_leftDownTime = 0f;
                __instance.m_dragView = false;
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(Minimap_OnMapLeftUp_Prefix)} failed (non-fatal). Reason: {ex}");
                return true;
            }
        }

        // Vanilla 1.0 deleted Minimap.OnMapRightClick and moved its body into the private
        // RemovePinUnderPointer(), which Start() now reaches through an anonymous delegate on m_onRightClick.
        // The compiler-generated delegate has no stable name, so the real method is the patch site. It has
        // TWO callers since 1.0.7 - that right-click delegate and TouchLongPressPerformed() - so this guard
        // also stops a touch long-press deleting a pin under the portal window. That is the same "the
        // pointer is over our UI" case and is covered deliberately. A string rather than nameof: the member
        // is private, and the string keeps the patch honest about targeting something that could move again.
        [HarmonyPatch(typeof(Minimap), "RemovePinUnderPointer")]
        [HarmonyPrefix]
        public static bool Minimap_RemovePinUnderPointer_Prefix()
        {
            try
            {
                // Right-click (or long-press) removes pins in vanilla; through the window that deleted
                // whatever pin happened to sit under the portal list.
                return !PortalUIManager.IsPointerOverWindow();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(Minimap_RemovePinUnderPointer_Prefix)} failed (non-fatal). Reason: {ex}");
                return true;
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapMiddleClick))]
        [HarmonyPrefix]
        public static bool Minimap_OnMapMiddleClick_Prefix()
        {
            try
            {
                // Middle-click pings the whole server; misfiring it through a UI window is how you wake five
                // people up at midnight.
                return !PortalUIManager.IsPointerOverWindow();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(Minimap_OnMapMiddleClick_Prefix)} failed (non-fatal). Reason: {ex}");
                return true;
            }
        }

        // The scroll wheel reaches the map through UpdateMap's takeInput flag, so this is the one place the
        // wheel can be taken away from the zoom while the cursor is over the portal list. Everything after
        // the input block - map centring, drag continuation - still runs, which is exactly what we want.
        [HarmonyPatch(typeof(Minimap), "UpdateMap")]
        [HarmonyPrefix]
        public static void Minimap_UpdateMap_Prefix(ref bool takeInput)
        {
            try
            {
                if (PortalUIManager.IsPointerOverWindow()) takeInput = false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(Minimap_UpdateMap_Prefix)} failed (non-fatal, scroll may zoom the map under the window). Reason: {ex}");
            }
        }

        // ---- marking the chosen destination ------------------------------------------------------------------

        // Has to be a POSTFIX. UpdatePins assigns every pin's icon colour on every run, so anything set
        // before it runs is overwritten in the same frame; running afterwards is the only way a tint sticks.
        // It also re-applies after Minimap destroys and rebuilds a marker (which it does whenever one scrolls
        // off the edge of the map), so the highlight survives panning without any bookkeeping on our side.
        [HarmonyPatch(typeof(Minimap), "UpdatePins")]
        [HarmonyPostfix]
        public static void Minimap_UpdatePins_Postfix()
        {
            try
            {
                PortalUIManager.StyleMapPins();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(Minimap_UpdatePins_Postfix)} failed (non-fatal, the chosen portal's marker will look like the others). Reason: {ex}");
            }
        }

        // ---- click-n-go --------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapLeftClick))]
        [HarmonyPrefix]
        public static bool Minimap_OnMapLeftClick_Prefix(Minimap __instance)
        {
            try
            {
                // Belt and braces: OnMapLeftDown already refuses presses over the window.
                if (PortalUIManager.IsPointerOverWindow()) return false;
                if (!PortalUIManager.IsSelectUIVisible()) return true;

                // 1.0: ZInput.mousePosition became pointerPosition (mouse, or the primary touch), and the
                // radius vanilla hit-tests its own pins with is the private PinInteractRadius property - the
                // old m_removeRadius * (m_largeZoom * 2f) plus a touch allowance. Reading that property
                // keeps a marker hit exactly where vanilla would hit a pin.
                Vector3 world = __instance.ScreenToWorldPoint(ZInput.pointerPosition);
                float radius = __instance.PinInteractRadius;

                // True when the click landed on one of our markers and was acted on - in which case
                // vanilla's pin-toggle must not also run on the same click.
                return !PortalUIManager.HandleMapClick(world, radius);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(Minimap_OnMapLeftClick_Prefix)} failed (non-fatal, falling back to vanilla click). Reason: {ex}");
                return true;
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapDblClick))]
        [HarmonyPrefix]
        public static bool Minimap_OnMapDblClick_Prefix(Minimap __instance)
        {
            try
            {
                // Checked BEFORE the visibility guard on purpose: travelling closes the window, so by the
                // time the trailing double-click of a click-n-go arrives there is no window left to guard
                // with and vanilla would open its pin-name box over the player's arrival.
                if (PortalUIManager.SuppressMapDblClick()) return false;

                if (PortalUIManager.IsPointerOverWindow()) return false;
                if (!PortalUIManager.IsSelectUIVisible()) return true;

                // Click-then-click-to-travel arrives as a double click, and vanilla answers those by opening
                // its pin-name box. Near one of our markers, that box is never what the player meant.
                // Same pointer and radius as OnMapLeftClick above - vanilla's own 1.0 pin lookup.
                Vector3 world = __instance.ScreenToWorldPoint(ZInput.pointerPosition);
                float radius = __instance.PinInteractRadius;
                return !PortalUIManager.HasEntryNear(world, radius);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(Minimap_OnMapDblClick_Prefix)} failed (non-fatal). Reason: {ex}");
                return true;
            }
        }
    }
}
