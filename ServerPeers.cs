using System;
using System.Collections.Generic;
using UnityEngine;

namespace TortalPortal
{
    // One connected player as the server actually sees them: the peer plus their character ZDO.
    //
    // Player.GetAllPlayers() walks the LOCAL instance list, and a dedicated server never instantiates a
    // Player, so on the one machine the corpse run and the health scan run on that list is always empty. What
    // the server does hold for every connected player is the character ZDO behind ZNetPeer.m_characterID,
    // and that is what this yields. A listen host never appears in its own peer list, so its player is added
    // by hand from the local Player object.
    public readonly struct ConnectedCharacter
    {
        // The routed-RPC address of this peer, or 0 for the listen host's own player.
        public readonly long PeerId;
        public readonly ZDO Zdo;
        public readonly bool IsLocal;

        public ConnectedCharacter(long peerId, ZDO zdo, bool isLocal)
        {
            PeerId = peerId;
            Zdo = zdo;
            IsLocal = isLocal;
        }

        // The stable identity - PlayerProfile.m_playerID, copied into the world as s_playerID. Never
        // Peer.m_uid (a connection, new every session) and never Zdo.m_uid.UserID (the ZDOMan session that
        // minted the ZDO). 0 until the owning client's Player.SetPlayerID has run.
        public long PlayerId => Zdo != null ? Zdo.GetLong(ZDOVars.s_playerID, 0L) : 0L;

        public string Name => Zdo != null ? Zdo.GetString(ZDOVars.s_playerName, "") : "";

        public Vector3 Position => Zdo != null ? Zdo.GetPosition() : Vector3.zero;
    }

    public static class ServerPeers
    {
        public static List<ConnectedCharacter> All()
        {
            List<ConnectedCharacter> result = new List<ConnectedCharacter>();
            if (ZNet.instance == null || ZDOMan.instance == null) return result;

            try
            {
                foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                {
                    // m_characterID arrives via RPC_CharacterID after the peer is otherwise ready, so a ready
                    // peer with no character yet is normal for the first moments of a connection.
                    if (peer == null || !peer.IsReady() || peer.m_characterID == ZDOID.None) continue;
                    ZDO zdo = ZDOMan.instance.GetZDO(peer.m_characterID);
                    if (zdo == null || !zdo.IsValid()) continue;
                    result.Add(new ConnectedCharacter(peer.m_uid, zdo, false));
                }

                if (!ZNet.instance.IsDedicated() && Player.m_localPlayer != null)
                {
                    ZNetView nview = Player.m_localPlayer.GetComponent<ZNetView>();
                    if (nview != null && nview.IsValid()) result.Add(new ConnectedCharacter(0L, nview.GetZDO(), true));
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"TortalPortal: could not list the connected characters (non-fatal). Reason: {ex.Message}");
            }
            return result;
        }

        public static bool TryFind(long playerId, out ConnectedCharacter who)
        {
            foreach (ConnectedCharacter cc in All())
            {
                if (cc.PlayerId == playerId)
                {
                    who = cc;
                    return true;
                }
            }
            who = default;
            return false;
        }

        // Server-to-player feedback. Player registers a message RPC on its own character ZDO - owner only,
        // which is exactly the recipient - that hands the text to MessageHud. TopLeft is the ordinary
        // pickup-style toast; Center is the big fading line. The listen host's own player is messaged directly.
        public static void Toast(ConnectedCharacter who, string message, bool center = false)
        {
            try
            {
                if (who.IsLocal)
                {
                    if (Player.m_localPlayer != null) Player.m_localPlayer.Message(center ? MessageHud.MessageType.Center : MessageHud.MessageType.TopLeft, message);
                    return;
                }
                if (ZRoutedRpc.instance == null || who.Zdo == null) return;
                int type = (int)(center ? MessageHud.MessageType.Center : MessageHud.MessageType.TopLeft);
                ZRoutedRpc.instance.InvokeRoutedRPC(who.PeerId, who.Zdo.m_uid, PlayerMessageRpc, type, message, 0);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"TortalPortal: could not message {who.Name} (non-fatal). Reason: {ex.Message}");
            }
        }

        // The name Player.Awake registers its toast handler under.
        public const string PlayerMessageRpc = "Message";

        // The write primitive for every server-side change to a portal ZDO. Claiming ownership is what makes a
        // vanilla client's own routed RPCs to that ZDO (retag, PIN) drop while the write is in flight, but it
        // is not durable - ZDOMan.ReleaseNearbyZDOS hands ownership back to any nearby peer every couple of
        // seconds - so it is re-claimed before every write, never assumed. The extra counter write bumps the
        // data revision a second time, so this write strictly outranks a client write that landed in the same
        // tick; ForceSendZDO pushes the result now rather than on the next sync cadence.
        public static void ClaimAndWrite(ZDO zdo, Action<ZDO> writes)
        {
            if (zdo == null || !zdo.IsValid() || ZDOMan.instance == null) return;
            try
            {
                zdo.SetOwner(ZDOMan.GetSessionID());
                writes(zdo);
                zdo.Set(PortalKeys.Revision, zdo.GetInt(PortalKeys.Revision, 0) + 1);
                ZDOMan.instance.ForceSendZDO(zdo.m_uid);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"TortalPortal: a server-side write to portal {zdo.m_uid} failed (non-fatal). Reason: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Is this peer on the server's own adminlist? The same test tortal_scatterclear uses.
        public static bool IsAdminPeer(long sender, out string host)
        {
            host = "";
            try
            {
                if (ZNet.instance == null) return false;
                ZNetPeer peer = ZNet.instance.GetPeer(sender);
                host = peer != null && peer.m_socket != null ? peer.m_socket.GetHostName() : "";
                return ZNet.instance.IsAdmin(host);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"TortalPortal: could not check the adminlist for peer {sender} (treated as not an admin). Reason: {ex.Message}");
                return false;
            }
        }
    }
}
