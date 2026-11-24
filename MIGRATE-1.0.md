# TortalPortal — Valheim 1.0.7 migration instructions

> **Verified 2026-09-11 on Valheim 1.0.12 (build 25253764/25253791, network version 40) - TortalPortal 1.4.7, no change needed.**
> The 1.4.7 build staged on 2026-09-09 was re-checked, unmodified, against the 1.0.7 -> 1.0.12 delta
> (`MigrationStation/diffs/server_assembly_valheim_1.0.7_to_1.0.12.md` + `.ildiff.txt`, and the decompile diffs).
> The delta touches 37 types / 55 method bodies; the only member removed is the field `PlayerProfile.s_bypassCheatChecks`
> (now a property), no signature changed, no interface changed, no constructor was reshaped, and no new
> non-defaulted parameter appeared. `assembly_utils`, `assembly_guiutils`, `gui_framework`, `assembly_postprocessing`
> and `Splatform` are IL-identical. Not rebuilt, not re-versioned, csproj untouched.
>
> **Touchpoints (every Harmony target, reflection lookup, direct call and mirrored vanilla rule, checked one by one):**
> - *Harmony targets whose 1.0.12 body changed* - **unaffected**:
>   `Player.TryPlacePiece` (`PlacementPatch.cs:87`, prefix): the only IL change is `ldsfld s_bypassCheatChecks` ->
>   `call get_s_bypassCheatChecks` (ildiff line 484-485); the prefix reads none of that.
>   `ZNet.RPC_PeerInfo` (`ServerSync.cs:753` prefix/postfix, `ServerSync.cs:1305` prefix): three `ldc.i4.s 39` -> `40`
>   (ildiff 798-813), i.e. the network-version literal; the socket-buffering and mod-version-check patches never read it.
>   `Terminal.InitTerminal` (`ScatterCommand.cs:101`, `TortalPinLibrary.cs:727`, postfixes): vanilla added the
>   `yesiuseddevcommandsbutiwantmyachievementsanyway` command and renumbered compiler locals; a postfix that
>   registers its own commands is indifferent to both.
> - *Harmony targets in changed types but unchanged methods* - **unaffected**: `ZNet.Awake/OnNewConnection/Shutdown/
>   Start/Disconnect`, `Chat.HasFocus` (`InputFocusPatch.cs:17`), `Player.TeleportTo/UpdatePlacement/PlacePiece`,
>   `Inventory.IsTeleportable` (`FilterPatch.cs:90`), `FejdStartup.ShowConnectError` (`ServerSync.cs:1379`),
>   `ZDOMan.CreateSyncList` (`SectorSubscription.cs:106`) - none appears in the ildiff.
> - *Harmony targets in untouched types* - **unaffected**: all `Minimap.*` (`MapInputPatch.cs`), all `TeleportWorld.*`
>   (`PortalBehaviorPatch.cs`, `SecurityPatch.cs`), `SpawnSystem.*` (`AnchorPresence.cs`), `ZNetScene.CreateObjects`
>   (`SnapshotAnchor.cs:159`), `SnapToGround.Snap/SnappAll` (`SnapGuard.cs`), `Trader.Start` (`TraderRepair.cs:45`),
>   `Humanoid.IsTeleportable` (`FilterPatch.cs:17`), `GameCamera.UpdateMouseCapture`, `MessageHud.Update`,
>   `ZRpc.HandlePackage`, `ConfigEntryBase.Get/SetSerializedValue` (BepInEx). refcheck resolves all 48 targets.
> - *Reflection surface (`ServerSync.cs`)* - **unaffected**: `ZNet.m_adminList`, `ZNet.ListContainsId`, `ZNet.GetPeer(ZRpc)`,
>   `ZNet.m_connectionStatus`, `ZRpc.m_socket`, `ZRpc.m_functions`, `ZNetPeer.m_socket`, `ZRoutedRpc.m_peers`,
>   `ZPlayFabSocket.m_remotePlayerId` all still exist (the one removed field in 1.0.12 is `PlayerProfile.s_bypassCheatChecks`,
>   which no file in this repo mentions). `SnapGuard.cs:63-64` `SnapToGround.m_allSnappers/m_inList`: unchanged.
> - *`ZNet.ListContainsId`* - called directly at `ScatterCommand.cs:68` and `TortalPinLibrary.cs:694` (server-side
>   admin gate for the routed `tortal_scatterclear` / `tortal_pinreload` RPCs) and reflectively at `ServerSync.cs:264/349/886`
>   (lockexempt) - **behaviour-changed, benign, no adaptation**: 1.0.12 turned `flag = list.Contains(filtered)` into
>   `flag |= ...` (decompile 1.0.12 line 80159 ff. vs 1.0.7), so an adminlist entry in the *old* form still matches
>   after platform-ID filtering instead of being overwritten by the filtered lookup. Net effect: admins listed under
>   either ID form now pass the mod's gates, which is the fix vanilla intended. `Configuration.LocalPlayerIsServerAdmin`
>   (`Configuration.cs:500-548`) does its own match against `ZNet.GetAdminList()` (unchanged) and mirrors the
>   Steam-bare-number rule, which 1.0.12 did not alter.
> - *Console commands* - **unaffected**: `tortal_scatter` / `tortal_scatterclear` (`ScatterCommand.cs:110-118`) are
>   `isCheat: true, isNetwork: false, onlyServer: false`; `tortal_pinreload` (`TortalPinLibrary.cs:736-758`) is
>   `isCheat: false, isNetwork: false, onlyServer: false`. None passes `hideBehindDevCommands`, so the 1.0.12 gating
>   rewrite of `ConsoleCommand.IsValid/ShowCommand` and `Chat.isAllowedCommand` (which only relaxed
>   HideBehindDevCommands) leaves them exactly as before: scatter needs devcommands, pinreload does not.
> - *Version / network version* - **unaffected**: the repo never reads `Version.*`, `GetVersionString` or the network
>   version; the `CurrentVersion` hits in `Configuration.cs:51` / `ServerSync.cs` are the mod's own ConfigSync/VersionCheck
>   strings ("1.4.7", floor 1.4.5).
> - *Item data on ZDOs* - **unaffected**: the mod reads/writes only `ZDOVars.s_tag`, `s_creator`, `s_creatorName`,
>   `s_owner`/`s_ownerName`, `s_playerID`/`s_playerName` and its own `"PortalPIN"` / `"PortalSnapshot"` keys
>   (`PortalRegistry.cs`, `PlayerNames.cs`, `SecurityPatch.cs`, `PlacementPatch.cs`, `PortalUIManager.cs`, `ScatterCommand.cs`);
>   nothing touches `ItemDrop.SaveToZDO`, item stands, containers or the new `"{index}_{name}"` conversion hashes.
> - *Cooking / smelting / fermenting, cheated-item flags, `Character.UpdateGroundContact` / `m_onLand`,
>   `TerrainComp.PaintCleared`, `Player.HaveRequirementItems`, `Inventory.Changed`, `Destructible`, `GrapplingPoint`,
>   `RuneStone`, `AchievementUnlockPopup`, `PresentManager`, matchmaking, `ZDOMan.Load/ConvertContainers`* - **no touchpoint**
>   (grep of the source root for each family returned nothing; `Heightmap.FindHeightmap` at `LivePreview.cs:304`,
>   `AnchorEnvironment.cs:416`, `SnapGuard.cs:77/93` calls an unchanged method; `EnvMan`, which `AnchorEnvironment.ResolveEnv`
>   mirrors, is untouched in 1.0.12).
> - *Interfaces / ctors / params*: the mod's game-facing types derive from `BaseUnityPlugin`, `MonoBehaviour`,
>   `AcceptableValueList<string>`, `Exception`, and `ServerSync.BufferingSocket : ZPlayFabSocket, ISocket`; neither `ISocket`
>   nor `ZPlayFabSocket` appears in the asmdiff. The asmdiff's signature-change sections are empty.
>
> **Evidence.** refcheck (Mono.Cecil, exact-signature) against the real 1.0.12 Managed folders: CLIENT `RESULT: OK`
> (9485 references, 48 Harmony targets), SERVER `RESULT: OK` (9485 / 48) -
> `scratchpad/refcheck-1012/final-TortalPortal-TortalPortal-{client,server}.txt`. Boot on the real 1.0.12 Linux dedicated
> server (profile `~/valheim-testbed/profiles/final-TortalPortal`, `Valheim version: l-1.0.12 (network version 40)`):
> **BOOTED after 25 s** - `scratchpad/boot-1012/final-TortalPortal.txt`; `[Info : BepInEx] Loading [TortalPortal 1.4.7]`,
> all fifteen `Patched <X> successfully.` lines, `TortalPortal v1.4.7 loaded successfully.`, both console-command
> registration lines, `offering 0 map icon(s)`, and `Registered 'wubarrk.TortalPortal ConfigSync' RPC` in
> `server-console.log:184`; no Harmony / Missing* / TypeLoad / exception lines. The plugin-tagged lines are
> **byte-identical (timestamps stripped)** to the 1.0.7 run preserved at
> `~/valheim-testbed/profiles/final-TortalPortal/logs-1.0.7-run/{LogOutput.log,server-console.log}`.
> Scratch: `scratchpad/agents/TortalPortal/`. (`scratchpad` =
> `/tmp/claude-1000/-home-rohan-WubarrkCODING/7e501e8a-0a25-46f2-aaca-99a485eceb49/scratchpad`.)
>
> **Still open (needs a real client, unchanged from 2026-09-09):** the list at the end of the block below, plus one
> 1.0.12-specific check - an admin whose adminlist.txt entry is in the old form should now pass `tortal_scatterclear` /
> `tortal_pinreload` on the server (the `ListContainsId` fix), which a headless boot cannot exercise.

> **Applied 2026-09-09 - TortalPortal 1.4.7.** Sections A, B and C below are done, in the shape
> recommended: `MapInputPatch` targets `Minimap.RemovePinUnderPointer` by string (touch long-press over
> the window is now covered too, deliberately); `PlayerNames.BackfillChunk` walks a snapshot of
> `ZDOMan.m_objectsByID`; `ZoneCompat.cs` wraps `FindSectorObjects` with
> `new SimulationDistance(ring, distantRing, classic: true)` and exposes the live near/far distances, and
> every sector variable in `AnchorPresence` / `SnapshotAnchor` / `SectorSubscription` / `PlacementPatch` is
> a `Vector2s`. The stale `FindSectorObjects` comment in `SnapshotAnchor.cs` and the pre-1.0 invariant
> comment in `PlayerNames.cs` are rewritten; `ZONE-ANCHOR-IMPLEMENTATION.md` carries a 1.0.7 note at the top.
>
> **The report undercounted the breaks: four more compile errors surfaced on the first build, all fixed.**
> - `ZInput.mousePosition` is gone from 1.0.7's `assembly_utils` (only `pointerPosition` exists). Section A
>   says "you do not reference either directly" - wrong: `MapInputPatch.cs` used it twice (click-n-go) and
>   `PortalUIManager.IsPointerOverWindow()` once. All three now read `ZInput.pointerPosition`, and click-n-go
>   takes its hit radius from vanilla's own `Minimap.PinInteractRadius` (private property, publicized) - the
>   old `m_removeRadius * (m_largeZoom * 2f)` plus a touch allowance - rather than re-deriving it.
> - `PortalBehaviorPatch.cs:228` called `local.IsTeleportable()` with no argument; section E only looked at
>   the two prefixes. It now passes `__instance.m_allowAllItems`, exactly as 1.0.7's `TeleportWorld` does.
> - `ZDOMan.GetPortals()` now returns `Dictionary<ZoneSystem.SectorIndex, List<ZDO>>` (the per-sector
>   `m_portalObjects`). `PlacementPatch.GetAllPortals` uses the new `ZDOMan.GetPortalList()`, which flattens it.
> - `EnvMan.GetAvailableEnvironments` takes a `BiomeSector` (1.0 picks weather per contiguous biome sector,
>   with alt-biome additions/blocks). `AnchorEnvironment.ResolveEnv` is re-transcribed from 1.0.7's
>   `EnvMan.GetBiome()` + `UpdateEnvironment`: `WorldGenerator.instance.GetBiomeSector(pos)`, with vanilla's
>   ocean-level substitution of the biome's first sector in the Ashlands / Deep North; `_envBiome` is a
>   `BiomeSector` compared by reference, as vanilla's `m_currentBiome == biome` is.
>
> **Deviations from the report, and why.**
> - (B) One bucket, not 512-item chunks: the walk below already paces itself at `BackfillZdosPerFrame`, so
>   chunking only doubled the transient copy of a possibly million-entry table.
> - (C) `SectorSubscription` uses the **peer's** negotiated `ZNetPeer.m_simulationDistance` for both the
>   overlap guard and the distant ring - the very value vanilla's `CreateSyncList` sweeps for that peer one
>   line after the postfix - and falls back to `ZoneCompat` while it is still `default` before the
>   simulation-distance handshake. `SnapshotAnchor` (client side) reads `ZNet.GetSyncedSimulationDistance()`
>   through `ZoneCompat`, as the report recommends.
> - (E) `IsTeleportable(bool allowAllItems)`: the flag is **honoured** - both prefixes return early and let
>   vanilla decide when it is set. That is the no-behaviour-change option: pre-1.0 vanilla OR'd
>   `m_allowAllItems` around the call, so an allow-all portal never consulted the filter. Recorded in
>   `FilterPatch.cs`. The patch parameter is named `allowAllItems` to match the target exactly.
> - `AnchorPresence.SpawnRing` stays 1 (the comment now explains it is one less than 1.0's default near
>   distance, on purpose). The overlap back-off in `SnapshotAnchor` / `SectorSubscription` does grow from the
>   old fixed `m_activeArea` to the live near distance (default 2, player-raisable) - see the CHANGELOG.
> - Version bumped 1.4.6 -> 1.4.7 in lockstep (csproj, `Plugin.cs`, `Configuration.cs` `CurrentVersion`,
>   manifest, README badge); `MinimumRequiredVersion` left at 1.4.5 (a 1.4.6 build cannot load on 1.0.7 at
>   all, so the floor is academic). The manifest dependency was moved to
>   `denikson-BepInExPack_Valheim-5.4.2350`, the 1.0 pack - a deliberate change a human may revert.
>
> **Evidence.**
> - Build: `dotnet build TortalPortal.csproj -c Release --no-incremental` -> **0 errors, 0 warnings**;
>   `PostBuild` staged `HexiumDist/plugins/TortalPortal.dll` (byte-identical to the build output).
> - refcheck (Mono.Cecil, exact-signature): CLIENT `RESULT: OK` (9485 references, 48 Harmony targets);
>   SERVER `RESULT: OK` (9485 references, 48 Harmony targets). Refcheck sees compile-time bindings only;
>   `ServerSync.cs`'s string-named reflection targets were checked by hand against the 1.0.7 decompile and
>   all exist: `ZNet.m_adminList` / `ListContainsId` / `GetPeer` / `m_connectionStatus` / `OnNewConnection` /
>   `RPC_PeerInfo` / `Shutdown` / `Awake`, `ZRpc.HandlePackage` / `m_socket` / `m_functions`,
>   `ZNetPeer.m_socket`, `ZRoutedRpc.m_peers`, `ZPlayFabSocket.m_remotePlayerId`, `FejdStartup.ShowConnectError`
>   (the boot log's `Registered 'wubarrk.TortalPortal ConfigSync' RPC` shows the ZNet.Awake hook firing).
> - Boot-check on the real 1.0.7 Linux dedicated server, profile `~/valheim-testbed/profiles/tortal`, port
>   2620: **BOOTED after 25 s**. `[Info : BepInEx] Loading [TortalPortal 1.4.7]`, then
>   `TortalPortal: Patched <X> successfully.` for all sixteen patch classes (PlacementPatch, SnapshotAnchor,
>   SectorSubscription, SnapGuard, TraderRepair, FilterPatch, PortalBehaviorPatch, SecurityPatch, TamesPatch,
>   InputFocusPatch, MessageHudPatch, MapInputPatch, ScatterCommand, TortalPinLibrary, AnchorPresence),
>   `TortalPortal v1.4.7 loaded successfully.`, `console commands tortal_scatter / tortal_scatterclear
>   registered.`, `console command tortal_pinreload registered.`, `offering 8 map icon(s) to clients from
>   TortalPortalPins.`, and `Registered 'wubarrk.TortalPortal ConfigSync' RPC`. No Harmony / MissingMethod /
>   MissingField / TypeLoad lines; only vanilla's headless "video decode shader" noise. Logs:
>   `~/valheim-testbed/profiles/tortal/BepInEx/LogOutput.log` and `.../profiles/tortal/server-console.log`.
> - Off-game tests: none exist in this repo.
>
> **Left open (needs a real client, not a headless server).** Right-click and touch long-press over the
> portal window must leave vanilla pins alone; click-n-go hit radius with `PinInteractRadius`; a fresh
> `BackfillChunk` pass on a large world (the snapshot is one reference per ZDO, transient); the anchor's
> overlap back-off at simulation distance 2 and above; anchored-camera weather on an Ashlands / Deep North
> coast (the ocean-level substitution path).

**Status: BROKEN on 1.0.7. Three independent breakages, one of which will not even compile.**
Audited 2026-09-09 against `libs-Tools/1.0/DECOMPILED/` (client 1.0.7, build 25185596, network 39).

| # | Break | Severity |
|---|---|---|
| A | **`Minimap.OnMapRightClick` was deleted** — `nameof()` will not compile | hard, compile-time |
| B | **`ZDOMan.m_objectsByOutsideSector` was removed** | hard, compile-time |
| C | The whole **zone / sector API** (`FindSectorObjects`, `m_activeArea`, `Vector2i` sectors) | hard, compile-time |

Everything else in this mod is fine — all 40-odd other Harmony targets resolve, and `ServerSync.cs`'s
large reflection surface is intact on 1.0.7.

---

## A. `Minimap.OnMapRightClick` no longer exists

This is the one the other Wubarrk mods do not have, and the one the migration-facts doc does not mention.

**pre-1.0:**
```csharp
public void OnMapRightClick(UIInputHandler handler)
{
    ZLog.Log("Right click");
    HidePinTextInput();
    Vector3 pos = ScreenToWorldPoint(ZInput.mousePosition);
    RemovePin(pos, m_removeRadius * (m_largeZoom * 2f));
    m_namePin = null;
}
```

**1.0.7:** the named method is gone. `Minimap.Start` now wires an **anonymous delegate** into
`m_onRightClick`, and the body moved into a new private method:

```csharp
component.m_onRightClick = (Action<UIInputHandler>)Delegate.Combine(
    component.m_onRightClick, (Action<UIInputHandler>)delegate { RemovePinUnderPointer(); });

private void RemovePinUnderPointer()
{
    ZLog.Log(ZInput.IsTouchActive() ? "Long touch" : "Right click");
    HidePinTextInput();
    Vector3 pos = ScreenToWorldPoint(ZInput.pointerPosition);
    RemovePin(pos, PinInteractRadius);
    if (ZInput.IsTouchActive()) UnityEngine.Object.Instantiate(m_touchVibrationPrefab);
    m_namePin = null;
}
```

`MapInputPatch.cs:60` uses `nameof(Minimap.OnMapRightClick)`, so this is a **C# compile error**, not a
runtime surprise. It cannot be missed, but it also cannot be fixed by retyping an argument.

### Fix: patch `Minimap.RemovePinUnderPointer` instead

```csharp
// Vanilla 1.0 deleted Minimap.OnMapRightClick and moved the body into RemovePinUnderPointer(),
// which Start() now invokes through an anonymous delegate on m_onRightClick. The compiler-generated
// delegate has an unstable name, so patch the real method instead - it is what right-click does.
[HarmonyPatch(typeof(Minimap), "RemovePinUnderPointer")]
[HarmonyPrefix]
public static bool Minimap_RemovePinUnderPointer_Prefix()
{
    try
    {
        return !PortalUIManager.IsPointerOverWindow();
    }
    catch (Exception ex)
    {
        Plugin.Log.LogError($"TortalPortal: {nameof(Minimap_RemovePinUnderPointer_Prefix)} failed (non-fatal). Reason: {ex}");
        return true;
    }
}
```

Note the string `"RemovePinUnderPointer"` rather than `nameof` — the method is private, and while the
publicizer would let `nameof` compile, the string keeps this patch honest about targeting a private
member that could move again.

**Scope change you are accepting:** `RemovePinUnderPointer` has **two** callers on 1.0.7 — the
right-click delegate, and `TouchLongPressPerformed()` (long-press on touch devices). Guarding it
therefore also suppresses long-press pin removal over the portal window. That is almost certainly what
you want — the reason for the guard is "the pointer is over our UI" — but it is strictly broader than
the pre-1.0 patch, so it is a deliberate decision, not an accident.

Also note `m_removeRadius * (m_largeZoom * 2f)` became a `PinInteractRadius` property, and
`ZInput.mousePosition` became `ZInput.pointerPosition`. You do not reference either directly, but the
comment at `MapInputPatch.cs:66-67` describing vanilla's behaviour should be updated.

---

## B. `ZDOMan.m_objectsByOutsideSector` was removed

`PlayerNames.cs:266`:
```csharp
foreach (List<ZDO> list in ZDOMan.instance.m_objectsByOutsideSector.Values)
    if (list != null && list.Count > 0) state.Buckets.Add(list);
```

On 1.0.7 that field does not exist (nor do `m_halfWidth` or `SectorToIndex(Vector2i)`). The sibling
field on the line above, **`ZDOMan.m_objectsBySector` (`List<ZDO>[]`), still exists and is unchanged** —
so the backfill keeps working for everything inside the sector grid, and only the out-of-grid overflow
bucket is lost.

### Fix: enumerate `m_objectsByID` instead

`ZDOMan.m_objectsByID` is `private readonly Dictionary<ZDOID, ZDO>` on 1.0.7, unchanged from pre-1.0,
and it is the **one ZDO container that is safe to enumerate across this change** — it holds every ZDO,
in-grid and out. Replace both bucket sources with a single walk:

```csharp
// 1.0 removed m_objectsByOutsideSector (and m_halfWidth / SectorToIndex with it). m_objectsByID is
// the only container that still holds every ZDO regardless of sector, so the backfill walks that.
// It is a Dictionary, not a List<ZDO>[], so buckets are chunks of a snapshot rather than live
// sector lists - see the mid-walk note below.
state.Buckets = new List<List<ZDO>>();
var all = new List<ZDO>(ZDOMan.instance.m_objectsByID.Values);
for (int i = 0; i < all.Count; i += 512)
    state.Buckets.Add(all.GetRange(i, Math.Min(512, all.Count - i)));
```

**This changes an invariant the current code documents and relies on.** The comment at
`PlayerNames.cs:252-257` explains that sector lists are taken *by reference*, never copied, so a ZDO
appearing or vanishing mid-walk cannot trip the iteration. Copying out of a `Dictionary` gives you a
**snapshot**: it cannot throw either, and it is still safe, but ZDOs created after the snapshot are
missed for the whole scan rather than only for the sectors already passed. The existing comment already
argues that missing newly-created ZDOs costs nothing (their owner is online, so the harvest has their
name). That argument still holds — but rewrite the comment, because the mechanism it describes is gone.

The `while (budget > 0 && state.Bucket < state.Buckets.Count)` loop below needs no change.

---

## C. The zone / sector API

> `libs-Tools/VALHEIM-1.0-MIGRATION-FACTS.md` calls this a plain `Vector2i -> Vector2s` swap
> absorbable by pass-through bridges. **That is not what shipped** — the release also collapsed the two
> `int` distance parameters into a struct and deleted the two public distance fields.

| Member | pre-1.0 | 1.0.7 |
|---|---|---|
| `ZDOMan.FindSectorObjects` | `(Vector2i, int area, int distantArea, List<ZDO>, List<ZDO> = null)` | `(Vector2s, SimulationDistance, List<ZDO>, List<ZDO> = null)` |
| `ZoneSystem.m_activeArea` / `m_activeDistantArea` | `public int = 1` | **removed** |
| `ZoneSystem.GetZone(Vector3)` | `-> Vector2i` | `-> Vector2s` |
| `ZoneSystem.PokeLocalZone` / `IsZoneGenerated` / `SpawnZone` | `(Vector2i, ...)` | `(Vector2s, ...)`, all **private** |

### C.1 Add `ZoneCompat.cs`

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace TortalPortal
{
    /// <summary>
    /// The 1.0 sector API in the shape this mod used pre-1.0. Sectors became Vector2s, the
    /// (area, distantArea) int pair became a SimulationDistance, and ZoneSystem's two public
    /// distance fields were deleted.
    /// `classic: true` reproduces the pre-1.0 square-ring sweep; without it FindSectorObjects
    /// narrows each ring through ZonesWithinRadius and the swept set becomes a disc, which would
    /// silently change what the snapshot anchor feeds ZNetScene.
    /// </summary>
    internal static class ZoneCompat
    {
        public static void FindSector(Vector2s sector, int ring, int distantRing, List<ZDO> results)
            => ZDOMan.instance.FindSectorObjects(
                   sector, new SimulationDistance(ring, distantRing, classic: true), results);

        public static int ActiveArea =>
            ZNet.instance != null ? ZNet.instance.GetSyncedSimulationDistance().NearSimulationDistance
                                  : SimulationDistance.OriginalNear;   // == 2

        public static int ActiveDistantArea =>
            ZNet.instance != null ? ZNet.instance.GetSyncedSimulationDistance().FarSimulationDistance
                                  : SimulationDistance.OriginalFar;    // == 2
    }
}
```

### C.2 Edits

| File | Line | Change |
|---|---|---|
| `AnchorPresence.cs` | 121, 122 | `Vector2i` -> `Vector2s` |
| `SnapshotAnchor.cs` | 75 | `ZoneSystem.instance?.m_activeDistantArea ?? 0` -> `ZoneCompat.ActiveDistantArea` (log string) |
| `SnapshotAnchor.cs` | 143 | `Vector2i center` -> `Vector2s center` |
| `SnapshotAnchor.cs` | 148 | `PokeLocalZone(new Vector2i(x, y))` -> `new Vector2s(x, y)` |
| `SnapshotAnchor.cs` | 170, 171 | `Vector2i anchorZone/refZone` -> `Vector2s` |
| `SnapshotAnchor.cs` | 172 | `m_activeArea + Ring` -> `ZoneCompat.ActiveArea + Ring` |
| `SnapshotAnchor.cs` | 186 | `FindSectorObjects(anchorZone, Ring, m_activeDistantArea, currentNearObjects)` -> `ZoneCompat.FindSector(anchorZone, Ring, ZoneCompat.ActiveDistantArea, currentNearObjects)` |
| `SectorSubscription.cs` | 127, 128 | `Vector2i` -> `Vector2s` |
| `SectorSubscription.cs` | 129 | `m_activeArea + SnapshotAnchor.Ring` -> `ZoneCompat.ActiveArea + ...` |
| `SectorSubscription.cs` | 154 | `Vector2i z = new Vector2i(x, y)` -> `Vector2s z = new Vector2s(x, y)` |
| `SectorSubscription.cs` | 155-156 | `IsZoneGenerated(z)` / `SpawnZone(z, ...)` — calls unchanged, `z` is now `Vector2s` |
| `SectorSubscription.cs` | 171 | `FindSectorObjects(subZone, Ring, m_activeDistantArea, TempSectorZdos)` -> `ZoneCompat.FindSector(...)` |
| `PlacementPatch.cs` | 613 | `FindSectorObjects(GetZone(pos), Ring, 0, _anchorCountTemp)` -> `ZoneCompat.FindSector(...)` |

`IsZoneGenerated`, `SpawnZone` and `PokeLocalZone` are private on 1.0.7; the csproj already sets
`<Publicize>true</Publicize>` on `assembly_valheim`, so direct calls still compile. Only the argument
type changes.

**Behaviour note:** pre-1.0 `m_activeArea` was **1**; `SimulationDistance.OriginalNear` is **2** and the
player can raise it. `SnapshotAnchor.cs:172` and `SectorSubscription.cs:129` add it to `Ring` to compute
an overlap, which now grows with the player's simulation-distance setting. Correct new semantics, but it
changes how far the anchor backs off — re-check during soak. The `SnapshotAnchor.cs:180` comment about
`FindSectorObjects` looping `area+1 .. area+distantArea` describes the pre-1.0 body and is **no longer
accurate**; the 1.0 body loops `1..Near` then `Near..Total` with a `ZonesWithinRadius` filter. Rewrite it.

---

## D. Rebuild and verify

`HintPath`s already resolve to `..\libs-Tools\*.dll`, which **is** the 1.0.7 client set as of
2026-09-09. No csproj edit needed.

```bash
dotnet build TortalPortal.csproj -c Release

# should print nothing
grep -rn "m_objectsByOutsideSector\|m_activeArea\|m_activeDistantArea\|OnMapRightClick" --include=*.cs . | grep -v ZoneCompat
```

Runtime: on a 1.0.7 world, open the portal window over the map and right-click inside it — no vanilla
pin may be deleted. Then confirm the portal list still populates player names after a fresh
`BackfillChunk` pass.

---

## E. Verified safe — do not change these

- **`ZDOMan.m_objectsBySector`** (`List<ZDO>[]`) and **`m_objectsByID`** — both unchanged.
- **`Humanoid.IsTeleportable`** and **`Inventory.IsTeleportable`** gained a `bool allowAllItems`
  parameter (`IsTeleportable()` -> `IsTeleportable(bool)`). Both are still single overloads and both
  `FilterPatch.cs` prefixes take only `__instance` + `ref bool __result`, so **the patches still
  resolve and still work**. No edit required. Consider whether `allowAllItems: true` should bypass your
  filter the way admin/`BypassAll` does — a behaviour question, not a break.
- **All Minimap targets except `OnMapRightClick`**: `OnMapDblClick`, `OnMapLeftClick`,
  `OnMapMiddleClick(UIInputHandler)`, `OnMapLeftDown`, `OnMapLeftUp`, `UpdateMap`, `UpdatePins` — present.
- **`TeleportWorld`** `Awake`, `Interact(Humanoid, bool, bool)`, `SetText(string)`, `Teleport(Player)`,
  `UpdatePortal`, `Update` — all unchanged.
- **`SpawnSystem`** `FindBaseSpawnPoint`, `GetPlayersInZone`, `UpdateSpawning` — unchanged.
- **`Player.TeleportTo(Vector3, Quaternion, bool)`**, `Player.PlacePiece`, `TryPlacePiece`,
  `UpdatePlacement` — unchanged.
- **`ServerSync.cs`'s reflection surface** — `ZNet.m_adminList`, `ZNet.ListContainsId`,
  `ZNet.GetPeer(ZRpc)`, `ZNet.m_connectionStatus`, `ZNet.m_onlineBackend`, `ZRoutedRpc.m_peers`,
  `ZRpc.m_socket`, `ZNetPeer.m_socket` — all still present.
- **`SnapToGround.m_allSnappers` / `m_inList`**, `Snap`, `SnappAll`; `ZDOMan.CreateSyncList`;
  `ZNetScene.CreateObjects`; `Chat.HasFocus`; `Trader.Start`; `Terminal.InitTerminal`;
  `MessageHud.Update`; `GameCamera.UpdateMouseCapture`; `FejdStartup.ShowConnectError`;
  `ZRpc.HandlePackage`; all `ZNet` targets — unchanged.
- **`StringExtensionMethods.GetStableHashCode(string)`** unchanged on release; no bridge needed.
