using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TortalPortal
{
    // Phase four of the portal anchor: give the anchored camera its own SKY.
    //
    // The anchor loads world correctly and always did. What it cannot load is the thing that turned
    // out not to be in the world at all. Sky, fog, sun, cloud domes, ash/rain/snow and grass are not
    // zone contents in Valheim - they are ONE global kit that the client builds once, keys to the
    // local player's camera, and teleports there every LateUpdate (the FollowPlayer component). So a
    // camera standing in the Ashlands five kilometres from the player photographed real ground and
    // real trees under SOMEBODY ELSE'S WEATHER: no ash rain, no ashen sky, no foliage but trees.
    // Loading more zone was never going to fix it. Full write-up in libs-Tools/IMPLEMENTATIONS.
    //
    // Three independent mechanisms, each separately guarded so one failing does not cost the others:
    //
    //   1. ATMOSPHERE - Camera.onPreRender/onPostRender bracket the anchored camera's render alone.
    //      Fog, ambient, the sun and EnvMan's shader globals are swapped to the destination's own
    //      environment for that one render and put back immediately. The player's view never sees it.
    //   2. THE KIT - weather particles and the cloud domes have simulated state, so they cannot be
    //      swapped for a single render; the anchor gets its own COPY, stripped of FollowPlayer so it
    //      stays put. Safe because the copy stands kilometres from the player, out of their sight.
    //   3. GRASS - ClutterSystem turns out to be built exactly like ZoneSystem: patches are added
    //      near a point and expire on a 2s timer, with no distance test against any camera. So there
    //      is a poke primitive here in the same shape as PokeLocalZone, and it is purely ADDITIVE -
    //      the player's own grass keeps its own timers.
    //
    // Not a Harmony patch class: two Unity callbacks and three publicized calls, nothing patched.
    public static class AnchorEnvironment
    {
        // EnvMan's own shader globals, by the same names it uses (EnvMan's are private statics).
        private static readonly int SkyboxSunDir = Shader.PropertyToID("_SkyboxSunDir");
        private static readonly int SunDir       = Shader.PropertyToID("_SunDir");
        private static readonly int SunFogColor  = Shader.PropertyToID("_SunFogColor");
        private static readonly int SunColor     = Shader.PropertyToID("_SunColor");
        private static readonly int AmbientColor = Shader.PropertyToID("_AmbientColor");
        private static readonly int Wet          = Shader.PropertyToID("_Wet");
        // Not a global: this one lives on the cloud dome's own material, so the anchor's copy carries
        // its own value and nothing needs bracketing.
        private static readonly int Rain         = Shader.PropertyToID("_Rain");
        // The player's reference position, published globally by EnvMan.Update every frame. Not known
        // to be read by any shader for distance fade - Valheim's shaders are not in the decompile - so
        // this is a hedge: pointing it at the anchor during the anchor's own render costs one vector
        // and closes off the only mechanism that could make correctly-loaded geometry refuse to draw.
        private static readonly int NetRefPos    = Shader.PropertyToID("_NetRefPos");

        private static bool _active;
        private static Vector3 _pos;
        private static Camera _cam;
        private static GameObject _kit;
        private static Renderer _cloudsRenderer;
        private static MaterialPropertyBlock _mpb;
        private static bool _hooked;
        // Stand-down latches, one per mechanism. A mechanism that throws stops trying rather than
        // flooding the log from a per-frame call, but it takes only ITSELF down with it.
        private static bool _grassFailed;
        private static bool _envFailed;
        private static bool _announced;

        // The destination's environment, resolved once per (period, biome) rather than per frame -
        // the selection runs a seeded RNG and walks the biome's weighted table.
        private static EnvSetup _env;
        private static long _envPeriod = long.MinValue;
        private static Heightmap.Biome _envBiome = Heightmap.Biome.None;

        // Everything the swap touches, captured on the way in and put back on the way out. If any
        // field is added to Apply() it must be added here too, or it leaks into the player's view.
        private static bool _saved;
        private static Quaternion _sLightRot;
        private static Vector3 _sLightPos;
        private static float _sLightIntensity;
        private static Color _sLightColor;
        private static Color _sFogColor;
        private static float _sFogDensity;
        private static AmbientMode _sAmbientMode;
        private static Color _sAmbientLight;
        private static Vector4 _sSkyboxSunDir, _sSunDir, _sSunFogColor, _sSunColor, _sAmbientColor, _sNetRefPos;
        private static float _sWet;

        public static bool IsActive => _active;

        // ---- lifecycle -------------------------------------------------------------------------------

        // Called the moment the anchor goes up - BEFORE the camera exists and before the settle wait,
        // deliberately: the particle copy needs those seconds to fill, or the first photograph catches
        // ash that has only just started falling.
        public static void Attach(Vector3 anchorPos)
        {
            try
            {
                if (!Configuration.anchoredEnvironment.Value) return;
                if (EnvMan.instance == null) return;

                Detach(); // never stack two

                _pos = anchorPos;
                _active = true;
                _grassFailed = false;
                _envFailed = false;
                _announced = false;
                _envPeriod = long.MinValue;
                _envBiome = Heightmap.Biome.None;
                _env = ResolveEnv(anchorPos);

                if (!_hooked)
                {
                    Camera.onPreRender += OnPreRender;
                    Camera.onPostRender += OnPostRender;
                    _hooked = true;
                }

                BuildKit(anchorPos);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: anchoring the environment failed (non-fatal, the shot keeps the player's own sky). Reason: {ex}");
                Detach();
            }
        }

        // The camera to bracket. Separate from Attach because the snapshot path builds its camera long
        // after the anchor goes up, and the live feed builds its own only once the scene has settled.
        public static void Bind(Camera cam)
        {
            if (!_active) return;
            _cam = cam;
        }

        public static void Detach()
        {
            try
            {
                // A render that threw between pre and post would have left the player's own sky
                // wearing the destination's weather. Cheap insurance; normally a no-op.
                if (_saved) Restore();

                if (_hooked)
                {
                    Camera.onPreRender -= OnPreRender;
                    Camera.onPostRender -= OnPostRender;
                    _hooked = false;
                }
                if (_kit != null)
                {
                    UnityEngine.Object.Destroy(_kit);
                    _kit = null;
                }
                _cloudsRenderer = null;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: tearing down the anchored environment failed (non-fatal). Reason: {ex}");
            }
            _active = false;
            _cam = null;
            _env = null;
        }

        // ---- the settle tick ---------------------------------------------------------------------------

        // Called every tick the anchor is poked, right beside SnapshotAnchor.PokeZones. Two jobs, kept
        // independently guarded so a failure in one cannot cost the other.
        public static void Tick()
        {
            if (!_active) return;
            ResolveDuringSettle();
            PokeGrass();
        }

        // The destination's environment cannot be known when Attach() runs - the anchor has only just
        // gone up and a far zone has no heightmap yet. So it is resolved here instead, on every settle
        // tick, until it takes. Doing it during the settle rather than at first render is the whole
        // point of attaching early: the first successful resolve is what builds the weather particles,
        // and they need those seconds to fill or the feed goes live on ash that has not started falling.
        private static void ResolveDuringSettle()
        {
            if (_envFailed) return;
            try
            {
                EnvSetup env = ResolveEnv(_pos);
                if (env != null) _env = env;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: resolving the destination's environment failed (non-fatal, the shot keeps the player's own sky). Reason: {ex}");
                _envFailed = true;
            }
        }

        // Additive and self-cleaning: ClutterSystem.TimeoutPatches drops any patch untouched for 2
        // seconds, with no reference to any camera, so the anchored grass expires on its own the moment
        // this stops being called and the player's own field is never disturbed.
        private static void PokeGrass()
        {
            if (_grassFailed) return;
            try
            {
                ClutterSystem cs = ClutterSystem.instance;
                if (cs == null || cs.m_quality == ClutterSystem.Quality.Off) return;

                // Vanilla's own pass gates on IsHeightmapReady(), which tests for queued rebuilds at
                // the PLAYER's camera - the wrong place entirely for us, and it would happily let
                // grass generate against terrain still being built. Test at the anchor instead.
                if (Heightmap.HaveQueuedRebuild(_pos, cs.m_distance)) return;

                // At most one NEW patch per call (ClutterSystem's own `generated` latch), so a 40m
                // field is ~81 patches over ~81 frames. Slow on purpose: it costs no frame spike and
                // lands well inside the anchor's existing settle budget.
                cs.GeneratePatches(rebuildAll: false, center: _pos);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: growing grass at the anchor failed (non-fatal, the shot is simply bald). Reason: {ex}");
                // Once is enough - a per-frame call that throws would flood the log. Stands down the
                // grass ONLY; the sky is a separate mechanism and has no reason to die with it.
                _grassFailed = true;
            }
        }

        // ---- the per-camera atmosphere swap -----------------------------------------------------------

        private static void OnPreRender(Camera cam)
        {
            if (!_active || _envFailed || cam == null || cam != _cam) return;
            try
            {
                EnvMan em = EnvMan.instance;
                if (em == null || em.m_dirLight == null) return;

                // The environment is re-resolved here rather than cached forever: a live feed can be
                // held open across an environment period boundary, and the weather should turn over
                // at the destination exactly as it would for somebody standing there.
                //
                // This must stay AHEAD of the null test, not behind it. Attach() runs in the same frame
                // the anchor goes up, when a far destination has no heightmap yet, so its own resolve
                // comes back null by design - and a `_env == null` early-out above this line latches
                // that null forever, which is exactly the bug that made every feed show the player's
                // own sky. The resolve is cheap: it early-outs on an unchanged (period, biome).
                EnvSetup env = ResolveEnv(_pos);
                if (env != null) _env = env;
                if (_env == null) return; // destination genuinely not built yet; try again next frame

                Save(em);
                Apply(em, _env, cam.transform.position);

                // One line, once per anchor, at Info. Whether this swap is running at all has now cost
                // two rounds of guessing from the outside; a silent success and a silent no-op look
                // exactly alike from the far side of a screenshot.
                if (!_announced)
                {
                    _announced = true;
                    Plugin.Log.LogInfo($"TortalPortal: anchored sky applied at ({_pos.x:0},{_pos.z:0}): env '{_env.m_name}', biome {_envBiome}, weather objects {(_kit != null ? _kit.transform.childCount : 0)}.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: swapping the anchored sky failed (non-fatal). Reason: {ex}");
                // Put the player's own sky back before anything else draws, then stand down. The sky
                // only - grass and the cloud copy are separate mechanisms and survive this.
                try { if (_saved) Restore(); } catch { }
                _envFailed = true;
            }
        }

        private static void OnPostRender(Camera cam)
        {
            if (cam == null || cam != _cam) return;
            try { if (_saved) Restore(); }
            catch (Exception ex) { Plugin.Log.LogError($"TortalPortal: restoring the player's sky failed. Reason: {ex}"); }
        }

        private static void Save(EnvMan em)
        {
            Light l = em.m_dirLight;
            _sLightRot = l.transform.rotation;
            _sLightPos = l.transform.position;
            _sLightIntensity = l.intensity;
            _sLightColor = l.color;

            _sFogColor = RenderSettings.fogColor;
            _sFogDensity = RenderSettings.fogDensity;
            _sAmbientMode = RenderSettings.ambientMode;
            _sAmbientLight = RenderSettings.ambientLight;

            _sSkyboxSunDir = Shader.GetGlobalVector(SkyboxSunDir);
            _sSunDir       = Shader.GetGlobalVector(SunDir);
            _sSunFogColor  = Shader.GetGlobalVector(SunFogColor);
            _sSunColor     = Shader.GetGlobalVector(SunColor);
            _sAmbientColor = Shader.GetGlobalVector(AmbientColor);
            _sNetRefPos    = Shader.GetGlobalVector(NetRefPos);
            _sWet          = Shader.GetGlobalFloat(Wet);

            _saved = true;
        }

        private static void Restore()
        {
            EnvMan em = EnvMan.instance;
            if (em != null && em.m_dirLight != null)
            {
                Light l = em.m_dirLight;
                l.transform.rotation = _sLightRot;
                l.transform.position = _sLightPos;
                l.intensity = _sLightIntensity;
                l.color = _sLightColor;
            }

            RenderSettings.fogColor = _sFogColor;
            RenderSettings.fogDensity = _sFogDensity;
            RenderSettings.ambientMode = _sAmbientMode;
            RenderSettings.ambientLight = _sAmbientLight;

            Shader.SetGlobalVector(SkyboxSunDir, _sSkyboxSunDir);
            Shader.SetGlobalVector(SunDir, _sSunDir);
            Shader.SetGlobalVector(SunFogColor, _sSunFogColor);
            Shader.SetGlobalVector(SunColor, _sSunColor);
            Shader.SetGlobalVector(AmbientColor, _sAmbientColor);
            Shader.SetGlobalVector(NetRefPos, _sNetRefPos);
            Shader.SetGlobalFloat(Wet, _sWet);

            _saved = false;
        }

        // The render-state half of EnvMan.SetEnv, and ONLY that half. Deliberately not a call to the
        // real thing: SetEnv also toggles env objects, flips whole particle arrays on and off and
        // queues ambient audio, and driving those twice a frame (out and back) would thrash the
        // player's own weather and audio. Everything below is state read at draw time, so setting it
        // here binds to this camera's render and nothing else.
        //
        // Arithmetic is EnvMan's, transcribed - including the day/night/morning/evening weights that
        // FixedUpdate computes and normalises before handing them to SetEnv.
        private static void Apply(EnvMan em, EnvSetup env, Vector3 camPos)
        {
            float f = em.m_smoothDayFraction;

            float nightInt = Mathf.Pow(Mathf.Max(1f - Mathf.Clamp01(f / 0.25f), Mathf.Clamp01((f - 0.75f) / 0.25f)), 0.5f);
            float dayInt = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(f - 0.5f) / 0.25f), 0.5f);
            float morningInt = Mathf.Min(Mathf.Clamp01(1f - (f - 0.26f) / (0f - em.m_sunHorizonTransitionL)),
                                         Mathf.Clamp01(1f - (f - 0.26f) / em.m_sunHorizonTransitionH));
            float eveningInt = Mathf.Min(Mathf.Clamp01(1f - (f - 0.74f) / (0f - em.m_sunHorizonTransitionH)),
                                         Mathf.Clamp01(1f - (f - 0.74f) / em.m_sunHorizonTransitionL));
            float norm = nightInt + dayInt + morningInt + eveningInt;
            if (norm <= 0f) return;
            norm = 1f / norm;
            nightInt *= norm; dayInt *= norm; morningInt *= norm; eveningInt *= norm;

            Light l = em.m_dirLight;
            l.transform.rotation = Quaternion.Euler(-90f + env.m_sunAngle, 0f, 0f)
                                 * Quaternion.Euler(0f, -90f, 0f)
                                 * Quaternion.Euler(-90f + 360f * f, 0f, 0f);
            Vector3 skyboxSunDir = -l.transform.forward;
            l.intensity = env.m_lightIntensityDay * dayInt + env.m_lightIntensityNight * nightInt;
            if (nightInt > 0f) l.transform.rotation = l.transform.rotation * Quaternion.Euler(180f, 0f, 0f);
            // The sun is a single directional light parked 3km over whoever is looking. For this one
            // render, that is the anchored camera.
            l.transform.position = camPos - l.transform.forward * 3000f;

            Color sun = new Color(0f, 0f, 0f, 0f);
            sun += env.m_sunColorNight * nightInt;
            if (dayInt > 0f)
            {
                sun += env.m_sunColorDay * dayInt;
                sun += env.m_sunColorMorning * morningInt;
                sun += env.m_sunColorEvening * eveningInt;
            }
            l.color = sun;

            Color fog = new Color(0f, 0f, 0f, 0f);
            fog += env.m_fogColorNight * nightInt;
            fog += env.m_fogColorDay * dayInt;
            fog += env.m_fogColorMorning * morningInt;
            fog += env.m_fogColorEvening * eveningInt;
            RenderSettings.fogColor = fog;

            Color sunFog = new Color(0f, 0f, 0f, 0f);
            sunFog += env.m_fogColorSunNight * nightInt;
            if (dayInt > 0f)
            {
                sunFog += env.m_fogColorSunDay * dayInt;
                sunFog += env.m_fogColorSunMorning * morningInt;
                sunFog += env.m_fogColorSunEvening * eveningInt;
            }
            sunFog = Color.Lerp(fog, sunFog, Mathf.Clamp01(Mathf.Max(nightInt, dayInt) * 3f));

            RenderSettings.fogDensity = env.m_fogDensityNight * nightInt
                                      + env.m_fogDensityDay * dayInt
                                      + env.m_fogDensityMorning * morningInt
                                      + env.m_fogDensityEvening * eveningInt;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.Lerp(env.m_ambColorNight, env.m_ambColorDay, dayInt);

            Shader.SetGlobalVector(SkyboxSunDir, skyboxSunDir);
            Shader.SetGlobalVector(SunDir, -l.transform.forward);
            Shader.SetGlobalColor(SunFogColor, sunFog);
            Shader.SetGlobalColor(SunColor, l.color * l.intensity);
            Shader.SetGlobalColor(AmbientColor, RenderSettings.ambientLight);
            // Vanilla eases this over m_wetTransitionDuration because it is watching one place change
            // its mind about the weather. A different place is simply wet or it is not.
            Shader.SetGlobalFloat(Wet, env.m_isWet ? 1f : 0f);
            Shader.SetGlobalVector(NetRefPos, camPos);
        }

        // ---- picking the destination's environment ----------------------------------------------------

        // EnvMan.GetBiome + EnvMan.UpdateEnvironment's selection, transcribed to run at an arbitrary
        // point instead of at the main camera. Faithful down to the seeded RNG, so the anchor gets
        // the SAME weather a player standing there would see, not merely plausible weather.
        private static EnvSetup ResolveEnv(Vector3 pos)
        {
            EnvMan em = EnvMan.instance;
            if (em == null || ZNet.instance == null) return null;

            Heightmap hm = Heightmap.FindHeightmap(pos);
            if (hm == null) return _env; // zone not built yet; keep whatever we had

            bool ashlands = WorldGenerator.IsAshlands(pos.x, pos.z);
            // pos.y, not pos.z. That is what vanilla passes (EnvMan.GetBiome and UpdateEnvironment
            // both do it) and matching it is the point - deviating here would hand the anchor a
            // DIFFERENT environment from the one a player standing on that spot gets.
            bool deepnorth = WorldGenerator.IsDeepnorth(pos.x, pos.y);
            Heightmap.Biome biome = hm.GetBiome(pos, em.m_oceanLevelEnvCheckAshlandsDeepnorth, ashlands || deepnorth);

            long period = (long)ZNet.instance.GetTimeSeconds() / Math.Max(1L, em.m_environmentDuration);
            if (period == _envPeriod && biome == _envBiome && _env != null) return _env;

            List<EnvEntry> available = em.GetAvailableEnvironments(biome);
            if (available == null || available.Count == 0) return _env;

            EnvSetup picked;
            // The selection is seeded off the period so every client agrees on the weather; borrowing
            // the global RNG means putting it back exactly as found, or every seeded system in the
            // game downstream of this frame quietly desyncs.
            UnityEngine.Random.State state = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState((int)period);
                picked = em.SelectWeightedEnvironment(available);
                foreach (EnvEntry entry in available)
                {
                    if (entry.m_ashlandsOverride && ashlands) picked = entry.m_env;
                    if (entry.m_deepnorthOverride && deepnorth) picked = entry.m_env;
                }
            }
            finally
            {
                UnityEngine.Random.state = state;
            }

            if (picked == null) return _env;

            _envPeriod = period;
            _envBiome = biome;
            _env = picked;
            RebuildWeather();
            return picked;
        }

        // ---- the kit: clouds and weather particles ----------------------------------------------------

        // Cloned rather than borrowed or moved. A particle system carries simulated history, so it
        // cannot be swapped for the duration of one render the way fog can - point it somewhere else
        // for a frame and you get a smear or an empty sky. The copy stands at the anchor and simulates
        // there continuously, which is the only way ash actually falls in the picture.
        private static void BuildKit(Vector3 anchorPos)
        {
            EnvMan em = EnvMan.instance;
            if (em == null) return;

            _kit = new GameObject("TortalPortal_AnchorEnvKit");
            // The player is kilometres away and cannot see any of this. The one case that would show
            // is an anchor close enough to overlap the player's own area - which SnapshotAnchor
            // already detects for its own reasons, and which is also the case where none of this is
            // needed, because the real kit is already standing there.
            if (em.m_clouds != null)
            {
                GameObject clouds = Clone(em.m_clouds.gameObject, anchorPos);
                // Kept so the dome's _Rain can be driven per environment below. Vanilla writes that
                // through m_clouds.material, which would instantiate a material we then have to own
                // and destroy; a property block does the same job per-renderer with nothing to leak.
                if (clouds != null) _cloudsRenderer = clouds.GetComponent<Renderer>();
            }
            if (em.m_rainClouds != null) Clone(em.m_rainClouds.gameObject, anchorPos);
            if (em.m_rainCloudsDownside != null) Clone(em.m_rainCloudsDownside.gameObject, anchorPos);
            RebuildWeather();
        }

        // The particle half of the kit, rebuilt whenever the destination's environment turns over.
        private static void RebuildWeather()
        {
            if (_kit == null || _env == null) return;
            try
            {
                // Only the weather is torn down; the cloud domes are environment-independent and stay.
                for (int i = _kit.transform.childCount - 1; i >= 0; i--)
                {
                    Transform child = _kit.transform.GetChild(i);
                    if (child != null && child.name.StartsWith("W_", StringComparison.Ordinal))
                    {
                        UnityEngine.Object.Destroy(child.gameObject);
                    }
                }

                // How heavy the cloud deck looks. Vanilla drives this on the one shared dome from
                // SetEnv; the anchor's dome is its own object, so it can simply be told directly.
                if (_cloudsRenderer != null)
                {
                    if (_mpb == null) _mpb = new MaterialPropertyBlock();
                    _cloudsRenderer.GetPropertyBlock(_mpb);
                    _mpb.SetFloat(Rain, _env.m_rainCloudAlpha);
                    _cloudsRenderer.SetPropertyBlock(_mpb);
                }

                // The environment's own scenery object - the thing that makes an Ashlands sky an
                // ASHLANDS sky rather than a grey one. Vanilla just toggles the single shared instance
                // active, which is no use to us: it is one object and it is standing with the player.
                // Cloned under the W_ prefix so it is torn down when the environment turns over.
                if (_env.m_envObject != null)
                {
                    GameObject envObj = Clone(_env.m_envObject, _pos);
                    if (envObj != null) envObj.name = "W_" + envObj.name;
                }

                // m_psystemsOutsideOnly is deliberately NOT honoured. Vanilla tests whether the PLAYER
                // is under a roof; the anchor is somewhere else entirely and the destination's weather
                // should fall on it regardless of what the viewer happens to be standing under.
                if (_env.m_psystems == null) return;
                foreach (GameObject ps in _env.m_psystems)
                {
                    GameObject clone = Clone(ps, _pos);
                    if (clone != null) clone.name = "W_" + clone.name;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: rebuilding the anchor's weather failed (non-fatal, the shot keeps its sky and loses its rain). Reason: {ex}");
            }
        }

        private static GameObject Clone(GameObject src, Vector3 at)
        {
            if (src == null || _kit == null) return null;
            try
            {
                GameObject clone = UnityEngine.Object.Instantiate(src);
                clone.transform.SetParent(_kit.transform, worldPositionStays: true);

                // FollowPlayer is the whole mechanism that keeps the vanilla kit welded to the
                // player's head, and it would drag this copy straight back there on its first
                // LateUpdate. Disable before destroying: Destroy is deferred to end of frame, and one
                // LateUpdate is all it takes. m_lockYPos is read off it on the way past - that is
                // what holds a cloud dome at its authored altitude rather than at eye level.
                bool lockY = false;
                foreach (FollowPlayer follow in clone.GetComponentsInChildren<FollowPlayer>(true))
                {
                    lockY |= follow.m_lockYPos;
                    follow.enabled = false;
                    UnityEngine.Object.Destroy(follow);
                }

                clone.transform.rotation = src.transform.rotation;
                clone.transform.position = lockY
                    ? new Vector3(at.x, src.transform.position.y, at.z)
                    : at;
                clone.SetActive(true);
                return clone;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: cloning '{src.name}' for the anchor failed (non-fatal). Reason: {ex}");
                return null;
            }
        }
    }
}
