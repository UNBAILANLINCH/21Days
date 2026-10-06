// 职责：道具能不能用的纯判定——「拥有 + 本场没用过」两条同时成立才可以用。
//
// 本模块**不读背包**：持有与否由调用方通过 `IBattleItemInventory` 或直接传布尔值注入
// （`docs/design/features-spotlight/09_BOSS战.md:276` 把背包读写划给 Loot / Inventory 模块）。
// 也不做界面高亮：07:42 的高亮 / 置暗 / 不显示是表现层的事，本模块只给判定结果。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:42`、:58；
// 转写：`docs/design/features-spotlight/09_BOSS战.md:193`（R45）。

namespace Game.TurnBased
{
    /// <summary>道具使用的纯判定。</summary>
    public static class ItemUseRules
    {
        /// <summary>
        /// 判定这件道具现在能不能用。
        /// </summary>
        /// <param name="owned">玩家是否拥有（由背包快照注入）。</param>
        /// <param name="usedThisBattle">本场战斗是否已经用过。</param>
        /// <param name="oncePerBattle">配置项：一件道具一场是否只能用一次（07:42 的口径，占位 true）。</param>
        public static ItemUseDecision Decide(bool owned, bool usedThisBattle, bool oncePerBattle)
        {
            if (!owned)
            {
                return ItemUseDecision.Refuse(ItemUseReject.NotOwned);
            }

            if (oncePerBattle && usedThisBattle)
            {
                return ItemUseDecision.Refuse(ItemUseReject.UsedThisBattle);
            }

            return ItemUseDecision.Allow();
        }

        /// <summary>拒绝原因的中文描述。</summary>
        public static string Describe(ItemUseReject reject)
        {
            switch (reject)
            {
                case ItemUseReject.NotOwned:
                    return "未拥有（07:42）";
                case ItemUseReject.UsedThisBattle:
                    return "本场战斗已经用过（07:42「用过了则置暗」，R45）";
                case ItemUseReject.WrongPhase:
                    return "不在「施放招式之前」的时点（07:58）";
                case ItemUseReject.InvalidItem:
                    return "道具 id 为空";
                default:
                    return "未拒绝";
            }
        }
    }
}
