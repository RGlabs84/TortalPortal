# Changelog - TortalPortal

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
