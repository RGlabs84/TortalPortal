using BepInEx.Configuration;
using ServerSync;
using UnityEngine;

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
    //
    // Rune and Custom are NOT vanilla icons: Rune is this mod's own glyph, generated at runtime like the rest
    // of its art, and Custom is a TortalPortalPin.png dropped beside the plugin DLL (falling back to Rune when
    // no file is there). Their values sit far above the PinType range so no future vanilla icon can collide.
    public enum PortalPinIcon
    {
        Icon0 = 0,
        Icon1 = 1,
        Icon2 = 2,
        Icon3 = 3,
        Icon4 = 6,
        Rune = 100,
        Custom = 101
    }

    public static class Configuration
    {
        public static ConfigSync configSync = new ConfigSync("wubarrk.TortalPortal")
        {
            DisplayName = "Total Portal Control System",
            CurrentVersion = "1.1.0",
            // Still 1.0.1: that is where the portal index was introduced, and nothing since has changed its
            // format. 1.1.0 adds two synced entries (AllowMountedTeleport, TameFollowRadius) but ServerSync
            // matches entries by name, so an older client simply never hears about them - it just cannot ride
            // a mount through, which is the feature it does not have anyway. A 1.0.0 client has no index at
            // all and would show an empty destination window, so it is turned away rather than left wondering.
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
        public static ConfigEntry<float> tameFollowRadius;
        public static ConfigEntry<bool> allowMountedTeleport;
        public static ConfigEntry<float> menuOpenDistance;
        public static ConfigEntry<float> menuCloseMargin;
        public static ConfigEntry<bool> enableCustomVFX;
        public static ConfigEntry<float> vfxOrbHeightOffset;
        public static ConfigEntry<float> vfxWakeDistance;
        public static ConfigEntry<float> vfxIntensity;
        public static ConfigEntry<Color> vfxColour;
        public static ConfigEntry<Color> pinColour;
        public static ConfigEntry<Color> uiGoldColour;
        public static ConfigEntry<bool> enablePortalCameraShot;
        public static ConfigEntry<int> snapshotRefreshIntervalMinutes;
        public static ConfigEntry<float> portalIndexRefreshSeconds;
        public static ConfigEntry<float> uiTextScale;
        public static ConfigEntry<int> uiFontSizeDelta;
        public static ConfigEntry<bool> showPortalPinsOnMap;
        public static ConfigEntry<PortalPinIcon> portalPinIcon;
        public static ConfigEntry<bool> showPreviews;
        public static ConfigEntry<bool> openFavoritesView;
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
            tameFollowRadius = config.Bind("3 - Features", "TameFollowRadius", 10f,
                new ConfigDescription("How far around you, in metres, following tames are picked up when you teleport. Only tames actually set to follow you come along.",
                                      new AcceptableValueRange<float>(2f, 30f)));
            allowMountedTeleport = config.Bind("3 - Features", "AllowMountedTeleport", true,
                "If true, teleporting while riding a tamed creature sends the mount through with you and puts you back in the saddle on arrival. Works with anything ridden through the game's own mount controls - vanilla saddles, custom saddle items, OdinMounts and the like. Ships and other steered contraptions are never dragged through.");
            menuOpenDistance = config.Bind("3 - Features", "MenuOpenDistance", 2.5f,
                new ConfigDescription("How close you have to be, in metres, before the destination window and map open by themselves. Vanilla's portal activation range is 5m, which puts the window in your face while you are still walking up. Down at 0.1 you have to be standing IN the gate. Only affects the window - the portal's visuals keep their own range.",
                                      new AcceptableValueRange<float>(0.1f, 5f)));
            // Local: how sticky the window is once open is a personal taste, not a rule of the world.
            menuCloseMargin = config.Bind("3 - Features", "MenuCloseMargin", 1f,
                new ConfigDescription("Extra metres beyond MenuOpenDistance you have to retreat before an open destination window closes itself. This is the hysteresis that stops the window strobing when you hover right on the boundary.",
                                      new AcceptableValueRange<float>(0f, 5f)));
            enableCustomVFX = config.Bind("3 - Features", "EnableCustomVFX", true, "Replaces vanilla portal VFX with a custom procedural green runic gateway.");
            // Deliberately NOT server-synced: this is a local visual nudge, not a rule of the world.
            vfxOrbHeightOffset = config.Bind("3 - Features", "VFXOrbHeightOffset", 0f, "Vertical nudge in metres for the rune orb inside the gate. The orb is auto-centred on each portal's own arch, so this is only for fine-tuning.");
            // Also local: when someone's portals light up is their own business. The check is against ANY
            // player, so on a shared screen the gate still wakes for a friend walking up.
            vfxWakeDistance = config.Bind("3 - Features", "VFXWakeDistance", 8f,
                new ConfigDescription("How close, in metres, a player has to be before the rune gateway wakes up. Beyond this the portal sits dormant like vanilla - no rings, no runes, no motes - and it spins up as someone approaches.",
                                      new AcceptableValueRange<float>(2f, 50f)));
            vfxIntensity = config.Bind("3 - Features", "VFXIntensity", 1f,
                new ConfigDescription("Overall throttle on the rune gateway: particle density, brightness and ring thickness all scale with it. 1 is the designed look; below it the gate smoulders, above it it roars.",
                                      new AcceptableValueRange<float>(0.1f, 3f)));

            // The dials. All local - what colour someone's world glows is their own business, and none of it
            // crosses the wire. BepInEx renders these as colour pickers in ConfigurationManager.
            vfxColour = config.Bind("5 - Colours", "VFXColour", new Color(0.30f, 1.00f, 0.55f, 1f),
                "The rune gateway's colour - veil, rings, sigil, motes and the swirl trail all draw from this.");
            pinColour = config.Bind("5 - Colours", "PinColour", new Color(0.30f, 1.00f, 0.55f, 1f),
                "The Rune map pin's colour (glyph and ring). Ignored for vanilla icons and Custom PNGs, which bring their own colours.");
            uiGoldColour = config.Bind("5 - Colours", "UIGoldColour", new Color(0.80f, 0.62f, 0.26f, 1f),
                "The metal of the window frames. The deep shadow and bright highlight tones are derived from this one colour, so silver, bronze or verdigris is a single change. Applies live.");
            enablePortalCameraShot = config.Bind("3 - Features", "EnablePortalCameraShot", true, "If true, takes a camera snapshot when a portal is placed for the UI preview.");
            snapshotRefreshIntervalMinutes = config.Bind("3 - Features", "SnapshotRefreshIntervalMinutes", 30, "Interval in minutes for the background coroutine to refresh active portal snapshots. Set to 0 to disable.");
            portalIndexRefreshSeconds = config.Bind("3 - Features", "PortalIndexRefreshSeconds", 10f,
                new ConfigDescription("How often the server rescans the world for portals and republishes the destination list. Renames, new portals and demolitions show up within this window. Only republished when something actually changed.",
                                      new AcceptableValueRange<float>(5f, 300f)));
            
            // Local, like the rest of the map and UI preferences below: where a player likes their own map
            // marked is not the server's business.
            showPortalPinsOnMap = config.Bind("3 - Features", "ShowPortalPinsOnMap", true,
                "Marks every portal in the destination list on the large map while that window is open, labelled with its name. The markers are never saved and are removed when the window closes, so they cannot silt up your own pin list.");
            portalPinIcon = config.Bind("3 - Features", "PortalPinIcon", PortalPinIcon.Rune,
                "Which map icon to use for those markers. Icon0-Icon4 are the five icons vanilla offers in its own map-pin menu. Rune is TortalPortal's own glyph. Custom uses a TortalPortalPin.png dropped in the plugin folder (any size, transparency respected), falling back to Rune when the file is missing.");

            // Client-local UI shape. What a player's own window looks like is not the server's business.
            showPreviews = config.Bind("3 - Features", "ShowDestinationPreviews", true,
                "Show the destination snapshot panel in the full window. Turn off for a more compact window without pictures - snapshots are still captured and shared either way, so turning it back on costs nothing.");
            openFavoritesView = config.Bind("3 - Features", "OpenFavoritesView", true,
                "Open the destination window as the slim favorites strip (when you have favorites in this world) instead of the full list. The arrow on the strip expands to the full window either way.");

            // Also local-only, and picked up live: the theme rebuilds its styles when this changes.
            uiTextScale = config.Bind("3 - Features", "UITextScale", 1f,
                new ConfigDescription("Scales all text in the portal windows. 1 is the designed size.",
                                      new AcceptableValueRange<float>(0.6f, 3f)));
            uiFontSizeDelta = config.Bind("3 - Features", "UIFontSizeDelta", 2,
                new ConfigDescription(
                    "FLAT points added to every text size in the portal windows, applied after UITextScale. Defaults to +2 to match Fatty, which shares this theme and runs the same +2 - set both to the same number and the two mods' windows read identically. 0 = the theme's own base sizes.",
                    new AcceptableValueRange<int>(-6, 24)));

            enableDebugMode = config.Bind("4 - Debug", "EnableDebugLogging", false, "Enable verbose logging to the console for debugging UI and teleportation events.");

            configSync.AddConfigEntry(filterMode);
            configSync.AddConfigEntry(whitelistItems);
            configSync.AddConfigEntry(blacklistItems);
            configSync.AddConfigEntry(maxPortalsPerPlayer);
            configSync.AddConfigEntry(adminBypass);
            configSync.AddConfigEntry(allowTames);
            configSync.AddConfigEntry(tameFollowRadius);
            configSync.AddConfigEntry(allowMountedTeleport);
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
