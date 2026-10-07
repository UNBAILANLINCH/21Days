// 职责：战斗界面顶部道具格里的一格（07 画面表现：道具「可用则高亮、用过置暗、未拥有不显示」）。
// 为什么新建：背包里存的是 int id → 数量，界面要的是「名字 + 件数 + 本场用过没」；由 BattleItemInventory 按背包与账本拼出，一个类型一个文件。
namespace Game.Battle
{
    /// <summary>道具格（值类型，只在一次 WaitCommandAsync 调用内有效）。</summary>
    public readonly struct BattleItemSlot
    {
        public BattleItemSlot(string itemId, string displayName, int count, bool usedThisBattle)
        {
            ItemId = itemId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Count = count;
            UsedThisBattle = usedThisBattle;
        }

        /// <summary>道具 id（tbitem 主键的十进制字符串，回传给 <see cref="BattleCommand.UseItem"/>）。</summary>
        public string ItemId { get; }

        /// <summary>道具名（tbitem；查不到时「#id」）。</summary>
        public string DisplayName { get; }

        /// <summary>背包里现有件数（本场用掉的已扣，可能为 0）。</summary>
        public int Count { get; }

        /// <summary>本场已用过（07:42「用过了则置暗」）。</summary>
        public bool UsedThisBattle { get; }

        /// <summary>现在能不能点（高亮）。真正能否使用仍以 BattleSession.TryUseItem 的判定为准（还要看回合与时点）。</summary>
        public bool CanUse => Count > 0 && !UsedThisBattle;
    }
}
