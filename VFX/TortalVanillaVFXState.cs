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
