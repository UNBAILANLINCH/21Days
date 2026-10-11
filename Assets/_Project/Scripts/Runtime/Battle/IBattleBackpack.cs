// 职责：战斗侧看到的「背包」——读物品 id → 数量、问类别与名字、扣 1 件。只用 int id（tbitem 主键）。
// 为什么新建：LootService 的构造要真实 QuestService 与整套存档 / 通知，BattleItemInventory 的 string↔int 换算与
//   「只列消耗品」判定要能脱离它们单测；包成四个成员的接口（真实实现 LootBattleBackpack），同 ISessionStateSource 的做法。
using System.Collections.Generic;

namespace Game.Battle
{
    /// <summary>战斗用背包口（int id）。</summary>
    public interface IBattleBackpack
    {
        /// <summary>物品 id → 数量（只读视图，每次现取：读档会整体替换分区）。</summary>
        IReadOnlyDictionary<int, int> Items { get; }

        /// <summary>是不是消耗品（tbitem 类别 Consumable）；表未就绪或查不到时 false。</summary>
        bool IsConsumable(int itemId);

        /// <summary>道具名；查不到时「#id」。</summary>
        string NameOf(int itemId);

        /// <summary>扣 1 件；数量不够返回 false 且不改背包。</summary>
        bool TryConsumeOne(int itemId);
    }
}
