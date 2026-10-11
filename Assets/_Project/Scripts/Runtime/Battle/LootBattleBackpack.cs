// 职责：IBattleBackpack 的真实实现——数量读 LootService.Items、扣减走 LootService.TryConsume、类别与名字查 tbitem。
// 为什么新建：背包数据的写入方只有 Loot（loot-module-guide「禁止事项」），战斗侧不能自己改 LootSaveData；
//   这层薄适配放在战斗模块（依赖方向 Battle → Loot），Loot 不认识战斗。
using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core.Config;
using Game.Loot;

namespace Game.Battle
{
    /// <summary>经 Loot 的背包口（根作用域单例）。</summary>
    public sealed class LootBattleBackpack : IBattleBackpack
    {
        private readonly LootService loot;
        private readonly IConfigService config;

        public LootBattleBackpack(LootService loot, IConfigService config)
        {
            this.loot = loot ?? throw new ArgumentNullException(nameof(loot));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public IReadOnlyDictionary<int, int> Items => loot.Items;

        public bool IsConsumable(int itemId)
        {
            global::cfg.Item item = Find(itemId);
            return item != null && item.Category == global::cfg.EItemCategory.Consumable;
        }

        public string NameOf(int itemId)
        {
            global::cfg.Item item = Find(itemId);
            return item != null && !string.IsNullOrEmpty(item.Name) ? item.Name : "#" + itemId.ToString(CultureInfo.InvariantCulture);
        }

        public bool TryConsumeOne(int itemId) => loot.TryConsume(itemId, 1);

        // 表未就绪（直接 Play 玩法场景）时按「查不到」处理，不让开战路径崩（同 InventoryPanelController.TryGetTable）。
        private global::cfg.Item Find(int itemId)
        {
            try
            {
                return config.Tables.TbItem.GetOrDefault(itemId);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }
}
