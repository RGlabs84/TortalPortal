using HarmonyLib;
using UnityEngine;
using System;

namespace TortalPortal
{
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
