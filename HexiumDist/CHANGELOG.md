# Changelog - TortalPortal

## [1.0.1]

### Multiplayer
- **Fixed:** The destination list was empty on servers. A client is only ever sent the world data around itself, so scanning locally for portals found the one or two underfoot and nothing else — worst of all on an established world where every portal was somewhere the player had not yet stood. The server now builds the portal index and publishes it, and a joining player receives the whole thing during login.
- **Added:** Startup backfill. The server scans the entire world for portals when it comes up, so a world that had portals long before this mod was installed lists all of them immediately, with no need to visit them first.
- **Added:** New portals, renames, PIN changes and demolitions are picked up by a repeating rescan and republished only when something actually changed. `PortalIndexRefreshSeconds` (5–300, default 10) controls the window.
- **Changed:** A destination's photograph and PIN are now fetched from the server the moment you select its row, rather than being broadcast to every player. Portals you have never visited show their preview as soon as it arrives.

### Previews
- **Fixed:** Portals on a dedicated server were never photographed. Snapshots are camera renders and a dedicated server draws nothing, so the capture pass — which only ran server-side — could never produce an image. It now runs on players, who photograph the portals loaded around them; between everyone playing, the network fills itself in.
- **Added:** Portals with no photograph are captured first, so an existing world backfills its previews as people move about it.
- **Fixed:** Snapshots taken of somebody else's portal were silently discarded. Ownership of the portal is now claimed before the image is written.

### Configuration
- **Added:** Config migration. BepInEx binds settings by section and key, so the moment one is renamed or moved the old line is orphaned and stripped on the next save — an admin updates the mod and their filter lists are simply gone. The config file now carries a layout version; on an update it is backed up beside itself and every value is carried to its new home. Settings that keep their name were always safe and still are.
- **Added:** Anything in an old config that no longer exists anywhere is named in the log with its value, alongside the path of the backup, rather than disappearing quietly.
- **Added:** `MenuOpenDistance` (1–10 m, default 2.5). Vanilla's portal activation range is 5 m, which threw the destination window up while you were still walking in. The portal's own glow and connection effect keep their full range.
- **Clarified:** `MaxPortalsPerPlayer` is unlimited out of the box — `0` (the default) or any negative number disables the limit entirely.

### Interface
- **Fixed:** The destination window did not open if another player stood closer to the portal than you did. Range is now measured from your own character rather than from whoever the game considers nearest.
- **Fixed:** Typing in the search, PIN and portal name fields also drove the character — naming a portal walked you into the sea, and letters bound to hotkeys fired mid-word. Focusing one of the mod's text fields now suppresses gameplay input the same way the game's own chat box does, releasing it on `Escape` or when the window closes.

### Compatibility
- **Changed:** Minimum required version raised to 1.0.1. A 1.0.0 client has no portal index and would show an empty destination window against a 1.0.1 server.

## [1.0.0] - Initial Release

### Travel
- **Added:** Custom portal destination window replacing vanilla tag pairing — every portal in the world, listed with its lock state, biome, distance and owner, with name search.
- **Added:** Selecting a destination pins it on the Large Minimap, opened alongside the window.
- **Added:** The window opens on approach and closes when you walk away. Arriving through a portal leaves the destination's menu shut until you step out of range and return.

### Previews
- **Added:** Portal camera snapshots, captured on placement and stored on the portal's ZDO so every player on the server sees them. Wide-angle, framed from in front of the gate looking out at the destination.
- **Added:** Background refresh pass keeps snapshots current as the world changes.
- **Added:** Click a thumbnail to open the photograph full screen at your resolution, captioned with biome and coordinates.

### Interface
- **Added:** Ornate gilt frame chrome — polished double rails, acanthus corner scrollwork, palmette crests — generated procedurally at runtime. The mod ships no textures, shaders or asset bundles.
- **Added:** `UITextScale` (0.6–2.0) scales all portal UI text and the surrounding layout. Applies immediately; local to each player, not server-synced.

### Security
- **Added:** PIN locking for portals, set from a per-portal configuration panel opened with `E`.
- **Added:** Admin bypass for PIN locks, item filters and placement limits.

### Rules
- **Added:** ServerSync configuration for whitelist, blacklist and bypass item filtering modes.
- **Added:** Per-player portal placement limits.

### Effects
- **Added:** Green runic gateway VFX replacing vanilla portal particles — rune veil, counter-rotating rune circles, ground sigil and rising embers, all generated in code.
- **Added:** The gateway measures each portal's own arch and centres itself in it, so wood, stone and black marble portals are all placed correctly.
- **Added:** `VFXOrbHeightOffset` for manual fine-tuning. Local, not server-synced.

### Companions
- **Added:** Tames (wolves, boars, lox, etc.) teleport with players automatically.
