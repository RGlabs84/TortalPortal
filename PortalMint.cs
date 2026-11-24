using System;
using System.Collections.Generic;
using UnityEngine;

namespace TortalPortal
{
    // Raises and removes portals as bare ZDOs, server-side, with no GameObject and no player nearby.
    //
    // tortal_scatter instantiates the real prefab instead and lets ZNetScene cull the instance, which is the
    // right tool when a player is standing at a console. A corpse gate is minted by the server for a spot
    // nobody is near, so there is nothing to instantiate into; the ZDO has to be fabricated by hand, and the
    // fabrication has to be exact or the portal vanishes on the next world load or never replicates at all.
    // The sequence below is ZNetView.Awake's own, line for line: CreateNewZDO pools the ZDO and files it into
    // ZDOMan's portal registry (AddIfPortal, because the hash is in Game.PortalPrefabHash), but sets none of
    // Persistent / Type / Distant / prefab / rotation on its own, so those five follow explicitly.
    //
    // Behaviour the CLIENT applies when it instantiates the gate rides along as vanilla's own LoadFields keys:
    // the piece cannot be hammered down (a portal refunds surtling cores, and dying is free), mobs never pick
    // it as a target, and WearNTear treats it as immune. The guard in PortalGuard covers the rest.
    public static class PortalMint
    {
        private static int _cachedPrefabHash;

        // lockRemoval writes Piece.m_canBeRemoved=false through LoadFields so the hammer refuses the gate; a
        // portal recreated from a snapshot is an ordinary portal again and must stay removable by its builder.
        public static ZDO Create(Vector3 pos, Quaternion rot, string tag, string kind, long owner, bool immune, bool lockRemoval = true)
        {
            int hash = ResolvePortalPrefabHash();
            if (hash == 0)
            {
                Plugin.Log.LogError("TortalPortal: no portal prefab hash could be resolved - Game.PortalPrefabHash is empty, so no gate can be raised.");
                return null;
            }

            ZDO zdo;
            try
            {
                zdo = ZDOMan.instance.CreateNewZDO(pos, hash);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: CreateNewZDO failed while raising a gate at {pos}. Reason: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
            if (zdo == null) return null;

            ServerPeers.ClaimAndWrite(zdo, z =>
            {
                // ZNetView.Awake's fabrication order, verbatim.
                z.Persistent = true;
                z.Type = ZDO.ObjectType.Default;
                z.Distant = false;
                z.SetPrefab(hash);
                z.SetRotation(rot);

                z.Set(ZDOVars.s_tag, tag ?? "");
                if (!string.IsNullOrEmpty(kind))
                {
                    z.Set(ZDOVars.s_tagauthor, "server");
                    z.Set(PortalKeys.Kind, kind);
                    z.Set(PortalKeys.Owner, owner);
                }

                if (lockRemoval || immune)
                {
                    z.Set(PortalKeys.HasFields, true);
                    z.Set(PortalKeys.HasFieldsPiece, true);
                }
                if (lockRemoval) z.Set(PortalKeys.PieceCanBeRemoved, false);

                if (immune)
                {
                    z.Set(PortalKeys.PieceRandomTarget, false);
                    z.Set(PortalKeys.PiecePrimaryTarget, false);

                    z.Set(PortalKeys.HasFieldsWearNTear, true);
                    z.Set(PortalKeys.WearNTearHealth, PortalGuard.ImmuneHealth);
                    z.Set(PortalKeys.WearNTearNoSupportWear, false);
                    z.Set(PortalKeys.WearNTearNoRoofWear, false);
                    z.Set(PortalKeys.WearNTearSnowDamageImmune, true);
                    z.Set(PortalKeys.WearNTearAshDamageImmune, true);
                    z.Set(PortalKeys.WearNTearBurnable, false);

                    z.Set(ZDOVars.s_health, PortalGuard.ImmuneHealth);
                    z.Set(PortalKeys.Immune, true);
                }
            });

            // An immune gate is one the server raised and nobody else may take down. The lock lives in memory
            // rather than on the ZDO so that this mod's own removals - which all come back through Destroy
            // below - are not refused by it.
            if (immune) PortalGuard.Lock(zdo.m_uid);

            return zdo;
        }

        // Owner-gated destroy. DestroyZDO is a silent no-op on something this peer does not own, so ownership
        // is claimed first; if the portal happens to be instantiated here (a listen host standing beside it),
        // ZNetScene takes the instance and the ZDO down together.
        public static void Destroy(ZDO zdo)
        {
            if (zdo == null) return;
            // Released even when there is nothing left to destroy: an already-invalid ZDO is one whose lock
            // would otherwise sit in the set until the world changed.
            PortalGuard.Unlock(zdo.m_uid);
            if (!zdo.IsValid() || ZDOMan.instance == null) return;
            try
            {
                // The lock was released above, BEFORE the destroy rather than after: ZDOMan.DestroyZDO queues
                // the removal and SendDestroyed broadcasts it to peer 0, which includes this peer, so our own
                // destroy comes back through HandleDestroyedZDO and would be refused by our own veto.
                //
                // Ownership FIRST, and on both branches. ZNetScene.Destroy only forwards to DestroyZDO when
                // this peer owns the ZDO and is a silent no-op otherwise - and a gate whose owner is the client
                // standing at it is exactly the case that reaches the instance branch on a listen host. Taking
                // ownership up front means neither branch can quietly do nothing.
                zdo.SetOwner(ZDOMan.GetSessionID());

                ZNetView live = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(zdo) : null;
                if (live != null && live.IsValid())
                {
                    ZNetScene.instance.Destroy(live.gameObject);
                    return;
                }
                ZDOMan.instance.DestroyZDO(zdo);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"TortalPortal: could not remove portal {zdo.m_uid} (non-fatal). Reason: {ex.Message}");
            }
        }

        // The portal prefab gates are minted with: whichever entry of Game.PortalPrefabHash resolves to a name
        // containing "wood" - the always-available base-tier portal - or the first entry. Never hard-coded.
        public static int ResolvePortalPrefabHash()
        {
            if (_cachedPrefabHash != 0) return _cachedPrefabHash;
            if (Game.instance == null) return 0;

            List<int> hashes = Game.instance.PortalPrefabHash;
            if (hashes == null || hashes.Count == 0) return 0;

            if (ZNetScene.instance != null)
            {
                foreach (int hash in hashes)
                {
                    GameObject prefab = ZNetScene.instance.GetPrefab(hash);
                    if (prefab != null && prefab.name.IndexOf("wood", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _cachedPrefabHash = hash;
                        return hash;
                    }
                }
            }
            _cachedPrefabHash = hashes[0];
            return _cachedPrefabHash;
        }

        // A world change means a new prefab table, in principle.
        public static void Reset()
        {
            _cachedPrefabHash = 0;
        }
    }
}
