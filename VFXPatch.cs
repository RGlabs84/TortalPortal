using HarmonyLib;
using UnityEngine;
using System;

namespace TortalPortal
{
    // ##################################################################################################
    // ##                                                                                              ##
    // ##   DEAD CODE. DELETE THIS CLASS BEFORE THE NEXT RELEASE. DO NOT BUILD ON IT.                  ##
    // ##                                                                                              ##
    // ##################################################################################################
    //
    // Flagged 2026-08-02, after v1.1.2 was tagged. Left in place only because the tag had already been
    // cut; it is the first item in the next build. See MASTER-PLAN.md and
    // libs-Tools\IMPLEMENTATIONS\TortalPortal.md.
    //
    // WHAT THIS IS. The 1.0.0-era first attempt at custom portal visuals: load a "GreenRunicPortalVFX"
    // prefab out of MistsofAvalor's AssetBundle and hang it on the portal. It was superseded entirely by
    // TortalPortalRuneVFX, which generates the gateway procedurally from stock Unity components and
    // ships no bundle at all. Nothing calls into this and nothing depends on it.
    //
    // WHY IT IS STILL DANGEROUS RATHER THAN MERELY UNTIDY - three separate problems:
    //
    //   1. It is STILL REGISTERED. typeof(VFXPatch) is in Plugin.PatchTypes and InitBundle() is called
    //      from Awake, so the postfix is genuinely attached to TeleportWorld.Awake on every launch. It
    //      logs "Patched VFXPatch successfully" every boot. It is dormant, not absent.
    //
    //   2. The fallback path is a HARDCODED ABSOLUTE PATH INTO THE AUTHOR'S OWN MACHINE:
    //          C:\WubarrkCODING\MistsofAvalor\HexiumDist\plugins\MistsOfAvalorBundle
    //      No user can ever resolve that, so in a shipped build this is inert. On the DEV BOX it is one
    //      file away from firing - and it would then fire on a machine where it is hardest to notice,
    //      because everything else about that machine looks normal.
    //
    //   3. If it did fire it would UNDO 1.1.1's central fix. The postfix walks every ParticleSystem on
    //      the portal and SetActive(false)s it with no record of what it touched and no way back. That
    //      is precisely the bug TortalVanillaVFXState was written to fix - EnableCustomVFX = false
    //      doing nothing because the strip kept no note of itself. Two strippers on one portal, one of
    //      them irreversible, is a worse version of the original defect.
    //
    // It is also the ONLY line in this codebase implying a dependency on MistsofAvalor, in a mod whose
    // own technical report opens with "standalone - no Jotunn, no AssetBundle, no dependency on
    // MistsofAvalor". Deleting it makes the documentation true.
    //
    // TO REMOVE: delete this file, and delete typeof(VFXPatch) from Plugin.PatchTypes and the
    // VFXPatch.InitBundle() call in Plugin.Awake. Nothing else references it.
    [HarmonyPatch]
    public static class VFXPatch
    {
        private static GameObject MistsOfAvalorVFXPrefab;

        public static void InitBundle()
        {
            if (!Configuration.enableCustomVFX.Value) return;

            string bundlePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Plugin.Instance.Info.Location), "MistsOfAvalorBundle");
            // If the bundle exists locally in TortalPortal
            if (System.IO.File.Exists(bundlePath))
            {
                AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle != null)
                {
                    MistsOfAvalorVFXPrefab = bundle.LoadAsset<GameObject>("GreenRunicPortalVFX");
                    bundle.Unload(false);
                }
            }
            else
            {
                // Optionally fallback to loading from MistsOfAvalor mod folder if it exists on user system
                string fallbackPath = @"C:\WubarrkCODING\MistsofAvalor\HexiumDist\plugins\MistsOfAvalorBundle";
                if (System.IO.File.Exists(fallbackPath))
                {
                    AssetBundle bundle = AssetBundle.LoadFromFile(fallbackPath);
                    if (bundle != null)
                    {
                        MistsOfAvalorVFXPrefab = bundle.LoadAsset<GameObject>("GreenRunicPortalVFX");
                        bundle.Unload(false);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Awake))]
        [HarmonyPostfix]
        public static void TeleportWorld_Awake_Postfix(TeleportWorld __instance)
        {
            try
            {
                if (!Configuration.enableCustomVFX.Value || MistsOfAvalorVFXPrefab == null) return;

                // Find vanilla particles and destroy/disable them
                ParticleSystem[] vanillaParticles = __instance.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var ps in vanillaParticles)
                {
                    ps.gameObject.SetActive(false);
                }

                // Instantiate custom VFX
                GameObject customVfx = UnityEngine.Object.Instantiate(MistsOfAvalorVFXPrefab, __instance.transform);
                customVfx.transform.localPosition = Vector3.zero;
                customVfx.transform.localRotation = Quaternion.identity;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(TeleportWorld_Awake_Postfix)} failed (non-fatal). Reason: {ex}");
            }
        }
    }
}
