using BepInEx;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TortalPortal
{
    // Who owns a portal, in words.
    //
    // A portal's ZDO records its creator as a number and nothing else. Vanilla writes ZDOVars.s_creatorName
    // in exactly one place - PrivateArea.Setup, which is wards - so no gate any player has ever placed
    // carries a name, and the "Owner" row in the destination list has been blank on every world since 1.0.0.
    // The number is permanent and lives in the world; the name lives on the client whose profile it is.
    //
    // 1.4.0 asked each client to post its own (id, name) on arrival and kept the pairing on disk. On a live
    // server that produced twelve portal owners and not one name, so 1.4.1 stopped asking and went looking.
    // Three sources now, in order of how much they can answer:
    //
    //   Harvest   - the server reads the connected player's own character ZDO, which carries s_playerID and
    //               s_playerName side by side. s_playerID is the very field Piece.SetCreator copies into
    //               s_creator, so this is the same identity the portal recorded, taken from the world rather
    //               than from anybody's word for it. Covers everyone who is online.
    //   Backfill  - a one-off walk of every ZDO in the world for the (id, name) pairs vanilla leaves lying
    //               about: wards (s_creator + s_creatorName), and beds and tombstones (s_owner + s_ownerName).
    //               These outlive their owner's session, so a world running since day one names players who
    //               have not logged in since this mod was installed. This is what makes the register
    //               retroactive rather than merely eventually-correct.
    //   Claim     - the 1.4.0 client-side post, kept as a backstop for the player whose character ZDO the
    //               server cannot resolve yet.
    //
    // The export never drops a row it cannot name: "someone with ID 7204... owns nine portals" is true and
    // worth exporting, and it becomes a name later without the count ever having been wrong.
    public static class PlayerNames
    {
        private const string ClaimRpc = "TortalPortal_NameClaim";
        private const int MaxNameLength = 64;

        // How often the server re-reads its peers. Slow on purpose: a name changes when a player swaps
        // character, which is a logout, and the harvest catches that on the way back in.
        private const float HarvestIntervalSeconds = 10f;

        // The backfill waits for the world's ZDOs to be there to walk, then spends this many per frame. The
        // whole scan is a few seconds on a large world and happens once per session, so there is nothing to
        // gain by going harder and a stutter to lose.
        private const float BackfillDelaySeconds = 12f;
        private const int BackfillZdosPerFrame = 3000;

        // The client claim, resent on this schedule in seconds from ZNet start. 1.4.0 sent it once at two
        // seconds and never again, which meant one dropped packet - or one client whose profile was not
        // readable yet - was permanent for the session. There is no acknowledgement to wait on, so it simply
        // says the same true thing a few times; the server discards a repeat it already holds without a
        // disk write.
        private static readonly float[] ClaimAttemptSeconds = { 2f, 8f, 20f, 45f };

        // Names the game hands out when it has nothing better, and which must never reach the register.
        // "Stranger" is what a PlayerProfile is constructed with before a character is loaded - a dedicated
        // server holds exactly such a profile, with an ID generated at boot that belongs to no character in
        // any world - and "..." is what Player.GetPlayerName returns from a ZDO whose name has not been
        // written yet. Either one recorded as a name is worse than no name, because it reads as an answer.
        private static readonly HashSet<string> Placeholders =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Stranger", "..." };

        private static string StoreDir => Path.Combine(Paths.ConfigPath, "TortalPortal");
        private static string StorePath => Path.Combine(StoreDir, "playernames.json");

        // playerID -> display name, held on the server. A client has no use for anybody's name but its own.
        //
        // Not scoped to a world, and deliberately: a player ID belongs to the character profile, not to the
        // save it is playing on, so a name learned on one world is the right answer on the next. Only the
        // rows a world actually needs are ever read, because the export asks about the IDs its own portals
        // named.
        private static readonly Dictionary<long, string> Names = new Dictionary<long, string>();

        // Peer uid -> playerID, for this session only, and deliberately never persisted. It is how the export
        // can say who is online right now, and it goes stale the moment somebody disconnects - which costs
        // nothing, because it is only ever read intersected against ZNet's current peer list.
        private static readonly Dictionary<long, long> Session = new Dictionary<long, long>();

        private static bool _loaded;

        // Set when the register on disk existed but could not be read. Every later write is refused while it
        // is set, because the alternative is saving a register that has forgotten everything over one that
        // merely could not be parsed this once.
        private static bool _loadFailed;

        private static Coroutine _claim;
        private static Coroutine _harvest;
        private static Coroutine _backfill;

        // ---- lifecycle -------------------------------------------------------------------------------------

        public static void OnZNetStart()
        {
            try
            {
                // Unconditionally, and BEFORE the guard below. Reset is what stops the coroutines the world
                // just left behind, so bailing out ahead of it would leave the previous world's harvest and
                // backfill running against a register belonging to nobody.
                Reset();

                if (ZRoutedRpc.instance == null) return;

                // Registered on both sides for the reason the pin library gives: a listen-server host is a
                // client AND a server, and symmetry spares it a special case.
                ZRoutedRpc.instance.Register<long, string>(ClaimRpc, RPC_NameClaim);

                if (ZNet.instance != null && ZNet.instance.IsServer())
                {
                    Load();
                    _harvest = Plugin.Instance.StartCoroutine(HarvestRoutine());
                    _backfill = Plugin.Instance.StartCoroutine(BackfillRoutine());
                }

                _claim = Plugin.Instance.StartCoroutine(ClaimRoutine());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not start the player-name register (non-fatal, the portal export falls back to numeric IDs). Reason: {ex}");
            }
        }

        // The in-memory copy belongs to the server just left, so it goes. The file does not: names are keyed
        // by a profile ID that means the same thing everywhere, and Load reads it back on the next question
        // asked.
        public static void Reset()
        {
            StopRoutine(ref _claim);
            StopRoutine(ref _harvest);
            StopRoutine(ref _backfill);

            Names.Clear();
            Session.Clear();
            _loaded = false;
            _loadFailed = false;
        }

        private static void StopRoutine(ref Coroutine routine)
        {
            if (routine != null && Plugin.Instance != null) Plugin.Instance.StopCoroutine(routine);
            routine = null;
        }

        // ---- harvesting, from the server -------------------------------------------------------------------

        private static IEnumerator HarvestRoutine()
        {
            while (ZNet.instance == null || ZDOMan.instance == null) yield return null;

            while (true)
            {
                HarvestPeers();
                yield return new WaitForSeconds(HarvestIntervalSeconds);
            }
            // ReSharper disable once IteratorNeverReturns
        }

        private static void HarvestPeers()
        {
            try
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null) return;
                if (!Configuration.enablePortalExport.Value) return;

                foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                {
                    if (peer == null || !peer.IsReady()) continue;

                    // Sent by the client shortly after it spawns its character; until then there is nothing
                    // to look up and the next pass will find it.
                    if (peer.m_characterID == ZDOID.None) continue;

                    ZDO zdo = ZDOMan.instance.GetZDO(peer.m_characterID);
                    if (zdo == null || !zdo.IsValid()) continue;

                    long id = zdo.GetLong(ZDOVars.s_playerID, 0L);
                    if (id == 0) continue;

                    // The peer's own name is the fallback rather than the first choice: it arrives with the
                    // handshake and is whatever the client said, where the ZDO field was written by the
                    // character itself.
                    string name = zdo.GetString(ZDOVars.s_playerName, string.Empty);
                    if (string.IsNullOrWhiteSpace(name) || IsPlaceholder(name)) name = peer.m_playerName;

                    Record(peer.m_uid, id, name);
                }

                // A listen host never appears in its own peer list, and its profile is a real one being
                // played rather than the empty placeholder a dedicated server holds.
                if (!ZNet.instance.IsDedicated() && Game.instance != null)
                {
                    PlayerProfile profile = Game.instance.GetPlayerProfile();
                    if (profile != null) Record(0L, profile.GetPlayerID(), profile.GetName());
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not read the connected players' names (non-fatal, their portals export under their numeric IDs). Reason: {ex}");
            }
        }

        // ---- backfilling, from the world itself ------------------------------------------------------------

        // Where the walk has got to, kept outside the coroutine so the work itself can sit in a try/catch.
        private class BackfillState
        {
            public List<List<ZDO>> Buckets;
            public int Bucket;
            public int Index;
            public int Scanned;
            public int Named;
            public bool Done;
        }

        private static IEnumerator BackfillRoutine()
        {
            while (ZDOMan.instance == null || Game.instance == null) yield return null;
            yield return new WaitForSeconds(BackfillDelaySeconds);

            BackfillState state = new BackfillState();
            while (!state.Done)
            {
                BackfillChunk(state);
                yield return null;
            }

            if (state.Named > 0)
            {
                Save();
                Plugin.Log.LogInfo($"TortalPortal: named {state.Named} player(s) from marks left in the world ({state.Scanned} objects examined).");
            }
            else
            {
                Configuration.LogDebug($"Player-name backfill found nothing new in {state.Scanned} objects.");
            }
        }

        private static void BackfillChunk(BackfillState state)
        {
            try
            {
                if (ZDOMan.instance == null) { state.Done = true; return; }

                // Re-read every chunk rather than once at the top: an admin turning the export off partway
                // through a scan means the scan stops there, and nothing it has already found is written.
                if (!Configuration.enablePortalExport.Value) { state.Done = true; state.Named = 0; return; }

                if (state.Buckets == null)
                {
                    // Valheim 1.0 removed m_objectsByOutsideSector (and m_halfWidth / SectorToIndex with
                    // it), so the sector lists no longer add up to the whole world. m_objectsByID is the one
                    // container that still holds every ZDO regardless of sector, so the backfill walks that.
                    // It is a Dictionary, not a List<ZDO>[], so the bucket is a SNAPSHOT of it rather than a
                    // live list held by reference: a ZDO appearing or vanishing mid-walk still cannot trip
                    // the iteration (nothing below touches the dictionary again), and IsValid is re-checked
                    // per entry so one destroyed after the snapshot is skipped, not harvested. A ZDO created
                    // after this point is missed for the whole scan rather than only for the sectors already
                    // passed - which costs nothing: it was placed by somebody online, and the harvest has
                    // their name already. One bucket; the loop below still paces it at BackfillZdosPerFrame.
                    state.Buckets = new List<List<ZDO>> { new List<ZDO>(ZDOMan.instance.m_objectsByID.Values) };
                }

                int budget = BackfillZdosPerFrame;
                while (budget > 0 && state.Bucket < state.Buckets.Count)
                {
                    List<ZDO> bucket = state.Buckets[state.Bucket];
                    if (state.Index >= bucket.Count) { state.Bucket++; state.Index = 0; continue; }

                    ZDO zdo = bucket[state.Index++];
                    budget--;
                    state.Scanned++;
                    if (zdo == null || !zdo.IsValid()) continue;

                    // Wards, via PrivateArea.Setup.
                    if (Backfill(zdo, ZDOVars.s_creator, ZDOVars.s_creatorName)) state.Named++;

                    // Beds and tombstones, via Bed.RPC_SetOwner and TombStone.Setup. Both write the same two
                    // keys, so one read covers them and anything modded that follows the same convention.
                    if (Backfill(zdo, ZDOVars.s_owner, ZDOVars.s_ownerName)) state.Named++;
                }

                if (state.Bucket >= state.Buckets.Count) state.Done = true;
            }
            catch (Exception ex)
            {
                state.Done = true;
                Plugin.Log.LogError($"TortalPortal: the player-name backfill stopped early (non-fatal, names still arrive as their owners log in). Reason: {ex}");
            }
        }

        private static bool Backfill(ZDO zdo, int idKey, int nameKey)
        {
            long id = zdo.GetLong(idKey, 0L);
            if (id == 0) return false;

            // Gaps only. A mark in the world can be years old and carry a name its owner has since changed,
            // so it must never talk over the harvest, which reads the character being played right now.
            if (Names.ContainsKey(id)) return false;

            return RememberName(id, zdo.GetString(nameKey, string.Empty));
        }

        // ---- claiming, from the client ---------------------------------------------------------------------

        private static IEnumerator ClaimRoutine()
        {
            // A profile is not readable the instant ZNet starts, and a claim sent before the routed-RPC table
            // is up goes nowhere at all.
            while (Game.instance == null || ZNet.instance == null) yield return null;

            float elapsed = 0f;
            foreach (float at in ClaimAttemptSeconds)
            {
                yield return new WaitForSeconds(Mathf.Max(0f, at - elapsed));
                elapsed = at;
                SendClaim();
            }
        }

        private static void SendClaim()
        {
            try
            {
                if (Game.instance == null || ZNet.instance == null) return;

                // Not gated on enablePortalExport, which is where 1.4.0 lost this. That setting is
                // ServerSync'd, so a client reading it seconds into the session is as likely to be reading
                // its own local default as the server's answer - and a client that guessed wrong never tried
                // again. The server re-checks the real value in RPC_NameClaim before recording anything, so
                // asking here bought nothing and cost every client that booted faster than the sync landed.
                PlayerProfile profile = Game.instance.GetPlayerProfile();
                if (profile == null) return;

                long id = profile.GetPlayerID();
                string name = profile.GetName();
                if (id == 0 || string.IsNullOrWhiteSpace(name) || IsPlaceholder(name)) return;

                if (ZNet.instance.IsServer())
                {
                    // A listen host is its own server: nobody to send to, so it writes the pairing straight
                    // in - and it holds the authoritative copy of the setting, so checking it here is the
                    // one place that is not a race. A dedicated server reaches this line holding a profile
                    // nobody is playing: constructed, never loaded, named "Stranger" and carrying an ID
                    // generated at boot. That profile is precisely what 1.4.0 wrote into the register as its
                    // only entry, and it is the one caller that must not. The placeholder check above
                    // already turns it away; this says so out loud.
                    if (!ZNet.instance.IsDedicated() && Configuration.enablePortalExport.Value)
                        Record(0L, id, name);
                    return;
                }

                ZRoutedRpc.instance?.InvokeRoutedRPC(ClaimRpc, id, name);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not send this player's name to the server (non-fatal, their portals export under their numeric ID). Reason: {ex}");
            }
        }

        private static void RPC_NameClaim(long sender, long playerID, string name)
        {
            try
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                if (!Configuration.enablePortalExport.Value) return;
                if (playerID == 0 || string.IsNullOrWhiteSpace(name)) return;

                // A client can only be taken at its word here: nothing in the protocol proves the ID it claims
                // is its own. That is acceptable because this register decides a display label and nothing
                // else - the counts come from the portals' own ZDOs - so the worst a liar achieves is the
                // wrong name on somebody's row, never a change to what anyone is allowed to build. The
                // harvest reads the same pairing out of the world and overwrites this on its next pass.
                Record(sender, playerID, name);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(RPC_NameClaim)} failed (non-fatal). Reason: {ex}");
            }
        }

        // ---- recording -------------------------------------------------------------------------------------

        private static void Record(long peerUid, long playerID, string name)
        {
            if (playerID == 0) return;
            Load();

            // Kept even when the name turns out to be unusable. Who is online is a separate question from
            // what they are called, and the export answers both.
            if (peerUid != 0) Session[peerUid] = playerID;

            if (RememberName(playerID, name)) Save();
        }

        // The one gate every name passes through, whichever of the three sources found it. Returns whether
        // the register actually changed, so a returning player whose name has not moved costs no disk write.
        private static bool RememberName(long playerID, string name)
        {
            if (playerID == 0) return false;
            Load();

            name = (name ?? string.Empty).Trim();
            if (name.Length == 0 || IsPlaceholder(name)) return false;
            if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength);

            if (Names.TryGetValue(playerID, out string known) && known == name) return false;

            Names[playerID] = name;
            Configuration.LogDebug($"Player-name register: {playerID} is '{name}'.");
            return true;
        }

        private static bool IsPlaceholder(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && Placeholders.Contains(name.Trim());
        }

        // ---- reading ---------------------------------------------------------------------------------------

        // Null rather than a placeholder when the owner has not been seen since this feature was installed.
        // The export needs to tell "we have no name for them" apart from a player literally called "Unknown".
        public static string Get(long playerID)
        {
            Load();
            return Names.TryGetValue(playerID, out string name) ? name : null;
        }

        public static HashSet<long> OnlinePlayerIDs()
        {
            HashSet<long> online = new HashSet<long>();

            try
            {
                if (ZNet.instance == null) return online;

                // A listen host never appears in its own peer list, so its player is added by hand. A
                // dedicated server has no local player to add.
                if (!ZNet.instance.IsDedicated() && Game.instance != null)
                {
                    PlayerProfile profile = Game.instance.GetPlayerProfile();
                    long me = profile != null ? profile.GetPlayerID() : 0L;
                    if (me != 0) online.Add(me);
                }

                foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                {
                    if (peer == null) continue;
                    if (Session.TryGetValue(peer.m_uid, out long id)) online.Add(id);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not work out who is online (non-fatal, the export marks nobody). Reason: {ex}");
            }

            return online;
        }

        // ---- disk ------------------------------------------------------------------------------------------

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                if (!File.Exists(StorePath)) return;

                Dictionary<long, string> stored =
                    JsonConvert.DeserializeObject<Dictionary<long, string>>(File.ReadAllText(StorePath));
                if (stored == null) return;

                foreach (KeyValuePair<long, string> pair in stored)
                {
                    if (pair.Key == 0 || string.IsNullOrWhiteSpace(pair.Value)) continue;

                    // Drops the one row 1.4.0 was capable of writing on a dedicated server - the server's own
                    // empty profile - so an existing register cleans itself up on the next write rather than
                    // carrying "Stranger" forward for good.
                    if (IsPlaceholder(pair.Value)) continue;

                    Names[pair.Key] = pair.Value;
                }

                Configuration.LogDebug($"Player-name register: {Names.Count} names read from disk.");
            }
            catch (Exception ex)
            {
                _loadFailed = true;
                Plugin.Log.LogError($"TortalPortal: the player-name register could not be read, and will not be overwritten while that is true - fix or remove {StorePath} to start a fresh one. Reason: {ex}");
            }
        }

        private static void Save()
        {
            // A register that failed to load is empty for a reason that has nothing to do with the names in
            // it. Writing over the file at that point would turn a bad read into a real loss.
            if (_loadFailed) return;

            try
            {
                Directory.CreateDirectory(StoreDir);

                string tmp = StorePath + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(Names, Formatting.Indented));

                // Replace rather than delete-then-move: anything reading this file at the wrong moment gets
                // the previous register rather than no register.
                if (File.Exists(StorePath)) File.Replace(tmp, StorePath, null);
                else File.Move(tmp, StorePath);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: the player-name register could not be written - names will be asked for again next session. Reason: {ex}");
            }
        }
    }
}
