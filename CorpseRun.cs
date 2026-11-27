using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TortalPortal
{
    // The corpse run.
    //
    // A death in Valheim is followed by a bare-handed walk back to wherever it happened, through whatever
    // killed you, to a grave that is very often standing in the middle of it. This raises a private, temporary
    // portal near the spot the player respawns at - their bed, or the start temple if they have none - leading
    // straight to their tombstone. One way, by design: the grave-side gate is an exit, and there is no
    // automatic trip back. Both gates come down together the moment the grave is emptied or despawns, or after
    // a configurable time, and any gate a previous server run left standing is reaped at startup.
    //
    // Ported from TortalPortal Lite's Corpse-Run Gate, with the two halves this mod has that Lite does not:
    //
    //  - The client is running this mod too, so a death is REPORTED (tombstone and real respawn point, both
    //    of which only the client knows) rather than inferred. Lite guessed the bed by sweeping bed ZDOs for
    //    the player's ID and picking the one it happened to see first, which after a restart is bucket order.
    //    Here the gate goes where the player actually wakes up. The sweep still runs, as the fallback for a
    //    tombstone standing after a server restart, when nobody is going to report anything.
    //  - The client decides what a corpse gate does when walked up to: the owner is sent straight through
    //    from the bed side, nobody gets a destination window at either end, and the grave side leads nowhere.
    //    Lite needed a vanilla client to do the right thing on its own; this one just does it.
    //
    // Everything that touches the world happens on the server. Placement is the headless scanner (real
    // ground, real footprints, PortalPlacement); the gates are bare ZDOs (PortalMint); the client only ever
    // reads them.
    [HarmonyPatch]
    public static class CorpseRun
    {
        // ---- wire --------------------------------------------------------------------------------------------

        // Client -> server: "I died; this is my tombstone and this is where I will respawn."
        private const string DeathRpc = "TortalPortal_Death";

        // ---- server state --------------------------------------------------------------------------------------

        private readonly struct Gate
        {
            public readonly ZDOID Origin;
            public readonly ZDOID Destination;
            public readonly ZDOID Tombstone;
            public readonly float Age;
            // Where each end was placed. Kept so an end that goes missing can be raised again on the spot the
            // scan chose for it, without paying for a second placement scan or moving the gate under a player
            // already walking towards it.
            public readonly Vector3 OriginPos;
            public readonly Quaternion OriginRot;
            public readonly Vector3 GravePos;
            public readonly Quaternion GraveRot;
            public readonly string Tag;
            public readonly int Remints;

            public Gate(ZDOID origin, ZDOID destination, ZDOID tombstone, float age,
                Vector3 originPos, Quaternion originRot, Vector3 gravePos, Quaternion graveRot, string tag, int remints)
            {
                Origin = origin;
                Destination = destination;
                Tombstone = tombstone;
                Age = age;
                OriginPos = originPos;
                OriginRot = originRot;
                GravePos = gravePos;
                GraveRot = graveRot;
                Tag = tag;
                Remints = remints;
            }

            public Gate With(ZDOID origin, ZDOID destination, float age, int remints)
            {
                return new Gate(origin, destination, Tombstone, age, OriginPos, OriginRot, GravePos, GraveRot, Tag, remints);
            }
        }

        // How many times a gate's ends may be raised again before the mod stops trying. Vanilla's
        // ZDOMan.RPC_DestroyZDO applies a destroy from any peer with no sender check, so a peer that keeps
        // sending them would otherwise have the server re-minting on every maintenance pass for ever.
        private const int MaxRemints = 6;

        private struct PendingGate
        {
            public ZDOID Tombstone;
            public float Remaining;
            public bool HasSpawn;
            public Vector3 Spawn;
            // A reported tombstone can be a tick or two behind its report on the wire; the raise waits for it.
            public int Retries;
        }

        private const int MaxRaiseRetries = 5;
        private const float RetrySeconds = 2f;

        // How long a freshly seen tombstone gets to stop falling and sliding before its position is trusted.
        // It is a client-owned rigidbody that drops from the death point.
        private const float SettleSeconds = 3f;

        private const float MaintenanceIntervalSeconds = 2f;

        // Sectors the tombstone sweep visits per frame. A full pass of a large world takes a few seconds,
        // which only matters for the restart case the sweep exists for.
        private const int SweepSectorsPerFrame = 20;

        private static readonly Dictionary<long, Gate> Active = new Dictionary<long, Gate>();
        private static readonly Dictionary<long, PendingGate> Pending = new Dictionary<long, PendingGate>();
        private static readonly Dictionary<long, ZDOID> LastSeenTombstone = new Dictionary<long, ZDOID>();
        private static readonly Dictionary<long, ZDOID> NewestTombstone = new Dictionary<long, ZDOID>();
        // Graves seen before their time of death had replicated, so the "waiting" note is logged once each.
        private static readonly HashSet<ZDOID> UntimedTombstones = new HashSet<ZDOID>();
        private struct PendingToast
        {
            public long PlayerId;
            public string Message;
            public float NotBefore;
        }

        // Told when they are back on their feet, not while they are lying on the death screen: the character a
        // message would land on is destroyed at respawn, so one sent too early is lost with it.
        private static readonly List<PendingToast> Toasts = new List<PendingToast>();
        private const float ToastDelaySeconds = 12f;

        private static readonly List<int> TombstoneHashes = new List<int>();
        private static readonly HashSet<int> BedHashes = new HashSet<int>();
        private static ZdoQuery.PrefabSetSweeper _sweeper;
        private static readonly List<ZDO> SweepBuffer = new List<ZDO>();
        private static float _maintenanceTimer;
        private static float _reapTimer;
        // How often the stray reaper runs when this server has no corpse gates of its own standing.
        private const float IdleReapIntervalSeconds = 30f;
        private static bool _wasEnabled;
        private static bool _reapedAtStart;
        private static Coroutine _routine;

        // ---- lifecycle -----------------------------------------------------------------------------------------

        public static void OnZNetStart()
        {
            if (ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.Register<ZDOID, bool, Vector3>(DeathRpc, RPC_Death);
            }

            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (_routine != null) Plugin.Instance.StopCoroutine(_routine);
            _routine = Plugin.Instance.StartCoroutine(ServerRoutine());
        }

        public static void Reset()
        {
            if (_routine != null && Plugin.Instance != null) Plugin.Instance.StopCoroutine(_routine);
            _routine = null;
            Active.Clear();
            Pending.Clear();
            Toasts.Clear();
            LastSeenTombstone.Clear();
            NewestTombstone.Clear();
            UntimedTombstones.Clear();
            TombstoneHashes.Clear();
            BedHashes.Clear();
            _sweeper = null;
            _maintenanceTimer = 0f;
            _reapTimer = 0f;
            _wasEnabled = false;
            _reapedAtStart = false;
            _lastTombstone = ZDOID.None;
            PortalMint.Reset();
            // A world change re-mints every ZDOID, so nothing in the removal-lock set means anything.
            PortalGuard.ResetLocks();
        }

        private static IEnumerator ServerRoutine()
        {
            while (ZDOMan.instance == null || Game.instance == null || ZNetScene.instance == null) yield return null;
            yield return new WaitForSeconds(3f);

            while (true)
            {
                Tick(Time.deltaTime);
                yield return null;
            }
            // ReSharper disable once IteratorNeverReturns
        }

        // Kept out of the coroutine so the work can sit in a try/catch.
        private static void Tick(float dt)
        {
            try
            {
                if (ZDOMan.instance == null) return;

                // Leftovers from a previous run come down whether or not the feature is on now - a gate raised
                // by a server that has since been told to stop is still a stray.
                if (!_reapedAtStart)
                {
                    _reapedAtStart = true;
                    ReapUnknownGates();
                }

                bool enabled = Configuration.enableCorpseRun.Value;
                if (!enabled)
                {
                    // Turned off mid-session: every standing gate comes down, once, and the pass goes quiet.
                    if (_wasEnabled) TearDownAll("the corpse run was switched off");
                    _wasEnabled = false;
                    return;
                }
                _wasEnabled = true;

                DiscoverTombstonePrefabs();
                SweepTombstones();
                AdvancePending(dt);
                DeliverToasts();

                _maintenanceTimer += dt;
                if (_maintenanceTimer >= MaintenanceIntervalSeconds)
                {
                    float elapsed = _maintenanceTimer;
                    _maintenanceTimer = 0f;
                    MaintainGates(elapsed);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: the corpse-run pass failed this tick (non-fatal, it runs again next frame). Reason: {ex}");
            }
        }

        // ---- death detection -----------------------------------------------------------------------------------

        // Server side of the death report. The tombstone is verified against the sender before anything is
        // raised - a client can only ever open a gate to its own grave. The spawn point is taken on trust: the
        // worst a dishonest client can do with it is open a one-way gate from a spot of its choosing to its own
        // tombstone, which is a trip it could make on foot.
        private static void RPC_Death(long sender, ZDOID tombstone, bool hasSpawn, Vector3 spawn)
        {
            try
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                if (!Configuration.enableCorpseRun.Value) return;

                long playerId = 0L;
                foreach (ConnectedCharacter cc in ServerPeers.All())
                {
                    if (cc.PeerId == sender) { playerId = cc.PlayerId; break; }
                }
                // A listen host's own report is delivered locally, with the host's session ID as the sender.
                if (playerId == 0L && !ZNet.instance.IsDedicated() && sender == ZDOMan.GetSessionID() && Game.instance != null)
                {
                    PlayerProfile hostProfile = Game.instance.GetPlayerProfile();
                    if (hostProfile != null) playerId = hostProfile.GetPlayerID();
                }
                if (playerId == 0L)
                {
                    Configuration.LogDebug($"Corpse run: a death report from peer {sender} could not be matched to a player yet - the sweep will catch the tombstone.");
                    return;
                }

                LastSeenTombstone[playerId] = tombstone;
                Pending[playerId] = new PendingGate { Tombstone = tombstone, Remaining = SettleSeconds, HasSpawn = hasSpawn, Spawn = spawn };
                Configuration.LogDebug($"Corpse run: death reported by player {playerId}, tombstone {tombstone}, spawn {(hasSpawn ? spawn.ToString() : "world spawn")}.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(RPC_Death)} failed (non-fatal). Reason: {ex}");
            }
        }

        // Tombstone and bed prefabs are found by component, not by a guessed name, so a modded tombstone or bed
        // counts too.
        private static void DiscoverTombstonePrefabs()
        {
            if (TombstoneHashes.Count > 0 || ZNetScene.instance == null) return;
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab == null) continue;
                if (prefab.GetComponent<TombStone>() != null) TombstoneHashes.Add(prefab.name.GetStableHashCode());
                if (prefab.GetComponent<Bed>() != null) BedHashes.Add(prefab.name.GetStableHashCode());
            }
            if (TombstoneHashes.Count > 0) _sweeper = new ZdoQuery.PrefabSetSweeper(TombstoneHashes);
            Plugin.Log.LogInfo($"TortalPortal: corpse run watching {TombstoneHashes.Count} tombstone prefab(s) and {BedHashes.Count} bed prefab(s).");
        }

        // Vanilla only honours a custom spawn point if a bed still stands within 5m of it - Game.FindSpawnPoint
        // otherwise clears the point and sends the player to the start temple. The gate has to go where the
        // player actually wakes up, so the same test is applied here against the world's bed ZDOs.
        private static bool BedStandsAt(Vector3 spawn)
        {
            if (BedHashes.Count == 0) return true;
            foreach (ZDO zdo in ZdoQuery.FindNear(spawn, 5f))
            {
                if (BedHashes.Contains(zdo.GetPrefab())) return true;
            }
            return false;
        }

        // The fallback: a tombstone still standing after a restart, or a death whose report never arrived. Read
        // straight off the TombStone ZDO - s_owner is the player's stable profile ID and s_timeOfDeath is a real
        // timestamp, so the newest grave per player is just the maximum.
        private static void SweepTombstones()
        {
            if (_sweeper == null) return;
            SweepBuffer.Clear();
            _sweeper.Advance(SweepSectorsPerFrame, SweepBuffer);

            foreach (ZDO tomb in SweepBuffer)
            {
                if (tomb == null || !tomb.IsValid()) continue;
                long owner = tomb.GetLong(ZDOVars.s_owner, 0L);
                if (owner == 0L) continue;
                long timeOfDeath = tomb.GetLong(ZDOVars.s_timeOfDeath, 0L);

                if (!NewestTombstone.TryGetValue(owner, out ZDOID currentId))
                {
                    NewestTombstone[owner] = tomb.m_uid;
                    continue;
                }
                if (currentId == tomb.m_uid) continue;
                ZDO current = ZDOMan.instance.GetZDO(currentId);
                long currentTime = current != null && current.IsValid() ? current.GetLong(ZDOVars.s_timeOfDeath, 0L) : -1L;
                if (current == null || !current.IsValid() || timeOfDeath >= currentTime) NewestTombstone[owner] = tomb.m_uid;
            }

            // A NEW tombstone for a connected player is a death nobody reported. Only connected players: a gate
            // for someone who is not here would stand for the whole TTL with nobody to use it.
            foreach (ConnectedCharacter cc in ServerPeers.All())
            {
                long playerId = cc.PlayerId;
                if (playerId == 0L || !NewestTombstone.TryGetValue(playerId, out ZDOID tombId) || tombId == ZDOID.None) continue;
                if (LastSeenTombstone.TryGetValue(playerId, out ZDOID last) && last == tombId) continue;
                if (Pending.ContainsKey(playerId)) continue;

                ZDO tomb = ZDOMan.instance.GetZDO(tombId);
                if (tomb == null || !tomb.IsValid()) continue;

                // Only a recent death. A grave abandoned days ago would otherwise get a gate raised at the bed
                // on every restart and every login, for a trip nobody is making. The TTL is the natural window:
                // a gate raised for an older death would already have closed. Game time, because that is what
                // the tombstone recorded - it does not advance while the server is down.
                // TombStone.Awake writes s_timeOfDeath only on the peer that OWNS the tombstone, so there is a
                // window - before the dying client's Awake has run and its ZDO has reached us - where the field
                // reads 0. Treated as an age it is the whole epoch, which fails every limit. Leave it unseen
                // and look again next pass rather than marking a grave handled and refusing it forever.
                long timeOfDeath = tomb.GetLong(ZDOVars.s_timeOfDeath, 0L);
                if (timeOfDeath == 0L)
                {
                    // Said once per grave, not once per frame: this is the one branch that neither records the
                    // tombstone nor queues anything, and the sweep runs every tick.
                    if (UntimedTombstones.Add(tombId))
                    {
                        Configuration.LogDebug($"Corpse run: tombstone {tombId} for player {playerId} has no time of death yet - looking again next pass.");
                    }
                    continue;
                }
                UntimedTombstones.Remove(tombId);

                // A gate already stands for this player. Only a grave strictly NEWER than the one it leads to
                // may replace it: NewestTombstone is a ratchet the paced sweep updates sector by sector, so
                // right after a second death it still names the previous, still-standing grave - and acting on
                // that would tear down the fresh gate and raise one to the older body.
                if (Active.TryGetValue(playerId, out Gate standing))
                {
                    if (standing.Tombstone == tombId) continue;
                    ZDO standingTomb = ZDOMan.instance.GetZDO(standing.Tombstone);
                    if (standingTomb != null && standingTomb.IsValid() && standingTomb.GetLong(ZDOVars.s_timeOfDeath, 0L) >= timeOfDeath) continue;
                }

                float maxAgeMinutes = Configuration.corpseRunMaxGraveAgeMinutes.Value;
                long ageTicks = ZNet.instance.GetTime().Ticks - timeOfDeath;
                if (maxAgeMinutes > 0f && ageTicks > TimeSpan.FromMinutes(maxAgeMinutes).Ticks)
                {
                    LastSeenTombstone[playerId] = tombId;
                    Configuration.LogDebug($"Corpse run: the sweep found tombstone {tombId} for player {playerId}, but it is {TimeSpan.FromTicks(ageTicks).TotalMinutes:0} game-minutes old - no gate.");
                    continue;
                }

                Pending[playerId] = new PendingGate { Tombstone = tombId, Remaining = SettleSeconds, HasSpawn = false, Spawn = Vector3.zero };
                Configuration.LogDebug($"Corpse run: the sweep found an unreported tombstone {tombId} for player {playerId}.");
            }
        }

        private static void AdvancePending(float dt)
        {
            if (Pending.Count == 0) return;
            List<long> due = null;
            List<long> keys = new List<long>(Pending.Keys);
            foreach (long playerId in keys)
            {
                PendingGate pending = Pending[playerId];
                pending.Remaining -= dt;
                Pending[playerId] = pending;
                if (pending.Remaining <= 0f) (due ?? (due = new List<long>())).Add(playerId);
            }
            if (due == null) return;
            foreach (long playerId in due)
            {
                PendingGate pending = Pending[playerId];
                Pending.Remove(playerId);
                RaiseGate(playerId, pending);
            }
        }

        // ---- raising -------------------------------------------------------------------------------------------

        // Put a raise back in the queue with a widening gap, or give up out loud. Every failure that is not a
        // refusal comes through here: a death is never dropped in silence, because the tombstone stands for the
        // whole run and a player walking back to nothing has no way to know the server tried.
        private static bool RetryOrGiveUp(long playerId, PendingGate pending, string why)
        {
            if (pending.Retries < MaxRaiseRetries)
            {
                pending.Retries++;
                // Backoff: 2s, 4s, 6s ... so a condition that needs a moment gets one, and one that needs the
                // world to finish loading gets longer without hammering the pass.
                pending.Remaining = RetrySeconds * pending.Retries;
                Pending[playerId] = pending;
                Configuration.LogDebug($"Corpse run: {why} - retrying player {playerId}'s gate in {pending.Remaining:0}s (attempt {pending.Retries} of {MaxRaiseRetries}).");
                return true;
            }
            Plugin.Log.LogWarning($"TortalPortal: gave up raising player {playerId}'s corpse gate after {MaxRaiseRetries} attempts - {why}. The grave is still standing; the sweep will try again if it is still within CorpseRunMaxGraveAgeMinutes.");
            return false;
        }

        private static void RaiseGate(long playerId, PendingGate pending)
        {
            ZDO tomb = ZDOMan.instance.GetZDO(pending.Tombstone);
            if (tomb == null || !tomb.IsValid())
            {
                RetryOrGiveUp(playerId, pending, $"tombstone {pending.Tombstone} is not on the server yet");
                return;
            }
            if (tomb.GetLong(ZDOVars.s_owner, 0L) != playerId)
            {
                // A refusal, not a failure: retrying would not change the answer.
                Plugin.Log.LogWarning($"TortalPortal: refused a corpse gate for player {playerId} - tombstone {pending.Tombstone} belongs to somebody else.");
                return;
            }
            // Nothing can be minted without a portal prefab hash, and Game.PortalPrefabHash is only filled once
            // Game.instance has started. Checked BEFORE the placement scan so a boot-order miss costs a retry
            // rather than a wasted scan and a lost death.
            if (PortalMint.ResolvePortalPrefabHash() == 0)
            {
                RetryOrGiveUp(playerId, pending, "no portal prefab hash is available yet");
                return;
            }

            // One gate per player: a second death tears the first pair down before raising the next. Removed
            // from Active in the same breath - every failure below returns without writing a new record, and a
            // record naming two just-destroyed ZDOIDs and the PREVIOUS tombstone is exactly what
            // EnsureBothEnds would helpfully rebuild, re-raising the gate that was deliberately taken down and
            // pointing it at the older corpse.
            if (Active.TryGetValue(playerId, out Gate old))
            {
                TearDown(old);
                Active.Remove(playerId);
            }

            float clearance = Configuration.corpseRunClearanceMeters.Value;
            float minRoom = Configuration.corpseRunMinRoomMeters.Value;
            float searchRadius = Configuration.corpseRunSearchRadiusMeters.Value;
            float originMinOffset = Configuration.corpseRunBedOffsetMeters.Value;
            float graveMinOffset = Configuration.corpseRunGraveOffsetMeters.Value;

            if (!TryResolveOrigin(playerId, pending, out Vector3 originAnchor, out bool atBed))
            {
                RetryOrGiveUp(playerId, pending, "there is no usable respawn point for this player yet (no bed reported, no hint, and the start temple is not registered)");
                return;
            }
            Vector3 tombPos = tomb.GetPosition();

            PortalPlacement.Placement origin;
            PortalPlacement.Placement grave;
            try
            {
                origin = PortalPlacement.Find(originAnchor, originMinOffset, searchRadius, clearance, ignoreZdo: default, minRoom: minRoom);
                // The origin's spot is spoken for before it exists as a ZDO - a death beside one's own bed must
                // not land both ends of the gate in the same clearing.
                grave = PortalPlacement.Find(tombPos, graveMinOffset, searchRadius, clearance, ignoreZdo: pending.Tombstone, avoid: new[] { origin.Position }, minRoom: minRoom);
            }
            catch (Exception ex)
            {
                // A gate in a poor spot still beats no gate: never let a scan fault eat the death.
                Plugin.Log.LogError($"TortalPortal: the corpse-gate placement scan faulted for player {playerId}, falling back to fixed offsets. Reason: {ex}");
                origin = PortalPlacement.FixedOffset(originAnchor, originMinOffset);
                grave = PortalPlacement.FixedOffset(tombPos, graveMinOffset);
            }

            LogPlacement(playerId, "origin", originAnchor, origin);
            LogPlacement(playerId, "grave", tombPos, grave);

            // A per-player tag nobody will type by accident: a zero-width space, then the player's ID. Vanilla's
            // own pairing pass leaves a same-tag pair that already points at itself alone, and the client never
            // shows this string - the hover text is ours.
            string tag = $"\u200Bcr:{playerId}";

            ZDO originZdo = PortalMint.Create(origin.Position, origin.Rotation, tag, PortalKeys.KindCorpseOrigin, playerId, immune: true);
            if (originZdo == null)
            {
                RetryOrGiveUp(playerId, pending, "the bed-side gate could not be minted");
                return;
            }
            ZDO graveZdo = PortalMint.Create(grave.Position, grave.Rotation, tag, PortalKeys.KindCorpseDest, playerId, immune: true);
            if (graveZdo == null)
            {
                PortalMint.Destroy(originZdo);
                RetryOrGiveUp(playerId, pending, "the grave-side gate could not be minted, so the bed side came down again");
                return;
            }

            ServerPeers.ClaimAndWrite(originZdo, z =>
            {
                z.Set(PortalKeys.DestPos, grave.Position);
                z.Set(PortalKeys.DestRotY, grave.Rotation.eulerAngles.y);
                z.SetConnection(ZDOExtraData.ConnectionType.Portal, graveZdo.m_uid);
            });
            ServerPeers.ClaimAndWrite(graveZdo, z => z.SetConnection(ZDOExtraData.ConnectionType.Portal, originZdo.m_uid));

            Active[playerId] = new Gate(originZdo.m_uid, graveZdo.m_uid, pending.Tombstone, 0f,
                origin.Position, origin.Rotation, grave.Position, grave.Rotation, tag, 0);
            Plugin.Log.LogInfo($"TortalPortal: corpse gate raised for player {playerId} - {(atBed ? "bed" : "world spawn")} side at {Fmt(origin.Position)}, grave side at {Fmt(grave.Position)}, {Vector3.Distance(origin.Position, grave.Position):0}m apart.");

            string whereText = atBed ? "near your bed" : "at the world spawn";
            string noteText = grave.ReanchoredFromInterior ? " It comes out at the dungeon entrance." : "";
            QueueToast(playerId, $"A corpse gate has opened {whereText}.{noteText}");
        }

        // held: the message waits for the player however long it takes, and is dropped only when their gate is.
        // Used for the gate-opened announcement; the gate-closed notes still expire, since a note about a gate
        // that has already gone is worth nothing an hour later.
        // A gate announcement is NOT dropped on a timer. The gate itself stands until the grave is emptied, so
        // a player who alt-F4s on the death screen - or simply sits on it for a couple of minutes - must still
        // be told where their gate is when they come back. The message waits for them and is discarded when the
        // gate it describes comes down, which is the only expiry that means anything here. The gate-CLOSED
        // notes are not queued at all; they are sent straight out to a player who is present to read them.
        private static void QueueToast(long playerId, string message)
        {
            // One announcement per player: two deaths inside ToastDelaySeconds would otherwise queue two and
            // deliver both in the same pass.
            for (int i = Toasts.Count - 1; i >= 0; i--)
            {
                if (Toasts[i].PlayerId == playerId) Toasts.RemoveAt(i);
            }
            Toasts.Add(new PendingToast
            {
                PlayerId = playerId,
                Message = message,
                NotBefore = Time.time + ToastDelaySeconds,
            });
        }

        private static void DeliverToasts()
        {
            if (Toasts.Count == 0) return;
            for (int i = Toasts.Count - 1; i >= 0; i--)
            {
                PendingToast t = Toasts[i];
                // The gate it announces has come down - nothing left to tell them about.
                if (!Active.ContainsKey(t.PlayerId))
                {
                    Toasts.RemoveAt(i);
                    continue;
                }
                if (Time.time < t.NotBefore) continue;
                if (!ServerPeers.TryFind(t.PlayerId, out ConnectedCharacter who) || who.Zdo == null || who.Zdo.GetBool(ZDOVars.s_dead, false)) continue;
                ServerPeers.Toast(who, t.Message);
                Toasts.RemoveAt(i);
            }
        }

        // Where the bed-side gate goes: the respawn point the client reported with its death, failing that the
        // respawn point the client last wrote onto its character, failing that the start temple - which is
        // where vanilla puts a player with no bed.
        private static bool TryResolveOrigin(long playerId, PendingGate pending, out Vector3 anchor, out bool atBed)
        {
            atBed = false;
            anchor = Vector3.zero;

            if (pending.HasSpawn && BedStandsAt(pending.Spawn))
            {
                atBed = true;
                anchor = pending.Spawn;
                return true;
            }

            if (ServerPeers.TryFind(playerId, out ConnectedCharacter cc) && cc.Zdo != null && cc.Zdo.GetBool(PortalKeys.SpawnPointSet, false))
            {
                Vector3 hint = cc.Zdo.GetVec3(PortalKeys.SpawnPoint, Vector3.zero);
                if (hint != Vector3.zero && BedStandsAt(hint))
                {
                    atBed = true;
                    anchor = hint;
                    return true;
                }
            }

            if (ZoneSystem.instance != null && Game.instance != null && ZoneSystem.instance.GetLocationIcon(Game.instance.m_StartLocation, out Vector3 temple) && temple != Vector3.zero)
            {
                anchor = temple;
                return true;
            }

            // Deliberately NO fallback. The old one was Vector3.zero, which is open ocean on most seeds: the
            // scan would dutifully float a gate on the waves in the middle of the map and the player would be
            // told a gate had opened near their bed. Nothing usable means wait and ask again - the start
            // temple's location only goes missing while the zone holding it has yet to be registered.
            return false;
        }

        private static void LogPlacement(long playerId, string end, Vector3 anchor, PortalPlacement.Placement placement)
        {
            string line = $"TortalPortal: corpse-run {end} gate for player {playerId} near {Fmt(anchor)}: {placement.Describe()}";
            if (placement.IsGood) Plugin.Log.LogInfo(line);
            else Plugin.Log.LogWarning(line + " - nowhere in the widened search radius offered even CorpseRunMinRoomMeters of room; consider raising CorpseRunSearchRadiusMeters or lowering CorpseRunMinRoomMeters.");
        }

        private static string Fmt(Vector3 v) => $"{v.x:F1},{v.y:F1},{v.z:F1}";

        // ---- maintenance ---------------------------------------------------------------------------------------

        private static void MaintainGates(float dt)
        {
            // The reaper walks every portal in the world, which on a world with no portals yet falls through to
            // a full sector-array scan - too much to spend on a two-second timer. It still has to run with no
            // gates standing, because a stray left by a previous run can load in long after the start-up sweep
            // as the world's ZDOs arrive, so it runs on its own slower timer instead of on every pass.
            _reapTimer += dt;
            if (Active.Count > 0 || _reapTimer >= IdleReapIntervalSeconds)
            {
                _reapTimer = 0f;
                ReapUnknownGates();
            }
            if (Active.Count == 0) return;

            float ttlSeconds = Configuration.corpseRunGateTtlMinutes.Value * 60f;

            List<long> toRemove = null;
            List<long> keys = new List<long>(Active.Keys);
            foreach (long playerId in keys)
            {
                Gate gate = Active[playerId];
                float age = gate.Age + dt;

                ZDO tomb = ZDOMan.instance.GetZDO(gate.Tombstone);
                bool tombGone = tomb == null || !tomb.IsValid();
                // 0 means no limit: the gate stands until the grave is empty.
                bool expired = ttlSeconds > 0f && age >= ttlSeconds;

                if (tombGone || expired)
                {
                    TearDown(gate);
                    (toRemove ?? (toRemove = new List<long>())).Add(playerId);
                    Plugin.Log.LogInfo($"TortalPortal: corpse gate for player {playerId} closed - {(tombGone ? "the grave is gone" : "it timed out")}.");
                    if (ServerPeers.TryFind(playerId, out ConnectedCharacter who))
                    {
                        ServerPeers.Toast(who, tombGone ? "Your grave has been recovered - the corpse gate has closed." : "The corpse gate has closed.");
                    }
                    continue;
                }

                Active[playerId] = EnsureBothEnds(playerId, gate.With(gate.Origin, gate.Destination, age, gate.Remints));
            }

            if (toRemove != null)
            {
                foreach (long id in toRemove) Active.Remove(id);
            }
        }

        // Both ends of a standing gate, every maintenance pass: each one still present, still linked, still at
        // full health. An end can go missing for reasons this mod does not control - vanilla's RPC_DestroyZDO
        // takes a destroy from any peer without checking who sent it - so a gate is not assumed to stay up
        // merely because it was raised.
        private static Gate EnsureBothEnds(long playerId, Gate gate)
        {
            ZDOID originId = gate.Origin;
            ZDOID destId = gate.Destination;
            int remints = gate.Remints;

            ZDO origin = ZDOMan.instance.GetZDO(originId);
            ZDO dest = ZDOMan.instance.GetZDO(destId);
            bool originUp = origin != null && origin.IsValid();
            bool destUp = dest != null && dest.IsValid();

            if ((!originUp || !destUp) && remints >= MaxRemints)
            {
                // The vanished end's ZDOID is never coming back whether or not we re-mint, so its lock goes
                // either way - otherwise the set carries dead entries until the world changes.
                if (!originUp) PortalGuard.Unlock(originId);
                if (!destUp) PortalGuard.Unlock(destId);
                // Said once, at error level, rather than a warning every two seconds for the gate's whole life.
                if (remints == MaxRemints)
                {
                    Plugin.Log.LogError($"TortalPortal: player {playerId}'s corpse gate has needed {MaxRemints} end re-mints; leaving it as it stands. Something on this world is removing portal ZDOs.");
                    remints++;
                }
                return gate.With(originId, destId, gate.Age, remints);
            }

            if (!originUp)
            {
                // The vanished end's ZDOID will never come back; drop its lock so the set does not grow for the
                // life of the server.
                PortalGuard.Unlock(originId);
                ZDO raised = PortalMint.Create(gate.OriginPos, gate.OriginRot, gate.Tag, PortalKeys.KindCorpseOrigin, playerId, immune: true);
                if (raised != null)
                {
                    originId = raised.m_uid;
                    origin = raised;
                    originUp = true;
                    remints++;
                    Plugin.Log.LogWarning($"TortalPortal: the bed-side end of player {playerId}'s corpse gate had gone missing - raised again at {Fmt(gate.OriginPos)} (re-mints for this gate: {remints}).");
                }
                else
                {
                    // A mint that fails (no prefab hash yet, say) still costs an attempt: without this the
                    // two-second pass would retry for ever and the cap could never stop it.
                    remints++;
                }
            }

            if (!destUp)
            {
                PortalGuard.Unlock(destId);
                ZDO raised = PortalMint.Create(gate.GravePos, gate.GraveRot, gate.Tag, PortalKeys.KindCorpseDest, playerId, immune: true);
                if (raised != null)
                {
                    destId = raised.m_uid;
                    dest = raised;
                    destUp = true;
                    remints++;
                    Plugin.Log.LogWarning($"TortalPortal: the grave-side end of player {playerId}'s corpse gate had gone missing - raised again at {Fmt(gate.GravePos)} (re-mints for this gate: {remints}).");
                }
                else
                {
                    remints++;
                }
            }

            if (originUp && destUp)
            {
                // Re-link only when the pair does not already point at each other. Vanilla's ConnectPortals
                // pass leaves a reciprocal same-tag pair alone, so this is for a re-mint, not routine upkeep.
                if (origin.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) != destId)
                {
                    ZDOID target = destId;
                    Vector3 destPos = gate.GravePos;
                    float destRotY = gate.GraveRot.eulerAngles.y;
                    ServerPeers.ClaimAndWrite(origin, z =>
                    {
                        z.Set(PortalKeys.DestPos, destPos);
                        z.Set(PortalKeys.DestRotY, destRotY);
                        z.SetConnection(ZDOExtraData.ConnectionType.Portal, target);
                    });
                }
                if (dest.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) != originId)
                {
                    ZDOID back = originId;
                    ServerPeers.ClaimAndWrite(dest, z => z.SetConnection(ZDOExtraData.ConnectionType.Portal, back));
                }
            }

            // Health, on BOTH ends now. Conditional on purpose: ClaimAndWrite takes ownership and force-sends
            // the ZDO, so writing unconditionally would snatch the gate back off the client standing at it
            // every two seconds for as long as the gate stood.
            RepinHealth(origin);
            RepinHealth(dest);

            return gate.With(originId, destId, gate.Age, remints);
        }

        private static void RepinHealth(ZDO zdo)
        {
            if (zdo == null || !zdo.IsValid()) return;
            if (zdo.GetFloat(ZDOVars.s_health, 0f) >= PortalGuard.ImmuneHealth) return;
            ServerPeers.ClaimAndWrite(zdo, z => z.Set(ZDOVars.s_health, PortalGuard.ImmuneHealth));
        }

        // Any corpse gate no active record accounts for is a leftover - from a previous server run, since the
        // bookkeeping lives in memory while the ZDOs persist, or a mint that failed halfway - and is removed. A
        // tombstone still standing after a restart gets a fresh gate through the sweep, so nothing is lost.
        private static void ReapUnknownGates()
        {
            HashSet<ZDOID> known = null;
            foreach (ZDO zdo in PlacementPatch.GetAllPortals(includeCorpseGates: true))
            {
                if (!IsCorpseGate(zdo)) continue;
                if (known == null)
                {
                    known = new HashSet<ZDOID>();
                    foreach (Gate gate in Active.Values)
                    {
                        known.Add(gate.Origin);
                        known.Add(gate.Destination);
                    }
                }
                if (known.Contains(zdo.m_uid)) continue;
                Plugin.Log.LogInfo($"TortalPortal: reaping a stray corpse gate ({zdo.GetString(PortalKeys.Kind, "")}) at {Fmt(zdo.GetPosition())} - not raised by this server run.");
                PortalMint.Destroy(zdo);
            }
        }

        private static void TearDown(Gate gate)
        {
            ZDO origin = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(gate.Origin) : null;
            ZDO destination = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(gate.Destination) : null;
            if (origin != null) PortalMint.Destroy(origin);
            if (destination != null) PortalMint.Destroy(destination);
        }

        private static void TearDownAll(string why)
        {
            foreach (Gate gate in Active.Values) TearDown(gate);
            if (Active.Count > 0) Plugin.Log.LogInfo($"TortalPortal: took down {Active.Count} corpse gate(s) because {why}.");
            Active.Clear();
            Pending.Clear();
            // Held toasts are tied to a gate that no longer exists, and the "switched off" path returns before
            // DeliverToasts ever runs again, so they would sit here and be delivered against the NEXT gate.
            Toasts.Clear();
            ReapUnknownGates();
        }

        // Every corpse gate standing right now, for the health scan.
        public static int ActiveGateCount => Active.Count;

        public static bool IsTracked(ZDOID id)
        {
            foreach (Gate gate in Active.Values)
            {
                if (gate.Origin == id || gate.Destination == id) return true;
            }
            return false;
        }

        // ---- shared reads --------------------------------------------------------------------------------------

        public static bool IsCorpseGate(ZDO zdo)
        {
            if (zdo == null || !zdo.IsValid()) return false;
            string kind = zdo.GetString(PortalKeys.Kind, "");
            return kind == PortalKeys.KindCorpseOrigin || kind == PortalKeys.KindCorpseDest;
        }

        public static bool IsOrigin(ZDO zdo) => zdo != null && zdo.IsValid() && zdo.GetString(PortalKeys.Kind, "") == PortalKeys.KindCorpseOrigin;

        public static bool IsMine(ZDO zdo)
        {
            try
            {
                if (zdo == null || !zdo.IsValid() || Game.instance == null) return false;
                PlayerProfile profile = Game.instance.GetPlayerProfile();
                return profile != null && zdo.GetLong(PortalKeys.Owner, 0L) == profile.GetPlayerID();
            }
            catch
            {
                return false;
            }
        }

        // ---- client: reporting a death ---------------------------------------------------------------------------

        // The tombstone this client raised last. TombStone.Setup runs inside Player.OnDeath, on the dying
        // client, with the fresh tombstone's own ZDO - so it is remembered there and reported when OnDeath ends.
        private static ZDOID _lastTombstone = ZDOID.None;

        [HarmonyPatch(typeof(TombStone), nameof(TombStone.Setup))]
        [HarmonyPostfix]
        public static void TombStone_Setup_Postfix(TombStone __instance, long ownerUID)
        {
            try
            {
                if (Game.instance == null) return;
                PlayerProfile profile = Game.instance.GetPlayerProfile();
                if (profile == null || profile.GetPlayerID() != ownerUID) return;
                ZNetView nview = __instance.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) return;
                _lastTombstone = nview.GetZDO().m_uid;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(TombStone_Setup_Postfix)} failed (non-fatal, the server's sweep still finds the grave). Reason: {ex}");
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
        [HarmonyPostfix]
        public static void Player_OnDeath_Postfix(Player __instance)
        {
            try
            {
                if (__instance == null || __instance != Player.m_localPlayer) return;
                if (_lastTombstone == ZDOID.None) return;
                if (ZRoutedRpc.instance == null || Game.instance == null) return;
                if (!Configuration.enableCorpseRun.Value) return;

                PlayerProfile profile = Game.instance.GetPlayerProfile();
                bool hasSpawn = profile != null && profile.HaveCustomSpawnPoint();
                Vector3 spawn = hasSpawn ? profile.GetCustomSpawnPoint() : Vector3.zero;

                ZDOID tomb = _lastTombstone;
                _lastTombstone = ZDOID.None;

                // The two-argument overload addresses the server. On a listen host the "server" is this process,
                // and the call is delivered locally with the host's own session ID as the sender.
                ZRoutedRpc.instance.InvokeRoutedRPC(DeathRpc, tomb, hasSpawn, spawn);
                Configuration.LogDebug($"Corpse run: reported death (tombstone {tomb}, {(hasSpawn ? "bed at " + spawn : "no bed")}).");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(Player_OnDeath_Postfix)} failed (non-fatal, the server's sweep still finds the grave). Reason: {ex}");
            }
        }

        // ---- client: the spawn hint ----------------------------------------------------------------------------

        // Where this player respawns, written onto their own character ZDO whenever it is known to have changed:
        // on spawning in, and on claiming or leaving a bed. The server reads it for a gate the sweep raises
        // after a restart, when there was no death report to carry it.
        public static void WriteSpawnHint()
        {
            try
            {
                Player local = Player.m_localPlayer;
                if (local == null || Game.instance == null) return;
                ZNetView nview = local.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;
                PlayerProfile profile = Game.instance.GetPlayerProfile();
                if (profile == null) return;

                bool has = profile.HaveCustomSpawnPoint();
                ZDO zdo = nview.GetZDO();
                zdo.Set(PortalKeys.SpawnPointSet, has);
                zdo.Set(PortalKeys.SpawnPoint, has ? profile.GetCustomSpawnPoint() : Vector3.zero);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(WriteSpawnHint)} failed (non-fatal). Reason: {ex}");
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        [HarmonyPostfix]
        public static void Player_OnSpawned_Postfix(Player __instance)
        {
            try
            {
                if (__instance != null && __instance == Player.m_localPlayer) WriteSpawnHint();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(Player_OnSpawned_Postfix)} failed (non-fatal). Reason: {ex}");
            }
        }

        [HarmonyPatch(typeof(PlayerProfile), nameof(PlayerProfile.SetCustomSpawnPoint))]
        [HarmonyPostfix]
        public static void PlayerProfile_SetCustomSpawnPoint_Postfix()
        {
            try { WriteSpawnHint(); }
            catch (Exception ex) { Plugin.Log.LogError($"TortalPortal: {nameof(PlayerProfile_SetCustomSpawnPoint_Postfix)} failed (non-fatal). Reason: {ex}"); }
        }

        [HarmonyPatch(typeof(PlayerProfile), nameof(PlayerProfile.ClearCustomSpawnPoint))]
        [HarmonyPostfix]
        public static void PlayerProfile_ClearCustomSpawnPoint_Postfix()
        {
            try { WriteSpawnHint(); }
            catch (Exception ex) { Plugin.Log.LogError($"TortalPortal: {nameof(PlayerProfile_ClearCustomSpawnPoint_Postfix)} failed (non-fatal). Reason: {ex}"); }
        }

        // ---- client: what a corpse gate does ---------------------------------------------------------------------

        // The owner, at the bed-side gate, is sent through. Returns whether a trip started.
        public static bool TravelToGrave(TeleportWorld origin)
        {
            try
            {
                if (origin == null || Player.m_localPlayer == null) return false;
                ZNetView nview = origin.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) return false;
                ZDO zdo = nview.GetZDO();
                if (!IsOrigin(zdo) || !IsMine(zdo)) return false;

                Vector3 dest = zdo.GetVec3(PortalKeys.DestPos, Vector3.zero);
                if (dest == Vector3.zero) return false;
                Quaternion rot = Quaternion.Euler(0f, zdo.GetFloat(PortalKeys.DestRotY, 0f), 0f);
                Vector3 pos = dest + rot * Vector3.forward * origin.m_exitDistance + Vector3.up;

                PortalBehaviorPatch.SuppressAutoOpenFor(ZDOID.None, pos);
                PortalUIManager.HideUI();
                if (!Player.m_localPlayer.TeleportTo(pos, rot, true))
                {
                    PortalBehaviorPatch.ClearArrival();
                    return false;
                }
                Game.instance.IncrementPlayerStat(PlayerStatType.PortalsUsed);
                CorpseWard.OnTripStarted();
                Configuration.LogDebug($"Corpse run: travelling to the grave gate at {pos}.");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(TravelToGrave)} failed (non-fatal). Reason: {ex}");
                return false;
            }
        }

        // What the gate says when looked at. Vanilla's text would be the tag - a zero-width space and a number -
        // and "connected", neither of which means anything to a player.
        [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.GetHoverText))]
        [HarmonyPrefix]
        public static bool TeleportWorld_GetHoverText_Prefix(TeleportWorld __instance, ref string __result)
        {
            try
            {
                ZNetView nview = __instance.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) return true;
                ZDO zdo = nview.GetZDO();
                if (!IsCorpseGate(zdo)) return true;

                bool mine = IsMine(zdo);
                if (IsOrigin(zdo))
                {
                    __result = mine
                        ? "Corpse gate\n[<color=yellow><b>$KEY_Use</b></color>] Travel to your grave"
                        : "Corpse gate\n<color=grey>Not yours</color>";
                }
                else
                {
                    __result = mine
                        ? "Corpse gate\n<color=grey>One way - it closes when your grave is emptied</color>"
                        : "Corpse gate\n<color=grey>Not yours</color>";
                }
                __result = Localization.instance.Localize(__result);
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(TeleportWorld_GetHoverText_Prefix)} failed, showing vanilla text. Reason: {ex}");
                return true;
            }
        }
    }
}
