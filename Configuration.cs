using BepInEx.Configuration;
using ServerSync;

namespace TortalPortal
{
    public enum FilterMode
    {
        Vanilla,
        WhitelistBlacklist,
        BypassAll
    }

    public static class Configuration
    {
        public static ConfigSync configSync = new ConfigSync("wubarrk.TortalPortal")
        {
            DisplayName = "Total Portal Control System",
            CurrentVersion = "1.0.0",
            MinimumRequiredVersion = "1.0.0"
        };

        public static ConfigEntry<FilterMode> filterMode;
        public static ConfigEntry<string> whitelistItems;
        public static ConfigEntry<string> blacklistItems;
        public static ConfigEntry<int> maxPortalsPerPlayer;
        public static ConfigEntry<bool> adminBypass;
        public static ConfigEntry<bool> allowTames;
        public static ConfigEntry<bool> enableCustomVFX;
        public static ConfigEntry<float> vfxOrbHeightOffset;
        public static ConfigEntry<bool> enablePortalCameraShot;
        public static ConfigEntry<int> snapshotRefreshIntervalMinutes;
        public static ConfigEntry<float> uiTextScale;
        public static ConfigEntry<bool> enableDebugMode;

        public static void Init(ConfigFile config)
        {
            filterMode = config.Bind("1 - General", "FilterMode", FilterMode.Vanilla, "Determines how portal item filters behave: Vanilla, WhitelistBlacklist, BypassAll (everything allowed).");
            whitelistItems = config.Bind("1 - General", "WhitelistItems", "", "Comma-separated list of prefab names. If populated, ONLY these items can be teleported.");
            blacklistItems = config.Bind("1 - General", "BlacklistItems", "", "Comma-separated list of prefab names. Any item here CANNOT be teleported.");
            
            maxPortalsPerPlayer = config.Bind("2 - Limits", "MaxPortalsPerPlayer", 0, "Maximum portals a single player can place. 0 means unlimited.");
            adminBypass = config.Bind("2 - Limits", "AdminBypass", true, "Allows admins to bypass max portal limits, PIN locks, and item filters.");
            
            allowTames = config.Bind("3 - Features", "AllowTames", true, "If true, tameable creatures near the player will teleport with them.");
            enableCustomVFX = config.Bind("3 - Features", "EnableCustomVFX", true, "Replaces vanilla portal VFX with a custom procedural green runic gateway.");
            // Deliberately NOT server-synced: this is a local visual nudge, not a rule of the world.
            vfxOrbHeightOffset = config.Bind("3 - Features", "VFXOrbHeightOffset", 0f, "Vertical nudge in metres for the rune orb inside the gate. The orb is auto-centred on each portal's own arch, so this is only for fine-tuning.");
            enablePortalCameraShot = config.Bind("3 - Features", "EnablePortalCameraShot", true, "If true, takes a camera snapshot when a portal is placed for the UI preview.");
            snapshotRefreshIntervalMinutes = config.Bind("3 - Features", "SnapshotRefreshIntervalMinutes", 30, "Interval in minutes for the background coroutine to refresh active portal snapshots. Set to 0 to disable.");
            
            // Also local-only, and picked up live: the theme rebuilds its styles when this changes.
            uiTextScale = config.Bind("3 - Features", "UITextScale", 1f,
                new ConfigDescription("Scales all text in the portal windows. 1 is the designed size.",
                                      new AcceptableValueRange<float>(0.6f, 2f)));

            enableDebugMode = config.Bind("4 - Debug", "EnableDebugLogging", false, "Enable verbose logging to the console for debugging UI and teleportation events.");

            configSync.AddConfigEntry(filterMode);
            configSync.AddConfigEntry(whitelistItems);
            configSync.AddConfigEntry(blacklistItems);
            configSync.AddConfigEntry(maxPortalsPerPlayer);
            configSync.AddConfigEntry(adminBypass);
            configSync.AddConfigEntry(allowTames);
            configSync.AddConfigEntry(enableCustomVFX);
            configSync.AddConfigEntry(enablePortalCameraShot);
            configSync.AddConfigEntry(snapshotRefreshIntervalMinutes);
        }

        public static bool IsAdmin()
        {
            return adminBypass.Value && configSync.IsAdmin;
        }

        public static void LogDebug(string msg)
        {
            if (enableDebugMode != null && enableDebugMode.Value)
            {
                Plugin.Log.LogInfo($"[TortalPortal Debug] {msg}");
            }
        }
    }
}
