# Changelog - TortalPortal

## [1.3.1]

### ⛔ The floor rises to 1.3.0
- **Breaking:** **`MinimumRequiredVersion` is now `1.3.0`.** Clients on 1.2.x are turned away at the handshake with a clear version message instead of being let in to play a different mod — 1.3.0 is where the shipped icon set and the server-streamed icon catalogue landed, and a mixed table below it reads different maps for the same world. Everyone updates together.

### 🎥 The live view works more than once
- **Fixed:** **One live view per visit.** The first portal you opened went live; every one after it sat on its stored photograph saying "warming" until you left the portal system and came back — which bought you exactly one more. The cause was a wrong belief about Unity itself: the feed's entire cleanup lived in a `try/finally` inside its coroutine, on the assumption that `StopCoroutine` runs the `finally`. **It does not** — stopping a coroutine only unschedules it, and a `finally` never entered never runs. So paging to the next portal (or clicking another row, or the window closing) stopped the feed *without* dropping its anchor, and every later feed queued politely, forever, behind an anchor whose owner no longer existed. Leaving and returning "fixed" it only because the background photographer eventually took a far shot and its own natural cleanup swept the leak. Teardown is now called explicitly by everything that stops a feed, is safe to run twice, and — belt and braces — a starting feed that finds the anchor held for hundreds of frames with nobody standing down now clears the leak itself and says so in the log, instead of waiting on a corpse.
- **Fixed:** **The shimmer.** The fine, permanent vibration across the live image was temporal antialiasing doing exactly what it is designed to do with no way to finish the job: TAA nudges the camera a sub-pixel every frame and relies on re-projecting the *previous* frame to cancel the nudge out — and a hand-rendered camera read back at its own cadence never gives it a previous frame that matches, so the jitter shows instead of cancelling. A photograph never shimmered because a single frame *is* the jitter, uncancelled and invisible. The feed's own profile copy now swaps TAA for FXAA — smoothing that finishes inside one frame, which is the only kind of frame this camera has — and turns off the animated film grain, which was a second, fainter crawl on top. The live camera also stops inheriting whatever jittered projection matrix the player's camera happened to be holding at the moment it was built (that latch also quietly disconnected the zoom wheel from reality).

### 🐗 The destination is somebody's home now
- **Changed:** **`AnchoredSpawning` is ON by default.** Shipped off in 1.3.0 out of caution, which meant the flagship view — a *live* window onto somewhere else — opened onto a world standing perfectly still. A destination is supposed to look inhabited. The setting's description still says plainly what it costs (the creatures are real, written to the world, and remain after you look away), and off is one click for any admin who wants glances to change nothing. Still server-synced, still live-view-only — the unattended background photographer never spawns anything.
- **Fixed:** **Even switched on, wildlife almost never actually appeared.** Valheim's zone spawner waits ten seconds after it wakes before its *first* attempt — and the anchored zone's spawner is created fresh every time you open a view, so that clock restarted on every glance. Anyone holding a feed open for less than ten seconds (that is: nearly everyone) never saw a single spawn attempt, no matter how correctly everything else worked. The anchor now drives the spawner directly, once a second, while a view is actually live. Only the warm-up is skipped, never the rules: every spawn still runs vanilla's full checks, and vanilla's own per-spawner clocks on the zone still cap how much can spawn — the pump cannot spawn faster than standing there would.

## [1.3.0]

### ⭐ Favorites survive logging out
- **Fixed:** **Starred portals came back unstarred the next session.** Stars were filed under each portal's object id, and Valheim reassigns every one of those the moment a world is read off disk — `ZDO.Load` opens by handing the object a brand new id. So the ids were perfectly stable all session and pointed at nothing the next time you logged in. The file on disk was intact the whole time; every key in it had simply been orphaned.
- Favorites are now filed by the portal's **position**, which is the thing that genuinely does not change. Renaming a gate keeps its star, as it should. Demolishing and rebuilding somewhere else loses it, which is honest — that is a different portal.
- Stars saved in the old format cannot be recovered — the ids they named stopped existing — so they are dropped once, with a line in the log saying so rather than silently. Star them one more time and it will be the last time.

### 🌍 Destinations that look like the place you are going
- **Added:** **A far-off portal is drawn under its own sky.** Valheim keeps *one* sky, *one* sun, *one* set of weather and *one* field of grass, builds them once and welds the whole kit to wherever you are standing — so a camera in the Ashlands photographed real Ashlands ground lit by the sun over *your* head, in whatever weather *you* happened to be having: no ash rain, no ashen sky, no ground foliage. No amount of extra world loading could have fixed that, which is why loading more never helped. The anchored camera now gets the destination's own fog, sun, ambient light, weather and grass for the fraction of a second it is drawing, chosen by the same seeded roll vanilla uses — so it is the weather a player standing there would actually be having. **Your own view never sees any of it.**
- **Added:** `AnchoredEnvironment` (server-synced, default on) turns that off and returns to pictures wearing your local weather.
- **Fixed:** **The far horizon was never requested at all.** The anchor asked Valheim for objects using a near ring and a distant ring, and passed **zero** for the distant one — and zero there does not mean "a modest distance", it means the loop that fetches them never runs. Everything close to the camera arrived perfectly and everything behind it was never asked for, which is why a destination could be fully loaded, correctly lit and correctly weathered and still read as an empty stage set. On a dedicated server it cost twice over, since the server's send path was passing zero as well.
- **Added:** `AnchoredSpawning` (server-synced, **default off**) — wildlife at the destination. Valheim only makes creatures where a *player* is, not merely where the world is loaded, so the anchored camera now counts as a presence and the destination's own spawners run normally. **Read the setting's description before enabling it:** the creatures are real, they are written into the world, and they are still there after you look away. It is the one setting in this mod that changes the world rather than a picture of it. Only a live view ever does this — never the unattended background photographer.

### 🎥 Live view — experimental, and now off by default
- **Changed:** **`EnableLivePreviews` ships off.** Watching a destination live works and is genuinely the best way to see where a gate goes, but it is not finished — there is a faint shimmer in the image and a warm-up of several seconds on a large or distant destination. It is left in, working, for anyone who wants to switch it on, rather than held back until perfect or shipped on by default in a state that would be judged as the mod's normal quality. **The stored photograph is the shipped feature, and it is finished.**
- **Fixed:** the live feed and the stored photograph were two different rendering pipelines, and every difference between them showed up as a feed that did not look like the still it replaced. There is one pipeline now — the feed is the snapshot path, minus the file, on a timer.
- **Fixed:** **auto-exposure was bleaching the feed about a second after it opened.** Post-processing's eye adaptation is history-driven, so a camera fired once for a photograph never adapts, while one that keeps rendering does — the destination's sky was correct and then washed out in front of you. The feed gets its own copy of the game's profile with auto-exposure off; yours is untouched.
- **Fixed:** a feed that lost its borrowed sky to a background photo pass never got it back, and spent the rest of the session under your local weather with nothing in the log to say why. The background walk now stands aside for a live view, and the view restores its own sky if it ever finds it missing.
- **Fixed:** the feed used to wait for a *perfect* settle, including a distant-terrain bake, before opening — correct for a permanent photograph, but it left a live view sitting on a stale still for ten or fifteen seconds looking broken. It opens once there is ground under the camera and finishes loading on screen, which is what looking out of a window is like. A camera that failed to build once also used to end the feed permanently and silently; it retries now.

### ⚙️ Settings
- Every setting was audited for whether it is actually connected to anything. All of them are.
- **`AnchorZoneRing` and `EnableLivePreviews` now say what they really cost.** Neither is a picture-quality dial: they govern how much genuine world your machine builds and holds somewhere you are not standing — terrain, trees, buildings, containers, creatures — and on a dedicated server all of it crosses the wire first. Both descriptions now say so plainly, including which one to turn down first if the game stutters.

### 🎨 Eight icons in the box — the mod ships artwork for the first time
- **Added:** **Eight hand-drawn map icons install with the mod** — `boar_meat_eyes`, `crowned_neck`, `party_longship`, `rainbow_mead`, `surprised_greydwarf`, `sword_deathsquito`, `troll_sign` and `wacky_viking_helmet`. They land in the `TortalPortalPins` folder beside the plugin, so the per-portal icon picker has something in it the moment you install rather than being an empty control pointing at a folder you have to go and create.
- All eight are **48×48** — exactly the box vanilla draws a great-map marker into — so they are pixel-perfect with no scaling, and the whole set is under 25 KB.
- Because every install has the *same* eight files, a gate marked `troll_sign` reads as a troll sign for **everyone**, with nothing sent over the wire at all.
- **Note for the record:** this is the first version to install any asset file. The interface is still drawn entirely in code — no textures, no shaders, no asset bundles — but the "adds no asset files to your install" line from 1.2.0 is no longer literally true, and the README no longer says it. The icons are plain PNGs you can open, edit or delete.

### 📡 The server can hand out artwork
- **Added:** **Icons now travel.** Drop PNGs into the `TortalPortalPins` folder beside the **server's** plugin and every client downloads the ones it does not already have — once, cached by content hash — so a gate marked with your own crest looks the same to everybody at the table with **nothing for them to install and no modpack to rebuild**. A player joining with a bare install still gets the full set.
- **Added:** **`tortal_pinreload`** — an admin console command that rescans the server's icon folder and republishes without a restart. Add a file, run it, and it is on everybody's map at their next login. Admin-gated against the server's own adminlist and reports the count back to your console, exactly as `tortal_scatterclear` does.
- **Added:** `ShareIconsFromServer` (server-synced, default on). Off means every player sees only the icons on their own disk.
- **Nothing is re-sent that a client already has.** The catalogue names each icon by content hash, and a client that holds identical bytes — the shipped eight, or anything it put in its own folder — matches them locally and downloads nothing. A server full of stock icons costs zero bandwidth.
- **Same name, different picture?** The server's copy wins, because the alternative is two players looking at the same portal and seeing different things — which is the exact problem the feature exists to solve. Your own file is untouched on disk, and the log says once which one was used and why.
- **The server never decodes an image.** A dedicated server is headless, and running a PNG decoder over bytes that arrived from the network is precisely the thing not to do — dimensions are read arithmetically out of the IHDR header instead, and no pixel is ever touched. Transfers are capped at 256×256, 96 KB and 64 icons per world, move in 16 KB chunks one frame apart so they never sit on the socket gameplay is using, and are verified against their hash before anything is written to disk.
- **Nothing breaks on a mixed table.** The catalogue is a new synced value and the transfers are new RPCs, so a `1.2.x` client simply never registers either, ignores the catalogue and draws its own marker. **The minimum required version stays `1.2.0`** — no forced update.

### 🖼️ A grid instead of arrows
- **Changed:** The portal configuration window's **Map icon** row is now a **grid of clickable icons** rather than `<` `>` arrows. Cycling was fine for the handful of icons that existed when nothing could be shared; it is not fine for a modpack's worth. Two rows are always shown inline, and anything past that puts a **`+N ▸`** cell in the last slot which opens a scrolling panel with the whole library — so the window is the same shape whether you have three icons or sixty.
- The current pick is highlighted, an icon still arriving from the server is labelled `(arriving from the server...)` rather than being lumped in with `(not installed here)`, and underscores read as spaces in the picker (`deep_mine.png` shows as *deep mine*) — the filename is still what gets stored.
- **Added:** A marker whose picture arrives while the map is already open **updates in place**, without closing and reopening the window.

### 🐛 Fixes
- **Fixed:** The destination window's button into the portal configuration screen still read **"Name & PIN"**, which described two of that window's three jobs — so the way to set a map icon was a button that never mentioned icons. It reads **"Configure"** now, after the window it opens, which cannot go stale again. (Found the honest way: by not being able to find the icon picker on the test server.)
- **Fixed:** **An icon request the server never answered was never retried.** The catalogue arrives on ConfigSync's login handshake, which can beat the routed-RPC channel being ready, so the very first fetch of a session could vanish into that gap — and the hash stayed marked "already asked for" until the next login. On the dedicated rig this showed up as seven of eight icons arriving and the alphabetically-first one silently never doing so. Requests now carry a timestamp and are re-asked if nothing comes back, and the client waits for a peer before asking at all.
- **Fixed:** `PortalPinIcon = Custom` with no `TortalPortalPin.png` installed used to fall back to *whichever icon sorted first alphabetically*. Harmless while nobody had a library — and a real problem the moment eight icons ship, because everyone running `Custom` would have silently woken up to a boar with meat for eyes on every unmarked portal. **`Custom` now means that one file and nothing else**, falling back to the rune pin when it is absent. No install can quietly repoint your default at somebody else's artwork.
- **Fixed:** The icon library's state is now **torn down when you leave a world**. It was per-server state living in statics with nothing ever clearing it, so an icon one server sent you would have gone on overriding your own file on the next server, and in single player, until the game was restarted.
- **Changed:** The icon cache lives under BepInEx's `config` folder rather than the plugin folder, because a mod manager owns the plugin directory and rewrites it on every update — and a cache thrown away on each update is not a cache.

## [1.2.2]

### 🖼️ Every portal can wear its own mark
- **Added:** **A library of custom map icons instead of one.** Drop as many PNGs as you like into a `TortalPortalPins` folder beside the plugin and every one of them becomes available — a modpack can ship a whole set rather than picking a single icon for the entire world.
- **Added:** **Each portal chooses its own.** The portal configuration window (`E` on a gate you built) gains a **Map icon** picker — arrows cycle through every installed icon with a live preview of the marker beside the name. Your mine wears a pickaxe, your swamp outpost a skull, your capital its own crest. The choice is stored on the portal itself, so **everyone on the server sees it**, and it rides the destination index — meaning portals you have never been near are drawn with their proper marker too.
- **Graceful when an icon is missing:** a portal marked with an icon you do not have installed simply falls back to your own `PortalPinIcon` setting rather than vanishing or drawing a blank. Two players running different modpacks each see a sensible map. The picker keeps the name it does not recognise rather than silently clearing somebody else's choice, and labels it `(not installed here)`.
- **Nothing breaks on a mixed table.** The icons are appended to the portal index *after* the part older builds read, so a `1.2.0`/`1.2.1` client reads the destination list exactly as before, stops where it always stopped, and never sees the extra data. **The minimum required version stays `1.2.0`** — no forced update, and no reason for one.
- The original single-file `TortalPortalPin.png` beside the plugin still works untouched and still wins the name if a library file matches it, so an install that has used `PortalPinIcon = Custom` for a year keeps exactly the icon it has always had.

### 🔍 Honest answers about custom icon files
- **Fixed:** A **non-square icon was stretched** to fill the marker, because vanilla sizes every pin as a square (48 px on the great map, 32 px on the minimap) on the reasonable assumption that its own icons are square. Custom images are now **letterboxed to fit** instead of distorted. Square is still what fills the marker best.
- **Changed:** Icon files are loaded **without a system-memory copy** now that they are only ever drawn, halving what each one costs to keep.
- **Added:** An oversized icon says so in the log. "Any size works" is true — vanilla forces the marker box, so a huge PNG cannot spill across your map — but a 4096×4096 file spends about 64 MB of video memory drawing something 48 pixels across. Anything past 256×256 now names itself, its dimensions and its cost, once.
- **Fixed:** Saving the portal configuration window on a gate you did not build (as an admin) could be **silently discarded**, because a write to a ZDO this game does not own is dropped at the next sync. Ownership is claimed before the write, as the snapshot camera already did.
- **Restored:** Selecting `PortalPinIcon = Custom` with no PNG installed anywhere says so in the log again, once per session, instead of quietly drawing the rune pin as though the setting had done nothing.

## [1.2.1]

### 📖 The manual catches up to the mod
- **Changed:** The README was rewritten top to bottom — reordered around **Features**, **Instructions** and **How-To** sections, with a table of contents, and checked line by line against the shipped code rather than against memory. It had gone stale enough to be visibly wrong in two places: `VFXBrightness`'s documented default (`0.64`) hadn't followed the gold retune down to its real value (`0.35`), and the selected map marker was still described as drawn "twice the size," a leftover from before 1.1.2 replaced that with the actual current behaviour (30% larger, and red).
- **Added:** A dedicated step-by-step walkthrough for assigning a custom map pin icon (`TortalPortalPin.png`) — previously just one line in a config table, now its own How-To section with the session-caching caveat (the file is read once per launch) spelled out.
- **Added:** Default hex values for `VFXColour`, `PinColour` and `UIGoldColour` in the config reference, and the missing default (`10`) for `PortalIndexRefreshSeconds`.
- No code changed and nothing about the mod's behaviour is different — every fact below still describes v1.2.0. This version exists only so the corrected README ships under its own number instead of silently overwriting the 1.2.0 listing.

## [1.2.0]

### ⚠️ Every client must match the server again
- **Changed:** The minimum required version rises from `1.1.2` to **`1.2.0`**. The far photographs and live previews below run through a server RPC that older builds never registered, so a `1.1.2` client at a `1.2.0` table would keep a silently dead album — photos never refresh, previews never go live, and nothing errors. The floor exists to turn exactly that into a clear version-mismatch message at the door instead. **Update every client alongside the server.**

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
- **Changed:** The backfill shutter ticks **~16% faster** (10.5 s, from 12.5 s), and snapshots are saved at **JPG quality 85** (from 75) — noticeably cleaner skies and foliage for roughly a third more bytes per picture. Resolution ships at 1280×720 and is now a dial — see `CameraResolution` above.
- **Fixed:** Far shots of **never-visited wilderness came back treeless** — complete terrain, no foliage. Not a loading-time problem: vegetation only comes into existence when a zone is first *generated*, and nobody had ever generated those zones, so there were no trees to send no matter how long the camera waited. The server now ghost-generates the subscribed zones on demand — the same mechanism it already runs around every player — so the trees exist, arrive, and are stood up before the shutter fires. (Grass is the one thing far photos still lack: the engine only sprinkles grass around the *player's* camera. Trees, bushes and rocks are all real objects and all present.)
- **Fixed:** `tortal_scatterclear` from a client only removed the portals loaded around that client and told you to run it on the host — unhelpful advice on a dedicated server. The command now also asks the **server** to sweep the whole world (admin-gated against the server's adminlist) and reports the count back to your console.
- **Added:** The far pass narrates itself in the log — one line when it anchors a portal, one when the photograph lands (with elapsed seconds), one when it skips. A silent success was indistinguishable from a pass that never ran.
- **Fixed:** Far photographs had a **black void past the loaded area** — brutal from any mountaintop. The distant mountains and coastlines you see in normal play are a low-detail terrain carpet the engine keeps wrapped around the *player's* camera; the snapshot camera, kilometres away, had none. Each far shot now raises its own temporary copy of that carpet pinned to the anchor, waits for it to bake, photographs a proper horizon, and tears it down with everything else. The player's own horizon is never touched.
- **Fixed:** A portal unloaded in the single frame between being chosen for a photograph and the shutter firing crashed the capture (harmlessly, but with an error in the log). Seen once in a 100-portal scatter test; the shot is now skipped and retaken next pass.

### Seed a world with portals
The commands every 1.2.0 feature above was shaken down with turn out to be a world-building tool in their own right, so they are documented ones now. Both are cheat commands: enable `devcommands` in the console first, which on a server means being on its adminlist.
- **`tortal_scatter [count] [radius]`** — raises portals at random spots across the world (default 25 within 2 000 m; up to 500, radius up to 10 000 m). Spots are spread evenly across the disc rather than crowding the middle, each gate is named for its biome and registered in the index like any hand-built portal, and every 5th one is PIN-locked with `1234` so lock testing has subjects too. Paired with remote snapshots this **seeds a world**: scatter a network across the map on day one and the camera walks it on its own, so the server opens with a fully photographed portal network no player has ever visited.
- **`tortal_scatterclear`** — removes every portal `tortal_scatter` raised and nothing else; scattered gates carry a marker, and hand-built portals are never touched. Run from a client it sweeps what is loaded around you *and* asks the server to sweep the whole world (admin-gated against the server's adminlist), reporting the total back to your console.

### The gateway goes gold
Settled by eye in-game, like 1.1.2's tuning round. Existing config files keep their own values — these are the *defaults*.
- `VFXColour` `89FFACFF` mint → **`FFD24CFF`** warm gold. The greens always argued with vanilla's own warm light in the arch; gold agrees with it, so the gateway and the piece read as one object without needing `VFXTintPortalFrame`.
- `VFXBrightness` `0.64` → **`0.35`** — the warm colour carries more apparent light than the mint did at the same value, and glared where the green had merely glowed.
- `PinColour` stays hard green: a small mark on a busy map wants to shout, which is the opposite of what the gateway wants.

### Housekeeping
- **Removed:** a dormant 1.0.0-era visual loader that was superseded by the procedural gateway before it ever shipped, yet still registered a patch on every launch. Nothing called it and nothing changes in play — but it was the one piece of code implying a dependency on anything, and now the technical report's opening line ("no bundles, no dependencies") is true to the letter.

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
