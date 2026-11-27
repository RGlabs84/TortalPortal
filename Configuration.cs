using BepInEx.Configuration;
using ServerSync;
using System;
using System.Collections.Generic;
using System.IO;
using Splatform;
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

    // What the portal camera renders at - stored snapshots and the live feed both. An enum rather than
    // free width/height fields: ConfigurationManager draws it as a dropdown, and there is no way to type
    // a 16000x9000 render target into a dropdown.
    public enum CameraResolution
    {
        Res480p = 0,   // 854 x 480
        Res720p = 1,   // 1280 x 720
        Res900p = 2,   // 1600 x 900
        Res1080p = 3   // 1920 x 1080
    }

    public static class Configuration
    {
        public static ConfigSync configSync = new ConfigSync("wubarrk.TortalPortal")
        {
            DisplayName = "Total Portal Control System",
            CurrentVersion = "1.6.5",
            // NO floor change for 1.6.5, which adds the grave ward (CorpseRunArrivalWardSeconds). The ward runs
            // entirely on the arriving player's own client; an older client gets no ward and reads the new synced
            // float as an unknown entry. Nothing on the wire changed shape.
            // NO floor change for 1.6.4, which adds SkipPortalPrefabs. The server builds the destination index
            // and leaves skipped gates out of it, so even a 1.6.2 client is never offered one as a destination
            // and nothing on the wire changed shape - the new setting is one more synced string, which an older
            // client simply does not have an entry for. (1.6.3 was a store-page-only release, same DLL as 1.6.2.) What an older client still does is take a skipped gate
            // over for itself when it walks up to one: it opens the window there and can leave by it. That is
            // local to that player, harms nobody else's game and writes nothing to the world, the same size of
            // unfairness the 1.6.0 note below accepted for concealed gates.
            //
            // NO floor change for 1.6.2: a rebuild for Valheim 1.0 (network version 41), nothing on the wire.
            //
            // NO floor change for 1.6.1. It is a window that opened itself at the far end of a trip:
            // entirely local, nothing on the wire, nothing written to the world. A 1.6.0 client on a 1.6.1
            // table behaves identically apart from being greeted by its own destination window on arrival.
            //
            // NO floor change for 1.6.0, which adds the concealed gates. A 1.5.x client on a 1.6.0 server
            // degrades cleanly rather than desyncing: concealed gates are withheld from the published index,
            // so an old client is never sent one and simply never has them as destinations, and the reveal is
            // a routed RPC it never registered - an unknown routed method is dropped by the receiving peer,
            // which is the same shape SectorSubscription relies on. The one visible difference is that an old
            // client walking up to a concealed gate gets the ordinary destination window on it, because it
            // does not run the guard that holds the menu shut until the gate is found - it can depart from a
            // gate it has not discovered, but it can never travel TO one, because it holds no row for any of
            // them. That is a smaller unfairness than turning the whole table away for a feature that does
            // nothing at all until an admin seeds a world.
            //
            // Raised to 1.5.0 with the 1.5.0 release. Three reasons, any one of which would do. A 1.4.7
            // client never reports a death, so it would get no corpse gate while everyone else did; walking up
            // to somebody else's corpse gate, it would open the full destination window on a server-minted
            // gate that is meant to lead one place; and it does not run the portal guard, so a troll standing
            // next to a 1.4.7 player can still knock a "self-healing" portal down while the ZDO is that
            // player's to own. A rule that binds some of the table and not the rest is worse than a version
            // message - the same call as 1.4.0 and 1.4.3.
            //
            // NO floor change for 1.4.7 either. It is the Valheim 1.0.7 rebuild: nothing on the wire and
            // nothing written to the world changed. The floor is academic here anyway - a 1.4.6 build cannot
            // load on 1.0.7 at all (its Minimap patch binds to a member 1.0 deleted, so PatchAll fails and
            // the whole plugin is down), so a client below 1.4.7 does not reach a 1.0.7 table with this mod.
            // NO floor change for 1.4.6. It is a menu that failed to close on one of the two ways of
            // travelling - entirely local, entirely cosmetic, nothing on the wire and nothing written to
            // the world. A 1.4.5 client on a 1.4.6 server behaves identically apart from the stuck window,
            // and turning that table away to fix a window would cost every player an update for nothing.
            //
            // Raised to 1.4.5, and for a reason that is not about this mod's own wire at all: an
            // un-updated client actively damages the world for everybody else. Vanilla's SnapToGround
            // keeps one global list of everything waiting to be put on the ground, and Snap WRITES the
            // height it lands on into the ZDO whenever that peer owns the ZNetView. A client below 1.4.5
            // has no guard on any of that: it snaps the trader standing in the zone somebody else is
            // walking into against terrain that has not finished arriving, and writes her under her own
            // hut for the entire table.
            //
            // 1.4.5 does now REPAIR that rather than only preventing it, which is a fair argument that the
            // floor could be relaxed - an updated client arriving puts her back on the ground whoever put
            // her under it. The floor stays anyway, and the repair is the reason: on a mixed table the two
            // halves fight. The stale client buries her on its next far shot, the updated client claims her
            // and lifts her out, and the pair of them hand her back and forth for as long as both are
            // connected. A trader who is reliably wrong is better than a trader who is intermittently wrong
            // and generating ownership churn while she does it.
            //
            // 1.4.4 was folded with the prevention half only and never released; 1.4.5 supersedes it.
            //
            // Was 1.4.3 before that, and that one earned it too. Every build before this one registered its synced
            // settings without ever registering a LOCKING entry, which left ConfigSync.IsLocked permanently
            // false - so every client was free to broadcast its own config file at the whole server, and the
            // server was free to adopt it from anyone. The effective portal cap on a live server drifted
            // between numbers nobody had set while the server's own config file said something else again.
            //
            // The server-side half of the fix holds against any client: a 1.4.3 server refuses a config push
            // from a non-admin whatever version sent it. The client-side half does not. A client below 1.4.3
            // has no LockConfiguration entry to receive, so its own IsLocked stays false and it will still
            // push its settings straight at every other client in the world, the server's refusal
            // notwithstanding - and its admins still have no bypass, because reading the game's own admin
            // list is 1.4.3 code. Both holes close only when both ends are on it, so the floor makes it a
            // version message instead of a rule that quietly applies to some of the table.
            //
            // Was 1.4.0 for the 1.4.2 release, and that one was worth being explicit about because it looks
            // like a wire change and is not. 1.4.2 fills in the owner NAME the index has always carried a
            // slot for: the Creator string has been the third field of format version 1 since 1.0.0, and it
            // has been going out empty on every portal ever published. Nothing was added, moved or resized -
            // a byte that was always there stopped being blank. A 1.4.0 client reads it exactly where it has
            // always read it and starts showing names without being updated at all.
            //
            // Left at 1.4.0 for the 1.4.1 release, deliberately. Everything 1.4.1 changed is server-side -
            // where the owner names come from - and nothing about the wire or the way the cap is counted
            // moved, so a 1.4.0 client on a 1.4.1 server behaves identically and gets the same names in its
            // destination list. Turning that table away would cost every player an update to fix a file only
            // the admin reads.
            //
            // Raised to 1.4.0 with the 1.4.0 release, by the owner's call. The wire alone would not have
            // demanded it - the creator IDs ride in an appended block a 1.3.1 client reads straight past -
            // but the portal cap would be enforced differently either side of the table. A 1.4.0 client
            // counts the server's whole-world index and is held to the limit; a 1.3.1 client counts only
            // what is loaded around it and can still walk somewhere quiet and keep building. One rule that
            // applies to some players and not others is worse than a version message, so the floor makes it
            // a version message.
            //
            // The previous floor was 1.3.0, set at the 1.3.1 release, where the shipped icon set and the
            // server-streamed icon catalogue landed - a mixed 1.2.x table read different maps for the same
            // world.
            //
            // Raising this is a BREAKING change for anyone still on an older build: they are turned away at
            // the handshake rather than let in to desync. Every client has to update alongside the server.
            // Note it also cuts the other way while testing - a 1.4.0 client will now refuse a server below
            // 1.4.0, because the check is symmetric.
            MinimumRequiredVersion = "1.5.0"
        };

        // Where the migration stamp lives. Named here so ConfigMigration can find it in a raw file that
        // predates the entry existing.
        public const string MetaSection = "0 - Meta";
        public const string ConfigVersionKey = "ConfigVersion";

        public static ConfigEntry<int> configVersion;

        public static ConfigEntry<bool> lockConfiguration;

        public static ConfigEntry<FilterMode> filterMode;
        public static ConfigEntry<string> whitelistItems;
        public static ConfigEntry<string> blacklistItems;
        public static ConfigEntry<string> skipPortalPrefabs;
        public static ConfigEntry<int> maxPortalsPerPlayer;
        public static ConfigEntry<bool> adminBypass;
        public static ConfigEntry<bool> enablePortalExport;
        public static ConfigEntry<float> portalExportIntervalSeconds;
        public static ConfigEntry<bool> allowTames;
        public static ConfigEntry<float> tameFollowRadius;
        public static ConfigEntry<bool> allowMountedTeleport;
        public static ConfigEntry<float> menuOpenDistance;
        public static ConfigEntry<float> menuCloseMargin;
        public static ConfigEntry<float> menuVerticalDistance;
        public static ConfigEntry<bool> menuRequireVerticalLineOfSight;
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
        public static ConfigEntry<bool> remoteSnapshots;
        public static ConfigEntry<bool> livePreviews;
        public static ConfigEntry<bool> anchoredEnvironment;
        public static ConfigEntry<bool> anchoredSpawning;
        public static ConfigEntry<bool> repairSunkenTraders;
        public static ConfigEntry<CameraResolution> cameraResolution;
        public static ConfigEntry<int> anchorRing;
        public static ConfigEntry<float> anchorTimeoutSeconds;
        public static ConfigEntry<float> anchorSettleRadius;
        public static ConfigEntry<float> portalIndexRefreshSeconds;
        public static ConfigEntry<bool> shareIconsFromServer;
        public static ConfigEntry<float> uiTextScale;
        public static ConfigEntry<int> uiFontSizeDelta;
        public static ConfigEntry<bool> showPortalPinsOnMap;
        public static ConfigEntry<PortalPinIcon> portalPinIcon;
        public static ConfigEntry<string> defaultPinIcon;
        public static ConfigEntry<bool> showPreviews;
        public static ConfigEntry<bool> openFavoritesView;
        public static ConfigEntry<bool> focusSearchOnOpen;
        public static ConfigEntry<bool> enableDebugMode;
        public static ConfigEntry<bool> selfHealingPortals;
        public static ConfigEntry<bool> enableCorpseRun;
        public static ConfigEntry<float> corpseRunGateTtlMinutes;
        public static ConfigEntry<float> corpseRunMaxGraveAgeMinutes;
        public static ConfigEntry<float> corpseRunClearanceMeters;
        public static ConfigEntry<float> corpseRunMinRoomMeters;
        public static ConfigEntry<float> corpseRunSearchRadiusMeters;
        public static ConfigEntry<float> corpseRunGraveOffsetMeters;
        public static ConfigEntry<float> corpseRunBedOffsetMeters;
        public static ConfigEntry<float> corpseRunArrivalWardSeconds;
        public static ConfigEntry<bool> enableConcealedPortals;
        public static ConfigEntry<bool> concealedDiscoveryShared;
        public static ConfigEntry<float> healthScanIntervalSeconds;
        public static ConfigEntry<bool> autoRepair;
        public static ConfigEntry<bool> repairDryRunDefault;
        public static ConfigEntry<int> repairMaxActionsPerRun;
        public static ConfigEntry<int> snapshotRetain;
        public static ConfigEntry<bool> snapshotOnWorldSave;

        public static void Init(ConfigFile config)
        {
            // Reads the file as it stands on disk before a single Bind touches it, so a setting that has
            // moved can be carried to its new home instead of being stripped out on the next save.
            ConfigMigration.Begin(config);

            configVersion = config.Bind(MetaSection, ConfigVersionKey, ConfigMigration.CurrentConfigVersion,
                "Internal bookkeeping - the layout version of this file, used to carry your settings across mod updates. Do not edit.");

            // FIRST in the section, and bound before anything it governs, because it is the setting that
            // decides whether any of the others mean anything on a client.
            lockConfiguration = config.Bind("1 - General", "LockConfiguration", true,
                "Server only, and on by default. Makes the SERVER's copy of the shared settings the one that counts: every client is sent the server's values when it joins and cannot push its own back over them. Off - which is how every build before 1.4.3 behaved, having no such switch - any player's config file silently becomes everyone's: one player who still has MaxPortalsPerPlayer sitting at 10 in their own file sets the cap at 10 for the entire server the moment they touch a setting in-game, and the server's config file and its live settings quietly stop agreeing about what the rules are. Server admins are exempt either way, so an admin can still turn a dial in-game and have it apply to the whole table. Only the shared, world-affecting settings are covered; colours, UI scale, map pin preferences and the rest are yours alone and have never been sent anywhere.");

            filterMode = config.Bind("1 - General", "FilterMode", FilterMode.Vanilla, "Determines how portal item filters behave: Vanilla, WhitelistBlacklist, BypassAll (everything allowed).");
            whitelistItems = config.Bind("1 - General", "WhitelistItems", "", "Comma-separated list of prefab names. If populated, ONLY these items can be teleported.");
            blacklistItems = config.Bind("1 - General", "BlacklistItems", "", "Comma-separated list of prefab names. Any item here CANNOT be teleported.");
            skipPortalPrefabs = config.Bind("1 - General", "SkipPortalPrefabs", "",
                "Portal prefabs TortalPortal leaves alone, comma-separated - for portals other mods add that work their own way (one-way gates, dungeon or instance portals, gates with their own menu). A listed portal behaves exactly as it would without this mod: no destination window, vanilla tag pairing, no runic gateway, no damage immunity, and it never appears in anyone's destination list, on the map, in the export or against the portal cap. Names are prefab names, not case-sensitive, e.g. \"portal_stone, MWL_Portal\"; * matches anything, so \"MWL_*\" covers a whole family. Empty (the default) means every portal is TortalPortal's. Gates this mod raises itself (corpse and concealed gates) are never skipped. Applies live; the destination list catches up at the next index refresh.");
            
            maxPortalsPerPlayer = config.Bind("2 - Limits", "MaxPortalsPerPlayer", 0,
                "Maximum portals a single player can place. 0 (the default) or any negative number disables the limit entirely - nobody is capped until an admin sets a positive number here.");
            adminBypass = config.Bind("2 - Limits", "AdminBypass", true, "Allows admins to bypass max portal limits, PIN locks, and item filters.");
            enablePortalExport = config.Bind("2 - Limits", "EnablePortalExport", true,
                "Server only. Writes who owns how many portals to BepInEx/config/TortalPortal/portals/barrkbot_portals.json every so often, so an admin can see who is at their cap without counting by hand. It also powers the player-name register, which is where the destination list gets the 'Owner' on each row - so turning it off writes no file, creates no folder, has the server keep no record of which player name goes with which ID, and leaves every Owner in the portal list reading 'Unknown'.");
            portalExportIntervalSeconds = config.Bind("2 - Limits", "PortalExportIntervalSeconds", 60f,
                new ConfigDescription("How often that file is rewritten, in seconds. It is a whole-world portal count, so there is little point going faster than the portal index itself refreshes.",
                                      new AcceptableValueRange<float>(15f, 900f)));
            
            allowTames = config.Bind("3 - Features", "AllowTames", true, "If true, tameable creatures near the player will teleport with them.");
            tameFollowRadius = config.Bind("3 - Features", "TameFollowRadius", 10f,
                new ConfigDescription("How far around you, in metres, following tames are picked up when you teleport. Only tames actually set to follow you come along.",
                                      new AcceptableValueRange<float>(2f, 30f)));
            allowMountedTeleport = config.Bind("3 - Features", "AllowMountedTeleport", true,
                "If true, teleporting while riding a tamed creature sends the mount through with you and puts you back in the saddle on arrival. Works with anything ridden through the game's own mount controls - vanilla saddles, custom saddle items, OdinMounts and the like. Ships and other steered contraptions are never dragged through.");
            // 1.25 rather than 0.8 from 1.3.1: folded in from the tuning profile. 0.8m meant standing
            // practically inside the gate before the window would open, which read as unresponsive.
            menuOpenDistance = config.Bind("3 - Features", "MenuOpenDistance", 1.25f,
                new ConfigDescription("How close you have to be, in metres, before the destination window and map open by themselves. Vanilla's portal activation range is 5m, which puts the window in your face while you are still walking up. The default of 0.8 means you have to walk right up into the gate, so the window is something you go and get rather than something that ambushes you. Measured HORIZONTALLY, so standing on a raised platform does not add phantom distance. Only affects the window - the portal's visuals keep their own range.",
                                      new AcceptableValueRange<float>(0.1f, 5f)));
            // Local: how sticky the window is once open is a personal taste, not a rule of the world.
            menuCloseMargin = config.Bind("3 - Features", "MenuCloseMargin", 1f,
                new ConfigDescription("Extra metres beyond MenuOpenDistance you have to retreat before an open destination window closes itself. This is the hysteresis that stops the window strobing when you hover right on the boundary.",
                                      new AcceptableValueRange<float>(0f, 5f)));
            // The companion to MenuOpenDistance. That one is measured flat across the ground, which on its
            // own means a player flying or walking directly OVER a gate is nought metres away from it and
            // the window opens in their face. This is the other half of the cylinder.
            menuVerticalDistance = config.Bind("3 - Features", "MenuVerticalDistance", 2f,
                new ConfigDescription("How far above or below the gate's footing, in metres, you may stand and still have the destination window open by itself. MenuOpenDistance is measured flat across the ground, so without this a player walking the floor above a portal - or flying over it - counts as standing in the gate. 2m clears stairs, platforms and the slope a gate is planted on, and stops the storey above. MenuCloseMargin applies here too once the window is open.",
                                      new AcceptableValueRange<float>(0.25f, 10f)));
            // Local: whether your own client bothers with the raycast is your business, and a player who
            // has built something this misreads wants the switch without waiting on an admin.
            menuRequireVerticalLineOfSight = config.Bind("3 - Features", "MenuRequireVerticalLineOfSight", true,
                "If true, a solid floor, roof or ceiling between you and the gate below you keeps the destination window shut even when you are within MenuVerticalDistance of it. This is what separates standing on the storey above a portal from standing on a rock beside it, both of which are the same number of metres up. Only ever checked when you are ABOVE the gate; the column looked along is the gate's own, so nothing you build next to a portal can block it.");
            enableCustomVFX = config.Bind("3 - Features", "EnableCustomVFX", true, "Replaces vanilla portal VFX with a custom procedural runic gateway, coloured by VFXColour.");
            // Deliberately NOT server-synced: this is a local visual nudge, not a rule of the world.
            vfxOrbHeightOffset = config.Bind("3 - Features", "VFXOrbHeightOffset", 0f, "Vertical nudge in metres for the rune orb inside the gate. The orb is auto-centred on each portal's own arch, so this is only for fine-tuning.");
            // Also local: when someone's portals light up is their own business. The check is against ANY
            // player, so on a shared screen the gate still wakes for a friend walking up.
            vfxWakeDistance = config.Bind("3 - Features", "VFXWakeDistance", 25f,
                new ConfigDescription("How close, in metres, a player has to be before the rune gateway wakes up. Beyond this the portal sits dormant like vanilla - no rings, no runes, no motes - and it spins up as someone approaches. 25 is far enough that a gate is already alive as it comes into view and you watch it build as you walk in, rather than having it appear beside you.",
                                      new AcceptableValueRange<float>(2f, 50f)));
            // 0.64 / 0.35 / 3 rather than 1 / 0.9 / 1: the tuned look, settled by eye in-game against the
            // modpack rather than in isolation. Fewer, dimmer particles in the gate itself, and the trailing
            // ribbon packed to the top of its range - the gateway reads as a fine-drawn thing that leaves a
            // dense wake, instead of a bright cloud that leaves a thin one. Brightness came down again for
            // 1.2 (0.64 to 0.35) alongside the move to gold: the warm colour carries more apparent light
            // than the old mint at the same value, and glared where the green had merely glowed.
            vfxIntensity = config.Bind("3 - Features", "VFXIntensity", 0.64f,
                new ConfigDescription("How MUCH gateway there is: particle density, rune spacing in the swirl and ring thickness all scale with it. Below the default the gate smoulders, above it it roars. How brightly it burns is VFXBrightness, which moves independently.",
                                      new AcceptableValueRange<float>(0.1f, 3f)));
            vfxBrightness = config.Bind("3 - Features", "VFXBrightness", 0.35f,
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
            // FFD24CFF - warm gold, the 1.2 look, settled in-game like the rest of the tuned defaults. The
            // greens (4CFF8C, then 89FFAC mint) always argued with vanilla's own warm light in the arch;
            // gold agrees with it, so the gateway and the piece finally read as one object without needing
            // VFXTintPortalFrame turned on.
            vfxColour = config.Bind("5 - Colours", "VFXColour", new Color(1.000f, 0.824f, 0.298f, 1f),
                "The rune gateway's colour - veil, rings, sigil, motes and the swirl trail all draw from this.");
            // 12FF21FF - a hard, saturated green, kept even now the gateway has gone gold. The pin is a
            // small mark on a busy map and wants to shout, which is the opposite of what the gateway colour
            // wants, so the two dials part company here.
            pinColour = config.Bind("5 - Colours", "PinColour", new Color(0.071f, 1.000f, 0.129f, 1f),
                "The Rune map pin's colour (glyph and ring). Ignored for vanilla icons and Custom PNGs, which bring their own colours.");
            uiGoldColour = config.Bind("5 - Colours", "UIGoldColour", new Color(0.80f, 0.62f, 0.26f, 1f),
                "The metal of the window frames. The deep shadow and bright highlight tones are derived from this one colour, so silver, bronze or verdigris is a single change. Applies live.");
            enablePortalCameraShot = config.Bind("3 - Features", "EnablePortalCameraShot", true, "If true, takes a camera snapshot when a portal is placed for the UI preview.");
            snapshotRefreshIntervalMinutes = config.Bind("3 - Features", "SnapshotRefreshIntervalMinutes", 5, "Interval in minutes for the background coroutine to re-photograph portals that already have a picture, so a destination that has been built up or burnt down does not keep showing the old view. 5 rather than 30: a portal only gets photographed while a player is standing near it anyway, so a long interval mostly meant missing the chance entirely. Set to 0 to disable refreshes - portals with no picture at all are still filled in regardless.");
            repairSunkenTraders = config.Bind("3 - Features", "RepairSunkenTraders", true,
                "On by default. Watches Haldor, Hildir and the Bog Witch for half a minute after they appear and puts them back on the ground if they are standing under it. Vanilla snaps a trader to the ground twice - once directly, one frame after she spawns, and once more on the next global snap pass - and if her zone's terrain edits have not arrived by then she is left below the finished surface and the wrong height is written into the world. This is the only part of the fix that can reach a trader who is ALREADY buried on a live world; the rest only stops it happening again. Only ever lifts her up to the terrain, never down onto it, so a trader standing on a platform or a player-built floor is left where she is.");

            selfHealingPortals = config.Bind("3 - Features", "SelfHealingPortals", true,
                "On by default. Portals shrug off damage: monsters stop choosing them as targets, any hit that lands is refused, and a portal found below full health is healed. Removal with the hammer is unaffected - a portal is still its builder's to take down. Corpse gates are always protected, whatever this says.");

            enableCorpseRun = config.Bind("6 - Corpse Run", "EnableCorpseRun", true,
                "On by default. When a player dies, the server raises a private, temporary portal near where they will respawn - their bed, or the start temple if they have none - leading straight to their tombstone. One way by design: the grave-side gate is an exit. Both gates come down when the grave is emptied, or after CorpseRunGateTtlMinutes if that is set.");
            corpseRunGateTtlMinutes = config.Bind("6 - Corpse Run", "CorpseRunGateTtlMinutes", 0f,
                new ConfigDescription("How long a corpse gate stands before closing on its own if the grave has not been emptied by then. 0 means no limit: the gate stands until the grave is empty, which is the point of it. This replaces CorpseRunTtlMinutes, whose 30-minute default shut gates while their owner was still walking back - a corpse run across half the map is longer than half an hour, and the gate closing mid-run is worse than no gate at all.", new AcceptableValueRange<float>(0f, 1440f)));
            corpseRunMaxGraveAgeMinutes = config.Bind("6 - Corpse Run", "CorpseRunMaxGraveAgeMinutes", 30f,
                new ConfigDescription("Only used for a grave nobody reported - one still standing after a server restart, or a death whose report never arrived. How old (in game minutes, which do not advance while the server is down) such a grave may be and still earn a gate. 0 means no limit. Deliberately NOT the same dial as the gate's own lifetime: a grave abandoned days ago should not raise a gate at its owner's bed on every login, while a gate for a death that just happened should stand until the body is collected.", new AcceptableValueRange<float>(0f, 1440f)));
            corpseRunClearanceMeters = config.Bind("6 - Corpse Run", "CorpseRunClearanceMeters", 10f,
                new ConfigDescription("Room every corpse gate (both ends) must have from every tree, rock, building piece, portal and generated location, measured edge to edge from the portal frame and from each object's real footprint rather than its pivot. Tiny props (mushrooms, flowers, berry bushes) only need 2m. If no spot inside the search radius can honour it, the most open dry spot found is used and the server log says so.", new AcceptableValueRange<float>(1f, 30f)));
            corpseRunMinRoomMeters = config.Bind("6 - Corpse Run", "CorpseRunMinRoomMeters", 2f,
                new ConfigDescription("The hard floor under CorpseRunClearanceMeters. The clearance above is a preference, not a requirement: nowhere in a Valheim forest has ten metres of edge-to-edge room from everything, so it is scanned in descending steps and the gate takes the closest spot that honours the largest step the ground can actually offer. This is the least room the frame will ever be given; only when nothing inside the search radius can offer even this does the gate fall back to the most open dry spot found. Raise it to refuse cramped spots outright, at the cost of a longer walk.", new AcceptableValueRange<float>(0.5f, 10f)));
            corpseRunSearchRadiusMeters = config.Bind("6 - Corpse Run", "CorpseRunSearchRadiusMeters", 40f,
                new ConfigDescription("How far out from the tombstone (and from the bed) to look for a spot honouring the clearance on dry, level ground. Rings are scanned outward, so the gate lands as close as the clearance allows; a bigger radius only matters in dense forest or built-up ground, at the cost of a longer walk from gate to grave.", new AcceptableValueRange<float>(5f, 150f)));
            corpseRunGraveOffsetMeters = config.Bind("6 - Corpse Run", "CorpseRunGraveOffsetMeters", 2f,
                new ConfigDescription("Minimum distance from the tombstone at which the grave-side gate may stand.", new AcceptableValueRange<float>(1f, 20f)));
            corpseRunBedOffsetMeters = config.Bind("6 - Corpse Run", "CorpseRunBedOffsetMeters", 3f,
                new ConfigDescription("Minimum distance from the bed (or the start temple) at which the bed-side gate may stand.", new AcceptableValueRange<float>(1f, 20f)));
            corpseRunArrivalWardSeconds = config.Bind("6 - Corpse Run", "CorpseRunArrivalWardSeconds", 20f,
                new ConfigDescription("The grave ward: how many seconds a player who steps through their own corpse gate cannot be hurt after landing at the grave, so whatever killed them does not kill them again while they gear up. The clock starts when the trip lands, not when it begins, and the ward ends early the moment the player attacks. 0 turns it off.", new AcceptableValueRange<float>(0f, 60f)));

            enableConcealedPortals = config.Bind("8 - Concealed Gates", "EnableConcealedPortals", true,
                "Server only, and on by default - it does nothing at all until an admin seeds a world with tortal_scatterconcealed. A concealed gate is a real portal standing in the wilderness that is deliberately kept out of the published portal index, so no client is sent its position, its name, or the fact that it exists: it appears in nobody's destination list and on nobody's map until a player walks up to it and presses E, at which point the server hands that one gate to that one player. Turning this off leaves any gates already standing where they are and simply stops them being discovered or handed out - they do not become visible, and tortal_concealedclear is how you actually get rid of them.");
            concealedDiscoveryShared = config.Bind("8 - Concealed Gates", "ConcealedDiscoveryShared", false,
                "Server only. Off by default: each player has to find a concealed gate for themselves. Turn it on and the first player to touch a gate reveals it for everybody on the server, which suits a group that travels together. Switching it on does not retroactively publish gates individuals had already found on their own - it applies from the next discovery onwards.");

            healthScanIntervalSeconds = config.Bind("7 - Portal Health", "HealthScanIntervalSeconds", 120f,
                new ConfigDescription("Server only. How often the background health scan walks every portal in the world looking for trouble: portals buried in the terrain, destination photographs too large to send, map icons the server does not have, players over the portal cap, stray corpse gates. Findings are logged and read back with tortal_health.", new AcceptableValueRange<float>(10f, 3600f)));
            autoRepair = config.Bind("7 - Portal Health", "AutoRepair", false,
                "Server only. Off by default. Runs the repair pass on the scan's own timer. Whether it writes anything is decided by RepairDryRunDefault: with that on (the default) the automatic pass only reports what it would do.");
            repairDryRunDefault = config.Bind("7 - Portal Health", "RepairDryRunDefault", true,
                "Server only. On by default. The automatic repair pass reports instead of writing. The console command is unaffected - tortal_repair is always a dry run and only tortal_repair --apply writes, after taking a snapshot.");
            repairMaxActionsPerRun = config.Bind("7 - Portal Health", "RepairMaxActionsPerRun", 20,
                new ConfigDescription("Server only. The most fixes a single repair pass will apply. The rest wait for the next pass, so a bad scan can never rewrite the whole world in one go.", new AcceptableValueRange<int>(1, 500)));
            snapshotRetain = config.Bind("7 - Portal Health", "SnapshotRetain", 30,
                new ConfigDescription("Server only. How many automatic snapshots (pre-save, pre-repair) to keep in BepInEx/config/TortalPortal/snapshots before the oldest are deleted. Named snapshots taken by hand are never pruned.", new AcceptableValueRange<int>(1, 1000)));
            snapshotOnWorldSave = config.Bind("7 - Portal Health", "SnapshotOnWorldSave", true,
                "Server only. On by default. Takes a snapshot of every portal's settings each time the world saves, so there is always a recent one to restore from.");

            remoteSnapshots = config.Bind("3 - Features", "EnableRemoteSnapshots", true,
                "Quietly load the world around ONE never-photographed portal at a time - out where nobody is standing - photograph it and unload it again, so portals nobody visits still get their picture. On a hosted world the hosting player's game does this from its own memory; on a dedicated server (which draws nothing itself) each player's game asks the server for the far portal's surroundings, photographs it and hands the picture back. Costs whoever is doing it a few seconds of background zone loading per portal. Off returns to photographs only being taken where somebody actually walks.");
            // EXPERIMENTAL, and off. The stored photograph is the shipped feature and it is finished;
            // this is the same machinery driven continuously. 1.3.1 fixed the two failures that
            // earned the label - the one-view-per-visit teardown leak and the temporal-AA shimmer -
            // so what keeps it experimental now is cost and the warm-up, not correctness.
            livePreviews = config.Bind("3 - Features", "EnableLivePreviews", false,
                "EXPERIMENTAL, off by default. Open a portal's picture full-size and, instead of the stored photograph, watch the destination LIVE - real weather, real time of day, rendered by your own game through the same anchor the background camera uses. It works, and it is genuinely the best way to see where a gate goes. Known rough edge: a warm-up of several seconds on a large or distant destination before the feed opens. Understand what it actually does before turning it on: this is not a video, it is your game BUILDING somewhere else and holding it standing while you watch - terrain, trees, buildings and creatures all really there, sized by AnchorZoneRing, and on a dedicated server all of it crosses the wire to you first. It is by far the most expensive thing this mod does. If your framerate suffers, turn AnchorZoneRing down before turning this off. Server-synced: the server's setting is final for everyone.");
            anchoredEnvironment = config.Bind("3 - Features", "AnchoredEnvironment", true,
                "Give a far-off portal its OWN sky. Valheim keeps one sky, one sun, one set of weather and one field of grass, all welded to wherever you are standing - so without this a camera in the Ashlands photographs real Ashlands ground under YOUR weather: no ash rain, no ashen sky, no grass. On, the anchored camera gets the destination's own fog, sun, ambient light, weather particles and grass for the fraction of a second it is drawing, and your own view is put back untouched. Costs a copy of the weather effects and a patch of grass at the far end, both of which clean themselves up. Off returns to photographs and feeds wearing your local weather.");
            // ON by default from 1.3.1, reversing the 1.3.0 call. Shipped off, the flagship view - a
            // LIVE window onto somewhere else - opened onto a world standing perfectly still, which
            // read as broken rather than cautious; a destination is supposed to look inhabited. The
            // description still says plainly what it costs, and off remains one click away for an
            // admin who wants glances to change nothing.
            anchoredSpawning = config.Bind("3 - Features", "AnchoredSpawning", true,
                "Let wildlife appear at a portal's destination while somebody is looking at it. Valheim only makes creatures where a player is standing, which is why a far-off gate can be fully loaded, correctly lit, weather and all, and still look like nowhere anybody lives. On, the anchored camera counts as a presence and the destination's own spawners run normally - and promptly, without vanilla's ten-second spawner warm-up. UNDERSTAND WHAT IT DOES: the creatures are REAL. They are written into the world, they are simulated by whoever is watching, and they remain after the view closes - so browsing an album of eight gates can seed eight neighbourhoods with monsters that another player later walks into. It also makes the watching client responsible for simulating a place it is not standing in. Off, destinations show the world and whatever already lives there, and looking at a gate changes nothing. Only a LIVE view does this, never the background photographer - an unattended camera walking the world stocking it with monsters is not a thing anybody asked for.");
            // 1080p by default from 1.3.1. It costs real bandwidth - see the description - but a
            // photograph you open FULL SCREEN at 720p is visibly soft, which is the whole point of it.
            cameraResolution = config.Bind("3 - Features", "CameraResolution", CameraResolution.Res1080p,
                "What the portal camera renders at - stored snapshots and the live feed both. Higher looks better and costs more: stored pictures live on the portal itself and travel to every player, so 1080p roughly doubles that traffic against 720p. Applies to new photographs; existing ones keep the size they were taken at.");
            // 2 by default from 1.3.1: a 3x3 block puts the horizon barely past the gate, and a
            // destination photograph exists to show you the country you are stepping into.
            anchorRing = config.Bind("3 - Features", "AnchorZoneRing", 2,
                new ConfigDescription("How much world the far camera loads around a portal, in rings of 64m zones: 1 = a 3x3 block (192m), 2 = a 5x5 block (320m). READ THIS BEFORE RAISING IT. This is not a picture-quality dial - it is the amount of REAL WORLD your game builds somewhere you are not standing, and everything else scales off it. Each ring is terrain, vegetation, buildings, containers and creatures, all instantiated for real: 1 is 9 zones, 2 is 25, and a hypothetical 3 would be 49. On a dedicated server every one of those objects is also sent to you over the wire, per portal, per look. Raising it costs loading time before the view appears, memory while it is held, and a hitch when it lands - and with AnchoredSpawning on it also widens the area that gets stocked with monsters. Hard-capped at 2 for that reason; 2 is already the point where a mid-range machine notices. If a destination is slow to appear or the game stutters when you open the album, this is the first thing to turn down, not up.",
                                      new AcceptableValueRange<int>(1, 2)));
            anchorTimeoutSeconds = config.Bind("3 - Features", "AnchorSettleBudgetSeconds", 40f,
                new ConfigDescription("How long the far camera will wait for a destination to finish streaming in and settle before giving up on the attempt. It never shoots early - an unsettled scene is skipped and retried later, so raising this trades patience for coverage of enormous bases. The live preview uses it too, as the point where it stops waiting for perfection and goes live with what is standing.",
                                      new AcceptableValueRange<float>(10f, 120f)));
            anchorSettleRadius = config.Bind("3 - Features", "AnchorSettleRadius", 100f,
                new ConfigDescription("How far around the portal, in metres, terrain must be finished rebuilding before the camera considers the scene settled. Smaller accepts shots sooner; larger insists on a wider finished foreground.",
                                      new AcceptableValueRange<float>(30f, 300f)));
            portalIndexRefreshSeconds = config.Bind("3 - Features", "PortalIndexRefreshSeconds", 10f,
                new ConfigDescription("How often the server rescans the world for portals and republishes the destination list. Renames, new portals and demolitions show up within this window. Only republished when something actually changed.",
                                      new AcceptableValueRange<float>(5f, 300f)));

            // Server-synced, because it decides whether the server spends bandwidth at all. The art itself is
            // cosmetic, but the traffic is the server's to authorise - and an admin who wants a world where
            // nothing but the shipped icons ever appears can have one by turning this off.
            shareIconsFromServer = config.Bind("3 - Features", "ShareIconsFromServer", true,
                "Offer the server's own map icons to every player. Drop PNGs into the TortalPortalPins folder beside the server's plugin and each client downloads the ones it does not already have, once, caching them by content - so a portal marked with your artwork looks the same to everyone with nothing to install. Icons a client already has (including the ones that ship with the mod) are never sent. Off means each player sees only the icons on their own disk.");
            
            // Local, like the rest of the map and UI preferences below: where a player likes their own map
            // marked is not the server's business.
            showPortalPinsOnMap = config.Bind("3 - Features", "ShowPortalPinsOnMap", true,
                "Marks every portal in the destination list on the large map while that window is open, labelled with its name. The markers are never saved and are removed when the window closes, so they cannot silt up your own pin list.");
            portalPinIcon = config.Bind("3 - Features", "PortalPinIcon", PortalPinIcon.Rune,
                "Which map icon to use for portals that have not been given one of their own. Icon0-Icon4 are the five icons vanilla offers in its own map-pin menu. Rune is TortalPortal's own glyph. Custom uses whichever icon DefaultPinIcon names.");

            // Named rather than a magic filename. Until 1.3.1, Custom meant one hard-coded file called
            // TortalPortalPin.png - which made no sense once a whole library existed and, worse, once the
            // server could send icons this player never had a file for at all. Naming one is the only
            // answer that works for a downloaded icon, and it is the player's own choice rather than
            // whatever happened to sort first.
            defaultPinIcon = config.Bind("3 - Features", "DefaultPinIcon", "",
                new ConfigDescription(
                    "Which icon PortalPinIcon = Custom should draw, without the .png. The dropdown lists every icon installed in the TortalPortalPins folder; you can also type the name of one the server has sent you, which the list cannot know about at startup. Empty uses a TortalPortalPin.png beside the plugin if you have one, and falls back to the Rune pin when neither is available.",
                    new SuggestedList(InstalledPinIconNames())));

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

            // AddLockingConfigEntry rather than AddConfigEntry: this is the one entry ConfigSync reads to
            // decide whether the server's values are binding on a client at all. Registered the ordinary way
            // it would sync the switch itself and switch precisely nothing.
            configSync.AddLockingConfigEntry(lockConfiguration);

            configSync.AddConfigEntry(filterMode);
            configSync.AddConfigEntry(whitelistItems);
            configSync.AddConfigEntry(blacklistItems);
            // Synced: which gates are TortalPortal's is one answer for the whole table. Half the players opening
            // the window on a gate the other half sees as a vanilla portal would split the network in two.
            configSync.AddConfigEntry(skipPortalPrefabs);
            configSync.AddConfigEntry(maxPortalsPerPlayer);
            // Synced so a client can tell whether the server wants name claims at all. Without it, "off"
            // would still have every client posting its name to a server that only drops them on the floor.
            configSync.AddConfigEntry(enablePortalExport);
            configSync.AddConfigEntry(adminBypass);
            configSync.AddConfigEntry(allowTames);
            configSync.AddConfigEntry(tameFollowRadius);
            configSync.AddConfigEntry(allowMountedTeleport);
            configSync.AddConfigEntry(menuOpenDistance);
            // Synced for the same reason as MenuOpenDistance: how close is close enough to be at a gate is
            // one shape, and half of it travelling while the other half stays local would be an odd shape.
            configSync.AddConfigEntry(menuVerticalDistance);
            configSync.AddConfigEntry(enableCustomVFX);
            configSync.AddConfigEntry(enablePortalCameraShot);
            configSync.AddConfigEntry(snapshotRefreshIntervalMinutes);
            configSync.AddConfigEntry(remoteSnapshots);
            configSync.AddConfigEntry(repairSunkenTraders);
            configSync.AddConfigEntry(livePreviews);
            configSync.AddConfigEntry(anchoredEnvironment);
            configSync.AddConfigEntry(anchoredSpawning);
            configSync.AddConfigEntry(cameraResolution);
            configSync.AddConfigEntry(anchorRing);
            configSync.AddConfigEntry(anchorTimeoutSeconds);
            configSync.AddConfigEntry(anchorSettleRadius);
            configSync.AddConfigEntry(portalIndexRefreshSeconds);
            configSync.AddConfigEntry(shareIconsFromServer);
            configSync.AddConfigEntry(selfHealingPortals);
            configSync.AddConfigEntry(enableCorpseRun);
            configSync.AddConfigEntry(corpseRunGateTtlMinutes);
            configSync.AddConfigEntry(corpseRunMaxGraveAgeMinutes);
            configSync.AddConfigEntry(corpseRunClearanceMeters);
            configSync.AddConfigEntry(corpseRunMinRoomMeters);
            configSync.AddConfigEntry(corpseRunSearchRadiusMeters);
            configSync.AddConfigEntry(corpseRunGraveOffsetMeters);
            configSync.AddConfigEntry(corpseRunBedOffsetMeters);
            configSync.AddConfigEntry(corpseRunArrivalWardSeconds);
            // Synced so a client can tell whether the server runs concealed gates at all, and under which rule.
            // Without it, a client's own file could decide to stop asking for reveals on a server that offers
            // them, or to keep asking on one that does not.
            configSync.AddConfigEntry(enableConcealedPortals);
            configSync.AddConfigEntry(concealedDiscoveryShared);

            // Pushes anything that moved into its new home, stamps the file and saves it.
            ConfigMigration.Finish(config, configVersion);
        }

        // ---- sanitized reads for the anchor dials ----------------------------------------------------------
        //
        // AcceptableValueRange clamps what the settings UI can produce, but a hand-edited file can hold
        // anything - NaN, Infinity, 9999. NaN in particular is poison: Mathf.Clamp(NaN) is still NaN, and a
        // NaN deadline turns a wait loop into a forever loop. Every consumer of these dials reads through
        // here, so a poisoned file degrades to the shipped default rather than to a hang or a 49-zone load.

        public static int AnchorRingSafe => Mathf.Clamp(anchorRing != null ? anchorRing.Value : 1, 1, 2);

        public static float AnchorTimeoutSafe => SaneFloat(anchorTimeoutSeconds, 40f, 10f, 120f);

        public static float AnchorSettleRadiusSafe => SaneFloat(anchorSettleRadius, 100f, 30f, 300f);

        private static float SaneFloat(ConfigEntry<float> entry, float fallback, float min, float max)
        {
            float v = entry != null ? entry.Value : fallback;
            if (float.IsNaN(v) || float.IsInfinity(v)) return fallback;
            return Mathf.Clamp(v, min, max);
        }

        // An enum entry can also be poisoned in the file (BepInEx parses what it can, but a cast from a
        // stale int is not bounded). Unknown values fall back to 720p.
        // A MENU, not a gate.
        //
        // ConfigurationManager draws any AcceptableValueList as a combo box, which is the whole point -
        // picking an icon should not mean typing a filename exactly. But the base class also REFUSES
        // anything outside the list and clamps it to the first entry, and this list can only ever hold what
        // was on disk when the game started. An icon the SERVER sends later has no file here to be listed,
        // so a perfectly good setting would be silently rewritten to something the player never chose.
        // Offer the choices; enforce nothing.
        private sealed class SuggestedList : AcceptableValueList<string>
        {
            public SuggestedList(string[] values) : base(values) { }
            public override bool IsValid(object value) => true;
            public override object Clamp(object value) => value;
        }

        // Every PNG in the icon folder, for the dropdown above. Deliberately a small standalone scan rather
        // than a call into TortalPinIcon: this runs inside Plugin.Awake before anything else is wired up,
        // and the icon library reaches into the synced catalogue, which is not a thing to touch this early.
        private static string[] InstalledPinIconNames()
        {
            List<string> names = new List<string>();
            try
            {
                string dir = Path.GetDirectoryName(Plugin.Instance.Info.Location) ?? "";
                string folder = Path.Combine(dir, TortalPinIcon.LibraryFolder);
                if (Directory.Exists(folder))
                {
                    foreach (string file in Directory.GetFiles(folder, "*.png", SearchOption.TopDirectoryOnly))
                    {
                        string name = Path.GetFileNameWithoutExtension(file);
                        if (!string.IsNullOrEmpty(name) && !names.Contains(name)) names.Add(name);
                    }
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"TortalPortal: could not list the installed map icons for the config dropdown (non-fatal, you can still type a name). Reason: {ex.Message}");
            }

            // Empty first: "no named default" is the default, and belongs at the head of the menu.
            names.Insert(0, "");
            return names.ToArray();
        }

        public static void GetCameraResolution(out int width, out int height)
        {
            CameraResolution r = cameraResolution != null ? cameraResolution.Value : CameraResolution.Res720p;
            switch (r)
            {
                case CameraResolution.Res480p: width = 854; height = 480; break;
                case CameraResolution.Res900p: width = 1600; height = 900; break;
                case CameraResolution.Res1080p: width = 1920; height = 1080; break;
                default: width = 1280; height = 720; break;
            }
        }

        // Admin, as far as this mod is concerned - and asked of two authorities on purpose, because the one
        // it used to ask on its own is false at exactly the wrong moments.
        //
        // ConfigSync.IsAdmin is ServerSync's own answer, and on a client it is nothing but the "lockexempt"
        // flag out of the config package the server sends during login. If that package is late, is missed,
        // or simply never lands, the flag stays false and every admin on the server is quietly demoted to an
        // ordinary player - no portal cap bypass, no PIN bypass, no item filter bypass, and not one line
        // anywhere saying why. It is also a static that ZNet.Shutdown never clears, so it can be stale in the
        // other direction after a world change.
        //
        // The game keeps its own copy of the same fact and hands it to EVERY client: ZNet sends the admin
        // list on RPC_PeerInfo and ZNet.GetAdminList reads it back. That is the authority vanilla itself
        // uses for its own admin commands, it needs no mod at either end, and it is correct from the moment
        // the player is connected. Either source saying yes is enough; both have to say no to refuse anyone.
        public static bool IsAdmin()
        {
            try
            {
                if (adminBypass == null || !adminBypass.Value) return false;
                return configSync.IsAdmin || LocalPlayerIsServerAdmin();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not establish admin status, treating this player as an ordinary one. Reason: {ex}");
                return false;
            }
        }

        private static bool _adminCache;
        private static float _adminCacheStamp = float.NegativeInfinity;

        // Cached for a second, because this is reached from OnGUI - the destination window asks on every
        // repaint - while the answer only changes when ZNet hands the client a new list, which it does once,
        // at login. A second of staleness across a world change costs nothing and expires on its own.
        public static bool LocalPlayerIsServerAdmin()
        {
            float now = Time.realtimeSinceStartup;
            if (now - _adminCacheStamp < 1f) return _adminCache;
            _adminCacheStamp = now;
            _adminCache = false;

            try
            {
                if (ZNet.instance == null) return false;

                // The host and the dedicated server ARE the authority - there is no list to be on.
                if (ZNet.instance.IsServer())
                {
                    _adminCache = true;
                    return true;
                }

                List<string> admins = ZNet.instance.GetAdminList();
                if (admins == null || admins.Count == 0) return false;

                string me = PlatformManager.DistributionPlatform.LocalUser.PlatformUserID.ToString();
                if (string.IsNullOrEmpty(me)) return false;

                // "Steam_76561198...". adminlist.txt is allowed to hold either that or the bare number, and
                // vanilla's own ZNet.ListContainsId accepts the bare form for Steam ONLY - so this matches
                // that rule rather than inventing a looser one that could hand admin to a different
                // platform's identically numbered account.
                int sep = me.IndexOf('_');
                bool steam = sep > 0 && string.Equals(me.Substring(0, sep), "Steam", StringComparison.OrdinalIgnoreCase);
                string bare = (sep >= 0 && sep + 1 < me.Length) ? me.Substring(sep + 1) : me;

                foreach (string raw in admins)
                {
                    if (string.IsNullOrEmpty(raw)) continue;
                    string entry = raw.Trim();
                    if (entry.Length == 0) continue;

                    if (entry.Equals(me, StringComparison.OrdinalIgnoreCase) ||
                        (steam && entry.Equals(bare, StringComparison.OrdinalIgnoreCase)))
                    {
                        _adminCache = true;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"TortalPortal: could not read the server's admin list (non-fatal, falling back to ServerSync's answer). Reason: {ex.Message}");
            }

            return _adminCache;
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
