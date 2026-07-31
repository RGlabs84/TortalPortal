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

    // The five free icons from vanilla's own map-pin menu. Values match Minimap.PinType, where Icon4 is 6
    // rather than 4 - Death and Bed sit between them - so these are spelled out rather than counted.
    public enum PortalPinIcon
    {
        Icon0 = 0,
        Icon1 = 1,
        Icon2 = 2,
        Icon3 = 3,
        Icon4 = 6
    }

    public static class Configuration
    {
        public static ConfigSync configSync = new ConfigSync("wubarrk.TortalPortal")
        {
            DisplayName = "Total Portal Control System",
            CurrentVersion = "1.0.3",
            // Still 1.0.1: that is where the portal index was introduced, and nothing since has changed its
            // format - 1.0.3 only adds a client-local font size and an on-screen button, neither of which
            // crosses the wire. A 1.0.0 client has no index at all and would show an empty destination
            // window, so it is turned away rather than left wondering.
            MinimumRequiredVersion = "1.0.1"
        };

        // Where the migration stamp lives. Named here so ConfigMigration can find it in a raw file that
        // predates the entry existing.
        public const string MetaSection = "0 - Meta";
        public const string ConfigVersionKey = "ConfigVersion";

        public static ConfigEntry<int> configVersion;

        public static ConfigEntry<FilterMode> filterMode;
        public static ConfigEntry<string> whitelistItems;
        public static ConfigEntry<string> blacklistItems;
        public static ConfigEntry<int> maxPortalsPerPlayer;
        public static ConfigEntry<bool> adminBypass;
        public static ConfigEntry<bool> allowTames;
        public static ConfigEntry<float> menuOpenDistance;
        public static ConfigEntry<bool> enableCustomVFX;
        public static ConfigEntry<float> vfxOrbHeightOffset;
        public static ConfigEntry<bool> enablePortalCameraShot;
        public static ConfigEntry<int> snapshotRefreshIntervalMinutes;
        public static ConfigEntry<float> portalIndexRefreshSeconds;
        public static ConfigEntry<float> uiTextScale;
        public static ConfigEntry<int> uiFontSizeDelta;
        public static ConfigEntry<bool> showPortalPinsOnMap;
        public static ConfigEntry<PortalPinIcon> portalPinIcon;
        public static ConfigEntry<bool> enableDebugMode;

        public static void Init(ConfigFile config)
        {
            // Reads the file as it stands on disk before a single Bind touches it, so a setting that has
            // moved can be carried to its new home instead of being stripped out on the next save.
            ConfigMigration.Begin(config);

            configVersion = config.Bind(MetaSection, ConfigVersionKey, ConfigMigration.CurrentConfigVersion,
                "Internal bookkeeping - the layout version of this file, used to carry your settings across mod updates. Do not edit.");

            filterMode = config.Bind("1 - General", "FilterMode", FilterMode.Vanilla, "Determines how portal item filters behave: Vanilla, WhitelistBlacklist, BypassAll (everything allowed).");
            whitelistItems = config.Bind("1 - General", "WhitelistItems", "", "Comma-separated list of prefab names. If populated, ONLY these items can be teleported.");
            blacklistItems = config.Bind("1 - General", "BlacklistItems", "", "Comma-separated list of prefab names. Any item here CANNOT be teleported.");
            
            maxPortalsPerPlayer = config.Bind("2 - Limits", "MaxPortalsPerPlayer", 0,
                "Maximum portals a single player can place. 0 (the default) or any negative number disables the limit entirely - nobody is capped until an admin sets a positive number here.");
            adminBypass = config.Bind("2 - Limits", "AdminBypass", true, "Allows admins to bypass max portal limits, PIN locks, and item filters.");
            
            allowTames = config.Bind("3 - Features", "AllowTames", true, "If true, tameable creatures near the player will teleport with them.");
            menuOpenDistance = config.Bind("3 - Features", "MenuOpenDistance", 2.5f,
                new ConfigDescription("How close you have to be, in metres, before the destination window opens by itself. Vanilla's portal activation range is 5m, which puts the window in your face while you are still walking up. Lower this to be left alone until you are actually at the gate. Only affects the window - the portal's glow and connection effect keep their own range.",
                                      new AcceptableValueRange<float>(1f, 10f)));
            enableCustomVFX = config.Bind("3 - Features", "EnableCustomVFX", true, "Replaces vanilla portal VFX with a custom procedural green runic gateway.");
            // Deliberately NOT server-synced: this is a local visual nudge, not a rule of the world.
            vfxOrbHeightOffset = config.Bind("3 - Features", "VFXOrbHeightOffset", 0f, "Vertical nudge in metres for the rune orb inside the gate. The orb is auto-centred on each portal's own arch, so this is only for fine-tuning.");
            enablePortalCameraShot = config.Bind("3 - Features", "EnablePortalCameraShot", true, "If true, takes a camera snapshot when a portal is placed for the UI preview.");
            snapshotRefreshIntervalMinutes = config.Bind("3 - Features", "SnapshotRefreshIntervalMinutes", 30, "Interval in minutes for the background coroutine to refresh active portal snapshots. Set to 0 to disable.");
            portalIndexRefreshSeconds = config.Bind("3 - Features", "PortalIndexRefreshSeconds", 10f,
                new ConfigDescription("How often the server rescans the world for portals and republishes the destination list. Renames, new portals and demolitions show up within this window. Only republished when something actually changed.",
                                      new AcceptableValueRange<float>(5f, 300f)));
            
            // Local, like the rest of the map and UI preferences below: where a player likes their own map
            // marked is not the server's business.
            showPortalPinsOnMap = config.Bind("3 - Features", "ShowPortalPinsOnMap", true,
                "Marks every portal in the destination list on the large map while that window is open, labelled with its name. The markers are never saved and are removed when the window closes, so they cannot silt up your own pin list.");
            portalPinIcon = config.Bind("3 - Features", "PortalPinIcon", PortalPinIcon.Icon4,
                "Which map icon to use for those markers. These are the same five icons vanilla offers in its own map-pin menu - pick whichever reads best against your pins.");

            // Also local-only, and picked up live: the theme rebuilds its styles when this changes.
            uiTextScale = config.Bind("3 - Features", "UITextScale", 1f,
                new ConfigDescription("Scales all text in the portal windows. 1 is the designed size.",
                                      new AcceptableValueRange<float>(0.6f, 2f)));
            uiFontSizeDelta = config.Bind("3 - Features", "UIFontSizeDelta", 2,
                new ConfigDescription(
                    "FLAT points added to every text size in the portal windows, applied after UITextScale. Defaults to +2 to match Fatty, which shares this theme and runs the same +2 - set both to the same number and the two mods' windows read identically. 0 = the theme's own base sizes.",
                    new AcceptableValueRange<int>(-6, 12)));

            enableDebugMode = config.Bind("4 - Debug", "EnableDebugLogging", false, "Enable verbose logging to the console for debugging UI and teleportation events.");

            configSync.AddConfigEntry(filterMode);
            configSync.AddConfigEntry(whitelistItems);
            configSync.AddConfigEntry(blacklistItems);
            configSync.AddConfigEntry(maxPortalsPerPlayer);
            configSync.AddConfigEntry(adminBypass);
            configSync.AddConfigEntry(allowTames);
            configSync.AddConfigEntry(menuOpenDistance);
            configSync.AddConfigEntry(enableCustomVFX);
            configSync.AddConfigEntry(enablePortalCameraShot);
            configSync.AddConfigEntry(snapshotRefreshIntervalMinutes);
            configSync.AddConfigEntry(portalIndexRefreshSeconds);

            // Pushes anything that moved into its new home, stamps the file and saves it.
            ConfigMigration.Finish(config, configVersion);
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
