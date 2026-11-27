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
        public const string PluginVersion = "1.6.5";

        private readonly Harmony _harmony = new Harmony(PluginGUID);

        private static readonly Type[] PatchTypes =
        {
            typeof(PlacementPatch),
            typeof(SnapshotAnchor),
            typeof(SectorSubscription),
            typeof(SnapGuard),
            typeof(TraderRepair),
            typeof(FilterPatch),
            typeof(PortalBehaviorPatch),
            typeof(SecurityPatch),
            typeof(TamesPatch),
            typeof(InputFocusPatch),
            typeof(MessageHudPatch),
            typeof(MapInputPatch),
            typeof(ScatterCommand),
            typeof(TortalPinLibrary),
            typeof(AnchorPresence),
            typeof(PortalGuard),
            typeof(PortalLockdown),
            typeof(CorpseRun),
            typeof(CorpseWard),
            typeof(ConcealedPortals),
            typeof(PortalHealth)
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
            TortalPinLibrary.Init();

            ApplyPatches();
            WireLiveVFXSettings();
            WireRuleAudit();
            WireSkippedPortals();

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
                // Only the frame tint moves, so the gateway does not need rebuilding - ApplyVFXMode settles
                // the tint on its own.
                Configuration.vfxTintPortalFrame.SettingChanged += (s, e) => PortalBehaviorPatch.RefreshLoadedPortals(false);
                // Monster targeting is set on the live piece, so the dial reaches standing portals the same way.
                Configuration.selfHealingPortals.SettingChanged += (s, e) => PortalGuard.RefreshLoadedPortals();
            }
            catch (Exception ex)
            {
                Log.LogError($"TortalPortal: could not hook the live VFX settings - they will only apply on world reload. Reason: {ex}");
            }
        }

        // SkipPortalPrefabs reaches gates already standing: a newly skipped gate loses the runic gateway and
        // the immunity on the spot and gets its vanilla look back, a gate taken off the list gets them again,
        // and a window open on a gate that has just been handed back closes. Fires on the server's value
        // arriving too, so the log says what this peer settled on at login.
        private void WireSkippedPortals()
        {
            try
            {
                if (SkippedPortals.Any) Log.LogInfo($"TortalPortal: leaving these portal prefabs alone (SkipPortalPrefabs): {SkippedPortals.Describe()}.");
                Configuration.skipPortalPrefabs.SettingChanged += (s, e) =>
                {
                    SkippedPortals.Invalidate();
                    Log.LogInfo($"TortalPortal: SkipPortalPrefabs is now: {SkippedPortals.Describe()}.");
                    PortalBehaviorPatch.RefreshLoadedPortals(false);
                    PortalGuard.RefreshLoadedPortals();
                    if (PortalUIManager.IsUIVisible() && SkippedPortals.IsSkipped(PortalUIManager.CurrentSourcePortal))
                    {
                        PortalUIManager.HideUI();
                    }
                };
            }
            catch (Exception ex)
            {
                Log.LogError($"TortalPortal: could not hook SkipPortalPrefabs - a change will only apply on world reload. Reason: {ex}");
            }
        }

        // The portal cap, the admin bypass and the switch that decides who owns them are the three settings
        // a player actually feels, and until 1.4.3 nothing anywhere recorded them moving. They moved plenty:
        // with no locking entry registered, any client's config file could push its own numbers over the
        // server's, so a live server's effective cap wandered between values nobody had deliberately set
        // while the server's own config file went on claiming something else entirely. One line per change
        // is the entire audit trail that was missing - and it fires on the sync arriving too, so the log
        // says what this peer settled on at login as well as what changed afterwards.
        private void WireRuleAudit()
        {
            try
            {
                Configuration.maxPortalsPerPlayer.SettingChanged += (s, e) => LogRules("MaxPortalsPerPlayer");
                Configuration.adminBypass.SettingChanged += (s, e) => LogRules("AdminBypass");
                Configuration.lockConfiguration.SettingChanged += (s, e) => LogRules("LockConfiguration");
            }
            catch (Exception ex)
            {
                Log.LogError($"TortalPortal: could not hook the rule audit - portal cap changes will not be logged. Reason: {ex}");
            }
        }

        private static void LogRules(string what)
        {
            try
            {
                int cap = Configuration.maxPortalsPerPlayer.Value;
                Log.LogInfo(
                    $"TortalPortal: {what} settled - portal cap {(cap > 0 ? cap.ToString() : $"unlimited ({cap})")}, " +
                    $"AdminBypass={Configuration.adminBypass.Value}, LockConfiguration={Configuration.lockConfiguration.Value}.");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"TortalPortal: could not log the current portal rules (non-fatal). Reason: {ex.Message}");
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
