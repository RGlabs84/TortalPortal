using System.Collections.Generic;
using UnityEngine;

namespace TortalPortal
{
    // The runic gateway effect that lives in the gap between the two standing stones.
    //
    // Built entirely from STOCK Unity components (ParticleSystem, LineRenderer) driving procedurally generated
    // rune textures through a stock particle shader - see TortalRuneTexture and TortalRuneMaterial, both ported
    // from Wings of the Valkyrie. Nothing here ships a custom shader, so none of it can render pink, and none
    // of it needs to be in the AssetBundle: it is all generated at runtime.
    //
    // Five layers, because one particle system on its own reads as a campfire rather than a gateway:
    //   1. VEIL    - a wall of runes filling the doorway, drifting upward and fading. The "surface" you step
    //                through. Each particle draws a different glyph via the 4x4 atlas.
    //   2. RINGS   - two counter-rotating vertical rune circles standing in the gap, so the gateway reads as
    //                machinery that is running rather than a static decal.
    //   3. SIGIL   - a horizontal magic circle scribed on the ground across the threshold, which is what sells
    //                the "this is a portal" read from a distance and from above.
    //   4. MOTES   - slow embers rising out of the sigil and past the pillars, tying the whole site together.
    //   5. ORBIT   - runes circling the whole structure the way vanilla's swirl did, so the portal reads as a
    //                portal from every side and not just through the doorway.
    //
    // The whole thing responds to the player twice over. WAKE is distance: the gate sits fully dormant - no
    // rings, no runes, no motes, exactly like an unlit vanilla portal - until a player (ANY player, so a
    // friend walking up lights it for you too) comes within VFXWakeDistance, and it fades down again when
    // everyone leaves. CHARGE is activity: driven from the behaviour patch while the gate has someone in
    // range, and everything spins faster, brightens and thickens as it climbs.
    public class TortalPortalRuneVFX : MonoBehaviour
    {
        public Color Colour = new Color(0.30f, 1.00f, 0.55f);
        public float GateWidth = 2.4f;
        public float GateHeight = 2.8f;

        // 0 = idle, 1 = fully charged. Driven externally by the walk-in trigger.
        [System.NonSerialized] public float Charge;

        private ParticleSystem _veil;
        private ParticleSystem _motes;
        private LineRenderer _ringA, _ringB, _sigil, _sigilInner;
        private LineRenderer[] _spokes;
        private ParticleSystem _sigilRunes;
        private float _spin;

        // The swirling rune trail that replaces vanilla's target-found flare: an invisible head orbits the
        // structure and drops stationary particles behind it - runes sparsely, soft glow densely - so the
        // trail is a drawn ribbon of the head's actual path, Wings of the Valkyrie style. Manual Emit only;
        // both systems have their emission modules off.
        private ParticleSystem _swirlRunes;
        private ParticleSystem _swirlGlow;
        private float _swirlAngle;
        private Vector3 _lastRuneDrop, _lastGlowDrop;
        private bool _swirlPrimed;

        // 0 = dormant, 1 = fully awake. Chased toward how CLOSE the nearest player is - not toward whether
        // one exists - so the gateway breathes up and down with your approach instead of switching on.
        private float _wake;
        private bool _shown = true;

        // The two ends of the colour band the runes pick from, either side of VFXColour's hue. Derived once
        // per build; brightness is applied on top of them every frame.
        private Color _bandA, _bandB;

        // Local Y that the veil and the two vertical rings - the "orb" hanging in the doorway - are built at.
        // Measured from the portal's own geometry in Start(); see MeasureGateCentre.
        private float _orbHeight;

        // Everything this component created, so it can be hidden without touching the portal. See SetVisible.
        private readonly List<GameObject> _parts = new List<GameObject>();

        private const int RingSegments = 64;

        // Generated ONCE for the whole game, not once per portal. Both generators allocate a fresh Texture2D on
        // every call, and TortalRuneMaterial keys its material cache partly on the texture's instance ID - so
        // regenerating per instance would miss the cache every time and leak a material and two textures per
        // portal. Caching here is what makes that cache actually work.
        private static Texture2D _atlasTex, _softTex;

        private void Start()
        {
            Build();
        }

        // Tear the gateway down and put it back up. Colour and orb height are baked into materials and
        // transforms when the emitters are built, so turning either dial means building again - the config
        // listener calls this rather than waiting for a world reload.
        public void Rebuild()
        {
            Teardown();
            enabled = true;
            Build();
        }

        // Everything this component made, unmade. Without this, destroying the component - which is exactly
        // what turning EnableCustomVFX off does - would strand a dozen child objects per portal, still
        // drawing, with nothing left alive to switch them off.
        private void OnDestroy()
        {
            Teardown();
        }

        private void Teardown()
        {
            foreach (GameObject go in _parts)
            {
                if (go != null) Destroy(go);
            }
            _parts.Clear();

            _veil = null; _motes = null; _sigilRunes = null;
            _swirlRunes = null; _swirlGlow = null;
            _ringA = null; _ringB = null; _sigil = null; _sigilInner = null;
            _spokes = null;

            _wake = 0f;
            _shown = true;        // so the first SetShown(false) after a rebuild actually applies
            _swirlPrimed = false;
        }

        private void Build()
        {
            // The colour dial. Read once per portal build - the material cache is keyed on it, and the
            // rune band either side of it is derived here rather than recomputed every frame.
            if (Configuration.vfxColour != null)
            {
                Color c = Configuration.vfxColour.Value;
                Colour = new Color(c.r, c.g, c.b, 1f);
            }
            BuildColourBand();

            if (_atlasTex == null) _atlasTex = TortalRuneTexture.GenerateRuneAtlas(Color.white);
            if (_softTex == null) _softTex = TortalRuneTexture.GenerateSoftTrail(Color.white);
            Texture2D atlas = _atlasTex;
            Texture2D soft = _softTex;
            Material runeMat = TortalRuneMaterial.GetOrCreateRuneMaterial(atlas, Colour);
            Material glowMat = TortalRuneMaterial.GetOrCreateRuneMaterial(soft, Colour);
            if (runeMat == null || glowMat == null)
            {
                UnityEngine.Debug.LogWarning("[Tortal] rune VFX: no usable particle shader - portal keeps vanilla effects only.");
                enabled = false;
                return;
            }

            // Measure BEFORE building anything, so the only renderers in the hierarchy are the portal's own.
            _orbHeight = MeasureGateCentre();

            BuildVeil(runeMat);
            BuildRings(glowMat);
            BuildSigil(glowMat, runeMat);
            BuildMotes(runeMat);
            BuildSwirl(runeMat, glowMat);

            // Born dormant, like vanilla: a portal loading in at the edge of view distance should not be
            // running its light show for nobody. The first Update wakes it if someone is already close.
            _wake = 0f;
            SetShown(false);
        }

        // Where the middle of the doorway actually is, in local Y.
        //
        // This used to be the constant GateHeight * 0.45, hand-tuned against the wood portal. That constant
        // happens to land almost exactly on the centre of the wood arch - but the stone portal's opening sits
        // about a metre higher, so the same number parked the orb down by its feet while the ground sigil,
        // which is anchored to y=0, stayed correct. Rather than keep a table of per-prefab magic numbers,
        // measure the portal's own solid geometry: that reproduces the wood placement, fixes stone, and costs
        // nothing for black marble or any modded portal.
        //
        // GetComponentsInChildren<MeshRenderer> deliberately - ParticleSystemRenderer and LineRenderer derive
        // from Renderer, not MeshRenderer, so this only ever sees the arch and never anybody's effects.
        private float MeasureGateCentre()
        {
            float tuned = GateHeight * 0.45f;

            Bounds bounds = default(Bounds);
            bool any = false;
            foreach (MeshRenderer r in GetComponentsInChildren<MeshRenderer>())
            {
                if (r == null) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }

            float centre = any ? transform.InverseTransformPoint(bounds.center).y : tuned;

            // A centre on the floor or up in the clouds means we measured something that is not the arch.
            if (centre < 0.5f || centre > 6f)
            {
                Configuration.LogDebug($"Rune VFX: measured gate centre {centre:0.00}m is implausible, using {tuned:0.00}m.");
                centre = tuned;
            }

            float final = centre + Configuration.vfxOrbHeightOffset.Value;
            Configuration.LogDebug($"Rune VFX: gate centre {centre:0.00}m, orb placed at {final:0.00}m.");
            return final;
        }

        // Hide everything this component drew without touching the portal itself. Used by the snapshot camera.
        // Disabling the renderers leaves the systems simulating and every reference valid - unlike deactivating
        // the GameObject, which on the portal root would take the whole piece down with it.
        public void SetVisible(bool visible)
        {
            foreach (GameObject go in _parts)
            {
                if (go == null) continue;
                Renderer r = go.GetComponent<Renderer>();
                if (r != null) r.enabled = visible;
            }
        }

        // ---- 1. The veil: small runes swirling in off the edges of the opening -------------------------
        //
        // This used to be a box emitter filling the doorway with large runes drifting straight up, which read
        // as a sprite sheet scrolling behind a frame. Now the runes are born ON the rim of the opening, given
        // orbital velocity about the gate's own facing axis and a gentle inward pull, so the gateway reads as
        // something drawing glyphs in off its own edges. They are about half the old size and each takes its
        // own shade from a band either side of VFXColour, so the swirl is many-toned rather than one flat wash.
        private void BuildVeil(Material mat)
        {
            GameObject go = Child("RuneVeil", new Vector3(0f, _orbHeight, 0f));
            _veil = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = _veil.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startSpeed = 0f;   // every bit of the motion comes from the orbital module below
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.24f);
            main.startColor = ColourBand(1f);
            main.maxParticles = 260;
            // Local so the veil belongs to the gateway and does not smear if the portal is ever moved.
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            ParticleSystem.EmissionModule em = _veil.emission;
            em.rateOverTime = 26f;

            // A ring standing in the doorway. radiusThickness biases births hard against the rim instead of
            // filling the disc, which is what makes the runes arrive FROM the edges.
            ParticleSystem.ShapeModule sh = _veil.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = GateWidth * 0.5f;
            sh.radiusThickness = 0.25f;

            ParticleSystem.VelocityOverLifetimeModule vel = _veil.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            // Orbital about Z - the gate's own facing axis - is what makes them swirl in the plane of the
            // opening; radial pulls them slowly inward as they go round.
            //
            // ALL THREE axes of a triple must be written, and in the same MinMaxCurve mode. Writing only one
            // left the others as single-constant curves against a two-constant sibling, which is the error
            // this file used to put in the BepInEx log on every portal build: "Particle Velocity curves must
            // all be in the same mode". Unity then discards the module's curves entirely and nothing moves.
            vel.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.orbitalY = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.orbitalZ = new ParticleSystem.MinMaxCurve(0.7f, 1.6f);
            vel.radial = new ParticleSystem.MinMaxCurve(-0.20f, -0.05f);
            vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            vel.y = new ParticleSystem.MinMaxCurve(0.02f, 0.18f);   // a touch of lift, so it is not a flat wheel
            vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);

            FadeInOut(_veil);
            Atlas(_veil);

            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.material = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // ---- 2. Two counter-rotating vertical rune circles ---------------------------------------------
        private void BuildRings(Material glow)
        {
            float r = GateWidth * 0.44f;
            _ringA = BuildCircle("RuneRingA", new Vector3(0f, _orbHeight, 0f), r, glow, 0.055f, true);
            _ringB = BuildCircle("RuneRingB", new Vector3(0f, _orbHeight, 0f), r * 0.68f, glow, 0.04f, true);

            // Spokes across the inner ring turn it from a plain circle into a sigil wheel.
            _spokes = new LineRenderer[4];
            for (int i = 0; i < _spokes.Length; i++)
            {
                GameObject go = Child("RuneSpoke" + i, new Vector3(0f, _orbHeight, 0f));
                LineRenderer lr = go.AddComponent<LineRenderer>();
                float a = (Mathf.PI / _spokes.Length) * i;
                Vector3 d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * (r * 0.68f);
                Line(lr, glow, 0.03f, new[] { -d, d });
                _spokes[i] = lr;
            }
        }

        // ---- 3. Horizontal magic circle scribed across the threshold ------------------------------------
        private void BuildSigil(Material glow, Material runeMat)
        {
            float r = GateWidth * 0.85f;
            _sigil = BuildCircle("SigilOuter", new Vector3(0f, 0.06f, 0f), r, glow, 0.07f, false);
            _sigilInner = BuildCircle("SigilInner", new Vector3(0f, 0.06f, 0f), r * 0.62f, glow, 0.05f, false);

            // Runes standing around the rim of the ground circle, rotating with it.
            GameObject go = Child("SigilRunes", new Vector3(0f, 0.35f, 0f));
            _sigilRunes = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = _sigilRunes.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startSpeed = 0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
            main.startColor = Colour;
            main.maxParticles = 90;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            ParticleSystem.EmissionModule em = _sigilRunes.emission;
            em.rateOverTime = 14f;

            // Emit around the rim only (radiusThickness 0 = the edge, not the disc).
            ParticleSystem.ShapeModule sh = _sigilRunes.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = r * 0.9f;
            sh.radiusThickness = 0f;
            sh.rotation = new Vector3(90f, 0f, 0f); // lay the emitter flat on the ground

            FadeInOut(_sigilRunes);
            Atlas(_sigilRunes);

            ParticleSystemRenderer rr = go.GetComponent<ParticleSystemRenderer>();
            rr.renderMode = ParticleSystemRenderMode.Billboard;
            rr.material = runeMat;
            rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rr.receiveShadows = false;
        }

        // ---- 5. The swirling rune trail circling the whole portal --------------------------------------
        //
        // Wings of the Valkyrie's wingtip-trail technique, bent into a circle: the built-in emission module
        // stays OFF on both systems, and Update walks an invisible head around the structure, calling
        // Emit(1) at the head's position every time it has moved far enough. startSpeed is 0, so each
        // particle stays where it was dropped - the "swirl" on screen is the literal path of the head,
        // glyphs strung sparsely along a dense soft-glow ribbon, and it cannot gap at low framerate or
        // clump when the head slows.
        private void BuildSwirl(Material runeMat, Material glowMat)
        {
            _swirlRunes = BuildTrailSystem("SwirlRunes", runeMat, 0.26f, 0.44f, 1.5f, 2.3f, 90, true);
            _swirlGlow = BuildTrailSystem("SwirlGlow", glowMat, 0.10f, 0.18f, 0.7f, 1.1f, 200, false);
        }

        private ParticleSystem BuildTrailSystem(string name, Material mat, float sizeMin, float sizeMax,
                                                float lifeMin, float lifeMax, int maxParticles, bool runes)
        {
            GameObject go = Child(name, Vector3.zero);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startSpeed = 0f; // dropped, not launched - the trail is the head's path
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = Colour;
            main.maxParticles = maxParticles;
            // Local, so the ribbon belongs to the structure and survives the portal being moved.
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            if (runes) main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            ParticleSystem.EmissionModule em = ps.emission;
            em.enabled = false; // manual Emit only

            FadeInOut(ps);
            if (runes) Atlas(ps);

            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.material = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        // Where the head is right now, in local space: a circle just outside the pillars with a slow
        // vertical wander, so the ribbon weaves around the stone rather than drawing a flat hoop.
        private Vector3 SwirlHead()
        {
            float radius = GateWidth * 1.05f;
            float y = Mathf.Max(0.35f, _orbHeight * 0.55f + Mathf.Sin(_swirlAngle * 0.6f) * (_orbHeight * 0.45f));
            return new Vector3(Mathf.Cos(_swirlAngle) * radius, y, Mathf.Sin(_swirlAngle) * radius);
        }

        private static void EmitAt(ParticleSystem ps, Vector3 localPos)
        {
            if (ps == null) return;
            ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams
            {
                position = localPos,          // interpreted in the system's own (Local) simulation space
                applyShapeToPosition = false
            };
            ps.Emit(ep, 1);
        }

        // ---- 4. Slow embers rising out of the sigil -----------------------------------------------------
        private void BuildMotes(Material mat)
        {
            GameObject go = Child("RuneMotes", new Vector3(0f, 0.2f, 0f));
            _motes = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = _motes.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.26f);
            main.startColor = Colour;
            main.maxParticles = 120;
            main.simulationSpace = ParticleSystemSimulationSpace.World; // embers hang in the air as you walk past
            main.gravityModifier = -0.02f;                              // faint upward lift

            ParticleSystem.EmissionModule em = _motes.emission;
            em.rateOverTime = 10f;

            ParticleSystem.ShapeModule sh = _motes.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = GateWidth * 0.9f;
            sh.rotation = new Vector3(90f, 0f, 0f);

            FadeInOut(_motes);
            Atlas(_motes);

            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.material = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        private void Update()
        {
            // How CLOSE the nearest player is, not whether one exists. The old boolean-with-a-ramp is what
            // made the gateway read as a switch: it was either running flat out or off, with a fixed
            // three-quarter-second slide between the two regardless of what the player was doing.
            float target = Proximity();

            // Rising is brisk - the gate noticing you - and falling is a long exhale, so walking away bleeds
            // the gateway off behind you rather than snapping it shut.
            _wake = Mathf.MoveTowards(_wake, target, Time.deltaTime * (target > _wake ? 1.6f : 0.45f));

            // Renderers only go off once the fade has finished AND every emitter has run dry. Nothing is
            // ever cleared on the way down: the runes already in the air live out their own lifetimes and
            // fade on their own colour curve, which is exactly how the Wings of the Valkyrie trail bleeds
            // off when a player stops flying. Clearing them - which this used to do - cut the fade off
            // mid-air and was the other half of the on/off read.
            if (_wake > 0.001f)
            {
                if (!_shown) SetShown(true);
            }
            else if (_shown && !AnyLiveParticles())
            {
                SetShown(false);
                return;
            }
            else if (!_shown)
            {
                return; // fully asleep: nothing moves, nothing emits, nothing renders
            }

            // Charge decays on its own, so the effect settles back to idle the moment the player steps out
            // even if the trigger stops reporting (leaving the volume, dying, being teleported away).
            Charge = Mathf.Max(0f, Charge - Time.deltaTime * 1.4f);

            // Two independent dials. Intensity is HOW MUCH gateway there is - density, rune spacing, stroke
            // width. Brightness is only how hard it burns. They used to be the same number, so anyone who
            // wanted a dimmer gate got a thinner one as well.
            float k = Configuration.vfxIntensity != null ? Configuration.vfxIntensity.Value : 1f;
            float bright = Configuration.vfxBrightness != null ? Configuration.vfxBrightness.Value : 1f;

            float c = Mathf.Clamp01(Charge) * _wake;

            // What everything visible is multiplied by. Distance dims as well as thins the gate, but never
            // to nothing while it is still emitting - a far-off portal should read as a low ember, not a
            // black one.
            float glow = bright * Mathf.Lerp(0.35f, 1f, _wake) * (1f + 1.2f * c);
            _spin += Time.deltaTime * (18f + 150f * c) * _wake;

            // Walk the swirl head and let it drop its trail. Speed rides the charge, so the ribbon lazes
            // around a quiet gate and whips around a charged one; distance-gated emission keeps the spacing
            // even at either pace. Intensity tightens the rune spacing rather than the glow's - the ribbon
            // is already continuous.
            _swirlAngle += Time.deltaTime * (0.9f + 2.6f * c) * _wake;
            Vector3 head = SwirlHead();
            if (!_swirlPrimed)
            {
                _swirlPrimed = true;
                _lastRuneDrop = head;
                _lastGlowDrop = head;
            }
            if (Vector3.Distance(head, _lastGlowDrop) > 0.10f)
            {
                EmitAt(_swirlGlow, head);
                _lastGlowDrop = head;
            }
            if (Vector3.Distance(head, _lastRuneDrop) > Mathf.Max(0.18f, 0.55f / k))
            {
                EmitAt(_swirlRunes, head);
                _lastRuneDrop = head;
            }

            // Vertical rings counter-rotate; the ground sigil turns the other way again.
            if (_ringA != null) _ringA.transform.localRotation = Quaternion.Euler(0f, 0f, _spin);
            if (_ringB != null) _ringB.transform.localRotation = Quaternion.Euler(0f, 0f, -_spin * 1.6f);
            if (_spokes != null)
                foreach (LineRenderer s in _spokes)
                    if (s != null) s.transform.localRotation = Quaternion.Euler(0f, 0f, -_spin * 1.6f);
            if (_sigil != null) _sigil.transform.localRotation = Quaternion.Euler(0f, -_spin * 0.5f, 0f);
            if (_sigilInner != null) _sigilInner.transform.localRotation = Quaternion.Euler(0f, _spin * 0.8f, 0f);
            if (_sigilRunes != null) _sigilRunes.transform.localRotation = Quaternion.Euler(0f, -_spin * 0.5f, 0f);

            // Every emitter is throttled by wake and scaled by the intensity dial, so the gate spins up out
            // of nothing as you approach and winds down to nothing as you leave. Emission reaching zero is
            // the ONLY thing that stops the particles - what is already in the air is never touched.
            if (_veil != null)
            {
                ParticleSystem.EmissionModule em = _veil.emission;
                em.rateOverTime = (26f + 120f * c) * _wake * k;
                ParticleSystem.MainModule main = _veil.main;
                main.startColor = ColourBand(glow);
            }
            if (_sigilRunes != null)
            {
                ParticleSystem.EmissionModule em = _sigilRunes.emission;
                em.rateOverTime = (14f + 50f * c) * _wake * k;
                ParticleSystem.MainModule main = _sigilRunes.main;
                main.startColor = ColourBand(glow);
            }
            if (_motes != null)
            {
                ParticleSystem.EmissionModule em = _motes.emission;
                em.rateOverTime = 10f * _wake * k;
                ParticleSystem.MainModule main = _motes.main;
                main.startColor = ColourBand(glow * 0.85f);
            }
            if (_swirlRunes != null)
            {
                ParticleSystem.MainModule main = _swirlRunes.main;
                main.startColor = ColourBand(glow);
            }
            if (_swirlGlow != null)
            {
                ParticleSystem.MainModule main = _swirlGlow.main;
                main.startColor = Tint(Colour, glow * 0.8f);
            }

            // Line widths breathe with the charge so the rings read as energised, not just spinning faster -
            // and scale with wake so the rings condense out of the air instead of snapping on. The rings have
            // no particle lifetime to fade along, so they dim on colour as well as thinning, otherwise they
            // would still be at full glare at the moment the last of the runes faded out.
            float w = (1f + 1.8f * c) * _wake * Mathf.Sqrt(k);
            Width(_ringA, 0.055f * w); Width(_ringB, 0.04f * w);
            Width(_sigil, 0.07f * w); Width(_sigilInner, 0.05f * w);
            if (_spokes != null) foreach (LineRenderer s in _spokes) Width(s, 0.03f * w);

            Color lineCol = Tint(Colour, glow * Mathf.Clamp01(_wake));
            Recolour(_ringA, lineCol); Recolour(_ringB, lineCol);
            Recolour(_sigil, lineCol); Recolour(_sigilInner, lineCol);
            if (_spokes != null) foreach (LineRenderer s in _spokes) Recolour(s, lineCol);
        }

        // How lit this gateway should be, from where the nearest player is standing. 1 up close, easing to 0
        // exactly at VFXWakeDistance so the dial still means "beyond here the portal is dark".
        //
        // ANY player counts - on a shared server a friend walking up must light the gate for you too, so this
        // cannot be keyed to the local player alone.
        private float Proximity()
        {
            try
            {
                float wakeRange = Mathf.Max(2f, Configuration.vfxWakeDistance != null ? Configuration.vfxWakeDistance.Value : 8f);
                Player p = Player.GetClosestPlayer(transform.position, wakeRange);
                if (p == null) return 0f;

                float d = Vector3.Distance(p.transform.position, transform.position);
                // Full strength inside the first quarter of the range, then a smoothstep out to the edge -
                // the ease is what stops the taper reading as a linear dimmer switch.
                float t = 1f - Mathf.InverseLerp(wakeRange * 0.25f, wakeRange, d);
                return t * t * (3f - 2f * t);
            }
            catch
            {
                return 1f; // if the player list is unreadable, a lit portal is the safer failure
            }
        }

        private bool AnyLiveParticles()
        {
            return Alive(_veil) || Alive(_motes) || Alive(_sigilRunes) || Alive(_swirlRunes) || Alive(_swirlGlow);
        }

        private static bool Alive(ParticleSystem ps)
        {
            return ps != null && ps.particleCount > 0;
        }

        // Renderers on or off wholesale - the dormant portal must COST nothing to look at as well as show
        // nothing. Goes through SetVisible, the same switch the snapshot camera uses; if a snapshot restore
        // flips a dormant portal's renderers back on, the next Update here flips them off again.
        //
        // Only ever called with false once the fade is complete and every emitter has run dry, so there is
        // nothing left to clear and nothing gets cut off mid-air. It deliberately does NOT stop the systems:
        // the emission rates are already at zero by the time this runs.
        private void SetShown(bool show)
        {
            _shown = show;
            SetVisible(show);

            // Wake fresh: the swirl head must not draw a chord across the gap it slept through.
            if (!show) _swirlPrimed = false;
        }

        // Fired the instant the gate takes the player - a hard flash so the trip has a punctuation mark.
        public void Discharge()
        {
            _wake = 1f;
            if (!_shown) SetShown(true);
            if (_veil != null) _veil.Emit(70);
            if (_motes != null) _motes.Emit(45);
            if (_sigilRunes != null) _sigilRunes.Emit(40);

            // The swirl flashes as a full ring - its systems are manual-emit and position-less bursts would
            // just pile particles at the portal's root.
            for (int i = 0; i < 24; i++)
            {
                _swirlAngle += Mathf.PI * 2f / 24f;
                Vector3 head = SwirlHead();
                EmitAt(_swirlGlow, head);
                if (i % 3 == 0) EmitAt(_swirlRunes, head);
            }
            Charge = 1f;
        }

        // ---- helpers ------------------------------------------------------------------------------------

        // The two ends of the band every rune picks its own shade from: the configured hue with a little
        // swung either side of it, one end paler and one end deeper. A single flat colour read as one solid
        // sheet of green no matter how many glyphs were on screen.
        private void BuildColourBand()
        {
            float h, s, v;
            Color.RGBToHSV(Colour, out h, out s, out v);

            // A greyscale colour has no meaningful hue to swing around, so widen on saturation instead and
            // the band still varies rather than collapsing to one tone.
            float swing = s < 0.05f ? 0f : 0.055f;

            _bandA = Color.HSVToRGB(Mathf.Repeat(h - swing, 1f), Mathf.Clamp01(s * 0.70f), Mathf.Clamp01(v * 1.00f));
            _bandB = Color.HSVToRGB(Mathf.Repeat(h + swing, 1f), Mathf.Clamp01(s * 1.00f), Mathf.Clamp01(v * 0.80f));
            _bandA.a = 1f;
            _bandB.a = 1f;
        }

        // Handed to a system as its start colour: a random pick between the two ends of the band, scaled by
        // however brightly the gateway is currently burning.
        private ParticleSystem.MinMaxGradient ColourBand(float glow)
        {
            return new ParticleSystem.MinMaxGradient(Tint(_bandA, glow), Tint(_bandB, glow));
        }

        // The materials blend additively, so RGB is the brightness and alpha barely reads. Scaling the
        // channels is what actually dims these - and what lets a fading gate reach genuine black.
        private static Color Tint(Color c, float glow)
        {
            glow = Mathf.Max(0f, glow);
            return new Color(c.r * glow, c.g * glow, c.b * glow, c.a);
        }

        private static void Recolour(LineRenderer lr, Color c)
        {
            if (lr == null) return;
            lr.startColor = c;
            lr.endColor = c;
        }

        private GameObject Child(string name, Vector3 localPos)
        {
            GameObject go = new GameObject(name);
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            _parts.Add(go);
            return go;
        }

        // A circle of line segments. 'vertical' stands it up facing the approach; otherwise it lies flat.
        private LineRenderer BuildCircle(string name, Vector3 pos, float radius, Material mat, float width, bool vertical)
        {
            GameObject go = Child(name, pos);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            Vector3[] pts = new Vector3[RingSegments + 1];
            for (int i = 0; i <= RingSegments; i++)
            {
                float a = (Mathf.PI * 2f / RingSegments) * i;
                float x = Mathf.Cos(a) * radius, y = Mathf.Sin(a) * radius;
                pts[i] = vertical ? new Vector3(x, y, 0f) : new Vector3(x, 0f, y);
            }
            Line(lr, mat, width, pts);
            return lr;
        }

        private void Line(LineRenderer lr, Material mat, float width, Vector3[] pts)
        {
            lr.useWorldSpace = false;
            lr.loop = false;
            lr.material = mat;
            lr.positionCount = pts.Length;
            lr.SetPositions(pts);
            lr.startWidth = lr.endWidth = width;
            // Particle shaders multiply the texture by VERTEX COLOUR, so a LineRenderer left at its default
            // colours would render the runes muddy or invisible. Setting both ends explicitly is what makes
            // the glow material actually show up (learned the hard way in Wings of the Valkyrie).
            lr.startColor = Colour;
            lr.endColor = Colour;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.alignment = LineAlignment.View; // always face the camera, so thin rings never vanish edge-on
        }

        private static void Width(LineRenderer lr, float w)
        {
            if (lr == null) return;
            lr.startWidth = lr.endWidth = w;
        }

        // Soft fade in and out, so runes resolve out of the air instead of popping.
        private static void FadeInOut(ParticleSystem ps)
        {
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f),
                        new GradientAlphaKey(1f, 0.65f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
        }

        // Point the system at the 4x4 rune atlas and freeze each particle on a RANDOM tile, so the gateway
        // shows sixteen different glyphs at once rather than one repeated. frameOverTime is pinned to 0 -
        // we want a random glyph per particle, not an animation cycling through the alphabet.
        private static void Atlas(ParticleSystem ps)
        {
            ParticleSystem.TextureSheetAnimationModule t = ps.textureSheetAnimation;
            t.enabled = true;
            t.numTilesX = 4;
            t.numTilesY = 4;
            t.animation = ParticleSystemAnimationType.WholeSheet;
            t.timeMode = ParticleSystemAnimationTimeMode.Lifetime;
            t.startFrame = new ParticleSystem.MinMaxCurve(0f, 16f);
            t.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        }
    }
}
