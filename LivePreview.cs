using System;
using System.Collections;
using UnityEngine;

namespace TortalPortal
{
    // Phase three of the portal anchor: hold it open while the player is actually looking.
    //
    // Open a portal's picture full-size and the stored photograph gives way to a LIVE render of the
    // destination - real weather, real time of day, drawn by this client's own GPU through exactly the
    // machinery the background camera uses: SnapshotAnchor for the world, SectorSubscription for the ZDOs
    // on a dedicated server, the TerrainLod rig for the horizon. Nothing new crosses the wire; the only
    // novelty is that the anchor stays up as long as the album page does.
    //
    // Lifecycle is a DEADMAN SWITCH, not a wired-up close button. The album calls Tick() every frame it
    // draws; half a second without a tick - the album closed, the window closed, the world unloaded, the
    // UI threw - and the feed tears itself down. There is no exit path to forget, which is what makes it
    // failsafe: the worst possible bug in this class is a feed that stops.
    //
    // Not a Harmony patch class - it rides entirely on the anchor built in phases one and two.
    public static class LivePreview
    {
        private enum Phase { Idle, Acquiring, Live }

        // How many RENDERED FRAMES the album may go silent before the feed is torn down. Frames, not
        // seconds, deliberately: the anchor's own zone loading hitches the client, and during a hitch
        // neither the album's ticks NOR the frame counter advance - so a stall cannot kill the feed. A
        // wall-clock deadman could and did: hitch > deadline, teardown, zones unload, restart, zones
        // reload, hitch again - heavy destinations looped forever and never went live. A genuinely
        // closed album keeps rendering frames without ticking, and three of them (~50ms) end the feed.
        private const int DeadmanFrames = 3;

        private static Phase _phase = Phase.Idle;
        private static ZDOID _id = ZDOID.None;
        private static Vector3 _pos;
        private static Quaternion _rot;
        private static int _lastTickFrame;
        private static float _startedAt;
        private static Coroutine _routine;
        private static Camera _cam;
        private static RenderTexture _rt;

        // The background walk checks this: while somebody is watching, the walk waits its turn for the
        // single anchor rather than fighting over it.
        public static bool Active => _phase != Phase.Idle;

        // Called by the album every frame it draws a portal full-size. First call starts the feed, a
        // different portal restarts it, silence stops it. Idempotent within a frame - IMGUI calls its
        // draw several times per frame and all but the first are just a timestamp refresh.
        public static void Tick(ZDOID id, Vector3 pos, Quaternion rot)
        {
            try
            {
                // remoteSnapshots is the transport (anchor + subscription); livePreviews is this feature.
                // Both server-synced, so the server's word is final on each.
                if (!Configuration.remoteSnapshots.Value || !Configuration.livePreviews.Value) return;
                if (Plugin.Instance == null) return;

                _lastTickFrame = Time.frameCount;
                if (_phase != Phase.Idle && id == _id) return;

                Stop(); // switching portals mid-album, or first open
                _id = id;
                _pos = pos;
                _rot = rot;
                _phase = Phase.Acquiring;
                _startedAt = Time.realtimeSinceStartup;
                _routine = Plugin.Instance.StartCoroutine(Run());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: live preview failed to start (non-fatal, the stored picture stands in). Reason: {ex}");
                Stop();
            }
        }

        // What the album should draw for this portal right now: the live texture once the feed is up,
        // null while warming (the album falls back to the stored still).
        public static Texture CurrentTexture(ZDOID id)
        {
            return _phase == Phase.Live && id == _id && _rt != null ? _rt : null;
        }

        public static void Stop()
        {
            try
            {
                // StopCoroutine disposes the iterator, which runs its finally - the teardown lives there,
                // so a stopped feed cleans up exactly like a naturally-ending one.
                if (_routine != null && Plugin.Instance != null) Plugin.Instance.StopCoroutine(_routine);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: stopping the live preview failed (non-fatal). Reason: {ex}");
            }
            _routine = null;
            _phase = Phase.Idle;
            _id = ZDOID.None;
        }

        private static IEnumerator Run()
        {
            bool viaServer = ZNet.instance != null && !ZNet.instance.IsServer();
            bool anchored = false;
            float nextRenew = 0f;

            try
            {
                while (true)
                {
                    // The deadman and the world checks come before anything else, every lap.
                    if (Time.frameCount - _lastTickFrame > DeadmanFrames) yield break;
                    if (ZNetScene.instance == null || ZoneSystem.instance == null || Player.m_localPlayer == null) yield break;
                    if (!Configuration.remoteSnapshots.Value || !Configuration.livePreviews.Value) yield break;

                    if (!anchored)
                    {
                        // The background walk may hold the single anchor mid-shot; wait it out. The album
                        // is showing the stored still meanwhile, and the walk will not START a new shot
                        // while Active is true, so the wait is bounded by one shot's settle budget.
                        if (SnapshotAnchor.IsActive)
                        {
                            yield return new WaitForSeconds(0.25f);
                            continue;
                        }
                        SnapshotAnchor.Set(_pos);
                        anchored = true;
                        if (viaServer)
                        {
                            SectorSubscription.Renew(_pos);
                            nextRenew = Time.realtimeSinceStartup + SectorSubscription.RenewSeconds;
                        }
                    }

                    SnapshotAnchor.PokeZones();
                    if (viaServer && Time.realtimeSinceStartup >= nextRenew)
                    {
                        SectorSubscription.Renew(_pos);
                        nextRenew = Time.realtimeSinceStartup + SectorSubscription.RenewSeconds;
                    }

                    if (_phase == Phase.Acquiring)
                    {
                        // Go live when the scene is genuinely settled - or once the settle budget is
                        // spent, with whatever is standing. A photograph must never show a half-loaded
                        // world because it is forever; a live feed finishing its loading in front of the
                        // viewer is just a view out a window.
                        bool ready = ZNetScene.instance.IsAreaReady(_pos) &&
                                     !Heightmap.HaveQueuedRebuild(_pos, Configuration.AnchorSettleRadiusSafe) &&
                                     SnapshotAnchor.DistantTerrainReady;
                        bool budgetSpent = Time.realtimeSinceStartup - _startedAt > Configuration.AnchorTimeoutSafe;
                        if (ready || budgetSpent)
                        {
                            if (StartCamera())
                            {
                                _phase = Phase.Live;
                                Plugin.Log.LogInfo($"TortalPortal: live preview went live at ({_pos.x:0},{_pos.z:0}) after {Time.realtimeSinceStartup - _startedAt:0.0}s{(ready ? "" : " (settle budget spent - scene finishes loading on screen)")}.");
                            }
                            else yield break; // camera could not be built; the still stands in
                        }
                    }

                    yield return new WaitForSeconds(0.25f);
                }
            }
            finally
            {
                TearDown(anchored, viaServer);
            }
        }

        private static bool StartCamera()
        {
            try
            {
                Configuration.GetCameraResolution(out int w, out int h);
                _rt = new RenderTexture(w, h, 24);

                GameObject go = new GameObject("TortalPortal_LiveCam");
                Camera cam = go.AddComponent<Camera>();
                Camera gameCam = Camera.main;
                if (gameCam != null) cam.CopyFrom(gameCam);

                // Framed exactly like the stored photograph, so going live never jumps the viewpoint.
                go.transform.position = _pos
                                      + _rot * Vector3.forward * PlacementPatch.CameraForwardOffset
                                      + Vector3.up * PlacementPatch.CameraHeight;
                go.transform.rotation = _rot * Quaternion.Euler(PlacementPatch.CameraPitch, 0f, 0f);
                cam.fieldOfView = PlacementPatch.CameraFieldOfView;
                cam.nearClipPlane = 0.15f;
                if (gameCam == null) cam.farClipPlane = 300f;

                // With a target texture set, an ENABLED camera renders to it every frame on its own and
                // never touches the screen - that is the whole live feed.
                cam.targetTexture = _rt;
                cam.ResetAspect();
                cam.enabled = true;

                _cam = cam;
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: live preview camera could not be built (non-fatal, the stored picture stands in). Reason: {ex}");
                if (_cam != null) { UnityEngine.Object.Destroy(_cam.gameObject); _cam = null; }
                if (_rt != null) { UnityEngine.Object.Destroy(_rt); _rt = null; }
                return false;
            }
        }

        private static void TearDown(bool anchored, bool viaServer)
        {
            try
            {
                if (_cam != null) { UnityEngine.Object.Destroy(_cam.gameObject); _cam = null; }
                if (_rt != null) { UnityEngine.Object.Destroy(_rt); _rt = null; }
                if (anchored)
                {
                    SnapshotAnchor.Clear();
                    if (viaServer) SectorSubscription.Release();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: live preview teardown hit an error (the anchor is still dropped). Reason: {ex}");
                // Whatever happened above, the anchor must not survive this.
                try { SnapshotAnchor.Clear(); } catch { }
            }
            _phase = Phase.Idle;
            _id = ZDOID.None;
            _routine = null;
        }
    }
}
