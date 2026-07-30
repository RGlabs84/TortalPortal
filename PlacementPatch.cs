using HarmonyLib;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

namespace TortalPortal
{
    [HarmonyPatch]
    [HarmonyPatch]
    public static class PlacementPatch
    {
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Start))]
        [HarmonyPostfix]
        public static void ZNet_Start_Postfix()
        {
            InitializeBackgroundSnapshot();
        }

        [HarmonyPatch(typeof(Player), nameof(Player.UpdatePlacement))]
        [HarmonyPrefix]
        public static void Player_UpdatePlacement_Prefix(Player __instance, ref bool __state)
        {
            try
            {
                if (!__instance.m_placementMarkerInstance) return;

                Piece piece = __instance.m_placementMarkerInstance.GetComponent<Piece>();
                if (piece != null && piece.GetComponent<TeleportWorld>() != null)
                {
                    __state = true; // We are placing a portal
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(Player_UpdatePlacement_Prefix)} failed (non-fatal). Reason: {ex}");
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
        [HarmonyPrefix]
        public static bool Player_PlacePiece_Prefix(Player __instance, Piece piece, out bool __state)
        {
            __state = false;
            try
            {
                if (piece == null || piece.GetComponent<TeleportWorld>() == null) return true;

                int maxPortals = Configuration.maxPortalsPerPlayer.Value;
                if (maxPortals > 0 && !Configuration.IsAdmin())
                {
                    long playerID = Game.instance.GetPlayerProfile().GetPlayerID();
                    int currentPortals = CountPlayerPortals(playerID);

                    if (currentPortals >= maxPortals)
                    {
                        __instance.Message(MessageHud.MessageType.Center, $"Maximum portals reached ({maxPortals}).");
                        return false;
                    }
                }

                __state = true;
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(Player_PlacePiece_Prefix)} failed, falling back to vanilla placement. Reason: {ex}");
                return true;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
        [HarmonyPostfix]
        public static void Player_PlacePiece_Postfix(Player __instance, Piece piece, Vector3 pos, Quaternion rot, bool __state)
        {
            try
            {
                if (!__state || piece == null) return;
                
                TeleportWorld twPrefab = piece.GetComponent<TeleportWorld>();
                if (twPrefab != null && Configuration.enablePortalCameraShot.Value)
                {
                    // Delay screenshot slightly to ensure piece is fully initialized in the world
                    Plugin.Instance.StartCoroutine(FindAndSnapshotRoutine(pos));
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(Player_PlacePiece_Postfix)} failed (non-fatal). Reason: {ex}");
            }
        }

        private static IEnumerator FindAndSnapshotRoutine(Vector3 pos)
        {
            yield return new WaitForSeconds(0.5f); // Wait for instantiation and physics

            Collider[] cols = Physics.OverlapSphere(pos, 2f);
            TeleportWorld instantiatedPortal = null;
            foreach (Collider c in cols)
            {
                TeleportWorld tw = c.GetComponentInParent<TeleportWorld>();
                if (tw != null)
                {
                    instantiatedPortal = tw;
                    break;
                }
            }

            if (instantiatedPortal != null)
            {
                yield return Plugin.Instance.StartCoroutine(TakeSnapshotRoutine(instantiatedPortal.transform));
            }
        }

        public static List<ZDO> GetAllPortals()
        {
            List<ZDO> allZdos = new List<ZDO>();
            if (ZDOMan.instance == null) return allZdos;
            
            int index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative("portal_wood", allZdos, ref index)) { }
            index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative("portal_stone", allZdos, ref index)) { }
            index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative("portal_blackmarble", allZdos, ref index)) { }
            
            return allZdos;
        }

        private static int CountPlayerPortals(long playerID)
        {
            int count = 0;
            List<ZDO> allZdos = GetAllPortals();
            
            foreach (ZDO zdo in allZdos)
            {
                if (zdo.GetLong(ZDOVars.s_creator) == playerID)
                {
                    count++;
                }
            }
            return count;
        }

        private static IEnumerator TakeSnapshotRoutine(Transform portalTransform)
        {
            yield return new WaitForEndOfFrame(); // wait a frame
            TakeSnapshotCore(portalTransform);
        }

        public static void InitializeBackgroundSnapshot()
        {
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                Plugin.Instance.StartCoroutine(BackgroundSnapshotRoutine());
            }
        }

        private static IEnumerator BackgroundSnapshotRoutine()
        {
            while (true)
            {
                int interval = Configuration.snapshotRefreshIntervalMinutes.Value;
                if (interval <= 0)
                {
                    yield return new WaitForSeconds(60f); // Check again next minute if config changes
                    continue;
                }

                yield return new WaitForSeconds(interval * 60f);
                if (ZNetScene.instance == null || ZDOMan.instance == null) continue;

                List<ZDO> allZdos = GetAllPortals();
                int updated = 0;
                
                foreach (var zdo in allZdos)
                {
                    ZNetView nview = ZNetScene.instance.FindInstance(zdo);
                    if (nview != null)
                    {
                        TeleportWorld tw = nview.GetComponent<TeleportWorld>();
                        if (tw != null)
                        {
                            Plugin.Instance.StartCoroutine(TakeSnapshotRoutine(tw.transform));
                            updated++;
                            if (updated >= 3) break;
                        }
                    }
                }
            }
        }

        // Extracted from TakeSnapshotRoutine: C# forbids "yield return" inside a try block that has
        // a catch clause, so the risky post-yield work lives in this ordinary (non-iterator) method.
        // How the destination preview is framed. The camera used to sit 1.5m BEHIND the portal shooting through
        // the arch, so every preview was the gate itself with the rune VFX blazing across the middle of it and
        // the actual destination hidden behind. Standing it out in front looking away puts the portal, and
        // everything we draw inside it, behind the camera entirely.
        private const float CameraForwardOffset = 2.0f;   // metres out the front of the gate
        private const float CameraHeight = 1.7f;          // roughly eye level
        private const float CameraPitch = 8f;             // nose down a touch, so the shot is ground not sky
        private const float CameraFieldOfView = 78f;      // wide angle

        private static void TakeSnapshotCore(Transform portalTransform)
        {
            GameObject camObj = null;
            RenderTexture rt = null;
            Texture2D tex = null;
            TortalPortalRuneVFX vfx = null;
            try
            {
                camObj = new GameObject("PortalSnapshotCam");
                Camera cam = camObj.AddComponent<Camera>();
                cam.enabled = false; // we drive it with an explicit Render(); it must never draw to the screen

                // Inherit the game camera's clear flags, culling mask, fog and rendering path so the preview
                // looks like the world does rather than a flat render of it.
                Camera gameCam = Camera.main;
                if (gameCam != null) cam.CopyFrom(gameCam);

                camObj.transform.position = portalTransform.position
                                          + portalTransform.forward * CameraForwardOffset
                                          + Vector3.up * CameraHeight;
                camObj.transform.rotation = portalTransform.rotation * Quaternion.Euler(CameraPitch, 0f, 0f);

                cam.fieldOfView = CameraFieldOfView;
                cam.nearClipPlane = 0.15f;
                if (gameCam == null) cam.farClipPlane = 300f;

                rt = new RenderTexture(1280, 720, 24);
                cam.targetTexture = rt;
                cam.ResetAspect(); // CopyFrom brings the game window's aspect with it; the RT's is what matters

                // Belt and braces now that the camera faces away from the gate: the motes drift in WORLD space
                // and can wander into frame. This hides only what the rune VFX drew - the old code called
                // SetActive(false) on vfx.gameObject, which IS the portal root, briefly taking the whole piece
                // (ZNetView and all) down mid-frame.
                vfx = portalTransform.GetComponent<TortalPortalRuneVFX>();
                if (vfx != null) vfx.SetVisible(false);

                cam.Render();

                if (vfx != null)
                {
                    vfx.SetVisible(true);
                    vfx = null; // restored; keep the finally from doing it twice
                }

                RenderTexture.active = rt;
                tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                tex.Apply();

                cam.targetTexture = null;
                RenderTexture.active = null;

                byte[] jpgBytes = tex.EncodeToJPG(75); // Higher quality JPG

                // Store in closest ZDO (the one just placed)
                var nview = portalTransform.GetComponent<ZNetView>();
                if (nview && nview.IsValid())
                {
                    nview.GetZDO().Set("PortalSnapshot", jpgBytes);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: Portal snapshot capture failed (non-fatal, portal placement unaffected). Reason: {ex}");
            }
            finally
            {
                // Only set if Render() threw before we could put it back.
                if (vfx != null) vfx.SetVisible(true);

                RenderTexture.active = null;
                if (rt != null) UnityEngine.Object.Destroy(rt);
                if (camObj != null) UnityEngine.Object.Destroy(camObj);
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
        }
    }
}
