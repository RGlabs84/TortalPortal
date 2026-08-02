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
            CurrentVersion = "1.1.2",
            // Pinned to the CURRENT version deliberately: every client on a server must run exactly this
            // build or newer.
            //
            // It was 1.0.1 - the version that introduced the portal index, whose format has not changed since
            // - on the reasoning that an older client would merely miss features it did not have anyway.
            // That reasoning holds for the wire format and not for the experience: 1.1.x changed what a click
            // on a map marker DOES, when the destination window opens, whether the keyboard is captured, and
            // which portal is highlighted. A mixed table means players describing different behaviour to each
            // other and neither of them wrong, which is far harder to diagnose than a refused connection.
            //
            // Raising this is a BREAKING change for anyone still on an older build: they are turned away at
            // the handshake with a version-mismatch message rather than let in to desync. Every client has to
            // update alongside the server.
            MinimumRequiredVersion = "1.1.2"
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
        public static ConfigEntry<float> vfxBrightness;
        public static ConfigEntry<float> vfxSwirlDensity;
        public static ConfigEntry<bool> vfxTintPortalFrame;
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
        public static ConfigEntry<bool> focusSearchOnOpen;
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
            menuOpenDistance = config.Bind("3 - Features", "MenuOpenDistance", 0.8f,
                new ConfigDescription("How close you have to be, in metres, before the destination window and map open by themselves. Vanilla's portal activation range is 5m, which puts the window in your face while you are still walking up. The default of 0.8 means you have to walk right up into the gate, so the window is something you go and get rather than something that ambushes you. Measured HORIZONTALLY, so standing on a raised platform does not add phantom distance. Only affects the window - the portal's visuals keep their own range.",
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
            vfxWakeDistance = config.Bind("3 - Features", "VFXWakeDistance", 25f,
                new ConfigDescription("How close, in metres, a player has to be before the rune gateway wakes up. Beyond this the portal sits dormant like vanilla - no rings, no runes, no motes - and it spins up as someone approaches. 25 is far enough that a gate is already alive as it comes into view and you watch it build as you walk in, rather than having it appear beside you.",
                                      new AcceptableValueRange<float>(2f, 50f)));
            // 0.64 / 0.64 / 3 rather than 1 / 0.9 / 1: the tuned look, settled by eye in-game against the
            // modpack rather than in isolation. Fewer, dimmer particles in the gate itself, and the trailing
            // ribbon packed to the top of its range - the gateway reads as a fine-drawn thing that leaves a
            // dense wake, instead of a bright cloud that leaves a thin one.
            vfxIntensity = config.Bind("3 - Features", "VFXIntensity", 0.64f,
                new ConfigDescription("How MUCH gateway there is: particle density, rune spacing in the swirl and ring thickness all scale with it. Below the default the gate smoulders, above it it roars. How brightly it burns is VFXBrightness, which moves independently.",
                                      new AcceptableValueRange<float>(0.1f, 3f)));
            vfxBrightness = config.Bind("3 - Features", "VFXBrightness", 0.64f,
                new ConfigDescription("How brightly the rune gateway burns, with no effect on how much of it there is. Lower and the runes smoulder darkly against a night sky; higher and they glare. The particle materials are additive, so this climbs fast - 1.0 blows out against snow and daylight. Applies live to every loaded portal.",
                                      new AcceptableValueRange<float>(0.1f, 3f)));
            vfxSwirlDensity = config.Bind("3 - Features", "VFXSwirlDensity", 3f,
                new ConfigDescription("How thickly the runes that peel off the gateway and trail away are laid down. This is the ribbon of glyphs that spirals off the edge and bleeds out behind you as you walk away - higher packs them tighter along the same path, lower strings them out. Independent of VFXIntensity, which moves the whole gateway at once. Applies live.",
                                      new AcceptableValueRange<float>(0.25f, 3f)));
            // Off by default. It works, but it is a matter of taste rather than a fix: some portals read
            // better with vanilla's warm light in the arch against the cool runes than with everything in
            // one colour. Turn it on if you want the piece and the gateway to match exactly.
            vfxTintPortalFrame = config.Bind("3 - Features", "VFXTintPortalFrame", false,
                new ConfigDescription("Recolours the portal prefab's own glowing panel - the orange light in the arch on the wood and stone gates - to match VFXColour, so the piece and the rune gateway read as one object instead of an orange portal wearing a green light show. Only the emissive glow is touched; the timber, stone and metal keep their own materials. Off by default - vanilla's warm light against cool runes is a look in its own right. Applies live either way."));

            // The dials. All local - what colour someone's world glows is their own business, and none of it
            // crosses the wire. BepInEx renders these as colour pickers in ConfigurationManager.
            // 89FFACFF - a paler, mintier green than the old 4CFF8CFF. At the tuned brightness the deeper
            // green sat too close to black at the edges of the swirl.
            vfxColour = config.Bind("5 - Colours", "VFXColour", new Color(0.537f, 1.000f, 0.675f, 1f),
                "The rune gateway's colour - veil, rings, sigil, motes and the swirl trail all draw from this.");
            // 12FF21FF - a hard, saturated green. The pin is a small mark on a busy map and wants to shout,
            // which is the opposite of what the gateway colour wants, so the two dials part company here.
            pinColour = config.Bind("5 - Colours", "PinColour", new Color(0.071f, 1.000f, 0.129f, 1f),
                "The Rune map pin's colour (glyph and ring). Ignored for vanilla icons and Custom PNGs, which bring their own colours.");
            uiGoldColour = config.Bind("5 - Colours", "UIGoldColour", new Color(0.80f, 0.62f, 0.26f, 1f),
                "The metal of the window frames. The deep shadow and bright highlight tones are derived from this one colour, so silver, bronze or verdigris is a single change. Applies live.");
            enablePortalCameraShot = config.Bind("3 - Features", "EnablePortalCameraShot", true, "If true, takes a camera snapshot when a portal is placed for the UI preview.");
            snapshotRefreshIntervalMinutes = config.Bind("3 - Features", "SnapshotRefreshIntervalMinutes", 5, "Interval in minutes for the background coroutine to re-photograph portals that already have a picture, so a destination that has been built up or burnt down does not keep showing the old view. 5 rather than 30: a portal only gets photographed while a player is standing near it anyway, so a long interval mostly meant missing the chance entirely. Set to 0 to disable refreshes - portals with no picture at all are still filled in regardless.");
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
            // Off by default, and that default is the whole point. Auto-focusing the search box meant the
            // window took the keyboard the instant it opened, so the run-up to a portal typed "wwwww" into
            // the filter instead of moving the player. Click the field when you actually want to search.
            focusSearchOnOpen = config.Bind("3 - Features", "FocusSearchOnOpen", false,
                "Put the text cursor in the destination window's search box the moment the window opens. Off by default: the window opens while you are still walking, and a focused field swallows WASD as typing. Turn on if you always search and never walk with the window up.");

            // Also local-only, and picked up live: the theme rebuilds its styles when this changes.
            uiTextScale = config.Bind("3 - Features", "UITextScale", 1.05f,
                new ConfigDescription("Scales all text in the portal windows, and the layout around it. 1 is the theme's designed size; the default nudges it slightly up, which reads better at 1440p and above without crowding 1080p.",
                                      new AcceptableValueRange<float>(0.6f, 3f)));
            uiFontSizeDelta = config.Bind("3 - Features", "UIFontSizeDelta", 6,
                new ConfigDescription(
                    "FLAT points added to every text size in the portal windows, applied after UITextScale. Fatty shares this theme and has the same setting - set both to the same number and the two mods' windows read identically. 0 = the theme's own base sizes.",
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
