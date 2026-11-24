using HarmonyLib;
using System;
using System.Linq;

namespace TortalPortal
{
    [HarmonyPatch]
    public static class FilterPatch
    {
        // 1.0 gave both IsTeleportable overloads a `bool allowAllItems` - the portal's own m_allowAllItems,
        // which pre-1.0 TeleportWorld OR'd around the call (`IsTeleportable() || m_allowAllItems`) and now
        // passes in instead. Pre-1.0 that OR meant an allow-all portal never consulted this filter at all,
        // so to keep behaviour identical the flag hands the decision straight back to vanilla, which answers
        // true for everything short of its own hard block (an item with m_toolTier >= 1000). Decided on
        // 2026-09-09 for the 1.0.7 migration: honouring the flag IS the no-change option. The parameter
        // name must match the target's exactly - Harmony binds patch arguments by name.
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.IsTeleportable))]
        [HarmonyPrefix]
        public static bool HumanoidIsTeleportable_Prefix(Humanoid __instance, bool allowAllItems, ref bool __result)
        {
            try
            {
                if (allowAllItems) return true; // vanilla decides - see the note above

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
        public static bool InventoryIsTeleportable_Prefix(Inventory __instance, bool allowAllItems, ref bool __result)
        {
            try
            {
                if (allowAllItems) return true; // same allow-all rule as the Humanoid prefix above

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
