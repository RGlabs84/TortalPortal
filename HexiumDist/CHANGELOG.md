# Changelog - TortalPortal

## [1.2.0]

### The album goes live
- **Added:** Open a portal's picture full-size and the stored photograph gives way to a **live view of the destination** — real weather, real time of day, swaying trees, rendered by your own game through the same anchor the background camera uses. The still stands in while the far world streams in (`going live...` in the footer, then `LIVE`), and closing the album unloads it all within seconds. Works on hosted worlds and dedicated servers alike; nothing new crosses the wire.
- **Added:** `EnableLivePreviews` (synced, default **on**) — the server's word is final. The feed's lifecycle is a deadman switch: any way of leaving the album — closing it, the window, the world, or a UI error — starves the feed of its keep-alive and it tears itself down within half a second. There is no exit path to forget.
- **Added:** The live view answers the mouse — **right-drag looks around** the destination, the **wheel zooms** (30°–100°). Each portal opens on its photograph's own framing.
- The background camera and the live view share the one anchor politely: the walk waits while you watch, the view waits out a mid-flight shot.

### The dials come off the faceplate
All server-synced — the server's values are the law of the table — and every one is read through a clamped, NaN-proof accessor, so a hand-poisoned config file degrades to the shipped default rather than to a frozen loop or a 49-zone hitch.
- **`CameraResolution`** (dropdown: 480p / **720p** / 900p / 1080p) — what the portal camera renders at, stored snapshots and live feed both. Stored pictures travel to every player, so higher resolutions cost real bandwidth; existing photographs keep the size they were taken at.
- **`AnchorZoneRing`** (1–2, default **1**) — how much world the far camera loads: 1 = 3×3 zones (192 m), 2 = 5×5 (320 m). Hard-capped at 2; a wider ring is a guaranteed hitch on any hardware.
- **`AnchorSettleBudgetSeconds`** (10–120, default **40**) — how long a far shot may wait for the destination to settle before the attempt is skipped. Raising it trades patience for coverage of enormous bases. Also where the live preview stops waiting for perfection and goes live with what is standing.
- **`AnchorSettleRadius`** (30–300 m, default **100**) — how far around the portal terrain must be finished rebuilding before a scene counts as settled.
- The server refuses sector subscriptions outright when `EnableRemoteSnapshots` is off, and refuses NaN/Infinity anchor positions — a client with a doctored config gets silence, not sectors.

### The camera walks the world on its own
- **Added:** **Remote snapshots** — portals nobody visits finally get their photograph. Until now a destination picture could only be taken where a player happened to be standing, because the world simply does not exist anywhere else: Valheim builds terrain and objects only around players. The hosting player's game now quietly *anchors* one never-photographed portal at a time — out where nobody is — streams in a 3×3-zone patch of world around it, takes the shot, and lets it all unload again seconds later. One portal per backfill tick, round-robin so a slow-loading one cannot starve the rest, and the loading is spread thin enough that the host should never feel it.
- **Added:** `EnableRemoteSnapshots` (synced, default **on**), governing both editions of the walk below.
- **Added:** **Dedicated servers are covered too.** The server itself draws nothing and cannot take a photograph — so the players do it for it. A client picks a never-photographed portal from the destination index it already holds, pulls that portal's data with the game's own single-object request, and asks the server — through a new, renewal-driven *sector subscription* — to briefly send the world around it, exactly as if the player were standing there. The zone streams in, the shot is taken, the picture lands back on the portal for everyone, and the far world unloads seconds later. A client that crashes mid-shoot costs the server nothing: subscriptions expire on their own within 15 seconds unless renewed.
- Portals loaded around players are photographed exactly as before; the anchor pass only ever touches portals nobody is near.
- Graceful on a mixed setup: against a server still running pre-1.2 (which does not know the subscription), a client burns three quiet timeouts, logs one clear warning, and stops for the session — the walk-near-it rule simply applies as before.
- **Fixed:** Far shots on dedicated servers could photograph a **half-loaded scene** — missing terrain, bald ground where trees hadn't arrived. The readiness test judged only the objects that had *arrived so far*, so mid-stream an incomplete area read as complete. The camera now waits until the area is genuinely **settled** — no new objects arriving for a sustained stretch, everything instanced, and no terrain mesh still building — and if it never settles within the budget it *skips the shot entirely*: a missing photograph heals itself on a later lap, while a photograph of missing terrain would have stood forever.
- **Changed:** The backfill shutter ticks **~16% faster** (10.5 s, from 12.5 s), and snapshots are saved at **JPG quality 85** (from 75) — noticeably cleaner skies and foliage for roughly a third more bytes per picture. Resolution stays 1280×720; the preview panel never shows more.
- **Fixed:** Far shots of **never-visited wilderness came back treeless** — complete terrain, no foliage. Not a loading-time problem: vegetation only comes into existence when a zone is first *generated*, and nobody had ever generated those zones, so there were no trees to send no matter how long the camera waited. The server now ghost-generates the subscribed zones on demand — the same mechanism it already runs around every player — so the trees exist, arrive, and are stood up before the shutter fires. (Grass is the one thing far photos still lack: the engine only sprinkles grass around the *player's* camera. Trees, bushes and rocks are all real objects and all present.)
- **Fixed:** `tortal_scatterclear` from a client only removed the portals loaded around that client and told you to run it on the host — unhelpful advice on a dedicated server. The command now also asks the **server** to sweep the whole world (admin-gated against the server's adminlist) and reports the count back to your console.
- **Added:** The far pass narrates itself in the log — one line when it anchors a portal, one when the photograph lands (with elapsed seconds), one when it skips. A silent success was indistinguishable from a pass that never ran.
- **Fixed:** Far photographs had a **black void past the loaded area** — brutal from any mountaintop. The distant mountains and coastlines you see in normal play are a low-detail terrain carpet the engine keeps wrapped around the *player's* camera; the snapshot camera, kilometres away, had none. Each far shot now raises its own temporary copy of that carpet pinned to the anchor, waits for it to bake, photographs a proper horizon, and tears it down with everything else. The player's own horizon is never touched.
- **Fixed:** A portal unloaded in the single frame between being chosen for a photograph and the shutter firing crashed the capture (harmlessly, but with an error in the log). Seen once in a 100-portal scatter test; the shot is now skipped and retaken next pass.

### The gateway goes gold
Settled by eye in-game, like 1.1.2's tuning round. Existing config files keep their own values — these are the *defaults*.
- `VFXColour` `89FFACFF` mint → **`FFD24CFF`** warm gold. The greens always argued with vanilla's own warm light in the arch; gold agrees with it, so the gateway and the piece read as one object without needing `VFXTintPortalFrame`.
- `VFXBrightness` `0.64` → **`0.35`** — the warm colour carries more apparent light than the mint did at the same value, and glared where the green had merely glowed.
- `PinColour` stays hard green: a small mark on a busy map wants to shout, which is the opposite of what the gateway wants.

> Compatibility: everything new runs on the hosting player alone, so `1.1.2` clients connect and play unaffected — the minimum required version stays `1.1.2`.

## [1.1.2]

### ⚠️ Every client must now match the server
- **Changed:** The minimum required version is raised from `1.0.1` to **`1.1.2`** — a server running this build refuses any client on an older one, rather than letting it in to behave differently. It had been pinned to the version that introduced the portal index, on the reasoning that an older client only missed features it did not have; that holds for the wire format but not for the experience, since 1.1.x changed what clicking a map marker does, when the destination window opens and whether it captures the keyboard. A mixed table produces two players describing different behaviour and neither of them wrong, which is far harder to diagnose than a refused connection. **Update every client alongside the server.**

### The map is yours again
- **Fixed:** **Click-n-go had stopped working.** Clicking a portal marker selected it and swung the map round to centre on that portal — which dragged the marker out from under your cursor, so the second click landed on empty map somewhere else and never travelled. Selecting from the map no longer recentres it; selecting from the *list* still does, which is where that behaviour was always wanted.
- **Fixed:** The double-click that trails every successful click-n-go could open vanilla's pin-name box over your arrival. It is now refused for a moment after a marker click is acted on.
- **Fixed:** The destination window took the keyboard the instant it opened, so the rest of your walk up to the portal was typed into the search box instead of moving you. The search box is no longer focused automatically — click it when you want it. `FocusSearchOnOpen` (local, **default off**) puts the old behaviour back.

### Marking the chosen destination
- **Changed:** Portal markers are all **the same size** now. The selected one used vanilla's `m_doubleSize`, which is a boolean — the only sizes on offer were normal and *twice* normal, which is why some markers looked oddly large.
- **Added:** The selected portal's marker is **30% larger and red**, name label included, so which portal you are about to step to is unmistakable at a glance. Everything else stays a normal pin.

### Tuned defaults
A fresh install now starts on the settings this build was actually played on, rather than the ones it was designed with. Every one of these is still a dial — nothing is locked, and an existing config file keeps whatever it already holds.
- `VFXIntensity` `1` → **`0.64`** and `VFXBrightness` `1` → **`0.64`**: fewer and dimmer particles in the gate itself. The materials are additive, so the old numbers glared against snow and daylight.
- `VFXSwirlDensity` → **`3`** (top of the range): the trailing ribbon is packed tight. Together with the above, the gateway reads as a fine-drawn thing leaving a dense wake rather than a bright cloud leaving a thin one.
- `VFXWakeDistance` `8` → **`25`** metres: a gate is already alive as it comes into view and builds as you walk in, instead of appearing beside you.
- `VFXColour` → **`89FFACFF`**, a paler mint green — at the lower brightness the old deeper green fell away to black at the edges of the swirl. `PinColour` → **`12FF21FF`**, hard and saturated, because a small mark on a busy map wants to shout where the gateway does not.
- `MenuOpenDistance` → **`0.8`** m, `SnapshotRefreshIntervalMinutes` `30` → **`5`**, `UITextScale` → **`1.05`**, `UIFontSizeDelta` `+2` → **`+6`**.
- `VFXTintPortalFrame` ships **off**. It works, but matching the gate's own light to the runes is a matter of taste rather than a fix — vanilla's warm arch against cool runes is a look in its own right.

### The gateway
- **Added:** `VFXTintPortalFrame` (local, default off) — the **portal piece's own glow is tinted to `VFXColour`**. The orange light in the arch was vanilla's colour, not the mod's, so a green gateway sat in an orange gate. Only genuinely emissive surfaces are repainted (the shader's emission keyword, not merely a stale colour value), and only the hue is taken from the dial — a faint glow stays faint. The timber, stone and metalwork keep their own materials. Reversible: turn it off, or turn off `EnableCustomVFX`, and the vanilla colours come straight back.
- **Added:** `VFXSwirlDensity` (local, `0.25`–`3`, default `3`) — how thickly the runes trailing off the gateway are laid along their ribbon, independent of `VFXIntensity`. The particle ceiling scales with it, so a high setting does not simply cut the tail off partway along.

### Interface
- **Changed:** `MenuOpenDistance` now defaults to `0.8` m rather than `2.5`. The window is something you walk into the gate to get, not something that meets you on the path.
- **Changed:** The destination snapshot backfill scans **20% more often** (every 12.5 s rather than 15 s). Worth knowing what that cannot fix: only portals actually loaded around a player can be photographed at all, so a portal nobody has visited still waits for somebody to walk near it.

> Existing config files keep the values they already hold — BepInEx only writes a default into a file that has never seen the setting. The values above are *defaults*; change them by hand or delete the file to take the new ones.

## [1.1.1]

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

### For testing a build
- **Added:** Two console commands, behind `devcommands` like vanilla's own world-editing commands.
  - `tortal_scatter [count] [radius]` raises test portals at random spots across the world — default 25 within 2000 m, on dry land, spread at least 64 m apart, each named for the biome it landed in and credited to you, with every fifth one PIN-locked (`1234`) so the locked-portal paths have something to be tested against.
  - `tortal_scatterclear` removes every portal raised that way and leaves real ones alone — each one is marked when it is raised, so cleaning up never has to guess.
- They raise the real portal prefab rather than a hand-built record, so a scattered portal is a genuine portal in every respect: it survives a world reload, replicates, photographs itself and can be walked through.

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
