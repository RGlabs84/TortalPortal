using System;
using HarmonyLib;
using UnityEngine;

namespace TortalPortal
{
    // The grave ward: a short spell of invulnerability for a player stepping out of their own corpse gate.
    //
    // A corpse gate drops a naked player beside the very thing that killed them - often still standing over the
    // tombstone - and the distant-teleport load leaves them blind for the first moments on the ground. The ward
    // covers that: for CorpseRunArrivalWardSeconds after the trip lands, every hit on the local player is refused.
    //
    // Client side, on the player's own machine, because that is where vanilla applies damage to a player:
    // Character.RPC_Damage returns early on every peer but the owner, and the owner of a player is its client.
    // Refusing the RPC there refuses everything that funnels through it - blows, arrows, fall damage, burning,
    // poison, drowning - and stagger with it.
    //
    // The clock starts on arrival, not on stepping in: vanilla already refuses damage while IsTeleporting(), and a
    // distant teleport can take many seconds to find the floor, so a clock started at the bed would be spent in the
    // loading screen. The ward breaks early the moment the player swings, so it shelters a gear-up and not a fight.
    [HarmonyPatch]
    public static class CorpseWard
    {
        private static bool _pending;      // a corpse trip has started; the ward begins when it lands
        private static float _until = -1f; // Time.time the ward ends; negative = no ward

        public static bool Active => _until > 0f && Time.time < _until;

        // Called by CorpseRun.TravelToGrave once TeleportTo has accepted the trip.
        public static void OnTripStarted()
        {
            float seconds = Configuration.corpseRunArrivalWardSeconds.Value;
            _pending = seconds > 0f;
            _until = -1f;
        }

        private static void End(string why, bool tellPlayer)
        {
            // "Was raised", not Active: on a time-out Active has already gone false by the frame this runs.
            bool wasActive = _until > 0f;
            _pending = false;
            _until = -1f;
            if (wasActive)
            {
                Configuration.LogDebug($"Corpse run: grave ward ended ({why}).");
                if (tellPlayer) Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, "The grave ward fades.");
            }
        }

        // Lands the pending ward once the teleport is over. UpdateTeleport runs every frame on the local player, so
        // its postfix sees the frame m_teleporting drops; it also lets an expired ward announce itself.
        [HarmonyPatch(typeof(Player), "UpdateTeleport")]
        [HarmonyPostfix]
        public static void Player_UpdateTeleport_Postfix(Player __instance)
        {
            try
            {
                if (__instance != Player.m_localPlayer) return;

                if (_pending && !__instance.IsTeleporting())
                {
                    _pending = false;
                    float seconds = Configuration.corpseRunArrivalWardSeconds.Value;
                    if (seconds <= 0f) return;
                    _until = Time.time + seconds;
                    __instance.Message(MessageHud.MessageType.Center, $"Grave ward: {seconds:0} s");
                    Configuration.LogDebug($"Corpse run: grave ward up for {seconds:0.#} s.");
                    return;
                }

                if (_until > 0f && Time.time >= _until) End("timed out", tellPlayer: true);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(Player_UpdateTeleport_Postfix)} failed (non-fatal). Reason: {ex}");
            }
        }

        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        [HarmonyPrefix]
        public static bool Character_RPC_Damage_Prefix(Character __instance)
        {
            try
            {
                if (!Active || __instance == null || __instance != Player.m_localPlayer) return true;
                Configuration.LogDebug("Corpse run: grave ward refused a hit.");
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(Character_RPC_Damage_Prefix)} failed, letting the hit through. Reason: {ex}");
                return true;
            }
        }

        // Swinging ends the ward: it is for picking up your things, not for fighting what killed you.
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
        [HarmonyPostfix]
        public static void Humanoid_StartAttack_Postfix(Humanoid __instance, bool __result)
        {
            try
            {
                if (__result && __instance == Player.m_localPlayer && Active) End("player attacked", tellPlayer: true);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(Humanoid_StartAttack_Postfix)} failed (non-fatal). Reason: {ex}");
            }
        }

        // A death or a fresh spawn never carries a ward over.
        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        [HarmonyPostfix]
        public static void Player_OnSpawned_Postfix(Player __instance)
        {
            if (__instance == Player.m_localPlayer) End("spawned", tellPlayer: false);
        }
    }
}
