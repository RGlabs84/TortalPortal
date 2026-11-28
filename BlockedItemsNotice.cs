using System;
using System.Collections.Generic;
using System.Linq;

namespace TortalPortal
{
    // 1.6.6. The centre-screen notice for a player who walks up to a gate carrying something that may not pass.
    // No Harmony patch here (so nothing to add to Plugin.PatchTypes): PortalBehaviorPatch calls Show once per
    // approach when its canTeleport test fails.
    //
    // The items are named by the SAME rules the gate applies - vanilla's Inventory.IsTeleportable (1.0.31: an item
    // with m_toolTier >= 1000 always blocks; otherwise one with m_teleportable false blocks unless the portal allows
    // all items or the TeleportAll world key is set) with FilterPatch's blacklist and whitelist on top when the
    // filter mode is WhitelistBlacklist. If none can be named (another mod blocked the trip), a plain notice is shown.
    public static class BlockedItemsNotice
    {
        private const int MaxNamed = 4;

        public static void Show(Player player, TeleportWorld portal)
        {
            try
            {
                if (player == null || MessageHud.instance == null) return;
                List<string> names = BlockedItemNames(player, portal != null && portal.m_allowAllItems);
                string text;
                if (names.Count == 0)
                {
                    text = "You are carrying something that can't go through a portal.";
                }
                else
                {
                    string list = string.Join(", ", names.Take(MaxNamed));
                    if (names.Count > MaxNamed) list += $" and {names.Count - MaxNamed} more";
                    text = $"You can't teleport with: {list}.\nStore or drop them to use the portal.";
                }
                player.Message(MessageHud.MessageType.Center, text);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: the can't-teleport notice failed (non-fatal). Reason: {ex}");
            }
        }

        // Distinct localized names of the carried items the gate refuses, in inventory order.
        public static List<string> BlockedItemNames(Player player, bool allowAllItems)
        {
            var names = new List<string>();
            Inventory inventory = player?.GetInventory();
            if (inventory == null) return names;

            bool filtered = Configuration.filterMode.Value == FilterMode.WhitelistBlacklist;
            string[] wList = filtered ? Split(Configuration.whitelistItems.Value) : Array.Empty<string>();
            string[] bList = filtered ? Split(Configuration.blacklistItems.Value) : Array.Empty<string>();
            bool teleportAll = allowAllItems
                || (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.TeleportAll));

            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (item?.m_shared == null) continue;
                string prefab = item.m_dropPrefab != null ? item.m_dropPrefab.name : null;

                bool blocked;
                if (item.m_shared.m_toolTier >= 1000) blocked = true;          // vanilla's hard block
                else if (teleportAll) blocked = false;
                else if (filtered && !string.IsNullOrEmpty(prefab) && bList.Contains(prefab)) blocked = true;
                else if (filtered && !string.IsNullOrEmpty(prefab) && wList.Contains(prefab)) blocked = false;
                else blocked = !item.m_shared.m_teleportable;

                if (!blocked) continue;
                string name = Localization.instance != null ? Localization.instance.Localize(item.m_shared.m_name) : item.m_shared.m_name;
                if (!names.Contains(name)) names.Add(name);
            }
            return names;
        }

        private static string[] Split(string raw) =>
            (raw ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
    }
}
