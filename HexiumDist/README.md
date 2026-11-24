<!-- README schema v3 -->
<!-- README schema v3.1 -->
# 🌀 TortalPortal

![ᚱ TORTALPORTAL · EVERY GATE ON ONE MAP](https://img.shields.io/badge/TORTALPORTAL-EVERY_GATE_ON_ONE_MAP-15803d?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNMyAxM1YxTDggNEwzIDdMOCAxMyIgZmlsbD0ibm9uZSIgc3Ryb2tlPSIjN2ViOTk0IiBzdHJva2Utd2lkdGg9IjIuNCIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5kIi8%2BPC9zdmc%2B)
![VERSION · 1.6.4](https://img.shields.io/badge/VERSION-1.6.4-8b1e1e?style=for-the-badge&labelColor=1b1b1b)
![CLIENT + SERVER · SERVER-SYNCED](https://img.shields.io/badge/CLIENT_%2B_SERVER-SERVER--SYNCED-1f4e79?style=for-the-badge&labelColor=1b1b1b)

| 🗺️ **Every gate** | 📸 **Photos** | 💀 **Corpse run** | 🛡️ **Unbreakable** |
|:-:|:-:|:-:|:-:|
| listed, on the map | of where each gate leads | a gate back to your grave | trolls can't knock it down |

> *"The old way was to carve a name into the stone and hope another bore the same. A tally of runes, scratched and rescratched, and still you stepped through blind. Better to know the road before you walk it, Viking."*

### Walk up to any portal and choose where it goes.

Vanilla's tag pairing is gone. A framed window lists every portal in the world, with the great map open beside it marking your destination. Each gate keeps a photograph of where it comes out, under that place's own sky, so you see the country before you step into it.

Die, and a private gate to your grave waits by your bed. Lock your gates with a PIN, give each one its own map icon, bring your wolves and your lox through with you, and let the server seed the wilds with gates nobody has found yet.

---

![ᛞ NEW IN 1.6.4 · LEAVE OTHER GATES BE](https://img.shields.io/badge/NEW_IN_1.6.4-LEAVE_OTHER_GATES_BE-8b1e1e?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNMSAyVjEyTDkgMlYxMkwxIDIiIGZpbGw9Im5vbmUiIHN0cm9rZT0iI2JmODM4MyIgc3Ryb2tlLXdpZHRoPSIyLjQiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCIvPjwvc3ZnPg%3D%3D)

**Portals from other mods keep working their own way. List them, and TortalPortal leaves them alone.**

| You | It |
|---|---|
| **run a mod with its own portals** (one-way gates, dungeon or instance portals, gates with their own menu) | put their prefab names in `SkipPortalPrefabs`, comma-separated |
| **have a whole family of them** | `*` matches anything: `MWL_*` covers every one of them; names are not case-sensitive |
| **walk up to a listed gate** | it behaves exactly as it would without TortalPortal: no window, vanilla tags, its own look |
| **look at your destination list** | listed gates never appear there, on the map, in the export or against anyone's portal cap |

🔹 **Also:** the package now ships `TortalPortal.pdb`, so an error in your log names the file and line.

---

![ᚾ BEFORE YOU INSTALL · READ THIS](https://img.shields.io/badge/BEFORE_YOU_INSTALL-READ_THIS-b8860b?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNNSAxVjEzTTIgNUw4IDkiIGZpbGw9Im5vbmUiIHN0cm9rZT0iI2Q3YmM3OCIgc3Ryb2tlLXdpZHRoPSIyLjQiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCIvPjwvc3ZnPg%3D%3D)

| | If… | Then… |
|---|---|---|
| 🤝 | you play on a **server** | install it on the server **and** every player. The server builds the portal list and holds the rules; keep players on the server's version. |
| 🔀 | you **replace vanilla portals** | tags stop mattering. Every portal can reach every other one through the window. |
| 🌀 | another mod **adds its own portals** | add their prefab names to `SkipPortalPrefabs` so they keep working their own way. |
| 🗺️ | your world is **already running** | nothing to do. The server finds every gate already standing, and the camera photographs them on its own. |

---

![ᚨ I · EVERY GATE, LISTED](https://img.shields.io/badge/I-EVERY_GATE%2C_LISTED-15803d?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNMyAxM1YxTTMgMUw4IDVNMyA1TDggOSIgZmlsbD0ibm9uZSIgc3Ryb2tlPSIjN2ViOTk0IiBzdHJva2Utd2lkdGg9IjIuNCIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5kIi8%2BPC9zdmc%2B)

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

**Your own icons travel.** Drop PNGs into the `TortalPortalPins` folder on the server, run `tortal_pinreload`, and every player downloads the ones they lack, once, cached by content. Nothing to install, no modpack to rebuild.

</details>

---

![ᛊ II · SEE WHERE YOU GO](https://img.shields.io/badge/II-SEE_WHERE_YOU_GO-15803d?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNNyAxTDIgNkw4IDhMMyAxMyIgZmlsbD0ibm9uZSIgc3Ryb2tlPSIjN2ViOTk0IiBzdHJva2Utd2lkdGg9IjIuNCIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5kIi8%2BPC9zdmc%2B)

**Every portal photographs its own destination, and every player sees the picture.**

| You | It |
|---|---|
| 🏗️ **raise a portal** | it photographs the view out of it, wide-angle, and stores it on the gate |
| 🖼️ **click a thumbnail** | the photograph opens full screen, captioned with biome and coordinates |
| ⬅️➡️ **press the arrows** | flip through every gate's picture like an album |
| 🌩️ **look at a far gate** | it shows that place's own sky and weather, not yours: ash falls in the Ashlands |
| 🔮 **switch on** `EnableLivePreviews` | *(experimental, off by default)* the photograph becomes a live view you can look around in |

<details>
<summary>📖 <b>Read the saga</b> — how a gate nobody has visited gets photographed</summary>

**No portal waits for a visitor.** Valheim only builds the world around players, so a gate nobody stood near could never be photographed. TortalPortal's camera anchors one unphotographed gate at a time, streams a small patch of world in around it, waits for the ground to finish, takes the shot and lets it all unload. It works on dedicated servers too, so an established server fills its whole album by itself.

**The pictures stay current.** A background pass re-shoots gates on a timer, so a base that was built up or burnt down stops showing the old view.

**The live view is honest about its cost.** It is not a video: your game builds the destination and holds it while you watch. It is the most expensive thing the mod does, which is why it ships switched off. The stored photograph is the finished feature.

</details>

---

![ᛉ III · LOCKS AND WARDS](https://img.shields.io/badge/III-LOCKS_AND_WARDS-15803d?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNNSAxM1YxTTUgNkwxIDJNNSA2TDkgMiIgZmlsbD0ibm9uZSIgc3Ryb2tlPSIjN2ViOTk0IiBzdHJva2Utd2lkdGg9IjIuNCIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5kIi8%2BPC9zdmc%2B)

**Your gates are yours to name and lock, and nothing can knock them down.**

| You | It |
|---|---|
| 🔒 **press E on a gate you built** | name it, pick its icon and set a PIN, all in one window |
| 🔑 **travel to a locked gate** | a small PIN window opens with the keyboard already in the field |
| 🧌 **watch a troll swing at it** | `SelfHealingPortals`: monsters stop targeting portals, hits are refused, damage heals |
| 🔨 **take it down with the hammer** | still works: a portal is always its builder's to remove |
| 👑 **are an admin** | `AdminBypass` lets you pass PIN locks, item filters and the portal cap |

---

![ᛖ IV · BRING THE PACK](https://img.shields.io/badge/IV-BRING_THE_PACK-15803d?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNMSAxM1YxTDUgNUw5IDFWMTMiIGZpbGw9Im5vbmUiIHN0cm9rZT0iIzdlYjk5NCIgc3Ryb2tlLXdpZHRoPSIyLjQiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCIvPjwvc3ZnPg%3D%3D)

**Your tames step through with you, and you never leave the saddle.**

| You | It |
|---|---|
| 🐺 **travel with tames following** | every tame set to follow you within `TameFollowRadius` comes along |
| 🐗 **travel while riding** | the mount goes through beside you and you are seated again on arrival |
| 🛶 **bring a ship or a cart** | they stay behind: only tamed creatures travel |

Built on the game's own mount interface, so custom saddles and modded mounts ride through like a lox.

---

![ᛇ V · THE CORPSE RUN](https://img.shields.io/badge/V-THE_CORPSE_RUN-15803d?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNNSAxVjEzTTUgMUw4IDRNNSAxM0wyIDEwIiBmaWxsPSJub25lIiBzdHJva2U9IiM3ZWI5OTQiIHN0cm9rZS13aWR0aD0iMi40IiBzdHJva2UtbGluZWNhcD0icm91bmQiIHN0cm9rZS1saW5lam9pbj0icm91bmQiLz48L3N2Zz4%3D)

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

![ᛈ VI · CONCEALED GATES](https://img.shields.io/badge/VI-CONCEALED_GATES-15803d?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNMyAxVjEzTTMgMUw2IDRMOSAxTTMgMTNMNiAxMEw5IDEzIiBmaWxsPSJub25lIiBzdHJva2U9IiM3ZWI5OTQiIHN0cm9rZS13aWR0aD0iMi40IiBzdHJva2UtbGluZWNhcD0icm91bmQiIHN0cm9rZS1saW5lam9pbj0icm91bmQiLz48L3N2Zz4%3D)

**Gates seeded in the wilds that nobody has been told about.**

| You | It |
|---|---|
| 🌱 **are an admin** | `tortal_scatterconcealed` raises real gates across the world, on dry level ground |
| 🕯️ **walk past one** | it glows and says *touch the runes*, and opens no window |
| ✋ **press E on the stones** | the server checks you are really there, then hands the gate to you: it joins your list as `[FOUND]` |
| 🤝 **travel as a group** | `ConcealedDiscoveryShared` makes the first finder reveal a gate for everyone |

<details>
<summary>📖 <b>Read the saga</b> — why a concealed gate is really hidden</summary>

**Withheld, not just undrawn.** A concealed gate is kept out of the portal list the server sends, so no client is ever told its position, its name or that it exists. Only the player who finds it is sent that one gate.

**Recorded on the gate itself.** Who found it is written into the world save, so it survives reinstalls and new machines, and no client can award itself a find.

**Dormant until seeded.** `EnableConcealedPortals` is on by default and does nothing until an admin seeds a world.

</details>

---

![ᚲ VII · THE RUNIC GATEWAY](https://img.shields.io/badge/VII-THE_RUNIC_GATEWAY-15803d?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNOCAyTDMgN0w4IDEyIiBmaWxsPSJub25lIiBzdHJva2U9IiM3ZWI5OTQiIHN0cm9rZS13aWR0aD0iMi40IiBzdHJva2UtbGluZWNhcD0icm91bmQiIHN0cm9rZS1saW5lam9pbj0icm91bmQiLz48L3N2Zz4%3D)

**Vanilla's portal effects are gone. In their place stands a gateway of living runes.**

| You | It |
|---|---|
| 🌀 **walk towards a gate** | it wakes and builds as you come closer: glyphs swirl in the arch, rune circles turn, embers rise |
| 🚶 **walk away** | it fades behind you; the runes already in the air drift off on their own |
| 🎨 **change the colour** | `VFXColour` repaints the gateway, live; `VFXTintPortalFrame` tints the gate's own glow to match |
| 🔆 **turn the dials** | `VFXIntensity` is how much gateway there is, `VFXBrightness` how hard it burns |

<details>
<summary>📖 <b>Read the saga</b> — a frame worth looking at</summary>

**Every gate's arch is measured**, so wood, stone, black marble and modded portals all centre the gateway correctly.

**Nothing is a shipped image.** The window frames, gilt rails, scrollwork and crests are all drawn in code at runtime. The interface carries no textures, shaders or asset bundles and can never show a missing-texture square. `UIGoldColour` reskins the metal (silver, bronze, verdigris) live, and `UITextScale` and `UIFontSizeDelta` grow the text and everything around it.

</details>

---

![ᛏ VIII · THE SERVER KEEPS THE LAW](https://img.shields.io/badge/VIII-THE_SERVER_KEEPS_THE_LAW-15803d?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNNSAxM1YxTTEgNUw1IDFMOSA1IiBmaWxsPSJub25lIiBzdHJva2U9IiM3ZWI5OTQiIHN0cm9rZS13aWR0aD0iMi40IiBzdHJva2UtbGluZWNhcD0icm91bmQiIHN0cm9rZS1saW5lam9pbj0icm91bmQiLz48L3N2Zz4%3D)

**The server holds the rules, and it checks its own portal network.**

| The law | What it means |
|---|---|
| 🔒 **Locked from the start** | `LockConfiguration`: the server's settings are the ones that count; players cannot push their own |
| 🚧 **What may travel** | `FilterMode` with `WhitelistItems` and `BlacklistItems`, beyond vanilla's flat metal ban |
| 🏗️ **How many gates** | `MaxPortalsPerPlayer` caps each player, counted across the whole world |
| 🌀 **Whose gates** | `SkipPortalPrefabs` hands other mods' portals back to them |
| 🩺 **A health check** | the server scans every gate for trouble: buried gates, oversized photographs, strays, players over the cap |
| ↩️ **Undoable repairs** | repairs are a dry run until you say apply, and a snapshot is taken first so you can restore it all |

---

![ᛃ THE LEDGER · DEFAULT DIALS](https://img.shields.io/badge/THE_LEDGER-DEFAULT_DIALS-3d3d3d?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNNCAyTDEgNUw0IDhNNiA2TDkgOUw2IDEyIiBmaWxsPSJub25lIiBzdHJva2U9IiM5NDk0OTQiIHN0cm9rZS13aWR0aD0iMi40IiBzdHJva2UtbGluZWNhcD0icm91bmQiIHN0cm9rZS1saW5lam9pbj0icm91bmQiLz48L3N2Zz4%3D)

**What the main dials are set to out of the box.** 🔒 is set by the server, 🖥️ is your own.

| Dial | Default | Range | Side |
|---|---|---|---|
| `MenuOpenDistance` | **1.25** | 0.1 – 5 | 🔒 |
| `MaxPortalsPerPlayer` | **no cap** | — | 🔒 |
| `PortalIndexRefreshSeconds` | **10** | 5 – 300 | 🔒 |
| `SnapshotRefreshIntervalMinutes` | **5** | — | 🔒 |
| `TameFollowRadius` | **10** | 2 – 30 | 🔒 |
| `CorpseRunClearanceMeters` | **10** | 1 – 30 | 🔒 |
| `CorpseRunSearchRadiusMeters` | **40** | 5 – 150 | 🔒 |
| `VFXWakeDistance` | **25** | 2 – 50 | 🖥️ |

<details>
<summary>📜 <b>The whole ledger</b> — all 28 numeric dials, by section</summary>

| Dial | Default | Range | Side |
|---|---|---|---|
| `MaxPortalsPerPlayer` | **no cap** | — | 🔒 |
| `PortalExportIntervalSeconds` | **60** | 15 – 900 | 🖥️ |
| `AnchorSettleBudgetSeconds` | **40** | 10 – 120 | 🔒 |
| `AnchorSettleRadius` | **100** | 30 – 300 | 🔒 |
| `AnchorZoneRing` | **2** | 1 – 2 | 🔒 |
| `MenuCloseMargin` | **1** | 0 – 5 | 🖥️ |
| `MenuOpenDistance` | **1.25** | 0.1 – 5 | 🔒 |
| `MenuVerticalDistance` | **2** | 0.25 – 10 | 🔒 |
| `PortalIndexRefreshSeconds` | **10** | 5 – 300 | 🔒 |
| `SnapshotRefreshIntervalMinutes` | **5** | — | 🔒 |
| `TameFollowRadius` | **10** | 2 – 30 | 🔒 |
| `UIFontSizeDelta` | **6** | -6 – 24 | 🖥️ |
| `UITextScale` | **1.05** | 0.6 – 3 | 🖥️ |
| `VFXBrightness` | **0.35** | 0.1 – 3 | 🖥️ |
| `VFXIntensity` | **0.64** | 0.1 – 3 | 🖥️ |
| `VFXOrbHeightOffset` | **0** | — | 🖥️ |
| `VFXSwirlDensity` | **3** | 0.25 – 3 | 🖥️ |
| `VFXWakeDistance` | **25** | 2 – 50 | 🖥️ |
| `CorpseRunBedOffsetMeters` | **3** | 1 – 20 | 🔒 |
| `CorpseRunClearanceMeters` | **10** | 1 – 30 | 🔒 |
| `CorpseRunGateTtlMinutes` | **no limit** | 0 – 1440 | 🔒 |
| `CorpseRunGraveOffsetMeters` | **2** | 1 – 20 | 🔒 |
| `CorpseRunMaxGraveAgeMinutes` | **30** | 0 – 1440 | 🔒 |
| `CorpseRunMinRoomMeters` | **2** | 0.5 – 10 | 🔒 |
| `CorpseRunSearchRadiusMeters` | **40** | 5 – 150 | 🔒 |
| `HealthScanIntervalSeconds` | **120** | 10 – 3600 | 🖥️ |
| `RepairMaxActionsPerRun` | **20** | 1 – 500 | 🖥️ |
| `SnapshotRetain` | **30** | 1 – 1000 | 🖥️ |

</details>

---

![ᚠ THE TOOLBOX · QUICK START · HOW-TO](https://img.shields.io/badge/THE_TOOLBOX-QUICK_START_%C2%B7_HOW--TO-1b1b1b?style=for-the-badge&labelColor=8b1e1e&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNMyAxM1YxTTMgNUw4IDFNMyA5TDggNSIgZmlsbD0ibm9uZSIgc3Ryb2tlPSIjZThjMzVhIiBzdHJva2Utd2lkdGg9IjIuNCIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5kIi8%2BPC9zdmc%2B)

**First ten minutes:** install on the server and every player → build a portal and walk into it → pick a destination off the list or the map → press **E** on your gate to name it, lock it and give it an icon → star the gates you use most.

<details>
<summary>🛠️ <b>Leave another mod's portals alone</b></summary>

Find the portal's prefab name (the other mod's page, its config, or the hammer's piece list with a prefab-name mod). On the **server**, set `SkipPortalPrefabs` to it, for example `portal_stone, MWL_*`. It is synced, so every player follows the server. Listed gates go back to vanilla behaviour at once; the destination list catches up at the next refresh. Gates TortalPortal raises itself (corpse and concealed gates) are never skipped.

</details>

<details>
<summary>🛠️ <b>Give your portals their own map icons</b></summary>

Open a gate's configuration window (**E** at the gate, or **Configure** in the destination window) and pick from the **Map icon** grid. To add your own, drop square PNGs (48×48 is ideal) into the `TortalPortalPins` folder beside `TortalPortal.dll`; the file name is the icon's name, so name them once and leave them. To set your own default marker for gates without one, set `PortalPinIcon` to `Custom` and pick it in `DefaultPinIcon`.

</details>

<details>
<summary>🛠️ <b>Share icons from your server</b></summary>

Put the PNGs in the `TortalPortalPins` folder beside the **server's** `TortalPortal.dll` and run `tortal_pinreload`. Every player downloads only what they are missing, once per machine. Icons that ship with the mod are never sent. `ShareIconsFromServer` turns it off.

</details>

<details>
<summary>🛠️ <b>Seed a world with portals</b></summary>

| Command | Does |
|---|---|
| `tortal_scatter` `[count] [radius]` | raises ordinary gates across the world, named for their biome (cheat command: needs `devcommands`) |
| `tortal_scatterclear` | removes only what `tortal_scatter` raised; hand-built portals are never touched |
| `tortal_scatterconcealed` `[count] [radius]` | seeds concealed gates, spaced apart on dry level ground |
| `tortal_concealedclear` | removes every concealed gate, found or not |
| `tortal_concealed` | how many stand, how many are found, and by whom |

Scatter a network on day one and the camera photographs every gate by itself before anyone visits.

</details>

<details>
<summary>🛠️ <b>Check and repair the portal network</b></summary>

Typed at the server console they run there; typed in a client console they go to the server and are admin-gated against its admin list.

| Command | Does |
|---|---|
| `tortal_health` | scans every gate now and lists what it found, worst first |
| `tortal_repair` | a dry run: lists what a repair would do and touches nothing |
| `tortal_repair` `--apply` | does it, after saving a snapshot that undoes it |
| `tortal_snapshot` `[name]` | saves every gate's name, PIN, icon, builder and position |
| `tortal_snapshots` | lists the snapshots, newest first |
| `tortal_restore` `<name> [--recreate]` | puts every gate back the way the snapshot has it |

</details>

---

![ᚷ MULTIPLAYER · WHO NEEDS WHAT](https://img.shields.io/badge/MULTIPLAYER-WHO_NEEDS_WHAT-1f4e79?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNMSAxTDkgMTNNOSAxTDEgMTMiIGZpbGw9Im5vbmUiIHN0cm9rZT0iIzgzOWRiNSIgc3Ryb2tlLXdpZHRoPSIyLjQiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCIvPjwvc3ZnPg%3D%3D)

| 🌍 Works with vanilla players | 🏰 Needs it on the server | 🤝 Needs it on both ends |
|---|---|---|
| players without it can join, but TortalPortal gates carry no tags, so they cannot travel by them | the portal list, the photographs nobody visited, the corpse run, concealed gates, health checks | the window, the map, favorites, locks, the gateway, tames and mounts |

**Versions:** a player whose TortalPortal is older than the server accepts is turned away, with a message naming both versions.

---

![ᛗ COMPATIBILITY · OTHER MODS](https://img.shields.io/badge/COMPATIBILITY-OTHER_MODS-2e5e3a?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNMSAxM1YxTDkgN005IDEzVjFMMSA3IiBmaWxsPSJub25lIiBzdHJva2U9IiM4Y2E2OTIiIHN0cm9rZS13aWR0aD0iMi40IiBzdHJva2UtbGluZWNhcD0icm91bmQiIHN0cm9rZS1saW5lam9pbj0icm91bmQiLz48L3N2Zz4%3D)

| Mod | How they get on |
|---|---|
| ⚠️ **Mods that add their own portals** | TortalPortal takes over every portal by default: list theirs in `SkipPortalPrefabs` to give them back |
| ⚠️ **Other portal overhauls** | do not run two: they fight over the same gate |
| **Modded portal pieces** | work: the gateway measures each arch, and the list includes every portal the game knows |
| **Mounts and saddles** (OdinMounts and friends) | ride through with you |
| **Fatty** | shares this window theme: match `UIFontSizeDelta` and the two read the same |
| **Configuration Manager** | every setting shows; synced settings lock for anyone who is not an admin |

---

![ᛚ SETTINGS · CONFIGURATION MANAGER · F1](https://img.shields.io/badge/SETTINGS-CONFIGURATION_MANAGER_%C2%B7_F1-b8860b?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNMyAxM1YxTDggNSIgZmlsbD0ibm9uZSIgc3Ryb2tlPSIjZDdiYzc4IiBzdHJva2Utd2lkdGg9IjIuNCIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5kIi8%2BPC9zdmc%2B)

65 settings: 38 🔒 server-synced, 27 🖥️ local, in `wubarrk.TortalPortal.cfg`. Your settings are carried across updates when the file changes shape, with a backup beside it.

<details>
<summary>🔒 <b>Server-synced</b> — set on the server, locked to admins</summary>

| Section | Settings |
|---|---|
| 1 General | `LockConfiguration` · `FilterMode` · `WhitelistItems` · `BlacklistItems` · `SkipPortalPrefabs` |
| 2 Limits | `MaxPortalsPerPlayer` · `AdminBypass` · `EnablePortalExport` |
| 3 Features: travel | `AllowTames` · `TameFollowRadius` · `AllowMountedTeleport` · `MenuOpenDistance` · `MenuVerticalDistance` · `PortalIndexRefreshSeconds` |
| 3 Features: pictures | `EnablePortalCameraShot` · `SnapshotRefreshIntervalMinutes` · `EnableRemoteSnapshots` · `EnableLivePreviews` · `CameraResolution` · `AnchoredEnvironment` · `AnchoredSpawning` · `AnchorZoneRing` · `AnchorSettleBudgetSeconds` · `AnchorSettleRadius` |
| 3 Features: world | `EnableCustomVFX` · `RepairSunkenTraders` · `ShareIconsFromServer` · `SelfHealingPortals` |
| 6 Corpse Run | `EnableCorpseRun` · `CorpseRunGateTtlMinutes` · `CorpseRunMaxGraveAgeMinutes` · `CorpseRunClearanceMeters` · `CorpseRunMinRoomMeters` · `CorpseRunSearchRadiusMeters` · `CorpseRunGraveOffsetMeters` · `CorpseRunBedOffsetMeters` |
| 8 Concealed Gates | `EnableConcealedPortals` · `ConcealedDiscoveryShared` |

</details>

<details>
<summary>🖥️ <b>Local</b> — your own, never synced</summary>

| Section | Settings |
|---|---|
| 2 Limits (server's own file) | `PortalExportIntervalSeconds` |
| 3 Features: window | `MenuCloseMargin` · `MenuRequireVerticalLineOfSight` · `ShowDestinationPreviews` · `OpenFavoritesView` · `FocusSearchOnOpen` · `UITextScale` · `UIFontSizeDelta` |
| 3 Features: map | `ShowPortalPinsOnMap` · `PortalPinIcon` · `DefaultPinIcon` |
| 3 Features: gateway | `VFXOrbHeightOffset` · `VFXWakeDistance` · `VFXIntensity` · `VFXBrightness` · `VFXSwirlDensity` · `VFXTintPortalFrame` |
| 5 Colours | `VFXColour` · `PinColour` · `UIGoldColour` |
| 7 Portal Health (server's own file) | `HealthScanIntervalSeconds` · `AutoRepair` · `RepairDryRunDefault` · `RepairMaxActionsPerRun` · `SnapshotRetain` · `SnapshotOnWorldSave` |
| 4 Debug | `EnableDebugLogging` |

</details>

---

![ᛟ CREDITS · THANK YOU](https://img.shields.io/badge/CREDITS-THANK_YOU-1b1b1b?style=for-the-badge&labelColor=c9a227&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNMiAxM0w4IDZMNSAyTDIgNkw4IDEzIiBmaWxsPSJub25lIiBzdHJva2U9IiMxYjFiMWIiIHN0cm9rZS13aWR0aD0iMi40IiBzdHJva2UtbGluZWNhcD0icm91bmQiIHN0cm9rZS1saW5lam9pbj0icm91bmQiLz48L3N2Zz4%3D)

**Needs:** BepInExPack for Valheim · **Carries:** ServerSync by blaxxun · **More from Wubarrk:** *Mists of Avalor* on Hexium · *Made by Wubarrk.* · **License:** MIT

<details>
<summary>ᛟ <b>The runes on this page</b> — every banner is sealed with one</summary>

| Rune | Means | Seals |
|---|---|---|
| ![ᚱ Raidho](https://img.shields.io/badge/-RAIDHO-15803d?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNMyAxM1YxTDggNEwzIDdMOCAxMyIgZmlsbD0ibm9uZSIgc3Ryb2tlPSIjN2ViOTk0IiBzdHJva2Utd2lkdGg9IjIuNCIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5kIi8%2BPC9zdmc%2B) | the ride, the road | the name: every road through the gates |
| ![ᛞ Dagaz](https://img.shields.io/badge/-DAGAZ-8b1e1e?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNMSAyVjEyTDkgMlYxMkwxIDIiIGZpbGw9Im5vbmUiIHN0cm9rZT0iI2JmODM4MyIgc3Ryb2tlLXdpZHRoPSIyLjQiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCIvPjwvc3ZnPg%3D%3D) | dawn, a new day | what is new |
| ![ᚾ Nauthiz](https://img.shields.io/badge/-NAUTHIZ-b8860b?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNNSAxVjEzTTIgNUw4IDkiIGZpbGw9Im5vbmUiIHN0cm9rZT0iI2Q3YmM3OCIgc3Ryb2tlLXdpZHRoPSIyLjQiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCIvPjwvc3ZnPg%3D%3D) | need, what must be | what you need to know first |
| ![ᚨ Ansuz](https://img.shields.io/badge/-ANSUZ-15803d?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNMyAxM1YxTTMgMUw4IDVNMyA1TDggOSIgZmlsbD0ibm9uZSIgc3Ryb2tlPSIjN2ViOTk0IiBzdHJva2Utd2lkdGg9IjIuNCIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5kIi8%2BPC9zdmc%2B) | Odin's word, a message | the destination window and the map |
| ![ᛊ Sowilo](https://img.shields.io/badge/-SOWILO-15803d?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNNyAxTDIgNkw4IDhMMyAxMyIgZmlsbD0ibm9uZSIgc3Ryb2tlPSIjN2ViOTk0IiBzdHJva2Utd2lkdGg9IjIuNCIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5kIi8%2BPC9zdmc%2B) | the sun, victory | the photographs and the live view |
| ![ᛉ Algiz](https://img.shields.io/badge/-ALGIZ-15803d?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNNSAxM1YxTTUgNkwxIDJNNSA2TDkgMiIgZmlsbD0ibm9uZSIgc3Ryb2tlPSIjN2ViOTk0IiBzdHJva2Utd2lkdGg9IjIuNCIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5kIi8%2BPC9zdmc%2B) | the elk, protection | locks and self-healing gates |
| ![ᛖ Ehwaz](https://img.shields.io/badge/-EHWAZ-15803d?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNMSAxM1YxTDUgNUw5IDFWMTMiIGZpbGw9Im5vbmUiIHN0cm9rZT0iIzdlYjk5NCIgc3Ryb2tlLXdpZHRoPSIyLjQiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCIvPjwvc3ZnPg%3D%3D) | the horse, movement | tames and mounts |
| ![ᛇ Eihwaz](https://img.shields.io/badge/-EIHWAZ-15803d?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNNSAxVjEzTTUgMUw4IDRNNSAxM0wyIDEwIiBmaWxsPSJub25lIiBzdHJva2U9IiM3ZWI5OTQiIHN0cm9rZS13aWR0aD0iMi40IiBzdHJva2UtbGluZWNhcD0icm91bmQiIHN0cm9rZS1saW5lam9pbj0icm91bmQiLz48L3N2Zz4%3D) | the yew, endurance | the corpse run |
| ![ᛈ Perthro](https://img.shields.io/badge/-PERTHRO-15803d?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNMyAxVjEzTTMgMUw2IDRMOSAxTTMgMTNMNiAxMEw5IDEzIiBmaWxsPSJub25lIiBzdHJva2U9IiM3ZWI5OTQiIHN0cm9rZS13aWR0aD0iMi40IiBzdHJva2UtbGluZWNhcD0icm91bmQiIHN0cm9rZS1saW5lam9pbj0icm91bmQiLz48L3N2Zz4%3D) | the lot cup, chance | the concealed gates |
| ![ᚲ Kenaz](https://img.shields.io/badge/-KENAZ-15803d?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNOCAyTDMgN0w4IDEyIiBmaWxsPSJub25lIiBzdHJva2U9IiM3ZWI5OTQiIHN0cm9rZS13aWR0aD0iMi40IiBzdHJva2UtbGluZWNhcD0icm91bmQiIHN0cm9rZS1saW5lam9pbj0icm91bmQiLz48L3N2Zz4%3D) | the torch, light in the dark | the runic gateway |
| ![ᛏ Tiwaz](https://img.shields.io/badge/-TIWAZ-15803d?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNNSAxM1YxTTEgNUw1IDFMOSA1IiBmaWxsPSJub25lIiBzdHJva2U9IiM3ZWI5OTQiIHN0cm9rZS13aWR0aD0iMi40IiBzdHJva2UtbGluZWNhcD0icm91bmQiIHN0cm9rZS1saW5lam9pbj0icm91bmQiLz48L3N2Zz4%3D) | Tyr, law and justice | the server's law: limits, filters, health |
| ![ᛃ Jera](https://img.shields.io/badge/-JERA-3d3d3d?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNNCAyTDEgNUw0IDhNNiA2TDkgOUw2IDEyIiBmaWxsPSJub25lIiBzdHJva2U9IiM5NDk0OTQiIHN0cm9rZS13aWR0aD0iMi40IiBzdHJva2UtbGluZWNhcD0icm91bmQiIHN0cm9rZS1saW5lam9pbj0icm91bmQiLz48L3N2Zz4%3D) | the harvest, the year's reckoning | the ledger of defaults |
| ![ᚠ Fehu](https://img.shields.io/badge/-FEHU-1b1b1b?style=for-the-badge&labelColor=8b1e1e&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNMyAxM1YxTTMgNUw4IDFNMyA5TDggNSIgZmlsbD0ibm9uZSIgc3Ryb2tlPSIjZThjMzVhIiBzdHJva2Utd2lkdGg9IjIuNCIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5kIi8%2BPC9zdmc%2B) | cattle, wealth earned | the toolbox |
| ![ᚷ Gebo](https://img.shields.io/badge/-GEBO-1f4e79?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNMSAxTDkgMTNNOSAxTDEgMTMiIGZpbGw9Im5vbmUiIHN0cm9rZT0iIzgzOWRiNSIgc3Ryb2tlLXdpZHRoPSIyLjQiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCIvPjwvc3ZnPg%3D%3D) | the gift, give and take | multiplayer |
| ![ᛗ Mannaz](https://img.shields.io/badge/-MANNAZ-2e5e3a?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNMSAxM1YxTDkgN005IDEzVjFMMSA3IiBmaWxsPSJub25lIiBzdHJva2U9IiM4Y2E2OTIiIHN0cm9rZS13aWR0aD0iMi40IiBzdHJva2UtbGluZWNhcD0icm91bmQiIHN0cm9rZS1saW5lam9pbj0icm91bmQiLz48L3N2Zz4%3D) | humankind, the folk | other mods |
| ![ᛚ Laguz](https://img.shields.io/badge/-LAGUZ-b8860b?style=for-the-badge&labelColor=1b1b1b&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNMyAxM1YxTDggNSIgZmlsbD0ibm9uZSIgc3Ryb2tlPSIjZDdiYzc4IiBzdHJva2Utd2lkdGg9IjIuNCIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5kIi8%2BPC9zdmc%2B) | water, the flow | the settings |
| ![ᛟ Othala](https://img.shields.io/badge/-OTHALA-1b1b1b?style=for-the-badge&labelColor=c9a227&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xIC0xIDEyIDE2Ij48cGF0aCBkPSJNMiAxM0w4IDZMNSAyTDIgNkw4IDEzIiBmaWxsPSJub25lIiBzdHJva2U9IiMxYjFiMWIiIHN0cm9rZS13aWR0aD0iMi40IiBzdHJva2UtbGluZWNhcD0icm91bmQiIHN0cm9rZS1saW5lam9pbj0icm91bmQiLz48L3N2Zz4%3D) | the homestead, heritage | the credits |

</details>
