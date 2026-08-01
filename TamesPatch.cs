using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TortalPortal
{
    [HarmonyPatch]
    public static class TamesPatch
    {
        [HarmonyPatch(typeof(Player), nameof(Player.TeleportTo))]
        [HarmonyPrefix]
        public static void Player_TeleportTo_Prefix(Player __instance, Vector3 pos, Quaternion rot, bool distantTeleport)
        {
            try
            {
                if (!Configuration.allowTames.Value) return;

                float searchRadius = Configuration.tameFollowRadius.Value;
                List<Character> allCharacters = Character.GetAllCharacters();

                foreach (Character c in allCharacters)
                {
                    if (c == __instance) continue;

                    if (Vector3.Distance(__instance.transform.position, c.transform.position) <= searchRadius)
                    {
                        Tameable tame = c.GetComponent<Tameable>();
                        if (tame != null && c.IsTamed())
                        {
                            // Check if it belongs to the player or follows the player
                            MonsterAI ai = c.GetComponent<MonsterAI>();
                            if (ai != null && ai.GetFollowTarget() == __instance.gameObject)
                            {
                                // Teleport the tame
                                Vector3 offset = UnityEngine.Random.insideUnitSphere * 2f;
                                offset.y = 0;
                                MoveCreature(c, pos + offset, rot);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(Player_TeleportTo_Prefix)} failed (non-fatal). Reason: {ex}");
            }
        }

        // ---- riding through -----------------------------------------------------------------------------

        // Deliberately generic: the rider's controller is taken through IDoodadController rather than cast
        // to the vanilla Sadle, because custom saddle items and OdinMounts-style creatures ride through the
        // same interface with their own controller components. The one hard requirement is that the
        // controlled thing is a tamed Character - that is what keeps ships, carts and turrets (also doodad
        // controllers) from being dragged through a portal.
        public static bool FindMount(Player player, out Character mount, out Component controller)
        {
            mount = null;
            controller = null;

            try
            {
                if (player == null) return false;

                IDoodadController ctrl = player.m_doodadController;
                if (ctrl == null || !ctrl.IsValid()) return false;

                Component controlled = ctrl.GetControlledComponent();
                if (controlled == null) return false;

                // Vanilla Sadle hands back the Character itself; a custom controller may hand back its own
                // component sitting somewhere on the creature. Take whichever Character is actually there.
                Character c = controlled as Character;
                if (c == null) c = controlled.GetComponentInParent<Character>();
                if (c == null || !c.IsTamed()) return false;

                mount = c;
                // The CONTROLLER component is what gets Interact()ed to remount - that routes through the
                // owning mod's own request/attach RPCs, vanilla or otherwise.
                controller = ctrl as Component;
                return controller != null;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(FindMount)} failed (non-fatal, teleporting unmounted). Reason: {ex}");
                mount = null;
                controller = null;
                return false;
            }
        }

        // Riding THROUGH the teleport cannot work: while attached, the saddle writes the player's position
        // every physics tick, so the player has to come out of the saddle before the trip and be put back in
        // after it. StopDoodadControl runs the controller's own OnUseStop (release RPC plus AttachStop for
        // saddle-shaped controllers); the explicit AttachStop after it is for any custom controller that
        // forgot to detach its rider.
        public static void DismountForTeleport(Player player)
        {
            try
            {
                player.StopDoodadControl();
                if (player.IsAttached()) player.AttachStop();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(DismountForTeleport)} failed (non-fatal). Reason: {ex}");
            }
        }

        public static void SendMountAhead(Character mount, Vector3 pos, Quaternion rot)
        {
            try
            {
                // Beside the exit rather than on it, so the mount is not standing in the player's landing
                // spot when they materialise.
                MoveCreature(mount, pos + rot * Vector3.right * 2f, rot);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(SendMountAhead)} failed (non-fatal, the mount stays behind). Reason: {ex}");
            }
        }

        // Ownership first, always: on a dedicated server the creature is owned by whichever peer simulates
        // it, and moving a ZDO this peer does not own is silently undone at the next sync. Claiming makes
        // the move stick for everyone.
        private static void MoveCreature(Character c, Vector3 pos, Quaternion rot)
        {
            ZNetView nview = c.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid() && !nview.IsOwner()) nview.ClaimOwnership();

            c.transform.position = pos;
            c.transform.rotation = rot;

            Rigidbody rb = c.GetComponent<Rigidbody>();
            if (rb)
            {
                rb.linearVelocity = Vector3.zero;
            }
        }

        // Waits out the teleport (fade, area load), then asks the controller to seat the player again. The
        // remount goes through Interact -> the mod's own control-request RPC, exactly as if the player had
        // pressed E on the saddle - so whatever rules the saddle mod enforces (in-use, range, stamina) still
        // apply, and on a busy server the server still arbitrates who is in the seat.
        public static IEnumerator RemountWhenArrived(Player player, Component controller, Character mount)
        {
            float deadline = Time.realtimeSinceStartup + 25f;
            while (player != null && player.IsTeleporting() && Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForSeconds(0.2f);
            }

            // A beat for both bodies to land and settle before asking for the seat back.
            yield return new WaitForSeconds(0.6f);

            TryRemount(player, controller, mount);
        }

        // Split out of the iterator so it can sit in a try/catch.
        private static void TryRemount(Player player, Component controller, Character mount)
        {
            try
            {
                if (player == null || player.IsTeleporting() || player.IsDead()) return;
                if (controller == null || mount == null || mount.IsDead()) return;
                if (Vector3.Distance(player.transform.position, mount.transform.position) > 12f)
                {
                    Configuration.LogDebug("Mount arrived too far away to remount automatically.");
                    return;
                }

                Interactable seat = controller as Interactable;
                if (seat != null)
                {
                    Configuration.LogDebug($"Requesting remount on {mount.name}.");
                    seat.Interact(player, false, false);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: automatic remount failed (non-fatal - walk up and mount normally). Reason: {ex}");
            }
        }
    }
}
