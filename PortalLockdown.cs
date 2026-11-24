using HarmonyLib;
using System;

namespace TortalPortal
{
    // The removal veto for server-raised gates.
    //
    // Vanilla's ZDOMan.RPC_DestroyZDO reads a list of ZDOIDs out of a packet and forgets every one of them
    // WITHOUT ever looking at who sent it, so any peer on the table can tell every other peer to delete any
    // object in the world. A corpse gate pinned at a billion health is no defence against that: the gate is
    // not damaged, it is simply gone. The gates this mod raises are held in PortalGuard's lock set and a
    // destroy aimed at one is refused here.
    //
    // HandleDestroyedZDO rather than DestroyZDO because DestroyZDO is only the local queueing half - it adds
    // to m_destroySendList and nothing more - while HandleDestroyedZDO is where a destroy actually lands, and
    // it has exactly one caller in the whole assembly: the RPC. (An earlier note here claimed the choice was
    // about not breaking ZNetScene's "prefab will not instantiate" path. That was wrong: refusing either
    // method spins that loop identically. It never fires for a corpse gate anyway, because
    // Game.PortalPrefabHash is built from Game.m_portalPrefabs, which are real ZNetScene prefabs.)
    //
    // DELIBERATELY ITS OWN PATCH CLASS. Plugin.ApplyPatches calls PatchAll once per type inside one try/catch,
    // so every patch declared beside another shares its fate: if Valheim ever renames this private method,
    // PatchClassProcessor throws and takes the rest of the class down with it. Sitting next to
    // WearNTear.ApplyDamage - the one funnel every kind of damage reaches - that would have silently removed
    // damage immunity from every portal on the table because a removal patch could not bind. Separate classes
    // means one signature drift costs one layer and says so.
    [HarmonyPatch]
    public static class PortalLockdown
    {
        [HarmonyPatch(typeof(ZDOMan), "HandleDestroyedZDO")]
        [HarmonyPrefix]
        public static bool ZDOMan_HandleDestroyedZDO_Prefix(ZDOID uid)
        {
            try
            {
                if (!PortalGuard.IsLocked(uid)) return true;

                PortalGuard.CountRefusedRemoval(uid);

                // The refusal alone is not enough, and without this the veto is worse than no veto at all.
                // SendDestroyed broadcasts to peer 0, which the server relays to every OTHER peer, so by the
                // time we refuse here every client has already dropped its own copy. ZDOPeer.ShouldSend is
                // purely revision-based - an entry that is still present with unchanged revisions is never
                // resent - and nothing about a stationary gate bumps a revision, so the gate would exist on
                // the server and on nobody's screen until they reconnected. ForceSendZDO bypasses ShouldSend
                // for every peer and puts the portal back.
                try
                {
                    if (ZDOMan.instance != null) ZDOMan.instance.ForceSendZDO(uid);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"TortalPortal: refused a removal of corpse gate {uid} but could not force it back out to peers (non-fatal, a reconnect restores it). Reason: {ex.Message}");
                }
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(ZDOMan_HandleDestroyedZDO_Prefix)} failed, letting the removal through. Reason: {ex}");
                return true;
            }
        }
    }
}
