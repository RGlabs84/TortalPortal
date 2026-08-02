using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;

namespace TortalPortal
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "wubarrk.TortalPortal";
        public const string PluginName = "TortalPortal";
        public const string PluginVersion = "1.1.1";

        private readonly Harmony _harmony = new Harmony(PluginGUID);

        private static readonly Type[] PatchTypes =
        {
            typeof(PlacementPatch),
            typeof(FilterPatch),
            typeof(PortalBehaviorPatch),
            typeof(SecurityPatch),
            typeof(TamesPatch),
            typeof(VFXPatch),
            typeof(InputFocusPatch),
            typeof(MessageHudPatch),
            typeof(MapInputPatch),
            typeof(ScatterCommand)
        };

        public static Plugin Instance;
        public static ManualLogSource Log;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            Configuration.Init(Config);

            // Has to happen before ZNet.Awake wires up the ConfigSync RPCs, or the portal index would not be
            // part of the login handshake.
            PortalRegistry.Init();

            ApplyPatches();
            WireLiveVFXSettings();

            try
            {
                VFXPatch.InitBundle();
            }
            catch (Exception ex)
            {
                Log.LogError($"TortalPortal: VFX bundle failed to load, custom VFX will be disabled. Reason: {ex}");
            }

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded successfully.");
        }

        // The visual dials reach portals that are already standing, instead of waiting for a world reload.
        // Turning EnableCustomVFX off used to do nothing whatsoever until you reloaded the world, which read
        // as the setting being broken - and it half was, because the strip kept no record of what it had
        // switched off and so could never be undone.
        //
        // Only the settings that are BAKED at build time need a rebuild. Brightness, intensity and wake
        // distance are read live every frame and need no handler at all.
        private void WireLiveVFXSettings()
        {
            try
            {
                Configuration.enableCustomVFX.SettingChanged += (s, e) => PortalBehaviorPatch.RefreshLoadedPortals(false);
                Configuration.vfxColour.SettingChanged += (s, e) => PortalBehaviorPatch.RefreshLoadedPortals(true);
                Configuration.vfxOrbHeightOffset.SettingChanged += (s, e) => PortalBehaviorPatch.RefreshLoadedPortals(true);
            }
            catch (Exception ex)
            {
                Log.LogError($"TortalPortal: could not hook the live VFX settings - they will only apply on world reload. Reason: {ex}");
            }
        }

        private void ApplyPatches()
        {
            foreach (Type patchType in PatchTypes)
            {
                try
                {
                    _harmony.PatchAll(patchType);
                    Log.LogInfo($"TortalPortal: Patched {patchType.Name} successfully.");
                }
                catch (Exception ex)
                {
                    Log.LogError($"TortalPortal: Failed to patch {patchType.Name} - related feature(s) will be disabled. Reason: {ex}");
                }
            }
        }
    }
}
