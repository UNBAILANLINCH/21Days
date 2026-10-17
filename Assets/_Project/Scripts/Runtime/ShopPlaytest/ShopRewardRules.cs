// 职责：三店成果与正式背包物品的映射、一次发放和酒肆乔装资格。
// 复用Loot发物品；各店规则不拥有跨店奖励，存档DTO也不执行玩法，故在组合模块集中接线。
using System;
using System.Collections.Generic;
namespace Game.ShopPlaytest
{
    public static class ShopRewardRules
    {
        public const int CostumeId = 1801;
        public const int ClothesId = 1802;
        public const int ClaspId = 1803;
        public const int BeltHookId = 1804;
        public const int RingId = 1805;
        private static readonly int[] ItemIds = { CostumeId, ClothesId, ClaspId, BeltHookId, RingId };
        private static readonly int[] Counts = { 1, 1, 1, 2, 3 };

        public static void GrantCompleted(ShopProgressSaveData progress, Func<int, int, bool> grant)
        {
            for (int i = 0; i < ItemIds.Length; i++)
            {
                bool completed = i == 0 ? progress.TheaterCompleted : i == 1 ? progress.ClothesReceived
                    : progress.Forging != null && progress.Forging.Submitted;
                if (completed && !progress.RewardedItems.Contains(ItemIds[i]) && grant(ItemIds[i], Counts[i]))
                    progress.RewardedItems.Add(ItemIds[i]);
            }
        }

        public static string MissingDisguise(IReadOnlyDictionary<int, int> items)
        {
            var missing = new List<string>();
            if (!Has(items, CostumeId, 1)) missing.Add("行头三样");
            if (!Has(items, ClothesId, 1)) missing.Add("黑色长衫");
            if (!Has(items, ClaspId, 1) || !Has(items, BeltHookId, 2) || !Has(items, RingId, 3)) missing.Add("金饰");
            return string.Join("、", missing);
        }

        private static bool Has(IReadOnlyDictionary<int, int> items, int id, int count)
            => items.TryGetValue(id, out int owned) && owned >= count;
    }
}
