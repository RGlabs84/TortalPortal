# The Zone Anchor

**Keeping any patch of Valheim's world alive, loaded, and (optionally) visible — with no player anywhere near it.**

From TortalPortal v1.2.0, where it lets a camera photograph and live-stream portals nobody has ever
visited. The technique is general: anything you want loaded, simulated, watched, or rendered while
every player is elsewhere sits on top of these five mechanisms. Written to be lifted whole into any
BepInEx/Harmony mod. Redistribute freely — attribution to Wubarrk appreciated, not required.

Verified against the live Valheim build as of **August 2026**, using
`BepInEx.AssemblyPublicizer.MSBuild` on `assembly_valheim` (every "private" member named below is
called directly through publicizing, not reflection). Method names can drift across game patches —
re-verify against a current decompile before trusting a new game version.

---

## Why this is hard at all

Valheim's world only exists near players. Three separate systems enforce that independently, and an
anchor has to answer all of them:

| System | What it does | What happens elsewhere |
|---|---|---|
| `ZoneSystem` | Builds terrain (heightmap, water, locations) for zones near each reference position | No ground. Zones expire on a 4-second TTL |
| `ZNetScene` | Instantiates GameObjects from ZDOs in sectors near the reference position; **destroys everything else every tick** | No objects — and anything you spawn by hand is swept within 1/30 s |
| `ZDOMan` (server) | Sends each peer only the ZDOs around that peer's reported position | A client literally does not *have* the data for distant areas |

And two quieter ones:

| System | The trap |
|---|---|
| Zone generation | Vegetation and location ZDOs **only come into existence when a zone is first generated**. An unvisited zone has no trees to load, no matter how long you wait |
| `TerrainLod` | Distant mountains/coastlines are a low-LOD mesh carpet wrapped around the *player's* camera. Any other viewpoint sees black void past the loaded ring |

A "keep this area loaded" mod must therefore: build terrain (1), protect objects (2), obtain the
data (3), cause the content to exist (4), and — if anything renders the area — bring a horizon (5).

---

## Mechanism 1 — Terrain: poke, don't patch

`ZoneSystem.PokeLocalZone(Vector2i)` is vanilla's own primitive and does exactly the right thing:
if the zone exists it resets its 4-second TTL and returns false; if not it builds it synchronously
and returns true. So the terrain half of an anchor needs **no Harmony patch at all** — a coroutine
that pokes the ring while the anchor is wanted:

```csharp
// Called every ~0.25s while the anchor is up. At most ONE new zone is built per call:
// SpawnZone is synchronous and a fresh zone can cost tens of ms, so the ring is spread
// across ticks instead of landing on a single frame. Pokes to existing zones are a
// dictionary lookup and a TTL reset.
public static void PokeZones(Vector3 anchorPos, int ring)
{
    Vector2i c = ZoneSystem.GetZone(anchorPos);
    for (int y = c.y - ring; y <= c.y + ring; y++)
        for (int x = c.x - ring; x <= c.x + ring; x++)
            if (ZoneSystem.instance.PokeLocalZone(new Vector2i(x, y)))
                return;   // one build per call — timeslice
}
```

**Cleanup is free.** Stop poking and the zones TTL out ~4 seconds later through
`ZoneSystem.UpdateTTL`, exactly as if a player had walked away. There is no teardown path to get
wrong — which is the design principle this whole technique keeps returning to.

On the **hosting player** (or singleplayer), `PokeLocalZone` on an ungenerated zone runs
`SpawnMode.Full` and generates it outright — vegetation ZDOs included. On a **pure client** it runs
`SpawnMode.Client`: terrain only, which is why mechanisms 3–4 exist.

## Mechanism 2 — Objects: one prefix, both jobs

`ZNetScene.CreateDestroyObjects` builds a list of the sectors' ZDOs each tick, instantiates from it
(`CreateObjects`) and then destroys every instance *not* on it (`RemoveObjects`). The killer detail:
**both calls receive the same `List<ZDO>` instance.** So a single prefix that appends the anchor's
sectors both spawns the anchored objects *and* shields them from the sweeper:

```csharp
[HarmonyPatch(typeof(ZNetScene), "CreateObjects")]
[HarmonyPrefix]
static void Prefix(List<ZDO> currentNearObjects)   // parameter name must match vanilla
{
    if (!AnchorActive) return;
    Vector2i anchorZone = ZoneSystem.GetZone(AnchorPos);

    // If the anchor sits inside the player's own active area, vanilla is already carrying
    // these sectors — appending would double-list the same ZDOs.
    Vector2i refZone = ZoneSystem.GetZone(ZNet.instance.GetReferencePosition());
    int overlap = ZoneSystem.instance.m_activeArea + Ring;
    if (Mathf.Abs(anchorZone.x - refZone.x) <= overlap &&
        Mathf.Abs(anchorZone.y - refZone.y) <= overlap) return;

    // FindSectorObjects APPENDS (it never clears), which is exactly what we want here.
    ZDOMan.instance.FindSectorObjects(anchorZone, Ring, ZoneSystem.instance.m_activeDistantArea, currentNearObjects);
}
```

**Cleanup is free here too:** drop the anchor flag and the very next tick's `RemoveObjects` sweeps
the anchored instances through the ordinary vanilla path. Nothing to undo, no state to restore.
(`UpdateTTL` also refuses to expire a zone that still has instances in it — `HaveInstanceInSector` —
so the two mechanisms sequence themselves correctly: objects go first, zones follow.)

## Mechanism 3 — Data: the sector subscription (dedicated servers)

A client of a dedicated server is only ever *sent* the ZDOs around its own reported position — the
far sectors it wants to anchor are simply absent from its `ZDOMan`. The server-side half is a
renewal-driven subscription:

**The RPC.** A routed RPC (`ZRoutedRpc.instance.Register<Vector3>(...)` at `ZNet.Start` time;
clients invoke the targetless `InvokeRoutedRPC(name, pos)` overload, which routes to the server).
One `Vector3` argument: the anchor position, or `Vector3.zero` to release. The server stores
`sender → (pos, expires)` where `expires` is ~15 s out; the client re-sends every ~5 s while it
wants the world. **A client that crashes, disconnects, or just stops asking costs the server
nothing within seconds, and there is no cleanup handshake to get wrong.**

**The send-list patch.** The server builds each peer's outgoing ZDO list in
`ZDOMan.CreateSyncList(ZDOPeer peer, List<ZDO> toSync)`. A postfix appends the subscribed sectors:

```csharp
[HarmonyPatch(typeof(ZDOMan), "CreateSyncList")]
[HarmonyPostfix]
static void Postfix(ZDOMan __instance, ZDOMan.ZDOPeer peer, List<ZDO> toSync)
{
    if (!Subs.TryGetValue(peer.m_peer.m_uid, out var sub)) return;
    if (Time.realtimeSinceStartup > sub.Expires) { Subs.Remove(peer.m_peer.m_uid); return; }
    // (same overlap guard as mechanism 2, vs the peer's own refpos)

    temp.Clear();
    __instance.FindSectorObjects(ZoneSystem.GetZone(sub.Pos), Ring, ZoneSystem.instance.m_activeDistantArea, temp);
    foreach (ZDO zdo in temp)
        if (peer.ShouldSend(zdo)) toSync.Add(zdo);
}
```

`ZDOPeer.ShouldSend` is per-peer revision tracking and it is what makes this cheap: **each object
crosses the wire once, then only when it changes** — bandwidth-wise the subscription is
indistinguishable from the peer standing there. (`ZDOMan.ZDOPeer` is a private nested class;
publicizing makes it a legal parameter type.)

**Authority lives here, not in the client.** The handler refuses subscriptions when the feature is
disabled server-side and refuses NaN/Infinity positions outright — a client with a doctored config
gets silence, not sectors.

## Mechanism 4 — Existence: ghost-generate on demand

If nobody has ever stood in a zone, the server has never *generated* it: there are no vegetation or
location ZDOs to send. The subscription faithfully delivers bald terrain, and no amount of waiting
fixes it — the content doesn't exist. The answer is vanilla's own: **ghost generation**, the mode
the server already runs around every connected peer. While a subscription is live, the same
`CreateSyncList` postfix (before the `FindSectorObjects` sweep) runs:

```csharp
// One zone per send tick — SpawnZone is synchronous. Newly created ZDOs join the very
// same FindSectorObjects sweep below, so they flow out on the subscription immediately.
for each zone z in ring:
    if (!ZoneSystem.instance.IsZoneGenerated(z) &&
        ZoneSystem.instance.SpawnZone(z, ZoneSystem.SpawnMode.Ghost, out _))
        break;
```

Ghost mode creates the zone's ZDOs without keeping instances. Note the one permanent footprint of
the whole technique: **generation is a one-time act, saved to the world file** — anchoring a virgin
zone "explores" it forever, exactly as a sailing player would have.

## Mechanism 5 — The horizon (only if you render)

Anything that *draws* the anchored area — a snapshot camera, a live feed — sees black void past the
loaded ring, because distant terrain is a `TerrainLod` mesh carpet that follows the player's camera.
Give the anchor its own:

- **Build it fresh; do not `Instantiate` the scene's.** The LOD meshes are *children* of the
  component's GameObject, and its `m_heightmaps` list is non-serialized — a clone brings nine
  orphaned mesh copies and then builds nine more.
- Create an **inactive** GameObject, `AddComponent<TerrainLod>()`, copy the five serialized fields
  (`m_terrainSize`, `m_regionsPerAxis`, `m_vertexDistance`, `m_material`, `m_updateStepDistance`)
  from the scene's instance, **then** activate — `OnEnable` is what builds the meshes, and it must
  run with the real material or you get nine invisible ones.
- Pin it: `m_updateStepDistance = float.MaxValue` (never re-follow the player's camera),
  `m_lastPoint = <anchor, grid-snapped to m_vertexDistance>`, every `HeightmapWithOffset.m_state =
  NeedsRebuild`, `m_heightmapState = NeedsRebuild`. Its own `Update` then queues the bakes on the
  `HeightmapBuilder` worker thread and flips `m_heightmapState` to `Done` when the carpet is laid.
- Destroy the GameObject on teardown; `OnDisable → ResetMeshes` cleans up its children.

The player's own horizon is never touched — the two rigs coexist (heights come from the same
`WorldGenerator`, so overlap renders the same way vanilla's own LOD-under-real-terrain overlap does).

---

## ⚠️ The distant ring — a bug an earlier revision of this document shipped with

Both sweeps above pass a **distant ring** as `FindSectorObjects`' third argument. This document used
to show **`0`** there, and TortalPortal shipped `0` for several builds. It is wrong, and it fails
silently.

```csharp
// ZDOMan.FindSectorObjects, abridged
for (int l = area + 1; l <= area + distantArea; l++)   // distantArea = 0  ->  body never runs
    FindDistantObjects(...);
```

`0` does not mean "a modest distance". The loop runs from `area + 1` to `area + 0`, so **not one
distant object is ever requested.** Everything inside the near ring arrives perfectly, which is what
makes it so hard to spot — the anchor looks like it works. On a dedicated server it costs twice
over: the client holds nothing it was not sent, and the server-side sweep was passing `0` too.

Symptom to recognise: **a destination that is fully loaded, correctly lit, correctly weathered, and
still reads as an empty stage set.** Pass `ZoneSystem.instance.m_activeDistantArea` — the ring
vanilla gives the player's own camera — in both places.

---

## Mechanism 6 — Presence: making the area *inhabited*

The mechanisms above make an area exist and draw. They do not populate it, because Valheim gates
creature spawning on a **`Player`**, not on a loaded zone. `SpawnSystem` is the whole story:

```csharp
UpdateSpawning()        // needs m_nview.IsValid() && m_nview.IsOwner() && Player.m_localPlayer != null
GetPlayersInZone(list)  // Player.GetAllPlayers() filtered by InsideZone(pos) -> +-32m of zone centre
if (list.Count == 0) return;                    // <- the actual blocker at an anchor
FindBaseSpawnPoint(spawn, list, out centre, ..) // dart 40-80m around one of those players
```

Everything downstream is player-free — `IsSpawnPointGood` tests biome, blocked-ness, altitude and
tilt only. So **a presence in the zone is the entire feature.**

- **Do not fabricate a `Player`.** It needs a `ZNetView` to survive its own `Awake`, and a networked
  `Player` replicates — every other client gets a ghost standing in your anchored zone.
- **Patch the two methods that ask instead.** A postfix on `GetPlayersInZone` adding the local
  player *only when the list came back empty* (a real player standing there is already the right
  answer), plus a prefix on `FindBaseSpawnPoint` running vanilla's own twenty-dart loop centred on
  the anchor. Without the second, the first is useless: vanilla throws darts around the viewer's
  real position and `IsSpawnPointGood` rejects all twenty for wrong biome.
- **Ownership:** `UpdateSpawning` runs only for the owner of the zone-control object, which at an
  anchor is the server or nobody. A prefix calling `nview.ClaimOwnership()` covers it — and note
  what that means: the anchoring client becomes the simulator for a place it is not standing in.

> **This is where a rendering feature becomes a gameplay feature.** The creatures are real, written
> to the world, and they outlive the view that made them. Ship it **off by default**, and restrict
> the spawn ring — the anchor ring dial may reach 25 zones, and stocking all of them from a glance
> at a photograph is not a defensible default.

---

## Knowing when the area is actually ready

`ZNetScene.IsAreaReady(pos)` is vanilla's readiness test (zone loaded + every near ZDO instanced)
— **and it lies on a dedicated client**, because it only judges the ZDOs that have *arrived so
far*. Mid-stream, a half-empty area reads as complete. The settle test that actually works:

```
settled ⇔ for N consecutive polls (6 × 0.25s worked well):
    sector ZDO count > 0 and UNCHANGED since last poll     // nothing still in flight
    && ZNetScene.IsAreaReady(pos)                          // everything known is standing
    && !Heightmap.HaveQueuedRebuild(pos, ~100m)            // terrain meshes done (TerrainComp
                                                           //   edits arrive late and queue rebuilds)
    && terrainLodRig == null || rig.m_heightmapState == Done
```

For a **permanent record** (a stored photograph), never act on an unsettled scene — skip and retry,
because a bad artifact stands forever while a missing one heals. For a **live view**, going ahead
at budget exhaustion is fine — the scene finishes loading in front of the viewer.

---

## Lifecycle: the parts that make it failsafe

These patterns are why the anchor can't leak, and they transfer to any use of the technique:

1. **`try/finally` owns the anchor.** The coroutine that raises it drops it in `finally` — and
   Unity's `StopCoroutine` disposes the iterator, which *runs* the `finally`, so a killed routine
   cleans up identically to a finished one.
2. **Renewals, not open/close.** Anything held across a network (the subscription) expires on its
   own unless re-asked-for. Crash-safety without a cleanup protocol.
3. **Deadman switches count FRAMES, not seconds.** The anchor's own zone loading hitches the
   client. A wall-clock deadman reads its own hitch as abandonment and tears the feed down —
   unload, restart, hitch, repeat, forever. During a hitch neither ticks *nor the frame counter*
   advance, so `Time.frameCount - lastTickFrame > 3` survives any stall while still ending an
   abandoned hold within ~50 ms.
4. **One anchor, polite consumers.** Background work checks a `LiveThing.Active` flag before
   starting; the live thing waits out an in-flight background hold. No queue, no lock — two
   booleans.
5. **Every dial is clamped and NaN-proofed at the read.** `AcceptableValueRange` guards the UI, not
   a hand-edited file; `Mathf.Clamp(NaN)` is still NaN and a NaN deadline is an infinite loop. Read
   through accessors that reject NaN/Infinity and clamp to a hard range (ring capped at 2 — 5×5
   zones; a wider ring is a guaranteed hitch on any hardware).
6. **Authority is server-side.** The server refuses subscriptions when the feature is off and
   refuses poisoned positions. Synced config is a convenience; the refusal is the guarantee.
7. **Narrate at Info.** The whole feature is invisible work far from any player; a silent success
   is indistinguishable from a pass that never ran, and that ambiguity costs debugging sessions.

---

## Adapting it: "run things while I'm away"

The anchor as shipped loads and *renders*; making the area **tick** (smelters smelting, kilns
burning, crops visible-growing, spawners spawning) needs one more ingredient: **ownership.**

- Objects instantiated in an anchored zone are **unowned** — vanilla assigns simulation ownership
  by peer proximity (`ZDOMan.ReleaseNearbyZDOS`), and no peer is near. Unowned = rendered but
  frozen: no AI, no machine ticks.
- A keep-alive mod claims ownership for the anchoring client:
  `zdo.SetOwner(ZDOMan.GetSessionID())` across the anchored sectors once **settled** (see below),
  and simply stops claiming on teardown — vanilla reassigns or idles them exactly as it does when a
  player leaves an area.
- **Claim only after settle, and beware physics.** An owned `Rigidbody` on terrain that has not
  finished building falls through the world and — because `ZSyncTransform` writes the owner's
  transform into the ZDO every frame — *saves itself falling*. (This exact class of bug destroyed a
  player inventory elsewhere; treat "settled before owned" as a hard rule.)
- Know what actually needs it: much of Valheim catches up from timestamps on load (plant growth,
  fermenter, partially smelters). Keep-alive buys you the things that genuinely only run while
  loaded — active spawners, windmill/production in real time, wards, live observation — not the
  things the game already fakes retroactively.
- A **rendering client must exist for pictures only.** Pure keep-alive needs no camera, no horizon
  rig, and would even run on a modded *host* with no players in the area at all (mechanisms 1–2
  alone, since the host holds every ZDO).

## Cost model

One ring-1 anchor ≈ one extra player's worth of streaming and instancing, held only while wanted,
gone within ~5 seconds of release (one `RemoveObjects` tick + the 4 s zone TTL). Ghost generation
adds a one-time world-file growth per virgin zone. The subscription adds one full-sector send per
subscribe, then deltas. Budget accordingly: the shipped mod holds **one** anchor at a time, ever.

---

## Hook-point summary (for porting)

| Purpose | Hook | Kind |
|---|---|---|
| Terrain alive | `ZoneSystem.PokeLocalZone(Vector2i)` | direct call (publicized), coroutine-driven |
| Objects alive | `ZNetScene.CreateObjects(List<ZDO>, List<ZDO>)` | Harmony **prefix**, append to first list |
| Data to client | `ZDOMan.CreateSyncList(ZDOPeer, List<ZDO>)` | Harmony **postfix** on server, append via `ShouldSend` |
| Content exists | `ZoneSystem.SpawnZone(z, SpawnMode.Ghost, out _)` | direct call, server, one per send tick |
| Distant scenery | third arg of `FindSectorObjects` = `m_activeDistantArea`, **never 0** | see the warning above |
| Horizon | `TerrainLod` built fresh + pinned | scene object, no patch |
| Somebody is here | `SpawnSystem.GetPlayersInZone` | Harmony **postfix**, add local player only if empty |
| Where they are | `SpawnSystem.FindBaseSpawnPoint` | Harmony **prefix**, re-run vanilla's loop around the anchor |
| Zone simulates | `SpawnSystem.UpdateSpawning` | Harmony **prefix**, `nview.ClaimOwnership()` |
| Readiness | `ZNetScene.IsAreaReady` + `Heightmap.HaveQueuedRebuild` + count stability | direct calls |
| Transport | `ZRoutedRpc.Register<Vector3>` / targetless `InvokeRoutedRPC` | vanilla RPC layer |

Reference implementation: `SnapshotAnchor.cs`, `SectorSubscription.cs`, `LivePreview.cs`, and the
anchor pass in `PlacementPatch.cs` of TortalPortal v1.2.0.
