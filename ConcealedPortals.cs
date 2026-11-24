using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TortalPortal
{
    // Gates the world was seeded with that nobody has been told about.
    //
    // A concealed gate is an ordinary portal ZDO standing in the wilderness with one extra mark on it. It is
    // never published in the portal index, so no client is ever sent its position, its name, or the fact that
    // it exists at all: the destination window and the persistent map markers are both built from that index,
    // and what is not in the index cannot be listed, marked, filtered, favourited or travelled to. The only
    // way to learn of one is to walk into it and press E - and then the server, the one peer that knows where
    // they all are, hands that single gate to that single player.
    //
    // WHY THE INDEX CANNOT CARRY THEM. PortalRegistry publishes through one ServerSync value: a single blob,
    // broadcast identically to every peer as part of the login handshake. There is no per-player variant of
    // it. A concealed gate riding in that blob would land in every client's memory at login, and "concealed"
    // would mean no more than "our own window declines to draw it" - one config edit, one rival mod or one
    // teleport-to-coordinate away from nothing. Withholding them from the blob and sending each player their
    // own finds over a routed RPC is the only shape where the secret is kept by the machine that owns it.
    //
    // WHERE DISCOVERY IS RECORDED. On the gate's own ZDO (PortalKeys.Found), which means it persists in the
    // world save with no side file, survives a client reinstall, and cannot be edited by the player it is
    // about. Favorites made the opposite choice for good reason - which portals somebody likes is nobody
    // else's business - but a discovery is a fact about the world, and the world file is where facts about it
    // belong. A client standing close enough to hold that ZDO can read the list off it, which costs nothing:
    // they are at the gate, they have already found it, and the list says nothing whatsoever about any OTHER
    // concealed gate in the world.
    //
    // WHY INTERACTION AND NOT PROXIMITY. Walking past inside render distance is not finding something. E on
    // the gate is deliberate and unambiguous, and it gives the server something to check the claim against:
    // the claim is granted only when the claiming player's own character ZDO is genuinely standing beside the
    // gate they named.
    //
    // NOT TEST LITTER, AND DELIBERATELY NOT MARKED AS SUCH. These carry their own kind and never the
    // "TortalScattered" marker, because tortal_scatterclear sweeps everything carrying that and the health
    // scan reports it as leftovers to tidy. A seeded world feature would have been quietly deleted by the
    // first admin who ran the clear after a test.
    [HarmonyPatch]
    public static class ConcealedPortals
    {
        // ---- tuning ---------------------------------------------------------------------------------------

        // How close a claiming player has to be to the gate they named. Generous on purpose: the server checks
        // against the position on their character ZDO, which is a replicated value a second or two behind a
        // moving player, and refusing a legitimate find is a far worse failure than granting one to somebody
        // who ran past and pressed E from twenty metres.
        private const float ClaimRange = 30f;

        // Gates are landmarks, not litter - far enough apart that finding one is not finding three.
        private const float MinSeparation = 128f;

        // Room the seeder asks for around a gate, edge to edge, as a preference rather than a requirement -
        // PortalPlacement walks a ladder of smaller steps when the ground cannot offer this.
        private const float SeedClearance = 6f;

        // How far from the sampled point the placement scan may settle the gate.
        private const float SeedSettleRadius = 24f;

        private const int MaxPerCommand = 200;
        private const int SeedAttempts = 60;

        // How often the server looks for a player whose found-set has changed - a login, mostly, since a
        // discovery pushes immediately on its own.
        private const float PushPollSeconds = 5f;

        private const byte RevealFormatVersion = 1;

        // ---- the wire -------------------------------------------------------------------------------------
        //
        // Four names, not the six an obvious reading would want: the seed, clear and status commands all do
        // server-side work on behalf of an admin and all answer with a line of text, so they share one
        // request channel and one reply channel. That puts the adminlist check in exactly one place.
        private const string ClaimRpc = "TortalPortal_ConcealedClaim";
        private const string RevealRpc = "TortalPortal_ConcealedReveal";
        private const string CommandRpc = "TortalPortal_ConcealedCommand";
        private const string ReportRpc = "TortalPortal_ConcealedReport";

        private static bool _registered;
        private static Coroutine _serverRoutine;
        private static Coroutine _seeding;

        // What was last pushed to each connected peer, keyed by peer uid rather than player ID. A peer uid is a
        // CONNECTION and new every session, which is exactly what is wanted here: a player who reconnects has
        // forgotten everything they were shown, so they must look like somebody who has been sent nothing.
        private static readonly Dictionary<long, long> Pushed = new Dictionary<long, long>();

        // The gate this client has just claimed, so the window can be opened on it the moment the reveal lands
        // rather than making the player press E a second time.
        private static TeleportWorld _pendingClaim;

        // ---- identity -------------------------------------------------------------------------------------

        public static bool IsConcealed(ZDO zdo)
        {
            if (zdo == null || !zdo.IsValid()) return false;
            return zdo.GetString(PortalKeys.Kind, "") == PortalKeys.KindConcealed;
        }

        // Has this player found this gate? FoundAll is read first and separately: turning
        // ConcealedDiscoveryShared on reveals a gate to everybody the next time anyone touches it, and does not
        // retroactively publish what individuals had already found on their own.
        public static bool IsFoundBy(ZDO zdo, long playerId)
        {
            if (zdo == null || !zdo.IsValid()) return false;
            if (zdo.GetBool(PortalKeys.FoundAll, false)) return true;
            if (playerId == 0L) return false;

            string csv = zdo.GetString(PortalKeys.Found, "") ?? "";
            if (csv.Length == 0) return false;

            foreach (string part in csv.Split(','))
            {
                if (part.Length == 0) continue;
                if (long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) && id == playerId) return true;
            }
            return false;
        }

        // A concealed gate this client has not been handed yet: no menu on approach, no destination row, and a
        // hover prompt instead of a portal's ordinary text. Asked of the CLIENT's own knowledge, which is the
        // only thing it has - the ZDO in front of it says who has found it, but the answer that matters is
        // whether the server has sent this gate over, because that is what puts it in the list.
        public static bool IsUndiscoveredHere(ZDO zdo)
        {
            if (!IsConcealed(zdo)) return false;
            return !PortalRegistry.IsConcealedKnown(zdo.m_uid);
        }

        private static bool Enabled => Configuration.enableConcealedPortals == null || Configuration.enableConcealedPortals.Value;

        private static bool Shared => Configuration.concealedDiscoveryShared != null && Configuration.concealedDiscoveryShared.Value;

        // ---- lifecycle ------------------------------------------------------------------------------------

        public static void OnZNetStart()
        {
            try
            {
                RegisterCommands();

                if (ZRoutedRpc.instance != null)
                {
                    // Registered on every peer, server included: a listen host is both ends of this.
                    ZRoutedRpc.instance.Register<ZDOID>(ClaimRpc, RPC_Claim);
                    ZRoutedRpc.instance.Register<ZPackage>(RevealRpc, RPC_Reveal);
                    ZRoutedRpc.instance.Register<string>(CommandRpc, RPC_Command);
                    ZRoutedRpc.instance.Register<string>(ReportRpc, RPC_Report);
                }

                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

                // ZNet.Start runs again on every world load; these coroutines live on the plugin object, which
                // does not.
                if (_serverRoutine != null && Plugin.Instance != null) Plugin.Instance.StopCoroutine(_serverRoutine);
                _serverRoutine = Plugin.Instance.StartCoroutine(ServerRoutine());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: the concealed gates could not start (non-fatal, they stay hidden and nothing is seeded). Reason: {ex}");
            }
        }

        public static void Reset()
        {
            try
            {
                if (Plugin.Instance != null)
                {
                    if (_serverRoutine != null) Plugin.Instance.StopCoroutine(_serverRoutine);
                    if (_seeding != null) Plugin.Instance.StopCoroutine(_seeding);
                }
                _serverRoutine = null;
                _seeding = null;
                Pushed.Clear();
                _pendingClaim = null;

                // A world change re-mints every ZDOID, so everything this client was shown names nothing now.
                PortalRegistry.ClearConcealed();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(Reset)} failed for the concealed gates (non-fatal). Reason: {ex}");
            }
        }

        private static IEnumerator ServerRoutine()
        {
            // The world's ZDOs stream in after ZNet.Start. Counting or locking gates before that arrives would
            // find none and say so.
            while (ZDOMan.instance == null || Game.instance == null) yield return null;
            yield return new WaitForSeconds(4f);

            RelockAndReport();

            while (true)
            {
                PushChanged();
                yield return new WaitForSeconds(PushPollSeconds);
            }
            // ReSharper disable once IteratorNeverReturns
        }

        // ---- server: the standing gates -------------------------------------------------------------------

        public static List<ZDO> AllConcealed()
        {
            List<ZDO> found = new List<ZDO>();
            foreach (ZDO zdo in PlacementPatch.GetAllPortals(includeConcealed: true))
            {
                if (IsConcealed(zdo)) found.Add(zdo);
            }
            return found;
        }

        // PortalMint locks a gate against removal as it mints it, but that lock is an in-memory set of ZDOIDs
        // and this server did not mint the gates a previous run seeded. Without this the hammer refusal and the
        // RPC_DestroyZDO veto would both apply to gates seeded this session and to nothing else standing.
        private static void RelockAndReport()
        {
            try
            {
                List<ZDO> gates = AllConcealed();
                if (gates.Count == 0) return;

                int found = 0;
                foreach (ZDO zdo in gates)
                {
                    PortalGuard.Lock(zdo.m_uid);
                    if (zdo.GetBool(PortalKeys.FoundAll, false) || (zdo.GetString(PortalKeys.Found, "") ?? "").Length > 0) found++;
                }

                Plugin.Log.LogInfo($"TortalPortal: {gates.Count} concealed gate(s) standing, {found} of them found by somebody, all held against removal. Discovery is {(Shared ? "shared - the first finder reveals a gate for everyone" : "per-player")}.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not take stock of the concealed gates (non-fatal). Reason: {ex}");
            }
        }

        // ---- server: handing gates to the players who found them ------------------------------------------

        private static void PushChanged()
        {
            try
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

                List<ConnectedCharacter> here = ServerPeers.All();

                // A peer that has gone costs a dictionary entry that would otherwise sit there until the world
                // changed - and, on the small chance its uid is reused, would suppress the new player's push.
                if (Pushed.Count > 0)
                {
                    List<long> gone = null;
                    foreach (KeyValuePair<long, long> pair in Pushed)
                    {
                        bool stillHere = false;
                        foreach (ConnectedCharacter cc in here)
                        {
                            if (cc.PeerId == pair.Key) { stillHere = true; break; }
                        }
                        if (!stillHere) (gone ?? (gone = new List<long>())).Add(pair.Key);
                    }
                    if (gone != null) foreach (long key in gone) Pushed.Remove(key);
                }

                if (!Enabled) return;

                PushAll(force: false);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: the concealed-gate push failed (non-fatal, it runs again on the next poll). Reason: {ex}");
            }
        }

        // Deliberately not gated on Enabled: the one caller that forces it is the clear, and a client left
        // holding destinations that have just been deleted from the world is worse than any dial.
        private static void PushAll(bool force)
        {
            // The standing gates read ONCE for the whole round rather than per player: AllConcealed walks
            // ZDOMan's portal registry and builds two lists doing it, and on a busy table that is the same work
            // repeated for everybody on a five-second timer.
            List<ZDO> gates = AllConcealed();
            foreach (ConnectedCharacter who in ServerPeers.All()) PushTo(who, gates, force);
        }

        private static void PushTo(ConnectedCharacter who, List<ZDO> gates, bool force)
        {
            try
            {
                long playerId = who.PlayerId;
                // 0 means the owning client's Player.SetPlayerID has not run yet, which is normal for the first
                // moments of a connection. Nothing is sent rather than an empty list, so the next poll tries again.
                if (playerId == 0L) return;

                List<ZDO> mine = new List<ZDO>();
                foreach (ZDO zdo in gates)
                {
                    if (IsFoundBy(zdo, playerId)) mine.Add(zdo);
                }

                long signature = Signature(mine);
                if (!force && Pushed.TryGetValue(who.PeerId, out long last) && last == signature) return;

                ZPackage pkg = EncodeReveal(mine);

                // A listen host's own player is the same process: there is nothing to send it to.
                if (who.IsLocal) Apply(pkg);
                else ZRoutedRpc.instance?.InvokeRoutedRPC(who.PeerId, RevealRpc, pkg);

                // Recorded only now. Set before the send, a send that threw would leave this peer marked as
                // told and the poll would skip them from then on - the one failure mode a poll should heal.
                Pushed[who.PeerId] = signature;

                Configuration.LogDebug($"Handed {mine.Count} concealed gate(s) to {(who.Name.Length > 0 ? who.Name : who.PlayerId.ToString(CultureInfo.InvariantCulture))}.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not hand the concealed gates to {who.Name} (non-fatal). Reason: {ex}");
            }
        }

        // Cheap enough to run per player per poll, and it changes whenever a gate is added to somebody's set,
        // removed from the world, renamed, re-iconed or moved.
        private static long Signature(List<ZDO> gates)
        {
            long h = 17L + gates.Count;
            foreach (ZDO zdo in gates)
            {
                unchecked
                {
                    h = (h * 31L) + zdo.m_uid.GetHashCode();
                    h = (h * 31L) + (zdo.GetString(ZDOVars.s_tag, "") ?? "").GetHashCode();
                    h = (h * 31L) + zdo.GetPosition().GetHashCode();
                }
            }
            return h;
        }

        private static ZPackage EncodeReveal(List<ZDO> gates)
        {
            ZPackage pkg = new ZPackage();
            pkg.Write(RevealFormatVersion);
            pkg.Write(gates.Count);
            foreach (ZDO zdo in gates)
            {
                pkg.Write(zdo.m_uid);
                pkg.Write(zdo.GetString(ZDOVars.s_tag) ?? "");
                pkg.Write(zdo.GetPosition());
                pkg.Write(zdo.GetRotation());
                // Whether a PIN exists, never the PIN itself - the same line the index holds.
                pkg.Write(!string.IsNullOrEmpty(zdo.GetString("PortalPIN")));
                pkg.Write(zdo.GetString(TortalPinIcon.ZdoKey) ?? "");
            }
            return pkg;
        }

        // ---- server: granting a claim ---------------------------------------------------------------------

        private static void RPC_Claim(long sender, ZDOID gate)
        {
            try
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                if (!Enabled) return;

                foreach (ConnectedCharacter who in ServerPeers.All())
                {
                    if (who.PeerId != sender) continue;
                    Grant(gate, who);
                    return;
                }

                // Not "refused": a claim from a peer whose character ZDO has not arrived yet is a claim we
                // cannot resolve, and the two must not be collapsed. The player can press E again.
                Plugin.Log.LogWarning($"TortalPortal: a concealed-gate claim came from peer {sender}, whose character the server cannot resolve yet - ignored.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(RPC_Claim)} failed (non-fatal). Reason: {ex}");
            }
        }

        // The whole of the authority: this runs only on the server, and everything it needs - which gate, where
        // it stands, where the claimant stands, who the claimant is - it reads for itself. Nothing in the claim
        // is trusted but the ZDOID, and that is checked against both.
        private static void Grant(ZDOID gateId, ConnectedCharacter who)
        {
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(gateId) : null;
            if (zdo == null || !zdo.IsValid()) return;
            if (!IsConcealed(zdo)) return;

            long playerId = who.PlayerId;
            if (playerId == 0L)
            {
                Plugin.Log.LogWarning("TortalPortal: a concealed-gate claim arrived from a character carrying no player ID - ignored, it can be claimed again.");
                return;
            }

            float distance = Vector3.Distance(who.Position, zdo.GetPosition());
            if (distance > ClaimRange)
            {
                Plugin.Log.LogWarning($"TortalPortal: refused a concealed-gate claim from {who.Name} - the gate is {distance:0}m away, further than the {ClaimRange:0}m a player can be standing at it from.");
                return;
            }

            if (IsFoundBy(zdo, playerId))
            {
                // Already theirs. Re-pushed rather than dropped: the ordinary way to get here is a player whose
                // reveal was lost or who is on a client that has forgotten it, and pressing E again should fix
                // that rather than do nothing.
                PushTo(who, AllConcealed(), force: true);
                return;
            }

            string tag = zdo.GetString(ZDOVars.s_tag, "") ?? "";
            bool shared = Shared;

            ServerPeers.ClaimAndWrite(zdo, z =>
            {
                if (shared)
                {
                    z.Set(PortalKeys.FoundAll, true);
                }
                else
                {
                    string csv = z.GetString(PortalKeys.Found, "") ?? "";
                    z.Set(PortalKeys.Found, csv.Length == 0
                        ? playerId.ToString(CultureInfo.InvariantCulture)
                        : csv + "," + playerId.ToString(CultureInfo.InvariantCulture));
                }
            });

            Plugin.Log.LogInfo($"TortalPortal: {who.Name} found the concealed gate '{tag}' at {zdo.GetPosition().x:0},{zdo.GetPosition().z:0}{(shared ? " - revealed to everyone" : "")}.");
            ServerPeers.Toast(who, $"Gate found: {tag}", center: true);

            // Shared discovery means every other player's set changed too, so everyone is re-checked; otherwise
            // only the finder needs telling, and immediately rather than on the next poll.
            if (shared) PushAll(force: false);
            else PushTo(who, AllConcealed(), force: true);
        }

        // ---- client: receiving, and claiming --------------------------------------------------------------

        private static void RPC_Reveal(long sender, ZPackage pkg)
        {
            try
            {
                Apply(pkg);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(RPC_Reveal)} failed - the gates already known stay known. Reason: {ex}");
            }
        }

        private static void Apply(ZPackage pkg)
        {
            if (pkg == null) return;
            pkg.SetPos(0);

            byte version = pkg.ReadByte();
            if (version != RevealFormatVersion)
            {
                Plugin.Log.LogWarning($"TortalPortal: a concealed-gate reveal arrived in format {version}, not {RevealFormatVersion} - the server is running a different version of the mod. Ignored.");
                return;
            }

            List<PortalRegistry.Entry> entries = new List<PortalRegistry.Entry>();
            int count = pkg.ReadInt();
            for (int i = 0; i < count; i++)
            {
                PortalRegistry.Entry e = new PortalRegistry.Entry();
                e.Id = pkg.ReadZDOID();
                e.Tag = pkg.ReadString();
                e.Pos = pkg.ReadVector3();
                e.Rot = pkg.ReadQuaternion();
                e.Locked = pkg.ReadBool();
                e.Icon = pkg.ReadString();
                // Nobody built these. The owner row prints "Unknown", which is the honest answer, and the
                // configuration window stays admin-only because s_creator on the gate is 0.
                e.Creator = "";
                e.CreatorId = 0L;
                e.Concealed = true;
                entries.Add(e);
            }

            PortalRegistry.SetConcealed(entries);

            // The gate the player just touched, opened the moment it becomes a place they can travel from,
            // rather than making them press E a second time for a window that was not there the first time.
            if (_pendingClaim != null)
            {
                TeleportWorld portal = _pendingClaim;
                _pendingClaim = null;
                try
                {
                    ZNetView nview = portal.GetComponent<ZNetView>();
                    if (nview != null && nview.IsValid() && PortalRegistry.IsConcealedKnown(nview.GetZDO().m_uid))
                    {
                        PortalUIManager.ShowUI(portal);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"TortalPortal: the gate was found but its window could not be opened (non-fatal, walk away and back). Reason: {ex.Message}");
                }
            }
        }

        // E on a gate this client has never been handed. Returns the Interact result, which is true either way:
        // the press was consumed by us and vanilla must not also run its tag flow on it.
        public static bool Claim(TeleportWorld portal, ZDO zdo)
        {
            try
            {
                if (!Enabled) return true;

                _pendingClaim = portal;

                // A listen host IS the authority, so its own claim is granted here rather than posted to
                // itself: a routed RPC to one's own peer works, but it would make the local player's find
                // depend on a round trip through a socket that is not there.
                if (ZNet.instance != null && ZNet.instance.IsServer())
                {
                    foreach (ConnectedCharacter who in ServerPeers.All())
                    {
                        if (!who.IsLocal) continue;
                        Grant(zdo.m_uid, who);
                        return true;
                    }
                }

                ZRoutedRpc.instance?.InvokeRoutedRPC(ClaimRpc, zdo.m_uid);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not claim a concealed gate (non-fatal, press again). Reason: {ex}");
                return true;
            }
        }

        // What an unfound gate says when looked at. Vanilla's own text would be the tag and "connected", which
        // would hand over the name of a place the player has not found yet.
        [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.GetHoverText))]
        [HarmonyPrefix]
        public static bool TeleportWorld_GetHoverText_Prefix(TeleportWorld __instance, ref string __result)
        {
            try
            {
                if (!Enabled) return true;

                ZNetView nview = __instance.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) return true;
                if (!IsUndiscoveredHere(nview.GetZDO())) return true;

                __result = Localization.instance.Localize("Silent gate\n[<color=yellow><b>$KEY_Use</b></color>] Touch the runes");
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(TeleportWorld_GetHoverText_Prefix)} failed, showing vanilla text. Reason: {ex}");
                return true;
            }
        }

        // ---- seeding ---------------------------------------------------------------------------------------

        // replyTo is the peer that asked, or 0 when the command was typed at the server's own console. Seeding
        // takes a frame per gate and answers when it is done, so the admin who asked from a client would
        // otherwise get "seeding 25 gates" and never hear whether any of them landed.
        private static string RunSeed(int count, float radius, long replyTo)
        {
            if (ZDOMan.instance == null || Game.instance == null) return "tortal_scatterconcealed: no world loaded.";
            if (!GroundProbe.Ready) return "tortal_scatterconcealed: the world generator is not ready yet.";
            if (_seeding != null) return "tortal_scatterconcealed: already seeding - let it finish first.";
            if (!Enabled) return "tortal_scatterconcealed: EnableConcealedPortals is off, so nothing would ever be discovered. Turn it on first.";

            count = Mathf.Clamp(count, 1, MaxPerCommand);
            radius = Mathf.Clamp(radius, 100f, 10000f);

            _seeding = Plugin.Instance.StartCoroutine(SeedRoutine(count, radius, replyTo));
            return $"tortal_scatterconcealed: seeding {count} concealed gate(s) within {radius:0}m. The log names each one.";
        }

        private static IEnumerator SeedRoutine(int count, float radius, long replyTo)
        {
            // Everything standing now: positions to stay away from, and tags no new gate may duplicate -
            // vanilla connects two portals whose tags match exactly, so a seeded gate sharing a name with a
            // player's would quietly hook the two together.
            List<Vector3> existing = new List<Vector3>();
            HashSet<string> tags = new HashSet<string>(StringComparer.Ordinal);
            foreach (ZDO zdo in PlacementPatch.GetAllPortals(includeCorpseGates: true, includeConcealed: true))
            {
                if (zdo == null || !zdo.IsValid()) continue;
                existing.Add(zdo.GetPosition());
                string tag = zdo.GetString(ZDOVars.s_tag, "") ?? "";
                if (tag.Length > 0) tags.Add(tag);
            }

            GroundProbe probe = new GroundProbe();
            float water = GroundProbe.WaterLevel;

            int made = 0, skipped = 0;
            int nextNumber = 1;

            for (int i = 0; i < count; i++)
            {
                // The whole of one gate's work sits in a method with its own try/catch rather than inline here,
                // because C# forbids a catch clause around a yield and a coroutine that throws simply stops -
                // leaving _seeding set for the rest of the session and every later seed command refused.
                if (SeedOne(probe, water, radius, existing, tags, ref nextNumber)) made++;
                else skipped++;

                // One full placement search per gate is a couple of thousand ground probes. Seeding is
                // somebody typing a command, not a tick, so a frame each is the right trade.
                yield return null;
            }

            _seeding = null;

            string summary = $"tortal_scatterconcealed: seeded {made} concealed gate(s)" +
                             (skipped > 0 ? $", {skipped} spot(s) rejected as water, lava or too near another portal." : ".") +
                             " They are in nobody's destination list until somebody walks up and presses E on one.";
            Plugin.Log.LogInfo($"TortalPortal: {summary}");
            if (replyTo != 0L) ZRoutedRpc.instance?.InvokeRoutedRPC(replyTo, ReportRpc, summary);
            else if (Console.instance != null) Console.instance.Print(summary);

            // Nothing is published. The index never hears about these, which is the whole point, and the
            // removal locks were taken as each one was minted.
        }

        private static bool SeedOne(GroundProbe probe, float water, float radius, List<Vector3> existing, HashSet<string> tags, ref int nextNumber)
        {
            try
            {
                if (!FindSpot(probe, water, radius, existing, out Vector3 sampled)) return false;

                PortalPlacement.Placement spot = PortalPlacement.Find(sampled, 0.5f, SeedSettleRadius, SeedClearance, ZDOID.None, minRoom: PortalPlacement.DefaultMinRoom);

                Heightmap.Biome biome = Heightmap.Biome.None;
                try { biome = WorldGenerator.instance.GetBiome(spot.Position.x, spot.Position.z); } catch { }

                string tag;
                do
                {
                    tag = $"Forgotten {biome} Gate {nextNumber:000}";
                    nextNumber++;
                } while (tags.Contains(tag));
                tags.Add(tag);

                // owner 0: nobody built it, so nobody but an admin may rename or lock it. immune and
                // lockRemoval both: a landmark the world was seeded with is not one gate's worth of wood to
                // whoever finds it first.
                ZDO zdo = PortalMint.Create(spot.Position, spot.Rotation, tag, PortalKeys.KindConcealed, 0L, immune: true, lockRemoval: true);
                if (zdo == null) return false;

                existing.Add(spot.Position);
                Plugin.Log.LogInfo($"TortalPortal: seeded concealed gate '{tag}' - {spot.Describe()}");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: one concealed gate could not be seeded (the rest carry on). Reason: {ex}");
                return false;
            }
        }

        // A random spot on dry land, far from every portal already standing.
        //
        // Height comes from GroundProbe, not from WorldGenerator.GetHeight the way tortal_scatter does: bare
        // GetHeight is the un-blended single-biome height and at a biome border it sits metres above or below
        // the mesh a player will actually stand on. Test litter nobody walks to can afford that. A gate whose
        // entire purpose is to be walked up to cannot.
        private static bool FindSpot(GroundProbe probe, float water, float radius, List<Vector3> existing, out Vector3 pos)
        {
            pos = Vector3.zero;

            for (int attempt = 0; attempt < SeedAttempts; attempt++)
            {
                // sqrt on the radius spreads the picks evenly across the disc instead of crowding the middle.
                float ang = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float r = Mathf.Sqrt(UnityEngine.Random.value) * radius;
                float x = Mathf.Cos(ang) * r;
                float z = Mathf.Sin(ang) * r;

                float y = probe.HeightAt(x, z);
                if (y <= water + 1f) continue;

                Vector3 candidate = new Vector3(x, y, z);
                if (GroundProbe.IsLava(candidate)) continue;

                bool crowded = false;
                foreach (Vector3 p in existing)
                {
                    if (Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(x, 0f, z)) < MinSeparation) { crowded = true; break; }
                }
                if (crowded) continue;

                pos = candidate;
                return true;
            }
            return false;
        }

        // ---- clearing and status --------------------------------------------------------------------------

        private static string RunClear()
        {
            if (ZDOMan.instance == null) return "tortal_concealedclear: no world loaded.";

            int removed = 0;
            foreach (ZDO zdo in AllConcealed())
            {
                try
                {
                    // Destroy releases the removal lock first, so the mod's own removal is not refused by the
                    // mod's own veto.
                    PortalMint.Destroy(zdo);
                    removed++;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"TortalPortal: could not remove concealed gate {zdo.m_uid}. Reason: {ex}");
                }
            }

            // Everyone's list has just lost everything it held from here.
            Pushed.Clear();
            PushAll(force: true);

            string summary = $"tortal_concealedclear: removed {removed} concealed gate(s) across the whole world.";
            Plugin.Log.LogInfo($"TortalPortal: {summary}");
            return summary;
        }

        private static string RunStatus()
        {
            if (ZDOMan.instance == null) return "tortal_concealed: no world loaded.";

            List<ZDO> gates = AllConcealed();
            int shared = 0;
            Dictionary<long, int> byPlayer = new Dictionary<long, int>();

            foreach (ZDO zdo in gates)
            {
                if (zdo.GetBool(PortalKeys.FoundAll, false)) { shared++; continue; }
                string csv = zdo.GetString(PortalKeys.Found, "") ?? "";
                foreach (string part in csv.Split(','))
                {
                    if (part.Length == 0) continue;
                    if (!long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)) continue;
                    byPlayer.TryGetValue(id, out int had);
                    byPlayer[id] = had + 1;
                }
            }

            string text = $"tortal_concealed: {gates.Count} gate(s) standing, {shared} revealed to everyone. Discovery is {(Shared ? "shared" : "per-player")}, the feature is {(Enabled ? "on" : "OFF")}.";
            foreach (KeyValuePair<long, int> pair in byPlayer)
            {
                string who = PlayerNames.Get(pair.Key) ?? pair.Key.ToString(CultureInfo.InvariantCulture);
                text += $"\n  {who}: {pair.Value} found";
            }
            return text;
        }

        // ---- the admin channel ----------------------------------------------------------------------------

        private static void RPC_Command(long sender, string request)
        {
            try
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

                if (!ServerPeers.IsAdminPeer(sender, out string host))
                {
                    Plugin.Log.LogWarning($"TortalPortal: refused a concealed-gate command from {host} - not on the adminlist.");
                    ZRoutedRpc.instance?.InvokeRoutedRPC(sender, ReportRpc, "The server refused - you are not on its adminlist.");
                    return;
                }

                string[] parts = (request ?? "").Split(' ');
                string verb = parts.Length > 0 ? parts[0] : "";
                string answer;

                switch (verb)
                {
                    case "seed":
                        int count = 25;
                        float radius = 4000f;
                        if (parts.Length > 1) int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out count);
                        if (parts.Length > 2) float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out radius);
                        answer = RunSeed(count, radius, sender);
                        break;
                    case "clear":
                        answer = RunClear();
                        break;
                    default:
                        answer = RunStatus();
                        break;
                }

                Plugin.Log.LogInfo($"TortalPortal: concealed-gate command '{request}' from {host}.");
                ZRoutedRpc.instance?.InvokeRoutedRPC(sender, ReportRpc, answer);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(RPC_Command)} failed (non-fatal). Reason: {ex}");
            }
        }

        private static void RPC_Report(long sender, string text)
        {
            try
            {
                // The console is the one the person typing can see; the log alone would make a refusal
                // indistinguishable from a command that does not exist.
                if (Console.instance != null) Console.instance.Print(text);
                Plugin.Log.LogInfo($"TortalPortal: {text}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(RPC_Report)} failed (non-fatal). Reason: {ex}");
            }
        }

        // ---- console commands ------------------------------------------------------------------------------
        //
        // isCheat: FALSE, unlike tortal_scatter's. ConsoleCommand.IsValid tests IsCheatsEnabled(), which returns
        // ZNet.IsServer() - so a cheat command is unrunnable by every admin on a dedicated server, which is
        // precisely where seeding a world matters. The gate that replaces it is the server's own adminlist,
        // checked in RPC_Command, which is the only machine holding the list in the first place.
        [HarmonyPatch(typeof(Terminal), "InitTerminal")]
        [HarmonyPostfix]
        public static void Terminal_InitTerminal_Postfix()
        {
            RegisterCommands();
        }

        private static void RegisterCommands()
        {
            try
            {
                if (_registered) return;
                _registered = true;

                // Registration only writes into Terminal's static command dictionary, so this works before any
                // Terminal exists and on a headless server where InitTerminal may never run at all. Called from
                // OnZNetStart as well for exactly that reason.
                new Terminal.ConsoleCommand("tortal_scatterconcealed",
                    "[count] [radius] - seeds the world with concealed gates (default 25 within 4000m): real portals that appear in nobody's destination list or map until a player walks up and presses E on one. Admin only, and the work happens on the server.",
                    delegate (Terminal.ConsoleEventArgs args) { RunLocalOrRoute(args, "seed", args.Length > 1 ? args[1] : "", args.Length > 2 ? args[2] : ""); },
                    isCheat: false, isNetwork: true, onlyServer: false);

                new Terminal.ConsoleCommand("tortal_concealedclear",
                    "removes every concealed gate in the world, found or not, leaving ordinary portals and corpse gates alone. Admin only.",
                    delegate (Terminal.ConsoleEventArgs args) { RunLocalOrRoute(args, "clear", "", ""); },
                    isCheat: false, isNetwork: true, onlyServer: false);

                new Terminal.ConsoleCommand("tortal_concealed",
                    "how many concealed gates are standing and who has found them. Admin only for the server's answer; a client also prints what it has been shown.",
                    delegate (Terminal.ConsoleEventArgs args) { RunLocalOrRoute(args, "status", "", ""); },
                    isCheat: false, isNetwork: true, onlyServer: false);

                Plugin.Log.LogInfo("TortalPortal: console commands tortal_scatterconcealed / tortal_concealedclear / tortal_concealed registered.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not register the concealed-gate console commands. Reason: {ex}");
            }
        }

        // A console command runs on the machine it was TYPED into, which for a dedicated server is the admin's
        // own client - where there is no world to seed and no ZDO but the handful loaded underfoot. So the
        // server-side half is asked for over the wire and its answer printed when it comes back.
        private static void RunLocalOrRoute(Terminal.ConsoleEventArgs args, string verb, string a, string b)
        {
            try
            {
                if (verb == "status")
                {
                    Say(args, $"tortal_concealed: this client has been shown {PortalRegistry.ConcealedCount} gate(s).");
                }

                if (ZNet.instance == null)
                {
                    Say(args, "No world loaded.");
                    return;
                }

                if (ZNet.instance.IsServer())
                {
                    string answer = verb == "seed"
                        ? RunSeed(ParseInt(a, 25), ParseFloat(b, 4000f), 0L)
                        : verb == "clear" ? RunClear() : RunStatus();
                    Say(args, answer);
                    return;
                }

                string request = verb == "seed" ? $"seed {ParseInt(a, 25).ToString(CultureInfo.InvariantCulture)} {ParseFloat(b, 4000f).ToString("0.###", CultureInfo.InvariantCulture)}" : verb;
                ZRoutedRpc.instance?.InvokeRoutedRPC(CommandRpc, request);
                Say(args, "Asked the server - its answer prints here when it arrives.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: a concealed-gate command failed. Reason: {ex}");
                Say(args, "That failed - see the log.");
            }
        }

        // Invariant culture on both, because a client with a comma decimal separator would otherwise send
        // "seed 25 4000,5" and the server would split it into an extra argument.
        private static int ParseInt(string s, int fallback)
        {
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
        }

        private static float ParseFloat(string s, float fallback)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;
        }

        // A dedicated server has a Terminal but nobody reading it, so everything is mirrored to the log.
        private static void Say(Terminal.ConsoleEventArgs args, string msg)
        {
            try { if (args != null && args.Context != null) args.Context.AddString(msg); } catch { }
            Plugin.Log.LogInfo($"TortalPortal: {msg}");
        }
    }
}
