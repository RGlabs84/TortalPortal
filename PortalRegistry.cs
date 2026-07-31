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
            public Vector3 Pos;
            public Quaternion Rot;
            public bool Locked;
        }

        private const byte IndexFormatVersion = 1;

        // ServerSync gives us compression, fragmentation and - the part that matters here - a push to each
        // client as part of its login handshake, so someone joining a world full of portals has the whole
        // index before they can walk up to one.
        public static readonly CustomSyncedValue<string> SyncedIndex =
            new CustomSyncedValue<string>(Configuration.configSync, "portalindex", "");

        private static readonly List<Entry> Entries = new List<Entry>();
        private static string _decodedFrom;

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
            if (blob == _decodedFrom) return Entries;

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
            return Entries;
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
                into.Add(e);
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
                Configuration.LogDebug($"Published portal index ({blob.Length} chars){(force ? " - startup backfill." : ".")}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: portal index publish failed (non-fatal, clients keep the previous list). Reason: {ex}");
            }
        }

        private static string Encode(List<ZDO> portals)
        {
            ZPackage pkg = new ZPackage();
            pkg.Write(IndexFormatVersion);
            pkg.Write(portals.Count);

            foreach (ZDO zdo in portals)
            {
                pkg.Write(zdo.m_uid);
                pkg.Write(zdo.GetString(ZDOVars.s_tag) ?? "");
                pkg.Write(zdo.GetString(ZDOVars.s_creatorName) ?? "");
                pkg.Write(zdo.GetPosition());
                pkg.Write(zdo.GetRotation());

                // Only whether a PIN exists, never the PIN itself. Clients pull the portal's own ZDO when a
                // row is selected, which is the same exposure 1.0.0 had - rather than every PIN on the
                // server landing in every client's memory at login.
                pkg.Write(!string.IsNullOrEmpty(zdo.GetString("PortalPIN")));
            }

            return pkg.GetBase64();
        }
    }
}
