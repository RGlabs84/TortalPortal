using BepInEx;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ServerSync;
using UnityEngine;

namespace TortalPortal
{
    // How a portal's chosen map icon reaches the people looking at the map.
    //
    // Up to 1.2.2 it did not. The portal's ZDO carried the icon's NAME and nothing else, so a marker only
    // drew correctly for players who already happened to have a PNG of that name on their own disk - which
    // in practice meant "everyone running the same modpack, and nobody else". Art could be shared, but only
    // by shipping it.
    //
    // The server is the one machine everybody is already connected to and already trusts for their config and
    // their portal index, so it carries the art too. An admin drops PNGs into a TortalPortalPins folder beside
    // the server's own plugin; the server publishes a CATALOGUE of what it holds, and each client pulls down
    // only the pictures it is actually missing, once, caching them to disk by content hash. Add a file to the
    // server and every player has it next time they log in, with nothing to install and no version to bump.
    //
    // Two things are worth knowing about the shape of it:
    //
    // The catalogue and the bytes travel separately. The catalogue is a few dozen bytes per icon and rides
    // ServerSync's login handshake beside the portal index, so it is there before the map can be opened. The
    // pictures are far too big for that channel and move over a routed RPC in 16 KB chunks instead, one chunk
    // per frame, so a transfer never sits on the socket that gameplay is using.
    //
    // Nothing here decodes an image on the server. A dedicated server is headless, and handing a PNG decoder
    // bytes that arrived over the network is precisely the thing not to do; the header parse below reads the
    // dimensions out of IHDR arithmetically and never touches a pixel.
    [HarmonyPatch]
    public static class TortalPinLibrary
    {
        // Bumped only if the catalogue's SHAPE changes. Unlike the portal index this one can afford a version
        // bump - an unreadable catalogue costs the markers their decoration, never the destination list.
        private const byte CatalogueVersion = 1;

        private const string FetchRpc = "TortalPortal_PinFetch";
        private const string ChunkRpc = "TortalPortal_PinChunk";

        // 16 KB a frame. Valheim's own ZDO stream is budgeted in roughly this unit, and at the sizes a map
        // marker actually wants (a 48x48 PNG is three kilobytes) an icon is a single chunk.
        private const int ChunkBytes = 16 * 1024;

        // A marker is drawn 48 pixels across on the great map. These are the ceilings that stop somebody's
        // 4096-pixel wallpaper becoming everybody else's download and everybody else's video memory.
        private const int MaxIconBytes = 96 * 1024;
        private const int MaxIconDimension = 256;
        private const int MaxIcons = 64;

        // A transfer that stops arriving is abandoned rather than held open forever.
        private const float TransferTimeoutSeconds = 15f;

        public sealed class Entry
        {
            public string Name;
            public string Hash;
            public int Width;
            public int Height;
            public int Length;
        }

        // ServerSync gives compression, fragmentation and a push as part of the login handshake - the same
        // three reasons PortalRegistry uses it for the destination index.
        public static readonly CustomSyncedValue<string> SyncedCatalogue =
            new CustomSyncedValue<string>(Configuration.configSync, "pincatalogue", "");

        // What the server says exists, by icon name. Read by TortalPinIcon when it resolves a marker.
        private static readonly Dictionary<string, Entry> Catalogue = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static string _decodedFrom;

        // Server side: hash -> the file on disk that has those bytes.
        private static readonly Dictionary<string, string> ServerBlobs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Client side: hashes already on this machine, so a rejoin costs nothing. A hash present here has a
        // file at CachePath(hash) OR a local PNG with the same bytes - either way there is nothing to fetch.
        private static readonly HashSet<string> Have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Hashes asked for and not yet complete, against the time they were asked for, so a catalogue
        // republish does not start the same download twice AND an unanswered request can be retried.
        //
        // The timestamp is load-bearing. A plain "have I asked?" set loses any request the server never
        // answers - and the first request of a session is exactly the one at risk, because the catalogue
        // arrives on ConfigSync's login handshake and can beat the routed-RPC channel being ready, so the
        // fetch goes nowhere. Observed on the dedicated rig: seven of eight icons downloaded and the
        // alphabetically-first one silently never did, with no retry for the rest of the session.
        private static readonly Dictionary<string, float> Requested = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        private sealed class Incoming
        {
            public byte[][] Chunks;
            public int Received;
            public int Total;
            public float LastChunkAt;
        }

        private static readonly Dictionary<string, Incoming> Transfers = new Dictionary<string, Incoming>(StringComparer.OrdinalIgnoreCase);

        private static Coroutine _pump;
        private static bool _serverScanned;

        // Every completed download moves this on. TortalPinIcon hands it to the map so markers that were
        // drawing a fallback while the picture was in flight pick up the real one without the map reopening.
        public static int Revision { get; private set; }

        // ---- paths -----------------------------------------------------------------------------------------

        // Under BepInEx's config folder rather than the plugin folder, for the reason Favorites already lives
        // there: a mod manager owns the plugin directory and rewrites it on every update, and a cache that is
        // thrown away on each update is not a cache.
        private static string CacheDir => Path.Combine(Paths.ConfigPath, "TortalPortal", "pincache");

        private static string CachePath(string hash) => Path.Combine(CacheDir, hash + ".png");

        // ---- lifecycle -------------------------------------------------------------------------------------

        // Touching the class registers the synced value with ConfigSync, which has to happen before ZNet.Awake
        // wires the RPCs up. Same contract as PortalRegistry.Init.
        public static void Init()
        {
            Configuration.LogDebug($"Portal icon catalogue registered ({SyncedCatalogue.Identifier}).");
        }

        public static void OnZNetStart()
        {
            try
            {
                if (ZRoutedRpc.instance == null) return;

                // Registered on every peer for symmetry. A client only ever sends Fetch and only ever receives
                // Chunk; a server does the opposite. Registering both on both costs two dictionary entries and
                // means a listen-server host - which is a client AND a server - needs no special case.
                ZRoutedRpc.instance.Register<ZPackage>(FetchRpc, RPC_PinFetch);
                ZRoutedRpc.instance.Register<ZPackage>(ChunkRpc, RPC_PinChunk);
                ZRoutedRpc.instance.Register(ReloadRpc, RPC_PinReload);
                ZRoutedRpc.instance.Register<int>(ReloadReplyRpc, RPC_PinReloaded);

                if (ZNet.instance != null && ZNet.instance.IsServer())
                {
                    ScanServerPack();
                    PublishCatalogue();
                }

                if (_pump != null) Plugin.Instance.StopCoroutine(_pump);
                _pump = Plugin.Instance.StartCoroutine(ClientPump());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not start the map icon library (non-fatal, markers use local files only). Reason: {ex}");
            }
        }

        // Leaving a world has to put this back to nothing. Everything here is per-SERVER state living in
        // statics: the catalogue, which hashes we hold, which downloads are in flight. Without this, an icon
        // one server sent you would go on overriding your own file on the next server and in single player,
        // until the game was restarted - and that is exactly the kind of bug that only ever reproduces for
        // the person who plays on two servers in one sitting.
        [HarmonyPatch(typeof(ZNet), "Shutdown")]
        [HarmonyPostfix]
        private static void ZNet_Shutdown_Postfix()
        {
            try
            {
                if (_pump != null && Plugin.Instance != null) Plugin.Instance.StopCoroutine(_pump);
                _pump = null;

                Catalogue.Clear();
                ServerBlobs.Clear();
                Have.Clear();
                Requested.Clear();
                Transfers.Clear();
                _decodedFrom = null;
                _serverScanned = false;

                // Icons resolved against the catalogue we just dropped have to be resolved again - and the
                // local scan goes too, because the next world may be a different install entirely.
                Revision++;
                TortalPinIcon.Reset();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: tearing down the map icon library failed (non-fatal). Reason: {ex}");
            }
        }

        // ---- reading the catalogue (every peer) --------------------------------------------------------------

        // Null when the server has never heard of this name, which is the ordinary case for an icon somebody
        // added to their own folder and never shared.
        public static Entry Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            EnsureDecoded();
            return Catalogue.TryGetValue(name, out Entry e) ? e : null;
        }

        public static bool HasBlob(string hash) => !string.IsNullOrEmpty(hash) && File.Exists(CachePath(hash));

        public static string BlobPath(string hash) => CachePath(hash);

        public static List<string> Names()
        {
            EnsureDecoded();
            List<string> names = new List<string>(Catalogue.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        private static void EnsureDecoded()
        {
            string blob = SyncedCatalogue.Value ?? "";
            if (blob == _decodedFrom) return;
            _decodedFrom = blob;

            Catalogue.Clear();
            try
            {
                if (string.IsNullOrEmpty(blob)) return;

                ZPackage pkg = new ZPackage(blob);
                byte version = pkg.ReadByte();
                if (version != CatalogueVersion)
                {
                    Plugin.Log.LogWarning($"TortalPortal: the server's icon catalogue is format {version}, not {CatalogueVersion} - its markers will use local icons only.");
                    return;
                }

                int count = pkg.ReadInt();
                for (int i = 0; i < count; i++)
                {
                    Entry e = new Entry
                    {
                        Name = pkg.ReadString(),
                        Hash = pkg.ReadString(),
                        Width = pkg.ReadInt(),
                        Height = pkg.ReadInt(),
                        Length = pkg.ReadInt()
                    };
                    if (!string.IsNullOrEmpty(e.Name) && !string.IsNullOrEmpty(e.Hash)) Catalogue[e.Name] = e;
                }

                Configuration.LogDebug($"Icon catalogue: {Catalogue.Count} icon(s) offered by the server.");

                // A new catalogue can rename what an existing marker should be drawing.
                Revision++;
                TortalPinIcon.ForgetServerIcons();
            }
            catch (Exception ex)
            {
                Catalogue.Clear();
                Plugin.Log.LogError($"TortalPortal: the server's icon catalogue could not be read (non-fatal, markers use local icons). Reason: {ex}");
            }
        }

        // ---- server: the pack --------------------------------------------------------------------------------

        // The admin's folder, read once at startup and again on tortal_pinreload. Deliberately the same folder
        // clients scan - a dedicated server is handed the same package, so "drop the PNGs in beside the DLL"
        // is one instruction rather than two.
        public static int ScanServerPack()
        {
            ServerBlobs.Clear();
            _serverScanned = true;

            try
            {
                string dir = Path.GetDirectoryName(Plugin.Instance.Info.Location) ?? "";
                string folder = Path.Combine(dir, TortalPinIcon.LibraryFolder);

                List<string> files = new List<string>();
                if (Directory.Exists(folder)) files.AddRange(Directory.GetFiles(folder, "*.png", SearchOption.TopDirectoryOnly));

                string legacy = Path.Combine(dir, TortalPinIcon.LegacyFileName);
                if (File.Exists(legacy)) files.Insert(0, legacy);

                int accepted = 0;
                foreach (string file in files)
                {
                    if (accepted >= MaxIcons)
                    {
                        Plugin.Log.LogWarning($"TortalPortal: the icon pack holds more than {MaxIcons} PNGs - the rest are not offered to clients.");
                        break;
                    }
                    if (RegisterPackFile(file)) accepted++;
                }

                Plugin.Log.LogInfo($"TortalPortal: offering {accepted} map icon(s) to clients from {TortalPinIcon.LibraryFolder}.");
                return accepted;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not scan the server's icon pack (non-fatal, clients use their own files). Reason: {ex}");
                return 0;
            }
        }

        private static readonly List<Entry> PackEntries = new List<Entry>();

        private static bool RegisterPackFile(string path)
        {
            try
            {
                FileInfo info = new FileInfo(path);
                if (info.Length > MaxIconBytes)
                {
                    Plugin.Log.LogWarning($"TortalPortal: not offering '{Path.GetFileName(path)}' - {info.Length / 1024} KB is over the {MaxIconBytes / 1024} KB limit for an icon.");
                    return false;
                }

                byte[] bytes = File.ReadAllBytes(path);
                if (!ReadPngHeader(bytes, out int w, out int h))
                {
                    Plugin.Log.LogWarning($"TortalPortal: not offering '{Path.GetFileName(path)}' - it is not a readable PNG.");
                    return false;
                }
                if (w > MaxIconDimension || h > MaxIconDimension)
                {
                    Plugin.Log.LogWarning($"TortalPortal: not offering '{Path.GetFileName(path)}' - {w}x{h} is over the {MaxIconDimension}x{MaxIconDimension} limit. A marker is drawn 48 pixels across.");
                    return false;
                }

                string name = Path.GetFileNameWithoutExtension(path);
                string hash = HashOf(bytes);
                ServerBlobs[hash] = path;

                PackEntries.RemoveAll(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
                PackEntries.Add(new Entry { Name = name, Hash = hash, Width = w, Height = h, Length = bytes.Length });
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not read the icon '{path}' (non-fatal, it is skipped). Reason: {ex}");
                return false;
            }
        }

        public static void PublishCatalogue()
        {
            try
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

                // Off means an empty catalogue rather than a withheld one: a client is then told plainly that
                // there is nothing to have, instead of asking for pictures that will never come.
                if (Configuration.shareIconsFromServer != null && !Configuration.shareIconsFromServer.Value)
                {
                    SyncedCatalogue.Value = "";
                    Configuration.LogDebug("Icon sharing is off - published an empty catalogue.");
                    return;
                }

                if (!_serverScanned) ScanServerPack();

                ZPackage pkg = new ZPackage();
                pkg.Write(CatalogueVersion);
                pkg.Write(PackEntries.Count);
                foreach (Entry e in PackEntries)
                {
                    pkg.Write(e.Name);
                    pkg.Write(e.Hash);
                    pkg.Write(e.Width);
                    pkg.Write(e.Height);
                    pkg.Write(e.Length);
                }

                // Assigning fires ConfigSync's broadcast to every connected peer.
                SyncedCatalogue.Value = pkg.GetBase64();
                Configuration.LogDebug($"Published icon catalogue ({PackEntries.Count} icon(s)).");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: publishing the icon catalogue failed (non-fatal, clients keep the previous one). Reason: {ex}");
            }
        }

        // Rebuild from disk and republish. The point of the reload command: an admin adds art without taking
        // the server down and without anybody reinstalling anything.
        public static int Reload()
        {
            PackEntries.Clear();
            int n = ScanServerPack();
            PublishCatalogue();
            return n;
        }

        // ---- client: pulling what is missing ------------------------------------------------------------------

        // One coroutine rather than a request storm at login: the catalogue can name sixty icons, and asking
        // for all sixty in the same frame as the world is streaming in is the one thing guaranteed to be felt.
        private static IEnumerator ClientPump()
        {
            while (ZNet.instance == null) yield return null;

            // The server holds the originals; it has nothing to fetch.
            if (ZNet.instance.IsServer())
            {
                SeedHaveFromServer();
            }
            else
            {
                // Wait for a peer before asking for anything. The catalogue rides ConfigSync's login
                // handshake and can land before the routed-RPC channel carries traffic, and a fetch sent
                // into that gap is dropped without a word. The retry above recovers it either way; this
                // just means the first icon arrives in a second rather than after a timeout.
                while (ZNet.instance != null && ZNet.instance.GetPeers().Count == 0) yield return null;
                yield return new WaitForSeconds(2f);
            }

            while (true)
            {
                yield return new WaitForSeconds(1f);
                PumpOnce();
            }
            // ReSharper disable once IteratorNeverReturns
        }

        // A listen-server host is its own source: everything in the pack is already on its disk.
        private static void SeedHaveFromServer()
        {
            foreach (string hash in ServerBlobs.Keys) Have.Add(hash);
        }

        private static void PumpOnce()
        {
            try
            {
                EnsureDecoded();
                if (Catalogue.Count == 0) return;

                // Expire anything asked for that has not finished in time - a transfer that stopped
                // mid-stream AND, just as importantly, a request that was never answered at all. Both are
                // measured from the same clock: a transfer's last chunk if one ever arrived, otherwise the
                // moment the request went out. Clearing the hash is what makes the next pass ask again.
                if (Requested.Count > 0)
                {
                    List<string> dead = null;
                    foreach (KeyValuePair<string, float> kv in Requested)
                    {
                        float last = Transfers.TryGetValue(kv.Key, out Incoming t) ? t.LastChunkAt : kv.Value;
                        if (Time.realtimeSinceStartup - last <= TransferTimeoutSeconds) continue;
                        (dead ?? (dead = new List<string>())).Add(kv.Key);
                    }
                    if (dead != null)
                    {
                        foreach (string hash in dead)
                        {
                            bool started = Transfers.Remove(hash);
                            Requested.Remove(hash);
                            Plugin.Log.LogWarning(started
                                ? "TortalPortal: an icon download stalled part-way and was abandoned - it will be asked for again."
                                : "TortalPortal: an icon request went unanswered - it will be asked for again.");
                        }
                    }
                }

                // One at a time. There is no hurry: a marker draws the player's own fallback until its picture
                // lands, which is a perfectly good map in the meantime.
                if (Transfers.Count > 0) return;

                foreach (Entry e in Catalogue.Values)
                {
                    if (Have.Contains(e.Hash) || Requested.ContainsKey(e.Hash)) continue;

                    // Already on disk from a previous session, or from another server entirely - the cache is
                    // keyed by content, so the same picture is only ever downloaded once per machine.
                    if (HasBlob(e.Hash)) { Have.Add(e.Hash); Revision++; continue; }

                    // Already on disk as one of the player's OWN files. This is what stops a client
                    // re-downloading the icons that shipped in its own package: the server offers them because
                    // it was handed the same package, and the client already has the identical bytes.
                    if (TortalPinIcon.LocalHashMatches(e.Name, e.Hash)) { Have.Add(e.Hash); continue; }

                    Request(e);
                    return;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: the icon download pump failed (non-fatal, markers use local icons). Reason: {ex}");
            }
        }

        private static void Request(Entry e)
        {
            try
            {
                if (e.Length > MaxIconBytes)
                {
                    Plugin.Log.LogWarning($"TortalPortal: not fetching '{e.Name}' - the server says it is {e.Length / 1024} KB, over the {MaxIconBytes / 1024} KB limit.");
                    Have.Add(e.Hash);
                    return;
                }

                Requested[e.Hash] = Time.realtimeSinceStartup;
                ZPackage pkg = new ZPackage();
                pkg.Write(e.Hash);
                ZRoutedRpc.instance?.InvokeRoutedRPC(FetchRpc, pkg);
                Configuration.LogDebug($"Asked the server for icon '{e.Name}' ({e.Length} bytes).");
            }
            catch (Exception ex)
            {
                Requested.Remove(e.Hash);
                Plugin.Log.LogError($"TortalPortal: asking for the icon '{e.Name}' failed (non-fatal). Reason: {ex}");
            }
        }

        // ---- the wire -----------------------------------------------------------------------------------------

        private static void RPC_PinFetch(long sender, ZPackage pkg)
        {
            try
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                if (pkg == null) return;

                // The server's own dial is the authority, whatever the caller believes - the same rule
                // SectorSubscription applies to its own feature. Turning sharing off mid-session stops
                // requests that were already in flight.
                if (Configuration.shareIconsFromServer != null && !Configuration.shareIconsFromServer.Value) return;

                string hash = pkg.ReadString();
                if (string.IsNullOrEmpty(hash)) return;

                // Only ever what the server itself put in the catalogue. A hash that is not in this dictionary
                // is not a file the server is willing to talk about, which keeps the request from being a way
                // to ask the server to read paths of the caller's choosing.
                if (!ServerBlobs.TryGetValue(hash, out string path) || !File.Exists(path))
                {
                    Configuration.LogDebug($"Icon fetch for an unknown hash from {sender} - ignored.");
                    return;
                }

                Plugin.Instance.StartCoroutine(SendBlob(sender, hash, path));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(RPC_PinFetch)} failed (non-fatal). Reason: {ex}");
            }
        }

        private static IEnumerator SendBlob(long target, string hash, string path)
        {
            byte[] bytes = null;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not read '{path}' to send it (non-fatal). Reason: {ex}");
            }
            if (bytes == null || bytes.Length == 0) yield break;

            int total = Mathf.Max(1, Mathf.CeilToInt(bytes.Length / (float)ChunkBytes));
            for (int i = 0; i < total; i++)
            {
                int offset = i * ChunkBytes;
                int size = Mathf.Min(ChunkBytes, bytes.Length - offset);
                byte[] slice = new byte[size];
                Buffer.BlockCopy(bytes, offset, slice, 0, size);

                try
                {
                    ZPackage pkg = new ZPackage();
                    pkg.Write(hash);
                    pkg.Write(i);
                    pkg.Write(total);
                    pkg.Write(slice);
                    ZRoutedRpc.instance?.InvokeRoutedRPC(target, ChunkRpc, pkg);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"TortalPortal: sending an icon chunk failed (non-fatal, the client will retry). Reason: {ex}");
                    yield break;
                }

                // A frame between chunks. The socket is carrying the world as well as this.
                yield return null;
            }
        }

        private static void RPC_PinChunk(long sender, ZPackage pkg)
        {
            try
            {
                if (pkg == null) return;

                // Only the server sends these. A client is not a source of pictures for other clients.
                if (ZNet.instance != null && ZNet.instance.IsServer()) return;

                string hash = pkg.ReadString();
                int index = pkg.ReadInt();
                int total = pkg.ReadInt();
                byte[] slice = pkg.ReadByteArray();

                if (string.IsNullOrEmpty(hash) || slice == null) return;
                if (total <= 0 || index < 0 || index >= total) return;
                if (total > (MaxIconBytes / ChunkBytes) + 1 || slice.Length > ChunkBytes) return;
                if (!Requested.ContainsKey(hash)) return;   // nothing asked for this

                if (!Transfers.TryGetValue(hash, out Incoming t))
                {
                    t = new Incoming { Chunks = new byte[total][], Total = total };
                    Transfers[hash] = t;
                }
                if (t.Total != total || t.Chunks[index] != null) return;

                t.Chunks[index] = slice;
                t.Received++;
                t.LastChunkAt = Time.realtimeSinceStartup;

                if (t.Received < t.Total) return;

                Transfers.Remove(hash);
                Requested.Remove(hash);
                Complete(hash, t);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(RPC_PinChunk)} failed (non-fatal). Reason: {ex}");
            }
        }

        private static void Complete(string hash, Incoming t)
        {
            try
            {
                int length = 0;
                foreach (byte[] c in t.Chunks) length += c.Length;
                if (length > MaxIconBytes) return;

                byte[] bytes = new byte[length];
                int at = 0;
                foreach (byte[] c in t.Chunks)
                {
                    Buffer.BlockCopy(c, 0, bytes, at, c.Length);
                    at += c.Length;
                }

                // The hash is the name of the thing we asked for, so checking it is also the integrity check:
                // a truncated, reordered or tampered transfer cannot produce these bytes.
                string actual = HashOf(bytes);
                if (!string.Equals(actual, hash, StringComparison.OrdinalIgnoreCase))
                {
                    Plugin.Log.LogWarning("TortalPortal: a downloaded icon did not match its hash and was discarded.");
                    return;
                }

                if (!ReadPngHeader(bytes, out int w, out int h) || w > MaxIconDimension || h > MaxIconDimension)
                {
                    Plugin.Log.LogWarning("TortalPortal: a downloaded icon was not a PNG of a sensible size and was discarded.");
                    return;
                }

                Directory.CreateDirectory(CacheDir);
                File.WriteAllBytes(CachePath(hash), bytes);
                Have.Add(hash);

                // Markers currently drawing a fallback for this icon pick it up on the next map pass.
                Revision++;
                TortalPinIcon.ForgetServerIcons();
                Configuration.LogDebug($"Icon {hash} received ({w}x{h}, {bytes.Length} bytes) and cached.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: storing a downloaded icon failed (non-fatal). Reason: {ex}");
            }
        }

        // ---- tortal_pinreload ------------------------------------------------------------------------------------

        // Adding art should not cost a restart. The command runs where the files are - which on a dedicated
        // server is a console you are not standing at - so it goes over an admin-gated RPC and reports back,
        // exactly as tortal_scatterclear does.
        private const string ReloadRpc = "TortalPortal_PinReload";
        private const string ReloadReplyRpc = "TortalPortal_PinReloaded";

        private static bool _commandRegistered;

        private static void RPC_PinReload(long sender)
        {
            try
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

                ZNetPeer peer = ZNet.instance.GetPeer(sender);
                string host = peer != null && peer.m_socket != null ? peer.m_socket.GetHostName() : "";
                if (!ZNet.instance.ListContainsId(ZNet.instance.m_adminList, host))
                {
                    Plugin.Log.LogWarning($"TortalPortal: refused an icon reload from {host} - not on the adminlist.");
                    ZRoutedRpc.instance.InvokeRoutedRPC(sender, ReloadReplyRpc, -1);
                    return;
                }

                int n = Reload();
                Plugin.Log.LogInfo($"TortalPortal: tortal_pinreload (via {host}): now offering {n} map icon(s).");
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, ReloadReplyRpc, n);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(RPC_PinReload)} failed (non-fatal). Reason: {ex}");
            }
        }

        private static void RPC_PinReloaded(long sender, int count)
        {
            try
            {
                string msg = count < 0
                    ? "tortal_pinreload: the server refused - you are not on its adminlist."
                    : $"tortal_pinreload: the server is now offering {count} map icon(s). Players receive any they are missing on their next login.";
                if (Console.instance != null) Console.instance.Print(msg);
                Plugin.Log.LogInfo($"TortalPortal: {msg}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(RPC_PinReloaded)} failed (non-fatal). Reason: {ex}");
            }
        }

        [HarmonyPatch(typeof(Terminal), "InitTerminal")]
        [HarmonyPostfix]
        private static void Terminal_InitTerminal_Postfix()
        {
            try
            {
                if (_commandRegistered) return;
                _commandRegistered = true;

                new Terminal.ConsoleCommand("tortal_pinreload",
                    "rescans the server's TortalPortalPins folder and offers any new map icons to every player, without a restart.",
                    delegate (Terminal.ConsoleEventArgs args)
                    {
                        try
                        {
                            // Standing on the host: do it here and say so. Otherwise ask the server, which is
                            // the machine holding the folder that matters.
                            if (ZNet.instance != null && ZNet.instance.IsServer())
                            {
                                int n = Reload();
                                args.Context?.AddString($"tortal_pinreload: now offering {n} map icon(s).");
                                return;
                            }
                            ZRoutedRpc.instance?.InvokeRoutedRPC(ReloadRpc);
                            args.Context?.AddString("tortal_pinreload: asked the server to rescan its icon folder...");
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.LogError($"TortalPortal: tortal_pinreload failed (non-fatal). Reason: {ex}");
                        }
                    },
                    isCheat: false, isNetwork: false, onlyServer: false);

                Plugin.Log.LogInfo("TortalPortal: console command tortal_pinreload registered.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not register tortal_pinreload. Reason: {ex}");
            }
        }

        // ---- helpers --------------------------------------------------------------------------------------------

        // Sixteen hex characters of SHA-1. Not a security boundary - the bytes are checked against this AND
        // against the PNG header before anything is kept - just enough that two different pictures do not
        // collide in a folder or a catalogue.
        public static string HashOf(byte[] bytes)
        {
            using (SHA1 sha = SHA1.Create())
            {
                byte[] digest = sha.ComputeHash(bytes);
                StringBuilder sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++) sb.Append(digest[i].ToString("x2"));
                return sb.ToString();
            }
        }

        // PNG dimensions without a decoder. The format puts an 8-byte signature first and then guarantees the
        // first chunk is IHDR, whose width and height are big-endian 32-bit integers at fixed offsets 16 and
        // 20. That is the whole read - no pixels are touched, which is the point: this runs on a headless
        // server, on bytes that arrived over the network.
        public static bool ReadPngHeader(byte[] b, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (b == null || b.Length < 24) return false;

            if (b[0] != 137 || b[1] != 80 || b[2] != 78 || b[3] != 71 ||
                b[4] != 13 || b[5] != 10 || b[6] != 26 || b[7] != 10) return false;
            if (b[12] != 'I' || b[13] != 'H' || b[14] != 'D' || b[15] != 'R') return false;

            width = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
            height = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
            return width > 0 && height > 0 && width <= 16384 && height <= 16384;
        }
    }
}
