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

        // The feed is now the STILL camera run repeatedly, rather than a second kind of camera that
        // happens to point at the same place. That was the whole trouble: an enabled camera drawing
        // its render texture straight to the screen is a different pipeline from Camera.Render() read
        // back into a Texture2D, and every difference between the two showed up as the feed looking
        // unlike the photograph beside it. So there is no second pipeline any more - this is the
        // snapshot path, minus the JPEG, on a timer.
        private static Texture2D _tex;
        private static Coroutine _renderRoutine;

        // A full-resolution ReadPixels is a synchronous GPU readback: the CPU waits for the GPU to
        // finish, every time. That is affordable a few times a minute for a photograph and not at all
        // affordable at the display's frame rate, so the feed runs at its own rate and the game keeps
        // the rest. 30 reads as motion; the camera's own responsiveness to the mouse is unaffected
        // because looking around moves the transform immediately and the next read picks it up.
        private const float LiveFps = 30f;

        // Floor on how soon a feed may open, so the weather copy has a moment to fill and the near
        // objects have a moment to arrive. Short on purpose - it is a floor, not a target.
        private const float MinWarmupSeconds = 2f;

        // How many times to rebuild a camera that failed to build. Failing once used to end the feed
        // outright and leave the stored still standing with no explanation and no second chance.
        private const int CameraAttempts = 3;

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
            // The Texture2D, not the render texture: this is the same surface, in the same format, that
            // the stored photograph is built from, so a feed and the still it replaces are the same
            // picture taken a moment apart.
            return _phase == Phase.Live && id == _id && _tex != null ? _tex : null;
        }

        // ---- looking around --------------------------------------------------------------------------------
        //
        // The feed is a real camera standing at the destination, so looking around is just steering it.
        // The album feeds mouse deltas in; yaw is free, pitch is clamped short of the poles, and the FOV
        // zoom keeps between "peering" and "fisheye". Everything resets when a feed starts, so each
        // portal opens on the photograph's own framing.

        private const float LookDegreesPerPixel = 0.22f;
        private const float ZoomDegreesPerNotch = 2.5f;
        private const float PitchLimit = 80f;
        private const float FovMin = 30f;
        private const float FovMax = 100f;

        private static float _yaw;
        private static float _pitch;

        public static void AddLook(float dxPixels, float dyPixels)
        {
            try
            {
                if (_phase != Phase.Live || _cam == null) return;
                if (float.IsNaN(dxPixels) || float.IsInfinity(dxPixels) ||
                    float.IsNaN(dyPixels) || float.IsInfinity(dyPixels)) return;

                // GUI y grows downward, and so does positive euler pitch - dragging down looks down.
                _yaw = Mathf.Repeat(_yaw + dxPixels * LookDegreesPerPixel, 360f);
                _pitch = Mathf.Clamp(_pitch + dyPixels * LookDegreesPerPixel, -PitchLimit, PitchLimit);
                _cam.transform.rotation = _rot * Quaternion.Euler(PlacementPatch.CameraPitch + _pitch, _yaw, 0f);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: live preview look failed (non-fatal). Reason: {ex}");
            }
        }

        public static void AddZoom(float notches)
        {
            try
            {
                if (_phase != Phase.Live || _cam == null) return;
                if (float.IsNaN(notches) || float.IsInfinity(notches)) return;

                _cam.fieldOfView = Mathf.Clamp(_cam.fieldOfView + notches * ZoomDegreesPerNotch, FovMin, FovMax);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: live preview zoom failed (non-fatal). Reason: {ex}");
            }
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
            int cameraAttempts = 0;
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
                        // The background walk may hold the single anchor mid-shot; wait it out. Since
                        // 1.3.4 the walk also stands DOWN the moment a feed opens, so this is a
                        // handover of a frame or two rather than a wait on a whole settle budget -
                        // hence polling it hard instead of a quarter-second at a time. Every one of
                        // those quarter-seconds was a quarter-second of the viewer staring at a still
                        // wondering whether the thing was broken.
                        if (SnapshotAnchor.IsActive)
                        {
                            yield return null;
                            continue;
                        }
                        SnapshotAnchor.Set(_pos);
                        // Before the settle wait, not after: the weather copy needs those seconds to
                        // fill, or the feed goes live on ash that has only just started falling.
                        AnchorEnvironment.Attach(_pos);
                        AnchorPresence.Set(_pos);
                        anchored = true;
                        if (viaServer)
                        {
                            SectorSubscription.Renew(_pos);
                            nextRenew = Time.realtimeSinceStartup + SectorSubscription.RenewSeconds;
                        }
                    }

                    SnapshotAnchor.PokeZones();
                    // A background photo pass already in flight when this feed opened will have called
                    // Detach on its way past and taken the borrowed sky with it - including this
                    // camera's binding, which is otherwise only ever handed over once, in StartCamera.
                    // The walk now stands aside for a live feed, so this should not fire; it is here
                    // because the cost of being wrong is a whole session watching gates under the
                    // player's own weather, with nothing in the log to say why.
                    if (anchored && Configuration.anchoredEnvironment.Value && !AnchorEnvironment.IsActive)
                    {
                        AnchorEnvironment.Attach(_pos);
                        if (_cam != null) AnchorEnvironment.Bind(_cam);
                    }
                    AnchorEnvironment.Tick();
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

                        // The middle tier, and the reason a feed used to feel unreliable. Perfect
                        // settle is the right bar for a PHOTOGRAPH, which is permanent - but a feed
                        // held to that bar sits on a stale still for ten or fifteen seconds waiting
                        // for a distant-terrain bake, and from the outside that is indistinguishable
                        // from nothing happening. Ground under the camera plus a moment for the near
                        // objects is enough to open the window; the rest arrives while you watch,
                        // which is exactly what looking out of a window is like.
                        bool groundUnderCamera = Heightmap.FindHeightmap(_pos) != null &&
                                                 !Heightmap.HaveQueuedRebuild(_pos, Configuration.AnchorSettleRadiusSafe);
                        bool goodEnough = groundUnderCamera &&
                                          Time.realtimeSinceStartup - _startedAt > MinWarmupSeconds;

                        if (ready || goodEnough || budgetSpent)
                        {
                            if (StartCamera())
                            {
                                _phase = Phase.Live;
                                _renderRoutine = Plugin.Instance.StartCoroutine(RenderLoop());
                                string why = ready ? "settled" : (goodEnough ? "ground ready - the rest arrives on screen" : "settle budget spent");
                                Plugin.Log.LogInfo($"TortalPortal: live preview went live at ({_pos.x:0},{_pos.z:0}) after {Time.realtimeSinceStartup - _startedAt:0.0}s ({why}).");
                            }
                            else
                            {
                                // One failure used to end the feed for good, leaving the stored still
                                // in place with nothing said. A camera can fail to build for reasons
                                // that pass - Camera.main missing for a frame during a scene change
                                // being the obvious one - so try again before giving up.
                                cameraAttempts++;
                                if (cameraAttempts >= CameraAttempts)
                                {
                                    Plugin.Log.LogWarning($"TortalPortal: live preview gave up building a camera at ({_pos.x:0},{_pos.z:0}) after {CameraAttempts} attempts; the stored picture stands in.");
                                    yield break;
                                }
                                yield return new WaitForSeconds(0.25f);
                                continue;
                            }
                        }
                    }

                    // Poll hard while warming up and idle once live: during Acquiring every lap is a
                    // chance to open the window, and a quarter-second granularity there was adding up
                    // to most of what felt like the feed being slow to wake.
                    if (_phase == Phase.Acquiring) yield return null;
                    else yield return new WaitForSeconds(0.25f);
                }
            }
            finally
            {
                TearDown(anchored, viaServer);
            }
        }

        // The snapshot pipeline, on a timer. Deliberately line-for-line what TakeSnapshotCore does
        // between framing the shot and encoding it: bind the anchored sky, render by hand, make the
        // render texture current, read it back into an RGB24 Texture2D, release it. Nothing here is a
        // live-feed-specific way of doing things, because that is exactly what went wrong before.
        //
        // WaitForEndOfFrame, not null: rendering a camera and reading pixels back is safe once the
        // frame's own drawing is done, and doing it mid-frame is how you get a half-drawn read or a
        // fight with whatever else holds RenderTexture.active.
        private static IEnumerator RenderLoop()
        {
            float next = 0f;
            while (_phase == Phase.Live)
            {
                yield return new WaitForEndOfFrame();

                if (_cam == null || _rt == null || _tex == null) yield break;
                if (Time.realtimeSinceStartup < next) continue;
                next = Time.realtimeSinceStartup + 1f / LiveFps;

                RenderTexture previous = RenderTexture.active;
                try
                {
                    // Re-bound every pass rather than once at startup. Binding is what tells the sky
                    // swap which camera it is bracketing, and a background pass that reset the
                    // environment would otherwise leave this camera drawing under the player's own
                    // weather for the rest of the feed - silently, which has already cost a round.
                    AnchorEnvironment.Bind(_cam);

                    _cam.Render();

                    RenderTexture.active = _rt;
                    _tex.ReadPixels(new Rect(0f, 0f, _tex.width, _tex.height), 0, 0);
                    _tex.Apply();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"TortalPortal: live frame failed (non-fatal, the feed holds its last picture). Reason: {ex}");
                }
                finally
                {
                    RenderTexture.active = previous;
                }
            }
        }

        private static bool StartCamera()
        {
            try
            {
                Configuration.GetCameraResolution(out int w, out int h);
                _rt = new RenderTexture(w, h, 24);

                // Each feed opens on the photograph's own framing; whatever the last one was steered to
                // does not carry over.
                _yaw = 0f;
                _pitch = 0f;

                GameObject go = new GameObject("TortalPortal_LiveCam");
                Camera cam = go.AddComponent<Camera>();
                Camera gameCam = Camera.main;
                if (gameCam != null) cam.CopyFrom(gameCam);

                // The same borrowed tonemapper the stored photograph is shot through. Without it the feed
                // went live and visibly washed out against the still it replaced - the exact moment a
                // difference between the two is most obvious.
                PlacementPatch.AttachPostProcessing(go, gameCam, liveFeed: true);

                // Framed exactly like the stored photograph, so going live never jumps the viewpoint.
                go.transform.position = _pos
                                      + _rot * Vector3.forward * PlacementPatch.CameraForwardOffset
                                      + Vector3.up * PlacementPatch.CameraHeight;
                go.transform.rotation = _rot * Quaternion.Euler(PlacementPatch.CameraPitch, 0f, 0f);
                cam.fieldOfView = PlacementPatch.CameraFieldOfView;
                cam.nearClipPlane = 0.15f;
                if (gameCam == null) cam.farClipPlane = 300f;

                cam.targetTexture = _rt;
                cam.ResetAspect();
                // DISABLED on purpose. An enabled camera renders itself, in Unity's own camera loop,
                // straight into the render texture the album then draws - which is not what the
                // snapshot does, and the difference is the reason a feed never looked like the
                // photograph it replaced. RenderLoop drives it by hand instead, exactly as
                // TakeSnapshotCore does, and reads the result back into a Texture2D the same way.
                cam.enabled = false;

                // Same format and same flags as the snapshot's readback surface. Allocated once and
                // reused for the life of the feed - a per-frame Texture2D would be 6MB of garbage at
                // 1080p thirty times a second.
                _tex = new Texture2D(w, h, TextureFormat.RGB24, false);

                // Bracket THIS camera's render with the destination's own sky. Bound after the
                // camera is fully configured, because the swap reads its position.
                AnchorEnvironment.Bind(cam);

                _cam = cam;
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: live preview camera could not be built (non-fatal, the stored picture stands in). Reason: {ex}");
                if (_cam != null) { UnityEngine.Object.Destroy(_cam.gameObject); _cam = null; }
                if (_rt != null) { UnityEngine.Object.Destroy(_rt); _rt = null; }
                if (_tex != null) { UnityEngine.Object.Destroy(_tex); _tex = null; }
                PlacementPatch.ReleaseLiveProfile();
                return false;
            }
        }

        private static void TearDown(bool anchored, bool viaServer)
        {
            try
            {
                // Stopped before the camera it drives is destroyed, not after.
                if (_renderRoutine != null && Plugin.Instance != null)
                {
                    Plugin.Instance.StopCoroutine(_renderRoutine);
                }
                _renderRoutine = null;

                if (_cam != null) { UnityEngine.Object.Destroy(_cam.gameObject); _cam = null; }
                if (_rt != null) { UnityEngine.Object.Destroy(_rt); _rt = null; }
                if (_tex != null) { UnityEngine.Object.Destroy(_tex); _tex = null; }
                PlacementPatch.ReleaseLiveProfile();
                if (anchored)
                {
                    AnchorEnvironment.Detach();
                    AnchorPresence.Clear();
                    SnapshotAnchor.Clear();
                    if (viaServer) SectorSubscription.Release();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: live preview teardown hit an error (the anchor is still dropped). Reason: {ex}");
                // Whatever happened above, neither the anchor nor the borrowed sky may survive this.
                try { AnchorEnvironment.Detach(); } catch { }
                try { AnchorPresence.Clear(); } catch { }
                try { SnapshotAnchor.Clear(); } catch { }
            }
            _phase = Phase.Idle;
            _id = ZDOID.None;
            _routine = null;
        }
    }
}
