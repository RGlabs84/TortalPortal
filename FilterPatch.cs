using HarmonyLib;
using System;
using System.Linq;

namespace TortalPortal
{
    [HarmonyPatch]
    public static class FilterPatch
    {
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.IsTeleportable))]
        [HarmonyPrefix]
        public static bool HumanoidIsTeleportable_Prefix(Humanoid __instance, ref bool __result)
        {
            try
            {
                if (Configuration.IsAdmin() || Configuration.filterMode.Value == FilterMode.BypassAll)
                {
                    __result = true;
                    return false;
                }

                if (Configuration.filterMode.Value == FilterMode.Vanilla)
                {
                    return true; // Let vanilla run
                }

                var items = __instance.GetInventory().GetAllItems();
                string[] wList = Configuration.whitelistItems.Value
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .ToArray();
                string[] bList = Configuration.blacklistItems.Value
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .ToArray();

                if (Configuration.filterMode.Value == FilterMode.WhitelistBlacklist)
                {
                    bool useWhitelist = wList.Length > 0;
                    bool useBlacklist = bList.Length > 0;

                    foreach (var item in items)
                    {
                        string prefabName = item.m_dropPrefab?.name;
                        bool isVanillaTeleportable = item.m_shared.m_teleportable;

                        // 1. Blacklist explicitly blocks
                        if (useBlacklist && !string.IsNullOrEmpty(prefabName) && bList.Contains(prefabName))
                        {
                            __result = false;
                            return false;
                        }

                        // 2. Whitelist explicitly allows
                        if (useWhitelist && !string.IsNullOrEmpty(prefabName) && wList.Contains(prefabName))
                        {
                            continue; // Bypass vanilla block for this item
                        }

                        // 3. Vanilla fallback
                        if (!isVanillaTeleportable)
                        {
                            __result = false;
                            return false;
                        }
                    }

                    __result = true;
                    return false; // Complete iteration passed
                }

                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(HumanoidIsTeleportable_Prefix)} failed, falling back to vanilla behavior. Reason: {ex}");
                return true;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.IsTeleportable))]
        [HarmonyPrefix]
        public static bool InventoryIsTeleportable_Prefix(Inventory __instance, ref bool __result)
        {
            try
            {
                if (Configuration.IsAdmin() || Configuration.filterMode.Value == FilterMode.BypassAll)
                {
                    __result = true;
                    return false;
                }

                if (Configuration.filterMode.Value == FilterMode.Vanilla)
                {
                    return true;
                }

                var items = __instance.GetAllItems();
                string[] wList = Configuration.whitelistItems.Value
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .ToArray();
                string[] bList = Configuration.blacklistItems.Value
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .ToArray();

                if (Configuration.filterMode.Value == FilterMode.WhitelistBlacklist)
                {
                    bool useWhitelist = wList.Length > 0;
                    bool useBlacklist = bList.Length > 0;

                    foreach (var item in items)
                    {
                        string prefabName = item.m_dropPrefab?.name;
                        bool isVanillaTeleportable = item.m_shared.m_teleportable;

                        if (useBlacklist && !string.IsNullOrEmpty(prefabName) && bList.Contains(prefabName))
                        {
                            __result = false;
                            return false;
                        }

                        if (useWhitelist && !string.IsNullOrEmpty(prefabName) && wList.Contains(prefabName))
                        {
                            continue;
                        }

                        if (!isVanillaTeleportable)
                        {
                            __result = false;
                            return false;
                        }
                    }

                    __result = true;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: {nameof(InventoryIsTeleportable_Prefix)} failed, falling back to vanilla behavior. Reason: {ex}");
                return true;
            }
        }
    }
}
