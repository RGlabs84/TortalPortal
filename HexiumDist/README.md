# 🌀 TortalPortal

![Valheim Mod](https://img.shields.io/badge/Valheim-Portal_Overhaul-orange.svg)
[![Multiplayer Compatible](https://img.shields.io/badge/Multiplayer-Server--Synced-blue.svg)]()
[![Framework](https://img.shields.io/badge/Requires-BepInEx-red.svg)]()
[![Assets](https://img.shields.io/badge/Assets-100%25_Procedural-green.svg)]()
[![Version](https://img.shields.io/badge/Version-1.0.0-lightgrey.svg)]()

> *"The old way was to carve a name into the stone and hope another bore the same. A tally of runes, scratched and rescratched, and still you stepped through blind. Better to know the road before you walk it, Viking."*

**TortalPortal** is a complete overhaul of Valheim's portal network. Vanilla's tag-pairing is gone — walk up to any portal and a framed window opens listing every portal in the world, with the great map beside it marking your chosen destination. Each portal keeps a photograph of where it comes out, so you can see the ground before you commit to it.

Lock your gates behind a PIN. Bring the wolves. Decide exactly what may and may not travel. And do it all through an interface built to look like it belongs in the game, carved entirely in code — the mod ships no bundles, no textures and no shaders.

---

## ⚔️ Features

### 🗺️ Every Portal in the Realm, Listed
Step into range and the destination window opens: every portal in the world, sorted by name, each showing its lock state, its biome, how far it is from you, and who raised it. Search by name to cut a long list down. Selecting a destination pins it on the **Large Minimap**, which opens alongside the window, so you always see the route in context rather than guessing from a tag.

Walk away and the window closes itself. Arrive somewhere and it stays shut, so you are never greeted by a menu the instant you land — step off and walk back to bring it up again.

### 📸 See Exactly Where You Are Going
Every portal quietly photographs its own destination when you raise it, on a wide-angle lens standing out in front of the gate, and stores the shot on the portal itself so **every player on the server sees it**. A background pass keeps the images current as the world changes around them.

The list shows each shot as a thumbnail — and **clicking it opens the photograph full screen** at your resolution, captioned with the biome and coordinates. Read the terrain, spot the tar pit, notice the Fuling camp, *then* decide.

### 🖼️ A Frame Worth Looking At
Both portal windows are set in an ornate gilt frame: polished double rails, acanthus scrollwork mitring every corner, and a palmette crest struck into the top and bottom edges. It is drawn on a dark leather panel with gold-edged insets, and the destination list reads as a proper set of cards rather than a wall of text.

None of it is a shipped image. Every rail, flourish and crest is **generated in code at runtime**, lit by a single consistent light — which is why the mod adds no asset files to your install and cannot ever show you a missing-texture pink square.

If the text is too small or too large for your display, `UITextScale` moves it between 0.6× and 2×, and the rows, fields, buttons and windows all grow with it so nothing clips. Changes apply immediately.

### 🔒 Lock Your Gates
Walk up to a portal you built and press `E` to open its configuration panel: set its name and toggle a PIN lock in one place. Anyone else selecting that destination must enter the PIN before the gate will take them.

### 🐺 Bring the Pack
Your loyal companions travel with you. Any tamed creature following you or standing nearby steps through at your side.

### 🚧 Decide What Travels
Admins get a dual filtering system beyond vanilla's flat metal ban. Populate the **whitelist** to create an exclusive "these may pass" list, and use the **blacklist** to further restrict specific items — or switch the whole thing to `BypassAll` and let everything through. All of it is server-synced, so the rules are the server's, not each client's.

### ✨ The Runic Gateway
Vanilla's portal particles are replaced by a green runic gateway: a veil of drifting Elder Futhark glyphs filling the doorway, two counter-rotating rune circles standing in the gap, a magic circle scribed across the threshold, and slow embers rising through the whole site. It brightens and spins up as you step in.

The gateway **measures each portal's own arch and centres itself in it**, so wood, stone and black marble portals all read correctly — and so will any portal a future update or another mod adds.

### 👑 Admin Supremacy
A built-in bypass lets admins ignore PIN locks, item filters and portal placement limits entirely.

---

## 🍗 Guide to Portals

- **Place a portal** — the game snaps a hidden high-resolution photo of its destination for the preview system.
- **Configure it** — walk up and press `E`. Set its name and toggle a PIN lock.
- **Travel** — walk into the portal's range. Pick a destination from the list; the big map marks it, and clicking its picture shows you the place full screen. Hit **Teleport**.
- **On arrival** — the menu stays shut so you can get on with it. Step away and walk back to bring it up again.

---

## ⚙️ Configuration

Settings live in `wubarrk.TortalPortal.cfg`.

### Server-synced (admin controlled)
| Setting | What it does |
| :--- | :--- |
| `FilterMode` | `Vanilla`, `WhitelistBlacklist`, or `BypassAll`. |
| `WhitelistItems` | Comma-separated. Any item here bypasses vanilla portal restrictions (e.g. adding `Bronze` lets you teleport with Bronze, but Iron remains blocked). |
| `BlacklistItems` | Comma-separated. Any item here is explicitly blocked, even if it normally would be allowed. |
| `MaxPortalsPerPlayer` | Limit how many portals each player can construct. `0` for unlimited. |
| `AdminBypass` | Lets admins ignore portal limits, PIN locks and item filters. |
| `AllowTames` | Whether tamed creatures follow you through. |
| `EnableCustomVFX` | Replaces vanilla portal particles with the runic gateway. |
| `EnablePortalCameraShot` | Whether placing a portal captures a preview snapshot. |
| `SnapshotRefreshIntervalMinutes` | Interval for the background snapshot refresh. `0` to disable. |

### Local to your game (not synced — set these however you like)
| Setting | What it does |
| :--- | :--- |
| `UITextScale` | `0.6`–`2.0`. Scales all portal UI text and the layout around it. Applies immediately, no restart. |
| `VFXOrbHeightOffset` | Metres to nudge the rune orb up or down inside the gate. The orb auto-centres on each portal's arch, so this is only for fine-tuning. |
| `EnableDebugLogging` | Verbose logging for UI and teleport events. |

---

> ⚠️ **Dependencies:** BepInEx
