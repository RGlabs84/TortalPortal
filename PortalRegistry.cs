using System;
using System.Collections;
using System.Collections.Generic;
using ServerSync;
using UnityEngine;

namespace TortalPortal
{
    // Where the destination list comes from.
    //
    // ZDOMan only holds the ZDOs a peer has actually been sent, and the server only sends what falls inside a
    // player's active area. On a dedicated server that means a client's ZDOMan knows about the handful of
    // portals it is standing next to and nothing else - so scanning it locally, which is what 1.0.0 did,
    // produced an empty destination window on any server whose portals were not already underfoot.
    //
    // The server is the only peer holding the whole world, so it builds the index and publishes it; clients
    // only ever read. The scan runs once at startup, which is what gets a world that already had portals
    // long before this mod was installed to populate at all, and then repeats to pick up changes.
    public static class PortalRegistry
    {
        public sealed class Entry
        {
            public ZDOID Id;
            public string Tag;
            public string Creator;

            // The creator's numeric player ID, straight off ZDOVars.s_creator. Kept alongside Creator rather
            // than folded into it: Creator is a display name the server looked up and may not have, while this
            // is the mark the portal itself carries and always has. It is what the per-player cap counts and
            // what the export keys on, and it must stay a number for both - two players can share a name, and
            // one player can change theirs.
            public long CreatorId;

            public Vector3 Pos;
            public Quaternion Rot;
            public bool Locked;

            // The custom map icon this portal has been given, or empty for "whatever the player looking at the
            // map has chosen". Carried in the index rather than read from the portal's ZDO because the map
            // marks portals the client has never been anywhere near and holds no ZDO for.
            public string Icon;

            // A concealed gate this player has found. Never set by the index - it cannot be, because concealed
            // gates are deliberately withheld from it - and only ever by the per-player reveal in
            // ConcealedPortals. Carried so the destination row can be marked as something that was discovered
            // rather than built.
            public bool Concealed;
        }

        private const byte IndexFormatVersion = 1;

        // Per-portal icons are written AFTER the entries rather than inside them, and the format version is
        // deliberately left at 1.
        //
        // Decode refuses a version it does not recognise and returns an empty list, so bumping the version
        // would blank the destination window on every client running an older build - a breaking change, for a
        // cosmetic feature. Appending instead means an older client reads the entries it understands and stops
        // at the end of them, never seeing the extra bytes and never knowing they were there; a newer client
        // reads on. The only requirement is that everything above this block keeps its exact shape.
        private const byte ExtensionVersion = 1;

        // Creator IDs ride in a second appended block for exactly the reason the icons got the first one: a
        // client on 1.3.1 reads the entries, reads the icon block it knows, and stops at what it believes is
        // the end. It never learns these bytes are there and keeps working unchanged.
        private const byte CreatorExtensionVersion = 1;

        // ServerSync gives us compression, fragmentation and - the part that matters here - a push to each
        // client as part of its login handshake, so someone joining a world full of portals has the whole
        // index before they can walk up to one.
        public static readonly CustomSyncedValue<string> SyncedIndex =
            new CustomSyncedValue<string>(Configuration.configSync, "portalindex", "");

        private static readonly List<Entry> Entries = new List<Entry>();
        private static string _decodedFrom;

        // Concealed gates this player has been handed, which reach this client over their own routed RPC and
        // never through the blob above - see ConcealedPortals for why the index cannot carry them. Held apart
        // from Entries because Decode clears that list wholesale on every republish, and a reveal is not the
        // server's to take back.
        private static readonly List<Entry> ConcealedEntries = new List<Entry>();

        // The two lists as one, which is what everything reading the destination list wants. Rebuilt only when
        // one of its sources changes, because GetEntries is called from OnGUI - once per frame, per window.
        private static readonly List<Entry> Merged = new List<Entry>();
        private static string _mergedFrom;
        private static int _mergedRevision = -1;
        private static bool _mergedValid;

        // Bumped on every reveal, so anything that decides whether to rebuild by comparing the index blob -
        // the persistent map markers, most of all - has something to compare for these too.
        public static int ConcealedRevision { get; private set; }

        private static Coroutine _serverRoutine;

        // Touching the class is enough to register the synced value with ConfigSync, which has to happen
        // before ZNet.Awake wires the RPCs up.
        public static void Init()
        {
            Configuration.LogDebug($"Portal index registered ({SyncedIndex.Identifier}).");
        }

        // ---- reading (every peer, server included) ---------------------------------------------------------

        public static List<Entry> GetEntries()
        {
            string blob = SyncedIndex.Value ?? "";
            if (blob != _decodedFrom)
            {
                try
                {
                    Decode(blob, Entries);
                }
                catch (Exception ex)
                {
                    Entries.Clear();
                    Plugin.Log.LogError($"TortalPortal: portal index could not be read, destination list will be empty until the next server publish. Reason: {ex}");
                }

                // Recorded even on failure so one bad payload does not re-throw on every frame of OnGUI.
                _decodedFrom = blob;
                _mergedValid = false;
            }

            if (_mergedValid && _mergedFrom == blob && _mergedRevision == ConcealedRevision) return Merged;

            Merged.Clear();
            Merged.AddRange(Entries);
            Merged.AddRange(ConcealedEntries);
            _mergedFrom = blob;
            _mergedRevision = ConcealedRevision;
            _mergedValid = true;
            return Merged;
        }

        // ---- concealed gates (client side) -----------------------------------------------------------------
        //
        // Set wholesale rather than added to: the server sends the WHOLE of what this player has found every
        // time, so a gate that has been cleared from the world by an admin disappears from the list on the next
        // push instead of lingering as a destination that is not there any more.
        public static void SetConcealed(List<Entry> entries)
        {
            ConcealedEntries.Clear();
            if (entries != null) ConcealedEntries.AddRange(entries);
            ConcealedRevision++;
            _mergedValid = false;
        }

        public static void ClearConcealed()
        {
            if (ConcealedEntries.Count == 0) return;
            ConcealedEntries.Clear();
            ConcealedRevision++;
            _mergedValid = false;
        }

        public static int ConcealedCount => ConcealedEntries.Count;

        // Has this client been handed this gate? The question every "should this be visible" decision asks, and
        // deliberately asked of what the SERVER has sent rather than of the found-list on the ZDO in front of
        // the player: the ZDO is only there while they are standing beside it, and the list has to keep working
        // the moment they walk away.
        public static bool IsConcealedKnown(ZDOID id)
        {
            if (id == ZDOID.None) return false;
            foreach (Entry e in ConcealedEntries)
            {
                if (e.Id == id) return true;
            }
            return false;
        }

        private static void Decode(string blob, List<Entry> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(blob)) return;

            ZPackage pkg = new ZPackage(blob);
            byte version = pkg.ReadByte();
            if (version != IndexFormatVersion)
            {
                Plugin.Log.LogWarning($"TortalPortal: portal index format {version} is not the expected {IndexFormatVersion} - the server is running a different version of the mod.");
                return;
            }

            int count = pkg.ReadInt();
            for (int i = 0; i < count; i++)
            {
                Entry e = new Entry();
                e.Id = pkg.ReadZDOID();
                e.Tag = pkg.ReadString();
                e.Creator = pkg.ReadString();
                e.Pos = pkg.ReadVector3();
                e.Rot = pkg.ReadQuaternion();
                e.Locked = pkg.ReadBool();
                e.Icon = "";
                into.Add(e);
            }

            // Chained rather than independent. An icon block that could not be read leaves the cursor at an
            // offset nobody knows, so the block after it is only safe to touch when the icon block was
            // consumed whole. Skipping the creator IDs costs the cap its long range until the next publish,
            // which recovers on its own; reading longs out of the middle of a string does not.
            if (DecodeIconExtension(pkg, into)) DecodeCreatorExtension(pkg, into);
        }

        // Its own try/catch, and deliberately not the caller's: a destination list that arrived intact is worth
        // far more than the icons on it, so anything unexpected in the trailing block costs the markers their
        // decoration and nothing else. The caller's catch clears the whole list.
        // Returns whether the block was read to its end, which is how the caller knows the cursor is sitting
        // where the next block begins rather than somewhere in the middle of this one.
        private static bool DecodeIconExtension(ZPackage pkg, List<Entry> into)
        {
            try
            {
                // Nothing after the entries: a server older than the feature, which is the ordinary case on a
                // mixed table and not worth a word in the log.
                if (pkg.GetPos() >= pkg.Size()) return false;

                byte ext = pkg.ReadByte();
                if (ext != ExtensionVersion) return false;

                // Sparse - only the portals that actually carry an icon, which on most worlds is none of them.
                int pairs = pkg.ReadInt();
                for (int i = 0; i < pairs; i++)
                {
                    int index = pkg.ReadInt();
                    string icon = pkg.ReadString();
                    if (index >= 0 && index < into.Count) into[index].Icon = icon;
                }

                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"TortalPortal: the portal index's icon block could not be read - the destinations are fine, their markers just use the default icon. Reason: {ex}");
                return false;
            }
        }

        // Dense where the icon block is sparse: every portal has a creator worth carrying, so eight bytes
        // each beats paying for an index alongside each one. A zero means the ZDO records no creator - an
        // admin-spawned gate, or one older than the field - and is left at zero rather than guessed at, so a
        // reader can tell "nobody owns this" apart from "owned by whoever player zero is".
        private static void DecodeCreatorExtension(ZPackage pkg, List<Entry> into)
        {
            try
            {
                if (pkg.GetPos() >= pkg.Size()) return;

                byte ext = pkg.ReadByte();
                if (ext != CreatorExtensionVersion) return;

                int count = pkg.ReadInt();
                for (int i = 0; i < count; i++)
                {
                    long id = pkg.ReadLong();
                    if (i < into.Count) into[i].CreatorId = id;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"TortalPortal: the portal index's creator block could not be read - destinations are fine, but the per-player cap falls back to only the portals this client can see nearby. Reason: {ex}");
            }
        }

        // ---- publishing (server only) ---------------------------------------------------------------------

        public static void OnZNetStart()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

            // ZNet.Start runs again on every world load, and these coroutines live on the plugin object,
            // which does not.
            if (_serverRoutine != null) Plugin.Instance.StopCoroutine(_serverRoutine);
            _serverRoutine = Plugin.Instance.StartCoroutine(ServerIndexRoutine());
        }

        private static IEnumerator ServerIndexRoutine()
        {
            // The world's ZDOs stream in after ZNet.Start, so the backfill has to wait for them. Publishing
            // immediately would push an empty index and look exactly like the bug it replaced.
            while (ZDOMan.instance == null || Game.instance == null) yield return null;
            yield return new WaitForSeconds(3f);

            Publish(force: true);

            while (true)
            {
                yield return new WaitForSeconds(Mathf.Max(5f, Configuration.portalIndexRefreshSeconds.Value));
                Publish(force: false);
            }
            // ReSharper disable once IteratorNeverReturns
        }

        // Kept out of the coroutine so the risky work can sit in a try/catch - C# forbids "yield return"
        // inside a try block with a catch clause.
        private static void Publish(bool force)
        {
            try
            {
                if (ZDOMan.instance == null) return;

                // On the server this is genuinely every portal in the world, which is the whole point.
                string blob = Encode(PlacementPatch.GetAllPortals());
                if (!force && blob == SyncedIndex.Value) return;

                // Assigning fires ConfigSync's broadcast to every connected peer.
                SyncedIndex.Value = blob;
                Configuration.LogDebug($"Published portal index ({blob.Length} chars, {_lastNamedCount}/{_lastPortalCount} owners named){(force ? " - startup backfill." : ".")}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: portal index publish failed (non-fatal, clients keep the previous list). Reason: {ex}");
            }
        }

        // Only ever read back out into the publish log. Answering "the list still says Unknown" needs to
        // separate "the server has no name for them" from "the name never reached the packet", and those look
        // identical from a client.
        private static int _lastNamedCount;
        private static int _lastPortalCount;

        private static string Encode(List<ZDO> portals)
        {
            _lastNamedCount = 0;
            _lastPortalCount = portals.Count;

            ZPackage pkg = new ZPackage();
            pkg.Write(IndexFormatVersion);
            pkg.Write(portals.Count);

            foreach (ZDO zdo in portals)
            {
                pkg.Write(zdo.m_uid);
                pkg.Write(zdo.GetString(ZDOVars.s_tag) ?? "");
                string creatorName = CreatorNameOf(zdo);
                if (creatorName.Length > 0) _lastNamedCount++;
                pkg.Write(creatorName);
                pkg.Write(zdo.GetPosition());
                pkg.Write(zdo.GetRotation());

                // Only whether a PIN exists, never the PIN itself. Clients pull the portal's own ZDO when a
                // row is selected, which is the same exposure 1.0.0 had - rather than every PIN on the
                // server landing in every client's memory at login.
                pkg.Write(!string.IsNullOrEmpty(zdo.GetString("PortalPIN")));
            }

            EncodeIconExtension(pkg, portals);
            EncodeCreatorExtension(pkg, portals);
            return pkg.GetBase64();
        }

        // The owner's name, for the "Owner" row in the destination list.
        //
        // This is the field that read "Unknown" on every portal on every world since 1.0.0, and the reason is
        // one line above: the index used to publish ZDOVars.s_creatorName straight off the portal's ZDO, and
        // vanilla writes that key in exactly ONE place - PrivateArea.Setup, which is wards. No gate a player
        // has ever raised carries it, so the string went out empty every time and the client dutifully turned
        // empty into "Unknown". The count was never wrong; there was simply never a name in the packet.
        //
        // What a portal DOES carry is s_creator, a number, and 1.4.1 gave the server a register that turns
        // that number into a name. This is what finally asks it. Server-side only - Encode never runs anywhere
        // else - so the name is resolved once by the peer that holds the register rather than asked of every
        // client, and it rides in a string field that has been in format version 1 since the beginning. A
        // 1.3.1 client reads it exactly where it always read it and starts showing names without being
        // updated.
        //
        // s_creatorName is still read first. No vanilla portal has one, but a modded piece that writes its own
        // is telling us something this server's register may not know, and its word about its own gate beats
        // our lookup.
        private static string CreatorNameOf(ZDO zdo)
        {
            try
            {
                string written = zdo.GetString(ZDOVars.s_creatorName);
                if (!string.IsNullOrEmpty(written)) return written;

                // Zero is an admin-spawned gate, or one older than the field. Nobody owns it, so it stays
                // blank rather than being attributed to whoever player zero would be.
                long creator = zdo.GetLong(ZDOVars.s_creator);
                if (creator == 0) return "";

                // Null while the owner is still a stranger, and empty on the wire is exactly that. The client
                // prints "Unknown" for it, which is the honest answer to a name we do not have - as opposed to
                // a name we invented, which is the one thing that must never go out.
                return PlayerNames.Get(creator) ?? "";
            }
            catch
            {
                // A destination list is worth far more than the name on one of its rows.
                return "";
            }
        }

        // Written as (index, name) pairs for only the portals that have an icon, so a world where nobody uses
        // the feature - which is every world until somebody does - pays five bytes for the whole block.
        private static void EncodeIconExtension(ZPackage pkg, List<ZDO> portals)
        {
            List<int> indices = new List<int>();
            List<string> icons = new List<string>();

            for (int i = 0; i < portals.Count; i++)
            {
                string icon = portals[i].GetString(TortalPinIcon.ZdoKey);
                if (string.IsNullOrEmpty(icon)) continue;
                indices.Add(i);
                icons.Add(icon);
            }

            pkg.Write(ExtensionVersion);
            pkg.Write(indices.Count);
            for (int i = 0; i < indices.Count; i++)
            {
                pkg.Write(indices[i]);
                pkg.Write(icons[i]);
            }
        }

        // Written last, after the icons, so that everything above it keeps the exact shape an older client
        // expects to find.
        private static void EncodeCreatorExtension(ZPackage pkg, List<ZDO> portals)
        {
            pkg.Write(CreatorExtensionVersion);
            pkg.Write(portals.Count);
            foreach (ZDO zdo in portals) pkg.Write(zdo.GetLong(ZDOVars.s_creator));
        }
    }
}
