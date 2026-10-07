// 职责：背包的只读查询口——本模块只问「有没有」，不读不写任何背包实现。
//
// 为什么要这个接口：道具的持有关系属于背包模块（`docs/design/features-spotlight/09_BOSS战.md:276`
// 把「物品 id → 数量」划给 Loot / Inventory）。回合制内核只依赖这个一方法的接口，
// 接线时用一个适配器把 Inventory 包起来即可，规则层不需要改一行（同
// `Game.Identity.IExposureOutcomePolicy` 的做法）。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:42`（未拥有则不显示）、:58（施放招式前用道具）。

namespace Game.TurnBased
{
    /// <summary>背包的只读查询口。实现由接线侧提供（适配 Inventory / Loot）。</summary>
    public interface IBattleItemInventory
    {
        /// <summary>玩家是否拥有某件道具。</summary>
        /// <param name="itemId">道具 id，非空。</param>
        bool Owns(string itemId);
    }
}
