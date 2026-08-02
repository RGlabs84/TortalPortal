# Changelog - TortalPortal

## [1.1.0]

### The map is now part of the interface
- **Fixed:** Clicks on the destination window fell straight through it onto the map behind — toggling pins, starting drags, opening vanilla's pin-name box, and a middle-click through the window pinged the whole server. The window is IMGUI and the map is not, and the two input systems had never been introduced. While the cursor is over any of the mod's windows, the map's mouse handling now stands down entirely.
- **Fixed:** The scroll wheel zoomed the map while it scrolled the portal list. It now does whichever one the cursor is actually over.
- **Added:** **Click-n-go.** The portal markers on the great map are now live: click one to select that portal (exactly as if you had clicked its row), click the selected one again to travel. Double-clicking near a portal marker no longer opens vanilla's pin-name box in your face.

### Favorites
- **Added:** Every row in the destination list carries a small heart — click it and it turns gold: that portal is starred. Favorites sort to the top of the list.
- **Added:** The **favorites strip**: when you have favorites, the destination window opens as a slender list of just them, one click to travel, with an arrow (`»`) that expands to the full window and one (`«`) that folds it back. `OpenFavoritesView` (local, default on) controls whether it opens folded.
- **Added:** A PIN-locked portal launched from the strip or a map marker gets the **quick PIN window**: the map closes, a small window opens with the keyboard already in the field, `Enter` fires the PIN — right and you are away, wrong and the screen says so and the field clears for another try. `Esc` cancels back to where you were. (The full window keeps its inline PIN row.)
- Favorites are yours alone — stored on your machine, per world, keyed to the portal itself so renaming keeps the star. Nothing about them crosses the wire.

### Riding through
- **Added:** Teleport while mounted and your mount comes with you — you are lifted out of the saddle, the beast is sent through beside the exit, and once you land you are seated again automatically. The remount goes through the saddle's own request, so on a busy server the server still arbitrates the seat, and whatever rules the saddle enforces still apply.
- Deliberately built against the game's mount interface rather than the vanilla saddle class, so **custom saddle items and modded mounts (OdinMounts and the like) ride through too**. Ships, carts and turrets are never dragged through — only a tamed creature qualifies.
- **Added:** `AllowMountedTeleport` (server-synced, default on) turns the whole behaviour off for a server that wants dismount-and-walk.
- **Added:** `TameFollowRadius` (server-synced, 2–30 m, default 10): how far around you following tames are collected when you travel — previously a hard-coded 10 m.

### The gateway breathes — and swirls
- **Changed:** The runic gateway is **dormant until someone approaches**, like vanilla's unlit stone: no rings, no runes, no motes, and no rendering cost, beyond `VFXWakeDistance` (local, 2–50 m, default 8).
- **Changed:** …and it no longer reads as a **switch**. Strength now follows how close the nearest player actually is, so the gate builds as you walk up and **bleeds away behind you** as you leave — emission falls off and the runes already in the air drift out on their own lifetimes, the way a flier's trail dissolves behind them rather than being cut off mid-air. Any player drives it, so a friend walking up lights the gate for you too.
- **Changed:** The veil is **rebuilt**. It was a wall of large glyphs drifting straight up the doorway, which read as a sprite sheet scrolling behind a frame. Now **small runes are drawn in off the edges of the opening**, swirling about the gate's own axis and spiralling slowly inward — and each takes its own shade from a band either side of `VFXColour`, so the gateway shimmers through a range instead of glowing one flat tone.
- **Added:** A fifth layer: a **swirling rune trail** circling the whole structure — an unseen point orbits the pillars weaving up and down, drawing a continuous soft-glow ribbon strung with rune glyphs behind it, the same trail technique as Wings of the Valkyrie's wingtips. It lazes around a quiet gate and whips around a charged one.
- **Changed:** The last vanilla portal visual — the blue "target found" flare — is **gone** while the custom VFX is on; the swirl and the rings carry that job now.
- **Fixed:** `EnableCustomVFX` **off did nothing** until you reloaded the world — and it half could not have worked, because the code that switched vanilla's effects off kept no record of what it had touched and so had no way back. It now records the strip per portal and undoes it exactly, and turning the setting either way re-applies to every portal already standing, live.
- **Added:** `VFXIntensity` (local, 0.1–3, default 1) and `VFXBrightness` (local, 0.1–3, default 1), and they move **independently**: intensity is how *much* gateway there is — density, ring thickness, rune spacing — while brightness is only how hard it burns. Previously one number did both, so anyone who wanted a dimmer gate got a thinner one too. `VFXColour` and `VFXOrbHeightOffset` now also apply live rather than on reload.

### Map markers
- **Changed:** The portal markers default to **TortalPortal's own rune pin** — a green glyph in a ring, generated at runtime like all of the mod's art. `PortalPinIcon` still offers vanilla's five icons, plus `Custom`: drop a `TortalPortalPin.png` beside the plugin DLL (ship it in a modpack to give a whole server one look) and the markers use it, falling back to the rune pin if the file is missing.

### Interface
- **Added:** The full-screen photograph is now an **album**: arrows on either side (and the keyboard's arrow keys) flip through every portal's picture without leaving the lightbox, the portal's name across the top of each. A picture still on its way from the server shows its frame while it loads.
- **Added:** `ShowDestinationPreviews` (local, default on). Turn it off and the destination window drops the photograph panel and narrows — snapshots are still captured and shared either way, so turning it back on costs nothing.
- **Added:** `MenuCloseMargin` (local, 0–5 m, default 1): the extra distance beyond `MenuOpenDistance` you must retreat before an open window closes itself — the hysteresis that stops it strobing on the boundary, previously hard-coded.
- **Changed:** `MenuOpenDistance` now runs 0.1–5 m — at the bottom of the dial you must be standing in the gate itself before the window opens.
- **Fixed:** …and any setting **below 1 m never opened the window at all**, however close you stood. The range was measured as a full 3-D distance from your feet to the portal's proximity point, which sits above the ground, leaving a permanent vertical gap of about a metre that no sub-1 m threshold could ever beat. It is now measured horizontally, which is also what the setting reads as meaning.
- **Fixed:** The hidden camera that photographs a portal's destination was left **enabled** — the flag was cleared before copying the game camera's settings, and the copy handed it straight back — so for one frame a second live camera rendered the player's view. Cleared after the copy now.
- **Changed:** More headroom on the text dials: `UITextScale` now reaches 3× and `UIFontSizeDelta` +24, for displays where +2 was still squinting territory.

### Colours
- **Added:** A `5 - Colours` section, all local, all live: `VFXColour` (the whole gateway — veil, rings, sigil, motes, swirl), `PinColour` (the rune map pin), and `UIGoldColour` (the window frames' metal — shadow and highlight tones derive from it, so silver or verdigris is one change).

### Compatibility
- Minimum required version stays at 1.0.1 — the portal index format is unchanged. The two new synced settings simply never reach an older client, which cannot ride a mount through anyway.

## [1.0.3]

### Interface
- **Fixed:** The name and PIN window took several attempts to open. 1.0.2 gave the destination window's filter box the keyboard as soon as it appeared, and Valheim gates its own `Use` key behind "is anything typing right now" — so `E` was being swallowed before it ever reached the portal. Whether it worked came down to whether the filter box happened to hold focus at that instant, which is why it looked intermittent. There is now a **Name & PIN** button in the destination window, so the way in is a button you can see rather than a keypress racing the focus. `E` still opens it directly when no window is up.
- **Fixed:** Messages like "Incorrect PIN!" appeared in the centre of the screen — behind the destination window, which covers the left half of it. They now shift to the clear side, using the same measurement the map already centres by, so the message and the map agree on which half you can see. Worked out from the HUD's own canvas scale, so it holds at any resolution and UI scale, and the message goes straight back to where vanilla puts it as soon as the window closes.
- **Added:** `UIFontSizeDelta` — a flat point offset on every bit of text in the portal windows, applied on top of `UITextScale`. Defaults to **+2** to match Fatty, which shares this exact theme and ships the same +2; set both mods to the same number and their windows read at one size. The title and footer bands grow with it so nothing clips.

### Under the hood
- The "who may rename this portal" test (its builder, plus server admins) is now one shared check rather than two copies, so the button and `E` can never disagree about it.
- Opening the configuration window from the destination window tears down the map markers and preview properly, instead of leaving portal markers stranded on the small minimap.

### Compatibility
- Minimum required version stays at 1.0.1 — the portal index format is unchanged, and everything above is client-side, so a 1.0.1 client works against a 1.0.3 server.

## [1.0.2]

### Interface
- **Fixed:** The configuration window opened with the mouse still locked to the camera and invisible, so the name and PIN fields could be looked at but not clicked. Valheim frees the cursor only for windows it knows by name — the map, the inventory, the store — and ours were not on that list. They are now, both of them. (The destination window got away with it only because it opens the large map.)
- **Fixed:** The configuration window is a modal opened deliberately with `E`, so it now holds the keyboard and the mouse outright rather than letting them half-reach the game underneath.
- **Added:** The destination window focuses its filter box the moment it opens, and clears the previous search. Walk up, start typing. `Escape` still hands the keyboard back without closing the window.

### Map
- **Added:** Every portal in the list is now marked on the large map, labelled with its name, with the selected one drawn at double size — vanilla marks nothing for a portal, so a list of names was the only clue where any of them were. The markers are never saved and are removed when the window closes, so they cannot silt up your own pin list. `ShowPortalPinsOnMap` turns them off; `PortalPinIcon` picks which of vanilla's five pin icons to use.
- **Fixed:** Selecting a destination centred the map on it — squarely behind the destination window, which covers the left half of the screen. The map now centres clear of the window, so the selection lands in the middle of the part you can actually see. Worked out from the map's own zoom and aspect, so it holds at any resolution, UI scale or zoom level.

### Compatibility
- Minimum required version stays at 1.0.1 — the portal index format is unchanged, so a 1.0.1 client works against a 1.0.2 server.

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
