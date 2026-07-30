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
    // Four layers, because one particle system on its own reads as a campfire rather than a gateway:
    //   1. VEIL    - a wall of runes filling the doorway, drifting upward and fading. The "surface" you step
    //                through. Each particle draws a different glyph via the 4x4 atlas.
    //   2. RINGS   - two counter-rotating vertical rune circles standing in the gap, so the gateway reads as
    //                machinery that is running rather than a static decal.
    //   3. SIGIL   - a horizontal magic circle scribed on the ground across the threshold, which is what sells
    //                the "this is a portal" read from a distance and from above.
    //   4. MOTES   - slow embers rising out of the sigil and past the pillars, tying the whole site together.
    //
    // The whole thing responds to the player: TortalPortalWalkTrigger drives Charge from 0..1 while they stand
    // in the doorway, and everything spins up, brightens and pulls inward as it climbs.
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

        // ---- 1. The veil: a wall of runes filling the doorway ------------------------------------------
        private void BuildVeil(Material mat)
        {
            // Sits a touch below the rings, exactly as it did when both were derived from GateHeight.
            GameObject go = Child("RuneVeil", new Vector3(0f, _orbHeight - 0.08f, 0f));
            _veil = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = _veil.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.5f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 3.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.55f);
            main.startColor = Colour;
            main.maxParticles = 220;
            // Local so the veil belongs to the gateway and does not smear if the portal is ever moved.
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            ParticleSystem.EmissionModule em = _veil.emission;
            em.rateOverTime = 26f;

            // A thin box exactly filling the doorway, so runes appear across the whole opening rather than
            // from a point. Slightly narrower than the gap so they never poke through the stone.
            ParticleSystem.ShapeModule sh = _veil.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(GateWidth * 0.82f, GateHeight * 0.72f, 0.12f);

            ParticleSystem.VelocityOverLifetimeModule vel = _veil.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            // ALL THREE axes must be written, and in the same MinMaxCurve mode. Setting only y left x and z as
            // single-constant curves while y was a two-constant range, which is the one error this mod put in the
            // BepInEx log every time the portal was built: "Particle Velocity curves must all be in the same mode".
            // Unity then discards the module's curves entirely, so the veil was not drifting at all. The tiny
            // lateral ranges are what the runes should have been doing anyway - a dead-straight rise reads as a
            // sprite sheet scrolling rather than as something adrift in the gate.
            vel.x = new ParticleSystem.MinMaxCurve(-0.06f, 0.06f);
            vel.y = new ParticleSystem.MinMaxCurve(0.25f, 0.75f); // drift upward through the gate
            vel.z = new ParticleSystem.MinMaxCurve(-0.06f, 0.06f);

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
            // Charge decays on its own, so the effect settles back to idle the moment the player steps out
            // even if the trigger stops reporting (leaving the volume, dying, being teleported away).
            Charge = Mathf.Max(0f, Charge - Time.deltaTime * 1.4f);

            float c = Mathf.Clamp01(Charge);
            _spin += Time.deltaTime * (18f + 150f * c);

            // Vertical rings counter-rotate; the ground sigil turns the other way again.
            if (_ringA != null) _ringA.transform.localRotation = Quaternion.Euler(0f, 0f, _spin);
            if (_ringB != null) _ringB.transform.localRotation = Quaternion.Euler(0f, 0f, -_spin * 1.6f);
            if (_spokes != null)
                foreach (LineRenderer s in _spokes)
                    if (s != null) s.transform.localRotation = Quaternion.Euler(0f, 0f, -_spin * 1.6f);
            if (_sigil != null) _sigil.transform.localRotation = Quaternion.Euler(0f, -_spin * 0.5f, 0f);
            if (_sigilInner != null) _sigilInner.transform.localRotation = Quaternion.Euler(0f, _spin * 0.8f, 0f);
            if (_sigilRunes != null) _sigilRunes.transform.localRotation = Quaternion.Euler(0f, -_spin * 0.5f, 0f);

            // The veil thickens as the gate charges - this is the visible feedback for the walk-in dwell.
            if (_veil != null)
            {
                ParticleSystem.EmissionModule em = _veil.emission;
                em.rateOverTime = 26f + 120f * c;
                ParticleSystem.MainModule main = _veil.main;
                main.startColor = Colour * (1f + 1.6f * c);
            }
            if (_sigilRunes != null)
            {
                ParticleSystem.EmissionModule em = _sigilRunes.emission;
                em.rateOverTime = 14f + 50f * c;
            }

            // Line widths breathe with the charge so the rings read as energised, not just spinning faster.
            float w = 1f + 1.8f * c;
            Width(_ringA, 0.055f * w); Width(_ringB, 0.04f * w);
            Width(_sigil, 0.07f * w); Width(_sigilInner, 0.05f * w);
            if (_spokes != null) foreach (LineRenderer s in _spokes) Width(s, 0.03f * w);
        }

        // Fired the instant the gate takes the player - a hard flash so the trip has a punctuation mark.
        public void Discharge()
        {
            if (_veil != null) _veil.Emit(70);
            if (_motes != null) _motes.Emit(45);
            if (_sigilRunes != null) _sigilRunes.Emit(40);
            Charge = 1f;
        }

        // ---- helpers ------------------------------------------------------------------------------------

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
