<!--
  SOURCE of the store README (README schema v3.1). Edit THIS file, then run:  python3 tools/fold.py
  The fold forges HexiumDist/README.md from it with ../libs-Tools/readme-forge/forge_readme.py. Never edit that output.
  Numbers that live in the code come from readme_gen.py ({{gen ...}}); every setting and command goes through {{key ...}}.
-->
# 🌀 TortalPortal

{{rune ᚱ | TORTALPORTAL | EVERY GATE ON ONE MAP | 15803d}}
{{badge VERSION | {{version}} | 8b1e1e}}
{{badge CLIENT + SERVER | SERVER-SYNCED | 1f4e79}}

| 🗺️ **Every gate** | 📸 **Photos** | 💀 **Corpse run** | 🛡️ **Unbreakable** |
|:-:|:-:|:-:|:-:|
| listed, on the map | of where each gate leads | a gate back to your grave | trolls can't knock it down |

> *"The old way was to carve a name into the stone and hope another bore the same. A tally of runes, scratched and rescratched, and still you stepped through blind. Better to know the road before you walk it, Viking."*

### Walk up to any portal and choose where it goes.

Vanilla's tag pairing is gone. A framed window lists every portal in the world, with the great map open beside it marking your destination. Each gate keeps a photograph of where it comes out, under that place's own sky, so you see the country before you step into it.

Die, and a private gate to your grave waits by your bed. Lock your gates with a PIN, give each one its own map icon, bring your wolves and your lox through with you, and let the server seed the wilds with gates nobody has found yet.

---

{{rune ᛞ | NEW IN {{version}} | LEAVE OTHER GATES BE | 8b1e1e}}

**Portals from other mods keep working their own way. List them, and TortalPortal leaves them alone.**

| You | It |
|---|---|
| **run a mod with its own portals** (one-way gates, dungeon or instance portals, gates with their own menu) | put their prefab names in {{key SkipPortalPrefabs}}, comma-separated |
| **have a whole family of them** | `*` matches anything: `MWL_*` covers every one of them; names are not case-sensitive |
| **walk up to a listed gate** | it behaves exactly as it would without TortalPortal: no window, vanilla tags, its own look |
| **look at your destination list** | listed gates never appear there, on the map, in the export or against anyone's portal cap |

🔹 **Also:** the package now ships `TortalPortal.pdb`, so an error in your log names the file and line.

---

{{rune ᚾ | BEFORE YOU INSTALL | READ THIS | b8860b}}

| | If… | Then… |
|---|---|---|
| 🤝 | you play on a **server** | install it on the server **and** every player. The server builds the portal list and holds the rules; keep players on the server's version. |
| 🔀 | you **replace vanilla portals** | tags stop mattering. Every portal can reach every other one through the window. |
| 🌀 | another mod **adds its own portals** | add their prefab names to {{key SkipPortalPrefabs}} so they keep working their own way. |
| 🗺️ | your world is **already running** | nothing to do. The server finds every gate already standing, and the camera photographs them on its own. |

---

{{rune ᚨ | I | EVERY GATE, LISTED | 15803d}}

**Step into a portal and the destination window opens: every gate in the world, with the great map beside it.**

| You | It |
|---|---|
| 🗺️ **step into range** | the window opens with every portal: name, lock, biome, distance and who built it |
| 🔎 **type a name** | the list narrows as you type |
| 📍 **look at the map** | every gate is marked; your chosen one is drawn larger and in red |
| 🖱️ **click a marker** | selects that gate; click it again to travel |
| 💛 **star a gate** | favorites sort to the top, and the window opens as a one-click strip of them |
| 🎯 **give a gate an icon** | each portal picks its own map mark from a grid: eight ship in the box, and you can add your own |

<details>
<summary>📖 <b>Read the saga</b> — where the list comes from, and why it is always complete</summary>

**The server builds the list.** It is the only machine that holds the whole world, so it scans every gate and hands the list to each player at login. Install TortalPortal on a world that has run for months and every portal already standing is in the list from the first gate you touch.

**The map marks are temporary.** They show while the window is open and vanish when it closes. Your own pins stay your own.

**Arriving never ambushes you.** The window stays shut when you land. Step off and walk back to bring it up.

**Your own icons travel.** Drop PNGs into the `TortalPortalPins` folder on the server, run {{key tortal_pinreload}}, and every player downloads the ones they lack, once, cached by content. Nothing to install, no modpack to rebuild.

</details>

---

{{rune ᛊ | II | SEE WHERE YOU GO | 15803d}}

**Every portal photographs its own destination, and every player sees the picture.**

| You | It |
|---|---|
| 🏗️ **raise a portal** | it photographs the view out of it, wide-angle, and stores it on the gate |
| 🖼️ **click a thumbnail** | the photograph opens full screen, captioned with biome and coordinates |
| ⬅️➡️ **press the arrows** | flip through every gate's picture like an album |
| 🌩️ **look at a far gate** | it shows that place's own sky and weather, not yours: ash falls in the Ashlands |
| 🔮 **switch on** {{key EnableLivePreviews}} | *(experimental, off by default)* the photograph becomes a live view you can look around in |

<details>
<summary>📖 <b>Read the saga</b> — how a gate nobody has visited gets photographed</summary>

**No portal waits for a visitor.** Valheim only builds the world around players, so a gate nobody stood near could never be photographed. TortalPortal's camera anchors one unphotographed gate at a time, streams a small patch of world in around it, waits for the ground to finish, takes the shot and lets it all unload. It works on dedicated servers too, so an established server fills its whole album by itself.

**The pictures stay current.** A background pass re-shoots gates on a timer, so a base that was built up or burnt down stops showing the old view.

**The live view is honest about its cost.** It is not a video: your game builds the destination and holds it while you watch. It is the most expensive thing the mod does, which is why it ships switched off. The stored photograph is the finished feature.

</details>

---

{{rune ᛉ | III | LOCKS AND WARDS | 15803d}}

**Your gates are yours to name and lock, and nothing can knock them down.**

| You | It |
|---|---|
| 🔒 **press E on a gate you built** | name it, pick its icon and set a PIN, all in one window |
| 🔑 **travel to a locked gate** | a small PIN window opens with the keyboard already in the field |
| 🧌 **watch a troll swing at it** | {{key SelfHealingPortals}}: monsters stop targeting portals, hits are refused, damage heals |
| 🔨 **take it down with the hammer** | still works: a portal is always its builder's to remove |
| 👑 **are an admin** | {{key AdminBypass}} lets you pass PIN locks, item filters and the portal cap |

---

{{rune ᛖ | IV | BRING THE PACK | 15803d}}

**Your tames step through with you, and you never leave the saddle.**

| You | It |
|---|---|
| 🐺 **travel with tames following** | every tame set to follow you within {{key TameFollowRadius}} comes along |
| 🐗 **travel while riding** | the mount goes through beside you and you are seated again on arrival |
| 🛶 **bring a ship or a cart** | they stay behind: only tamed creatures travel |

Built on the game's own mount interface, so custom saddles and modded mounts ride through like a lox.

---

{{rune ᛇ | V | THE CORPSE RUN | 15803d}}

**Die, and a private gate to your grave waits where you wake up.**

| You | It |
|---|---|
| 💀 **die** | the server raises a gate beside your bed (or the start temple) leading straight to your tombstone |
| 🚶 **walk up to it** | you are sent through; nobody else can use it or open a window on it |
| ⚰️ **empty your grave** | both ends close; it is one way by design, so there is no free trip home |
| 🌊 **died at sea or in a dungeon** | the grave gate floats on the water, or opens at the dungeon's entrance |

<details>
<summary>📖 <b>Read the saga</b> — how both ends find room to stand</summary>

**Placed with real clearance.** The server scans outward from the tombstone and the bed for a spot with room around it, measured edge to edge from every tree, rock, building piece and generated location. The room it asks for is a preference, tried in shrinking steps down to a hard floor, so a gate in a forest still lands close.

**On the real ground.** The height comes from the game's own terrain recipe, terraforming included, so a gate by a bed on a levelled base stands on the floor.

**Never in the way.** Corpse gates are never in the list, on the map or against anyone's cap. They cannot be hammered down for their materials, they always shrug off damage, and they refuse any removal the mod did not ask for. A stray from a past run is reaped at startup.

</details>

---

{{rune ᛈ | VI | CONCEALED GATES | 15803d}}

**Gates seeded in the wilds that nobody has been told about.**

| You | It |
|---|---|
| 🌱 **are an admin** | {{key tortal_scatterconcealed}} raises real gates across the world, on dry level ground |
| 🕯️ **walk past one** | it glows and says *touch the runes*, and opens no window |
| ✋ **press E on the stones** | the server checks you are really there, then hands the gate to you: it joins your list as `[FOUND]` |
| 🤝 **travel as a group** | {{key ConcealedDiscoveryShared}} makes the first finder reveal a gate for everyone |

<details>
<summary>📖 <b>Read the saga</b> — why a concealed gate is really hidden</summary>

**Withheld, not just undrawn.** A concealed gate is kept out of the portal list the server sends, so no client is ever told its position, its name or that it exists. Only the player who finds it is sent that one gate.

**Recorded on the gate itself.** Who found it is written into the world save, so it survives reinstalls and new machines, and no client can award itself a find.

**Dormant until seeded.** {{key EnableConcealedPortals}} is on by default and does nothing until an admin seeds a world.

</details>

---

{{rune ᚲ | VII | THE RUNIC GATEWAY | 15803d}}

**Vanilla's portal effects are gone. In their place stands a gateway of living runes.**

| You | It |
|---|---|
| 🌀 **walk towards a gate** | it wakes and builds as you come closer: glyphs swirl in the arch, rune circles turn, embers rise |
| 🚶 **walk away** | it fades behind you; the runes already in the air drift off on their own |
| 🎨 **change the colour** | {{key VFXColour}} repaints the gateway, live; {{key VFXTintPortalFrame}} tints the gate's own glow to match |
| 🔆 **turn the dials** | {{key VFXIntensity}} is how much gateway there is, {{key VFXBrightness}} how hard it burns |

<details>
<summary>📖 <b>Read the saga</b> — a frame worth looking at</summary>

**Every gate's arch is measured**, so wood, stone, black marble and modded portals all centre the gateway correctly.

**Nothing is a shipped image.** The window frames, gilt rails, scrollwork and crests are all drawn in code at runtime. The interface carries no textures, shaders or asset bundles and can never show a missing-texture square. {{key UIGoldColour}} reskins the metal (silver, bronze, verdigris) live, and {{key UITextScale}} and {{key UIFontSizeDelta}} grow the text and everything around it.

</details>

---

{{rune ᛏ | VIII | THE SERVER KEEPS THE LAW | 15803d}}

**The server holds the rules, and it checks its own portal network.**

| The law | What it means |
|---|---|
| 🔒 **Locked from the start** | {{key LockConfiguration}}: the server's settings are the ones that count; players cannot push their own |
| 🚧 **What may travel** | {{key FilterMode}} with {{key WhitelistItems}} and {{key BlacklistItems}}, beyond vanilla's flat metal ban |
| 🏗️ **How many gates** | {{key MaxPortalsPerPlayer}} caps each player, counted across the whole world |
| 🌀 **Whose gates** | {{key SkipPortalPrefabs}} hands other mods' portals back to them |
| 🩺 **A health check** | the server scans every gate for trouble: buried gates, oversized photographs, strays, players over the cap |
| ↩️ **Undoable repairs** | repairs are a dry run until you say apply, and a snapshot is taken first so you can restore it all |

---

{{rune ᛃ | THE LEDGER | DEFAULT DIALS | 3d3d3d}}

**What the main dials are set to out of the box.** 🔒 is set by the server, 🖥️ is your own.

{{gen ledger}}

---

{{rune ᚠ | THE TOOLBOX | QUICK START · HOW-TO | 1b1b1b | 8b1e1e | e8c35a}}

**First ten minutes:** install on the server and every player → build a portal and walk into it → pick a destination off the list or the map → press **E** on your gate to name it, lock it and give it an icon → star the gates you use most.

<details>
<summary>🛠️ <b>Leave another mod's portals alone</b></summary>

Find the portal's prefab name (the other mod's page, its config, or the hammer's piece list with a prefab-name mod). On the **server**, set {{key SkipPortalPrefabs}} to it, for example `portal_stone, MWL_*`. It is synced, so every player follows the server. Listed gates go back to vanilla behaviour at once; the destination list catches up at the next refresh. Gates TortalPortal raises itself (corpse and concealed gates) are never skipped.

</details>

<details>
<summary>🛠️ <b>Give your portals their own map icons</b></summary>

Open a gate's configuration window (**E** at the gate, or **Configure** in the destination window) and pick from the **Map icon** grid. To add your own, drop square PNGs (48×48 is ideal) into the `TortalPortalPins` folder beside `TortalPortal.dll`; the file name is the icon's name, so name them once and leave them. To set your own default marker for gates without one, set {{key PortalPinIcon}} to `Custom` and pick it in {{key DefaultPinIcon}}.

</details>

<details>
<summary>🛠️ <b>Share icons from your server</b></summary>

Put the PNGs in the `TortalPortalPins` folder beside the **server's** `TortalPortal.dll` and run {{key tortal_pinreload}}. Every player downloads only what they are missing, once per machine. Icons that ship with the mod are never sent. {{key ShareIconsFromServer}} turns it off.

</details>

<details>
<summary>🛠️ <b>Seed a world with portals</b></summary>

| Command | Does |
|---|---|
| {{key tortal_scatter}} `[count] [radius]` | raises ordinary gates across the world, named for their biome (cheat command: needs `devcommands`) |
| {{key tortal_scatterclear}} | removes only what `tortal_scatter` raised; hand-built portals are never touched |
| {{key tortal_scatterconcealed}} `[count] [radius]` | seeds concealed gates, spaced apart on dry level ground |
| {{key tortal_concealedclear}} | removes every concealed gate, found or not |
| {{key tortal_concealed}} | how many stand, how many are found, and by whom |

Scatter a network on day one and the camera photographs every gate by itself before anyone visits.

</details>

<details>
<summary>🛠️ <b>Check and repair the portal network</b></summary>

Typed at the server console they run there; typed in a client console they go to the server and are admin-gated against its admin list.

| Command | Does |
|---|---|
| {{key tortal_health}} | scans every gate now and lists what it found, worst first |
| {{key tortal_repair}} | a dry run: lists what a repair would do and touches nothing |
| {{key tortal_repair}} `--apply` | does it, after saving a snapshot that undoes it |
| {{key tortal_snapshot}} `[name]` | saves every gate's name, PIN, icon, builder and position |
| {{key tortal_snapshots}} | lists the snapshots, newest first |
| {{key tortal_restore}} `<name> [--recreate]` | puts every gate back the way the snapshot has it |

</details>

---

{{rune ᚷ | MULTIPLAYER | WHO NEEDS WHAT | 1f4e79}}

| 🌍 Works with vanilla players | 🏰 Needs it on the server | 🤝 Needs it on both ends |
|---|---|---|
| players without it can join, but TortalPortal gates carry no tags, so they cannot travel by them | the portal list, the photographs nobody visited, the corpse run, concealed gates, health checks | the window, the map, favorites, locks, the gateway, tames and mounts |

**Versions:** a player whose TortalPortal is older than the server accepts is turned away, with a message naming both versions.

---

{{rune ᛗ | COMPATIBILITY | OTHER MODS | 2e5e3a}}

| Mod | How they get on |
|---|---|
| ⚠️ **Mods that add their own portals** | TortalPortal takes over every portal by default: list theirs in {{key SkipPortalPrefabs}} to give them back |
| ⚠️ **Other portal overhauls** | do not run two: they fight over the same gate |
| **Modded portal pieces** | work: the gateway measures each arch, and the list includes every portal the game knows |
| **Mounts and saddles** (OdinMounts and friends) | ride through with you |
| **Fatty** | shares this window theme: match {{key UIFontSizeDelta}} and the two read the same |
| **Configuration Manager** | every setting shows; synced settings lock for anyone who is not an admin |

---

{{rune ᛚ | SETTINGS | CONFIGURATION MANAGER · F1 | b8860b}}

{{gen counts}}, in `wubarrk.TortalPortal.cfg`. Your settings are carried across updates when the file changes shape, with a backup beside it.

<details>
<summary>🔒 <b>Server-synced</b> — set on the server, locked to admins</summary>

| Section | Settings |
|---|---|
| 1 General | {{key LockConfiguration}} · {{key FilterMode}} · {{key WhitelistItems}} · {{key BlacklistItems}} · {{key SkipPortalPrefabs}} |
| 2 Limits | {{key MaxPortalsPerPlayer}} · {{key AdminBypass}} · {{key EnablePortalExport}} |
| 3 Features: travel | {{key AllowTames}} · {{key TameFollowRadius}} · {{key AllowMountedTeleport}} · {{key MenuOpenDistance}} · {{key MenuVerticalDistance}} · {{key PortalIndexRefreshSeconds}} |
| 3 Features: pictures | {{key EnablePortalCameraShot}} · {{key SnapshotRefreshIntervalMinutes}} · {{key EnableRemoteSnapshots}} · {{key EnableLivePreviews}} · {{key CameraResolution}} · {{key AnchoredEnvironment}} · {{key AnchoredSpawning}} · {{key AnchorZoneRing}} · {{key AnchorSettleBudgetSeconds}} · {{key AnchorSettleRadius}} |
| 3 Features: world | {{key EnableCustomVFX}} · {{key RepairSunkenTraders}} · {{key ShareIconsFromServer}} · {{key SelfHealingPortals}} |
| 6 Corpse Run | {{key EnableCorpseRun}} · {{key CorpseRunGateTtlMinutes}} · {{key CorpseRunMaxGraveAgeMinutes}} · {{key CorpseRunClearanceMeters}} · {{key CorpseRunMinRoomMeters}} · {{key CorpseRunSearchRadiusMeters}} · {{key CorpseRunGraveOffsetMeters}} · {{key CorpseRunBedOffsetMeters}} |
| 8 Concealed Gates | {{key EnableConcealedPortals}} · {{key ConcealedDiscoveryShared}} |

</details>

<details>
<summary>🖥️ <b>Local</b> — your own, never synced</summary>

| Section | Settings |
|---|---|
| 2 Limits (server's own file) | {{key PortalExportIntervalSeconds}} |
| 3 Features: window | {{key MenuCloseMargin}} · {{key MenuRequireVerticalLineOfSight}} · {{key ShowDestinationPreviews}} · {{key OpenFavoritesView}} · {{key FocusSearchOnOpen}} · {{key UITextScale}} · {{key UIFontSizeDelta}} |
| 3 Features: map | {{key ShowPortalPinsOnMap}} · {{key PortalPinIcon}} · {{key DefaultPinIcon}} |
| 3 Features: gateway | {{key VFXOrbHeightOffset}} · {{key VFXWakeDistance}} · {{key VFXIntensity}} · {{key VFXBrightness}} · {{key VFXSwirlDensity}} · {{key VFXTintPortalFrame}} |
| 5 Colours | {{key VFXColour}} · {{key PinColour}} · {{key UIGoldColour}} |
| 7 Portal Health (server's own file) | {{key HealthScanIntervalSeconds}} · {{key AutoRepair}} · {{key RepairDryRunDefault}} · {{key RepairMaxActionsPerRun}} · {{key SnapshotRetain}} · {{key SnapshotOnWorldSave}} |
| 4 Debug | {{key EnableDebugLogging}} |

</details>

---

{{rune ᛟ | CREDITS | THANK YOU | 1b1b1b | c9a227 | 1b1b1b}}

**Needs:** BepInExPack for Valheim · **Carries:** ServerSync by blaxxun · **More from Wubarrk:** *Mists of Avalor* on Hexium · *Made by Wubarrk.* · **License:** MIT

<details>
<summary>ᛟ <b>The runes on this page</b> — every banner is sealed with one</summary>

{{runes}}

</details>
