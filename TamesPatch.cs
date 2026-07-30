using HarmonyLib;
using System;
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

                float searchRadius = 10f;
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
                                c.transform.position = pos + offset;
                                c.transform.rotation = rot;

                                // Try to reset velocity if rigid body exists
                                Rigidbody rb = c.GetComponent<Rigidbody>();
                                if (rb)
                                {
                                    rb.linearVelocity = Vector3.zero;
                                }
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
    }
}
