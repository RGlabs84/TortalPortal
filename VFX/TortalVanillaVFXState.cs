using System;
using System.Collections.Generic;
using UnityEngine;

namespace TortalPortal
{
    // A record of exactly what this mod switched off on one portal, so it can all be switched back on.
    //
    // Turning EnableCustomVFX off used to do nothing at all until the world was reloaded. The strip ran once
    // from TeleportWorld.Awake and kept no note of what it had touched, so there was no way back: the vanilla
    // emitters stayed deactivated, their emission modules stayed silenced and the connection effect list
    // stayed empty. This component is that missing note - one per portal, written when the vanilla effects
    // are stripped and read when they are put back.
    //
    // Only things this mod ACTUALLY changed are recorded. A particle system that was already switched off
    // when we found it is left out, so restoring cannot switch on something vanilla wanted dark.
    public class TortalVanillaVFXState : MonoBehaviour
    {
        private readonly List<GameObject> _deactivated = new List<GameObject>();
        private readonly List<ParticleSystem> _silenced = new List<ParticleSystem>();
        private EffectList.EffectData[] _connectedEffects;
        private bool _stripped;

        // ---- the frame tint ------------------------------------------------------------------------------
        //
        // The piece itself glows orange - an emissive panel in the arch and a warm point light - which is
        // vanilla's colour, not ours. Left alone it read as an orange portal wearing a green light show.
        // These lists are the same idea as the ones above: a note of exactly what was changed so it can be
        // put back, because VFXTintPortalFrame and EnableCustomVFX are both live toggles.
        private readonly List<Renderer> _tintedRenderers = new List<Renderer>();
        private readonly List<Material[]> _originalMaterials = new List<Material[]>();
        private readonly List<Material> _createdMaterials = new List<Material>();
        private readonly List<Light> _tintedLights = new List<Light>();
        private readonly List<Color> _originalLightColours = new List<Color>();

        // What colour the tint currently on this portal was built from, so a live move of the colour dial
        // repaints and an idle frame does not.
        private bool _tinted;
        private Color _tintColour;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private const string EmissionKeyword = "_EMISSION";

        public bool IsStripped { get { return _stripped; } }

        // EffectFade - which is what TeleportWorld.m_target_found is - caches GetComponentsInChildren<ParticleSystem>()
        // into m_particles during its own Awake, then writes ps.emission.enabled on every entry from SetActive.
        // Unity does not guarantee component Awake order, so DESTROYING those systems left EffectFade holding
        // destroyed references and the module setter threw "Do not create your own module instances, get them
        // from a ParticleSystem instance" out of SetActive on every tick - which aborted the UpdatePortal prefix
        // before it ever reached the menu code, so the portal menu never opened on approach.
        //
        // Deactivating instead of destroying keeps every reference valid, and an inactive GameObject renders
        // nothing no matter what EffectFade does to its emission module afterwards. It is also the only reason
        // this is reversible at all.
        public void Strip(TeleportWorld portal)
        {
            if (_stripped || portal == null) return;

            GameObject fadeRoot = portal.m_target_found != null ? portal.m_target_found.gameObject : null;

            foreach (ParticleSystem ps in portal.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;

                // Anything this mod built is not vanilla's to strip - and on a live toggle the rune gateway
                // may still be standing when this runs.
                if (IsOurs(ps.transform)) continue;

                ParticleSystem.EmissionModule em = ps.emission;
                if (em.enabled)
                {
                    em.enabled = false;
                    _silenced.Add(ps);
                }
                ps.Clear(true);

                // Never deactivate the portal root (that would hide the portal itself) or the EffectFade host
                // (that would stop EffectFade updating) - silencing their emission is enough.
                if (ps.gameObject != portal.gameObject && ps.gameObject != fadeRoot && ps.gameObject.activeSelf)
                {
                    ps.gameObject.SetActive(false);
                    _deactivated.Add(ps.gameObject);
                }
            }

            if (portal.m_connected != null)
            {
                _connectedEffects = portal.m_connected.m_effectPrefabs;
                portal.m_connected.m_effectPrefabs = new EffectList.EffectData[0];
            }

            _stripped = true;
        }

        public void Restore(TeleportWorld portal)
        {
            if (!_stripped) return;

            foreach (GameObject go in _deactivated)
            {
                if (go != null) go.SetActive(true);
            }
            _deactivated.Clear();

            foreach (ParticleSystem ps in _silenced)
            {
                if (ps == null) continue;
                ParticleSystem.EmissionModule em = ps.emission;
                em.enabled = true;
            }
            _silenced.Clear();

            if (portal != null && portal.m_connected != null && _connectedEffects != null)
            {
                portal.m_connected.m_effectPrefabs = _connectedEffects;
            }
            _connectedEffects = null;

            _stripped = false;
        }

        // ---- frame tint ----------------------------------------------------------------------------------

        // The one entry point. Reads the dials itself and settles the portal into whatever they currently
        // say - paint, repaint or strip back to vanilla orange - so callers do not have to work out which of
        // those three it is. Safe to call every time anything might have changed.
        public void SyncTint(TeleportWorld portal, bool wanted)
        {
            try
            {
                if (!wanted)
                {
                    RestoreTint();
                    return;
                }

                Color colour = Configuration.vfxColour != null
                    ? Configuration.vfxColour.Value
                    : new Color(0.30f, 1.00f, 0.55f, 1f);
                colour.a = 1f;

                if (_tinted && _tintColour == colour) return; // already wearing exactly this
                RestoreTint();
                ApplyTint(portal, colour);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: tinting the portal frame failed (non-fatal, the piece keeps its own colours). Reason: {ex}");
            }
        }

        // Only the parts that actually EMIT are touched, and "actually emits" means the _EMISSION shader
        // keyword is switched on - NOT merely that _EmissionColor is non-black. This is BlightedWorldHeart's
        // lesson (BlightedItemsManager.TintMaterial): plenty of Valheim materials carry a leftover emission
        // colour with the keyword off, where the value is inert and the surface does not glow at all.
        // Selecting on the colour repaints timber and stonework that was never lit; selecting on the keyword
        // lands on exactly the panel that lights up.
        //
        // The keyword is never ENABLED here either, for the same reason BWH refuses to: switching emission on
        // for a material that did not have it is what produces that flat pale glow over the whole piece.
        private void ApplyTint(TeleportWorld portal, Color colour)
        {
            if (portal == null) return;

            foreach (Renderer r in portal.GetComponentsInChildren<Renderer>(true))
            {
                // ParticleSystemRenderer and LineRenderer both derive from Renderer, so this is where our own
                // gateway and vanilla's effect quads get skipped - they are already the right colour or are
                // not ours to repaint.
                if (r == null || r is ParticleSystemRenderer || r is LineRenderer) continue;
                if (IsOurs(r.transform)) continue;

                Material[] shared = r.sharedMaterials;
                if (shared == null || shared.Length == 0) continue;

                Material[] replacement = new Material[shared.Length];
                bool touched = false;

                for (int i = 0; i < shared.Length; i++)
                {
                    Material m = shared[i];
                    replacement[i] = m;
                    if (m == null) continue;
                    if (!m.HasProperty(EmissionColorId) || !m.IsKeywordEnabled(EmissionKeyword)) continue;

                    // A COPY, never the shared asset - that one is the prefab's, and writing to it would
                    // recolour every portal in the world including ones this component knows nothing about,
                    // with no way back short of a game restart. (BWH tints prefabs at registration, where
                    // permanence is the point; here the dial moves at runtime, so ownership matters.)
                    Material instance = new Material(m);

                    // Hue from the dial, strength from whatever vanilla was already emitting, so a faint
                    // glow stays faint and a fierce one stays fierce. Emission is often HDR (>1), which is
                    // why the intensity is taken from the largest channel rather than normalised away.
                    //
                    // A straight REPLACE rather than BWH's multiply: multiplying shifts a colour, which is
                    // what a blight tint wants, but orange x green is a murky dark yellow rather than the
                    // rune colour. The ask here is to MATCH, so vanilla's hue is discarded and only its
                    // brightness is kept.
                    Color emission = m.GetColor(EmissionColorId);
                    float power = Mathf.Max(emission.r, Mathf.Max(emission.g, emission.b));
                    if (power <= 0f) power = 1f; // keyword on but the colour zeroed - light it at face value
                    instance.SetColor(EmissionColorId, new Color(colour.r * power, colour.g * power, colour.b * power, emission.a));

                    // The albedo underneath the glow. Blended rather than overwritten, the way BWH does it -
                    // pushing it all the way to the dial colour flattens the panel's own shading into a
                    // single slab. Alpha is untouched; it is doing transparency, not brightness.
                    if (instance.HasProperty(ColorId))
                    {
                        Color baseCol = instance.GetColor(ColorId);
                        Color blended = Color.Lerp(baseCol, colour, 0.8f);
                        instance.SetColor(ColorId, new Color(blended.r, blended.g, blended.b, baseCol.a));
                    }

                    replacement[i] = instance;
                    _createdMaterials.Add(instance);
                    touched = true;
                }

                if (!touched) continue;

                _tintedRenderers.Add(r);
                _originalMaterials.Add(shared);
                r.sharedMaterials = replacement;
            }

            // The warm point light in the gate. Hue only - intensity, range and shadows are left alone, so
            // the portal lights its surroundings exactly as far as it always did, just in the new colour.
            foreach (Light l in portal.GetComponentsInChildren<Light>(true))
            {
                if (l == null || IsOurs(l.transform)) continue;
                _tintedLights.Add(l);
                _originalLightColours.Add(l.color);
                l.color = colour;
            }

            _tinted = true;
            _tintColour = colour;
            Configuration.LogDebug($"Frame tint: repainted {_tintedRenderers.Count} renderer(s) and {_tintedLights.Count} light(s).");
        }

        public void RestoreTint()
        {
            if (!_tinted) return;

            for (int i = 0; i < _tintedRenderers.Count; i++)
            {
                Renderer r = _tintedRenderers[i];
                if (r != null) r.sharedMaterials = _originalMaterials[i];
            }
            _tintedRenderers.Clear();
            _originalMaterials.Clear();

            for (int i = 0; i < _tintedLights.Count; i++)
            {
                Light l = _tintedLights[i];
                if (l != null) l.color = _originalLightColours[i];
            }
            _tintedLights.Clear();
            _originalLightColours.Clear();

            // Our copies are ours to clean up. Unity does not collect materials created from script, so
            // skipping this leaks one per glowing panel per portal, every time the colour dial moves.
            foreach (Material m in _createdMaterials)
            {
                if (m != null) UnityEngine.Object.Destroy(m);
            }
            _createdMaterials.Clear();

            _tinted = false;
        }

        // The portal being torn down takes the renderers with it, so there is nothing to put back - but the
        // material copies are plain assets and outlive the GameObject unless they are destroyed here.
        private void OnDestroy()
        {
            foreach (Material m in _createdMaterials)
            {
                if (m != null) UnityEngine.Object.Destroy(m);
            }
            _createdMaterials.Clear();
        }

        // Walks up to the portal root looking for one of the rune gateway's own child objects. Cheap - the
        // hierarchy under a portal is a handful of levels deep - and only runs on a strip.
        private static bool IsOurs(Transform t)
        {
            try
            {
                for (Transform c = t; c != null; c = c.parent)
                {
                    if (c.GetComponent<TortalPortalRuneVFX>() != null) return false; // reached the portal root
                    if (c.name.StartsWith("Rune", StringComparison.Ordinal) ||
                        c.name.StartsWith("Sigil", StringComparison.Ordinal) ||
                        c.name.StartsWith("Swirl", StringComparison.Ordinal)) return true;
                }
            }
            catch { }
            return false;
        }
    }
}
