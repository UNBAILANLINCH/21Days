// 职责：一次「使用道具」判定的结果码。
// 为什么新建：一个文件一个类型；与结果数据（ItemUseResult）分开，调用方只 switch 状态。

namespace Game.Inventory
{
    /// <summary>使用判定的结果码。除 <see cref="Ok"/> 外都不产生任何效果、也不扣件数。</summary>
    public enum ItemUseStatus
    {
        /// <summary>这件道具能使用，且背包件数够。</summary>
        Ok = 0,

        /// <summary>未知 id：使用表里没有这件道具（含 itemId 非正、使用表为 null / 为空）。</summary>
        UnknownItem = 1,

        /// <summary>使用表里那一行写坏：效果标识为空、消耗件数非正、道具 id 非正。</summary>
        InvalidSpec = 2,

        /// <summary>背包里这件道具的件数不够用一次（实际有几件见 <see cref="ItemUseResult.OwnedCount"/>）。</summary>
        NotEnough = 3,

        /// <summary>
        /// 效果标识没有执行者：<see cref="ItemUseResult.EffectId"/> 在注册表里查不到（本波次只接了占位治疗，
        /// 皮 / 面具类效果归 S1「换皮与附身」，见 ItemUseRules.cs 文件头）。
        /// <b>明确失败，不静默</b>——调用方要把它翻成给玩家看的中文短句。
        /// </summary>
        NoExecutor = 4,

        /// <summary>执行者拒绝了这个效果（例如玩家满血、目标不存在）。背包与件数都不动。</summary>
        EffectRejected = 5,
    }
}
