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
                // Before the dedicated-server bail-out below: the RPC handlers are precisely the part a
                // dedicated server DOES run.
                SectorSubscription.OnZNetStart();
                ScatterCommand.OnZNetStart();
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

        // How often the backfill looks around for a portal that has never been photographed. Has come down
        // twice by request: 15s originally, 12.5s in 1.1.2, and 10.5s now - a further ~16% - because with
        // the anchor walking the far portals the whole world fills at this cadence, not just the cluster
        // around the player.
        //
        // Two kinds of portal get filled in per tick: the ones spawned around this player (the ordinary
        // pass, CollectLoadedPortals), and ONE portal nobody is near, loaded briefly through SnapshotAnchor,
        // photographed and unloaded again - from local memory on the hosting player, over a
        // SectorSubscription on a client of a dedicated server.
        private const float BackfillScanSeconds = 10.5f;

        // How long the anchor pass will wait for the far zone's terrain and objects to stream in and
        // SETTLE. Longer than the original 25s because readiness is now judged properly (see the stability
        // loop) and a shot that is not ready gets skipped rather than taken - so the timeout is the budget
        // for an honest attempt, not a deadline to shoot regardless.
        private const float AnchorLoadTimeoutSeconds = 40f;

        // The area is considered settled when, this many consecutive polls (at 0.25s each), the sector
        // holds a stable number of ZDOs, every one of them has an instance, and no heightmap nearby has a
        // rebuild queued. IsAreaReady alone lied on dedicated servers: it judges only the ZDOs that have
        // ARRIVED so far, so early in the stream a half-empty area reads as complete - which is exactly
        // what the half-loaded pictures were. Stability catches in-flight arrivals; the heightmap check
        // catches terrain edits (TerrainComp) that landed but have not rebuilt their mesh yet.
        private const int AnchorSettledPolls = 6;

        // Radius for the heightmap-rebuild check. The camera stands 2m from the gate looking down at a
        // shallow pitch, so 100m covers everything that reads as foreground long before the fog.
        private const float AnchorSettleRadiusMeters = 100f;

        private static Coroutine _snapshotRoutine;

        public static void InitializeBackgroundSnapshot()
        {
            // A snapshot is a camera render, so only a peer that actually draws the world can take one. A
            // dedicated server draws nothing - which is why 1.0.0, where this ran server-side only, left
            // every portal on a dedicated server without a photograph indefinitely.
            if (ZNet.instance == null || ZNet.instance.IsDedicated()) return;

            // ZNet.Start runs again on every world load; these coroutines live on the plugin object, which
            // does not get torn down with the world - as does every scrap of static state below, so a
            // give-up on one server must not carry into the next world, nor a stopped routine's anchor.
            SnapshotAnchor.Clear();
            _farShotsGivenUp = false;
            _farShotTimeouts = 0;

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

                // Then the portals nobody is walking near - the camera walking the world on its own, one
                // portal per tick. Skipped mid-teleport because the player's own active area is not loaded
                // then, and ZNetScene refuses to create objects until it is.
                if (Configuration.remoteSnapshots.Value && !Player.m_localPlayer.IsTeleporting() &&
                    ZNet.instance != null)
                {
                    if (ZNet.instance.IsServer())
                    {
                        // The hosting player holds the whole world's ZDOs, so candidates come straight out
                        // of local memory and the anchor loads from disk.
                        ZDO remote = FindRemoteUnphotographedPortal();
                        if (remote != null)
                        {
                            yield return Plugin.Instance.StartCoroutine(SnapshotAnchoredPortal(remote));
                        }
                    }
                    else if (!_farShotsGivenUp)
                    {
                        // A client of a dedicated server holds almost nothing, but it does hold the portal
                        // INDEX. Candidates come from there, the portal's own ZDO is pulled with vanilla's
                        // RequestZDO, and the world around it arrives through SectorSubscription.
                        yield return Plugin.Instance.StartCoroutine(ClientFarShotRoutine());
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

        // ---- the client edition, for dedicated servers -----------------------------------------------------

        // Portals closer than this are the walk-near pass's business - and inside roughly this range the
        // anchor's overlap guards would refuse to double-load them anyway. A little beyond the active area
        // (2 zones = 128m) plus the anchor ring.
        private const float FarShotMinDistanceMeters = 250f;

        // A shoot that ends with no world ever arriving most likely means the server does not know the
        // subscription RPC - it is running a pre-1.2 TortalPortal, and every further attempt would burn a
        // full timeout to learn nothing. Three strikes and the client stops for the session. Set back to
        // false on every world load, so updating the server needs no client-side ceremony. Note the flag
        // tracks the world ARRIVING, not the shot landing: an area that arrived but needed longer to
        // settle than the budget is a retry, not a strike.
        private static int _farShotTimeouts;
        private static bool _farShotsGivenUp;
        private static bool _lastAnchorReceivedWorld;
        private static int _registryCursor;

        private static IEnumerator ClientFarShotRoutine()
        {
            // One candidate considered per tick, round-robin over the index the server already pushes to
            // every client for the destination list. No new wire format, nothing to version.
            PortalRegistry.Entry pick = null;
            List<PortalRegistry.Entry> entries = PortalRegistry.GetEntries();
            if (entries.Count == 0 || Player.m_localPlayer == null || ZDOMan.instance == null) yield break;

            Vector3 here = Player.m_localPlayer.transform.position;
            for (int i = 0; i < entries.Count; i++)
            {
                PortalRegistry.Entry cand = entries[_registryCursor++ % entries.Count];
                if (Vector3.Distance(cand.Pos, here) < FarShotMinDistanceMeters) continue;
                pick = cand;
                break;
            }
            if (pick == null)
            {
                Configuration.LogDebug("far pass: every portal in the index is within walking range - nothing for the anchor to do.");
                yield break;
            }

            // The index row says where the portal is; whether it already has its photograph lives on the
            // portal's own ZDO, which this client is not holding. Vanilla's RequestZDO pulls exactly one -
            // the same call the destination window already uses for far snapshots - so the expensive
            // anchor only ever spins up for a portal that genuinely has no picture.
            ZDO zdo = ZDOMan.instance.GetZDO(pick.Id);
            if (zdo == null)
            {
                ZDOMan.instance.RequestZDO(pick.Id);
                float deadline = Time.realtimeSinceStartup + 6f;
                while (zdo == null && Time.realtimeSinceStartup < deadline)
                {
                    yield return new WaitForSeconds(0.25f);
                    if (ZDOMan.instance == null) yield break;
                    zdo = ZDOMan.instance.GetZDO(pick.Id);
                }
            }
            // Never arriving is a stale index row (demolished portal) or a server too old to answer -
            // either way not worth a strike; the cursor has already moved on.
            if (zdo == null || !zdo.IsValid())
            {
                Configuration.LogDebug($"far pass: portal '{pick.Tag}' never arrived from the server (stale index row?); moving on.");
                yield break;
            }

            byte[] existing = zdo.GetByteArray("PortalSnapshot");
            if (existing != null && existing.Length > 0)
            {
                Configuration.LogDebug($"far pass: '{pick.Tag}' already photographed; moving on.");
                yield break;
            }

            yield return Plugin.Instance.StartCoroutine(SnapshotAnchoredPortal(zdo));

            if (_lastAnchorReceivedWorld)
            {
                _farShotTimeouts = 0;
            }
            else if (++_farShotTimeouts >= 3)
            {
                _farShotsGivenUp = true;
                Plugin.Log.LogWarning(
                    "TortalPortal: three far-portal shoots in a row never received their world from the server. " +
                    "It is most likely running a TortalPortal older than 1.2.0, which does not know the sector " +
                    "subscription - far portals will fill in when players walk near them, exactly as before. " +
                    "No further attempts this session.");
            }
        }

        private static IEnumerator SnapshotAnchoredPortal(ZDO zdo)
        {
            Vector3 pos = zdo.GetPosition();
            // On a client the world around the anchor is not on disk - it has to be ASKED for, and kept
            // asked for: the server-side subscription is renewal-driven so a crashed client costs nothing.
            bool viaServer = ZNet.instance != null && !ZNet.instance.IsServer();

            // One line per attempt and one per outcome, at Info on purpose: the whole feature is invisible
            // work far from any player, and a silent success is indistinguishable from a pass that never
            // ran - which has already cost a debugging session.
            float started = Time.realtimeSinceStartup;
            Plugin.Log.LogInfo($"TortalPortal: far pass: anchoring '{zdo.GetString(ZDOVars.s_tag)}' at ({pos.x:0},{pos.z:0}){(viaServer ? " via sector subscription" : " from local memory")}.");

            _lastAnchorReceivedWorld = false;
            SnapshotAnchor.Set(pos);
            // try/finally (no catch - the iterator rule) so the anchor NEVER outlives this routine: an
            // anchor left standing would keep a patch of far-off world loaded for the rest of the session.
            try
            {
                if (viaServer) SectorSubscription.Renew(pos);
                float nextRenew = Time.realtimeSinceStartup + SectorSubscription.RenewSeconds;

                int settledStreak = 0;
                int lastZdoCount = -1;
                float deadline = Time.realtimeSinceStartup + AnchorLoadTimeoutSeconds;
                while (Time.realtimeSinceStartup < deadline)
                {
                    if (ZNetScene.instance == null || ZoneSystem.instance == null || Player.m_localPlayer == null)
                    {
                        yield break; // world unloading; finally drops the anchor
                    }

                    SnapshotAnchor.PokeZones();
                    if (viaServer && Time.realtimeSinceStartup >= nextRenew)
                    {
                        SectorSubscription.Renew(pos);
                        nextRenew = Time.realtimeSinceStartup + SectorSubscription.RenewSeconds;
                    }

                    // Settled = vanilla's readiness (zone built, every KNOWN near ZDO instanced), no
                    // heightmap rebuild pending, and no new ZDOs arrived since the last poll - held for
                    // AnchorSettledPolls in a row, because on a dedicated server the ZDOs trickle in and
                    // any single poll can catch a lull mid-stream.
                    int zdoCount = CountAnchorSectorZdos(pos);
                    bool ready = zdoCount > 0 &&
                                 zdoCount == lastZdoCount &&
                                 ZNetScene.instance.IsAreaReady(pos) &&
                                 !Heightmap.HaveQueuedRebuild(pos, AnchorSettleRadiusMeters) &&
                                 SnapshotAnchor.DistantTerrainReady;
                    lastZdoCount = zdoCount;

                    settledStreak = ready ? settledStreak + 1 : 0;
                    if (settledStreak >= AnchorSettledPolls) break;

                    yield return new WaitForSeconds(0.25f);
                }
                bool settled = settledStreak >= AnchorSettledPolls;

                ZNetView nview = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(zdo) : null;
                TeleportWorld tw = (nview != null && nview.IsValid()) ? nview.GetComponent<TeleportWorld>() : null;

                // The portal instance existing proves the world DID arrive - which is what separates "this
                // server is too old to answer" (strikes toward giving up) from "a big area needed longer
                // than the budget" (just try again later).
                _lastAnchorReceivedWorld = tw != null;

                if (tw != null && settled)
                {
                    yield return TakeSnapshotRoutine(tw.transform);
                    Plugin.Log.LogInfo($"TortalPortal: far pass: photographed '{zdo.GetString(ZDOVars.s_tag)}' after {Time.realtimeSinceStartup - started:0.0}s.");
                }
                else if (tw != null)
                {
                    // Never shoot a half-loaded scene. A missing photograph heals itself on a later lap; a
                    // photograph of missing terrain and bald ground is worse than none and - because the
                    // backfill only chases portals with NO picture - would stand forever.
                    Plugin.Log.LogInfo($"TortalPortal: far portal at {pos} arrived but had not settled within {AnchorLoadTimeoutSeconds}s (still streaming or terrain still building); skipping the shot rather than storing a half-loaded picture.");
                }
                else
                {
                    Plugin.Log.LogInfo($"TortalPortal: far portal at {pos} did not stream in within {AnchorLoadTimeoutSeconds}s; will try it again next lap.");
                }
            }
            finally
            {
                SnapshotAnchor.Clear();
                if (viaServer) SectorSubscription.Release();
            }
        }

        private static readonly List<ZDO> _anchorCountTemp = new List<ZDO>();

        private static int CountAnchorSectorZdos(Vector3 pos)
        {
            if (ZDOMan.instance == null) return 0;
            _anchorCountTemp.Clear();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(pos), SnapshotAnchor.AnchorRing, 0, _anchorCountTemp);
            return _anchorCountTemp.Count;
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
            // The routine that got us here waits a frame between choosing the portal and this call, and in
            // that frame the instance can be unloaded out from under us - the player crossing a zone
            // boundary mid-walk, or an anchor's world being swept up. Seen exactly once in a 100-portal
            // scatter test, as an NRE on .position below; a destroyed Transform compares equal to null.
            if (portalTransform == null) return;

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

                // 85 rather than 75, asked for after the 1.2.0b run. Costs roughly a third more bytes per
                // picture on the wire and in the save; resolution stays 1280x720 on purpose - doubling
                // pixels would double the ZDO payload again, and the preview panel never shows more than
                // this anyway.
                byte[] jpgBytes = tex.EncodeToJPG(85);

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
