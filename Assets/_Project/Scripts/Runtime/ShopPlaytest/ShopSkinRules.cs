// 职责：五店身份与背包皮的固定对应；奖励规则不负责身份，通用背包不认识店内职业，故独立映射。
using System;
namespace Game.ShopPlaytest
{
    public static class ShopSkinRules
    {
        private static readonly string[] Roles = { "apprentice", "director", "musician", "customer0", "customer1", "customer2", "officer", "smith", "guest" };
        public static int ItemId(string role)
        {
            int index = Array.IndexOf(Roles, role);
            return index < 0 ? 0 : 1901 + index;
        }
        public static string Role(int itemId) => itemId >= 1901 && itemId <= 1909 ? Roles[itemId - 1901] : "";
    }
}
