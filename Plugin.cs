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
        public const string PluginVersion = "1.1.0";

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
            typeof(MapInputPatch)
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
