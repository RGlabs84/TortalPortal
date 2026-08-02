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
            try
            {
                PortalRegistry.OnZNetStart();
                InitializeBackgroundSnapshot();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(ZNet_Start_Postfix)} failed. Reason: {ex}");
            }
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

        // Every portal this peer knows about. On the server that is the whole world; on a client it is the
        // portals loaded around them, which is all a client is ever sent - see PortalRegistry for why the
        // destination list cannot be built from this.
        public static List<ZDO> GetAllPortals()
        {
            List<ZDO> allZdos = new List<ZDO>();
            if (ZDOMan.instance == null) return allZdos;

            // ZDOMan keeps its own portal list, maintained as ZDOs are loaded, received and destroyed. It
            // covers every prefab in Game.PortalPrefabHash, so modded portal pieces come along for free, and
            // it does not walk every sector in the world the way the prefab scan below does.
            List<ZDO> known = ZDOMan.instance.GetPortals();
            if (known != null)
            {
                foreach (ZDO zdo in known)
                {
                    if (zdo != null && zdo.IsValid()) allZdos.Add(zdo);
                }
            }
            if (allZdos.Count > 0) return allZdos;

            // Belt and braces for a world that somehow loaded without that list populated.
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

        // How often the backfill looks around for a portal that has never been photographed. 12.5s rather than
        // the original 15s - a 20% faster shutter, asked for because a world full of unphotographed portals
        // takes a long time to fill in.
        //
        // Two kinds of portal get filled in per tick: the ones spawned around this player (the ordinary
        // pass, CollectLoadedPortals), and - on the hosting player only - ONE portal nobody is near, loaded
        // briefly through SnapshotAnchor, photographed and unloaded again. A client of a dedicated server
        // can only ever do the first kind: it is never sent ZDOs for distant sectors, so it has nothing to
        // anchor to.
        private const float BackfillScanSeconds = 12.5f;

        // How long the anchor pass will wait for the far zone's terrain and objects to stream in before
        // shooting with whatever has arrived. Object creation is budgeted at roughly 300 a second, so this
        // covers a large base around the portal; the common case is ready in two or three seconds.
        private const float AnchorLoadTimeoutSeconds = 25f;

        private static Coroutine _snapshotRoutine;

        public static void InitializeBackgroundSnapshot()
        {
            // A snapshot is a camera render, so only a peer that actually draws the world can take one. A
            // dedicated server draws nothing - which is why 1.0.0, where this ran server-side only, left
            // every portal on a dedicated server without a photograph indefinitely.
            if (ZNet.instance == null || ZNet.instance.IsDedicated()) return;

            // ZNet.Start runs again on every world load; these coroutines live on the plugin object, which
            // does not get torn down with the world.
            if (_snapshotRoutine != null) Plugin.Instance.StopCoroutine(_snapshotRoutine);
            _snapshotRoutine = Plugin.Instance.StartCoroutine(BackgroundSnapshotRoutine());
        }

        private static IEnumerator BackgroundSnapshotRoutine()
        {
            float nextRefresh = Time.realtimeSinceStartup + 60f;

            while (true)
            {
                yield return new WaitForSeconds(BackfillScanSeconds);

                if (!Configuration.enablePortalCameraShot.Value ||
                    ZNetScene.instance == null || ZDOMan.instance == null || Player.m_localPlayer == null)
                {
                    continue;
                }

                // Gaps first. A world that already had portals when the mod was installed has none of them
                // photographed, and this is what fills them in: each player shoots the portals loaded around
                // them, so between everyone playing the network completes itself.
                yield return Plugin.Instance.StartCoroutine(SnapshotLoadedPortals(missingOnly: true, max: 2));

                // Then the portals nobody is walking near. The hosting player holds the whole world's ZDOs,
                // so their client can anchor-load one far portal per tick, photograph it and let it unload -
                // the camera walking the world on its own. Skipped mid-teleport because the player's own
                // active area is not loaded then, and ZNetScene refuses to create objects until it is.
                if (Configuration.remoteSnapshots.Value &&
                    ZNet.instance != null && ZNet.instance.IsServer() &&
                    !Player.m_localPlayer.IsTeleporting())
                {
                    ZDO remote = FindRemoteUnphotographedPortal();
                    if (remote != null)
                    {
                        yield return Plugin.Instance.StartCoroutine(SnapshotAnchoredPortal(remote));
                    }
                }

                int interval = Configuration.snapshotRefreshIntervalMinutes.Value;
                if (interval > 0 && Time.realtimeSinceStartup >= nextRefresh)
                {
                    nextRefresh = Time.realtimeSinceStartup + interval * 60f;
                    yield return Plugin.Instance.StartCoroutine(SnapshotLoadedPortals(missingOnly: false, max: 3));
                }
            }
            // ReSharper disable once IteratorNeverReturns
        }

        private static IEnumerator SnapshotLoadedPortals(bool missingOnly, int max)
        {
            foreach (TeleportWorld portal in CollectLoadedPortals(missingOnly, max))
            {
                if (portal == null) continue;
                yield return TakeSnapshotRoutine(portal.transform);
            }
        }

        // Round-robin cursor over the unphotographed far portals, so one that keeps timing out (a portal
        // buried in an enormous base, say) delays the others once per lap instead of starving them forever.
        private static int _remoteCursor;

        private static ZDO FindRemoteUnphotographedPortal()
        {
            try
            {
                List<ZDO> candidates = new List<ZDO>();
                foreach (ZDO zdo in GetAllPortals())
                {
                    if (zdo == null || !zdo.IsValid()) continue;

                    byte[] existing = zdo.GetByteArray("PortalSnapshot");
                    if (existing != null && existing.Length > 0) continue;

                    // A portal that is already spawned belongs to the ordinary pass - anchoring it would
                    // just photograph it a tick later than CollectLoadedPortals will anyway.
                    if (ZNetScene.instance.FindInstance(zdo) != null) continue;

                    candidates.Add(zdo);
                }
                if (candidates.Count == 0) return null;
                return candidates[_remoteCursor++ % candidates.Count];
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: scanning for far portals to photograph failed (non-fatal). Reason: {ex}");
                return null;
            }
        }

        private static IEnumerator SnapshotAnchoredPortal(ZDO zdo)
        {
            Vector3 pos = zdo.GetPosition();
            SnapshotAnchor.Set(pos);
            // try/finally (no catch - the iterator rule) so the anchor NEVER outlives this routine: an
            // anchor left standing would keep a patch of far-off world loaded for the rest of the session.
            try
            {
                float deadline = Time.realtimeSinceStartup + AnchorLoadTimeoutSeconds;
                while (Time.realtimeSinceStartup < deadline)
                {
                    if (ZNetScene.instance == null || ZoneSystem.instance == null || Player.m_localPlayer == null)
                    {
                        yield break; // world unloading; finally drops the anchor
                    }

                    SnapshotAnchor.PokeZones();

                    // Vanilla's own readiness test: the zone is built AND every near ZDO in the ring has
                    // an instance. Exactly the sectors the anchor is feeding in.
                    if (ZNetScene.instance.IsAreaReady(pos)) break;

                    yield return new WaitForSeconds(0.25f);
                }

                // Heightmap meshes build on a worker thread a beat behind the zone objects; one settle
                // second keeps the terrain from photographing as a flat sheet. Well inside the 4s zone TTL,
                // so nothing unloads while we hold still.
                yield return new WaitForSeconds(1f);

                ZNetView nview = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(zdo) : null;
                TeleportWorld tw = (nview != null && nview.IsValid()) ? nview.GetComponent<TeleportWorld>() : null;
                if (tw != null)
                {
                    // On the timeout path some props may still be missing from the frame; the portal itself
                    // is instanced first (ZDOs stream in nearest-first) so the shot is still worth taking,
                    // and the refresh pass will retake it properly next time somebody is actually there.
                    yield return TakeSnapshotRoutine(tw.transform);
                }
                else
                {
                    Plugin.Log.LogInfo($"TortalPortal: far portal at {pos} did not stream in within {AnchorLoadTimeoutSeconds}s; will try it again next lap.");
                }
            }
            finally
            {
                SnapshotAnchor.Clear();
            }
        }

        // Only portals whose instance is spawned around this player can be photographed, and on a client
        // GetAllPortals returns exactly those - a client's ZDOMan holds nothing further away.
        private static List<TeleportWorld> CollectLoadedPortals(bool missingOnly, int max)
        {
            List<TeleportWorld> found = new List<TeleportWorld>();
            try
            {
                foreach (ZDO zdo in GetAllPortals())
                {
                    if (found.Count >= max) break;
                    if (zdo == null || !zdo.IsValid()) continue;

                    if (missingOnly)
                    {
                        byte[] existing = zdo.GetByteArray("PortalSnapshot");
                        if (existing != null && existing.Length > 0) continue;
                    }

                    ZNetView nview = ZNetScene.instance.FindInstance(zdo);
                    if (nview == null || !nview.IsValid()) continue;

                    TeleportWorld tw = nview.GetComponent<TeleportWorld>();
                    if (tw != null) found.Add(tw);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: scanning for portals to photograph failed (non-fatal). Reason: {ex}");
            }
            return found;
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

                // Inherit the game camera's clear flags, culling mask, fog and rendering path so the preview
                // looks like the world does rather than a flat render of it.
                Camera gameCam = Camera.main;
                if (gameCam != null) cam.CopyFrom(gameCam);

                // AFTER CopyFrom, which copies the enabled flag along with everything else. Setting it before
                // the copy - as this did - handed the flag straight back, leaving a second live camera in the
                // scene rendering the player's view for a frame before the deferred Destroy caught up with it.
                // We drive this camera with one explicit Render(); it must never draw to the screen.
                cam.enabled = false;

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

                // Store in closest ZDO (the one just placed). Ownership has to be claimed first: a portal
                // somebody else raised is owned by them or by the server, and a write to a ZDO this peer
                // does not own is discarded at the next sync - which is most portals when backfilling a
                // world that already existed.
                var nview = portalTransform.GetComponent<ZNetView>();
                if (nview && nview.IsValid())
                {
                    if (!nview.IsOwner()) nview.ClaimOwnership();
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
