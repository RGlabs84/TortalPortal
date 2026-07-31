using HarmonyLib;
using System;
using TMPro;
using UnityEngine;

namespace TortalPortal
{
    // Valheim raises its centre messages - "Incorrect PIN!" and the rest - in the middle of the screen, which
    // is squarely behind the destination window. The map already dodges the window by centring on the strip of
    // screen the window is not covering; this moves the message to the same place, off the same number, so the
    // two can never disagree about where the visible half is.
    [HarmonyPatch]
    public static class MessageHudPatch
    {
        // The transform we captured _restPos from. A world reload builds a fresh HUD, and a rest position
        // read off an object that no longer exists would be restored onto the new one as a nonsense offset.
        private static RectTransform _tracked;
        private static Vector2 _restPos;
        private static bool _shifted;

        [HarmonyPatch(typeof(MessageHud), nameof(MessageHud.Update))]
        [HarmonyPostfix]
        public static void MessageHud_Update_Postfix(MessageHud __instance)
        {
            try
            {
                TMP_Text centre = __instance.m_messageCenterText;
                RectTransform rt = centre != null ? centre.rectTransform : null;
                if (rt == null) return;

                if (rt != _tracked)
                {
                    _tracked = rt;
                    _restPos = rt.anchoredPosition;
                    _shifted = false;
                }

                float offsetPixels = PortalUIManager.CentreMessageOffset();

                if (Mathf.Approximately(offsetPixels, 0f))
                {
                    if (_shifted)
                    {
                        rt.anchoredPosition = _restPos;
                        _shifted = false;
                    }
                    return;
                }

                // Captured on the way in rather than at Awake: nothing is owed back if the window is never
                // opened, and we never bake in a position some other mod happened to be holding it at.
                if (!_shifted)
                {
                    _restPos = rt.anchoredPosition;
                    _shifted = true;
                }

                // anchoredPosition is in canvas units and the offset is in screen pixels. The HUD canvas is
                // scaled to the player's resolution and UI scale, so the two are only the same number at a
                // scale factor of 1 - dividing through is what keeps the message under the same piece of map
                // on a 4K screen as on a 1080p one.
                Canvas canvas = centre.canvas;
                float scale = canvas != null ? canvas.scaleFactor : 1f;
                if (scale <= 0f) scale = 1f;

                // Reapplied every frame rather than once on open. The message is laid out again as it
                // cross-fades in and out, and the window itself moves with UITextScale, so a single write
                // when the window opened would not survive either.
                rt.anchoredPosition = new Vector2(_restPos.x + offsetPixels / scale, _restPos.y);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(MessageHud_Update_Postfix)} failed, leaving the centre message where vanilla put it. Reason: {ex}");
            }
        }
    }
}
