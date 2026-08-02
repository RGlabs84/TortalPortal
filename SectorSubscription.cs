using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TortalPortal
{
    // Phase two of the portal anchor: the server-side half that lets a client of a DEDICATED server take
    // far-portal snapshots.
    //
    // Phase one (SnapshotAnchor) could only run where the whole world's ZDOs were already in local memory,
    // which is the hosting player alone - a dedicated server draws nothing, and its clients are only ever
    // sent the sectors around their own reference position, so their anchor had nothing to load. This class
    // closes that loop: a client says "send me the sectors around this point for a moment" over a routed
    // RPC, and a postfix on the server's per-peer sync-list builder honours it for as long as the client
    // keeps renewing. ShouldSend's revision tracking makes the append cheap - each object crosses the wire
    // once and then only when it changes, exactly as if the peer were standing there.
    //
    // Subscriptions are deliberately short-lived and renewal-driven rather than open/close: a client that
    // crashes, disconnects or just stops asking costs the server nothing within seconds, with no cleanup
    // path to get wrong.
    //
    // Wire compatibility: this is a NEW routed RPC, invoked only by 1.2 clients. A 1.1.2 client never sends
    // it; a pre-1.2 server receiving it drops it as an unknown method, which the client detects as shots
    // that never receive their world and gives up on quietly (see PlacementPatch).
    [HarmonyPatch]
    public static class SectorSubscription
    {
        public const string RpcName = "TortalPortal_AnchorSectors";

        // One renewal holds the subscription this long; the waiting client renews every RenewSeconds. The
        // gap is deliberately wide so a couple of dropped packets do not stall a shoot mid-load.
        private const float SubscriptionSeconds = 15f;
        public const float RenewSeconds = 5f;

        private sealed class Sub
        {
            public Vector3 Pos;
            public float Expires;
        }

        // Keyed by peer uid. A handful of entries at most - one per 1.2 client currently mid-shoot.
        private static readonly Dictionary<long, Sub> Subs = new Dictionary<long, Sub>();
        private static readonly List<ZDO> TempSectorZdos = new List<ZDO>();

        public static void OnZNetStart()
        {
            Subs.Clear();
            if (ZRoutedRpc.instance == null) return;
            // Registered on every peer for symmetry; only a server ever receives it, because clients only
            // ever send it via the targetless InvokeRoutedRPC overload, which routes to the server.
            ZRoutedRpc.instance.Register<Vector3>(RpcName, RPC_AnchorSectors);
        }

        // ---- client side -----------------------------------------------------------------------------------

        public static void Renew(Vector3 pos)
        {
            try
            {
                ZRoutedRpc.instance?.InvokeRoutedRPC(RpcName, pos);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: renewing the sector subscription failed (non-fatal). Reason: {ex}");
            }
        }

        // Vector3.zero is the release. A real anchor position can never be exactly the world origin - that
        // is open ocean at the centre of the map, and portals sit on ground.
        public static void Release() => Renew(Vector3.zero);

        // ---- server side -----------------------------------------------------------------------------------

        private static void RPC_AnchorSectors(long sender, Vector3 pos)
        {
            try
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

                if (pos == Vector3.zero)
                {
                    Subs.Remove(sender);
                    return;
                }

                Subs[sender] = new Sub { Pos = pos, Expires = Time.realtimeSinceStartup + SubscriptionSeconds };
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(RPC_AnchorSectors)} failed (non-fatal). Reason: {ex}");
            }
        }

        [HarmonyPatch(typeof(ZDOMan), "CreateSyncList")]
        [HarmonyPostfix]
        private static void ZDOMan_CreateSyncList_Postfix(ZDOMan __instance, ZDOMan.ZDOPeer peer, List<ZDO> toSync)
        {
            if (Subs.Count == 0) return;
            try
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                if (!Subs.TryGetValue(peer.m_peer.m_uid, out Sub sub)) return;

                if (Time.realtimeSinceStartup > sub.Expires)
                {
                    Subs.Remove(peer.m_peer.m_uid);
                    return;
                }

                // When the subscription sits inside the peer's own active area their ordinary stream is
                // already carrying these sectors - appending would put the same ZDOs in the packet twice.
                // Mirrors the guard in SnapshotAnchor's CreateObjects prefix.
                Vector2i subZone = ZoneSystem.GetZone(sub.Pos);
                Vector2i peerZone = ZoneSystem.GetZone(peer.m_peer.GetRefPos());
                int overlap = ZoneSystem.instance.m_activeArea + SnapshotAnchor.AnchorRing;
                if (Mathf.Abs(subZone.x - peerZone.x) <= overlap &&
                    Mathf.Abs(subZone.y - peerZone.y) <= overlap)
                {
                    return;
                }

                TempSectorZdos.Clear();
                __instance.FindSectorObjects(subZone, SnapshotAnchor.AnchorRing, 0, TempSectorZdos);
                foreach (ZDO zdo in TempSectorZdos)
                {
                    if (peer.ShouldSend(zdo)) toSync.Add(zdo);
                }
            }
            catch (Exception ex)
            {
                // Fail closed: dropping every subscription puts the server's send path back to pure vanilla.
                Subs.Clear();
                Plugin.Log.LogError($"TortalPortal: feeding subscribed sectors into a sync list failed (non-fatal, subscriptions dropped). Reason: {ex}");
            }
        }
    }
}
