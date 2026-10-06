// 职责：BOSS 醉酒四档（S7 的核心机制）。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:70-73`：
//   状态1 正常 0-49（无特殊效果，不在头顶显示）
//   状态2 微醺 50-79（怪物回合 20% 概率跳过回合）
//   状态3 薄醉 80-99（怪物回合 40% 概率跳过回合）
//   状态4 酩酊 100（100% 跳过回合，醉酒值下降 50，持续 2 回合）
// 转写：`docs/design/features-spotlight/09_BOSS战.md:197`（R46）。

namespace Game.TurnBased
{
    /// <summary>BOSS 醉酒档位。</summary>
    public enum DrunkTier
    {
        /// <summary>正常：0-49，无特殊效果（07:70）。</summary>
        Normal = 0,

        /// <summary>微醺：50-79，怪物回合 20% 概率跳过（07:71）。</summary>
        Tipsy = 1,

        /// <summary>薄醉：80-99，怪物回合 40% 概率跳过（07:72）。</summary>
        Drunk = 2,

        /// <summary>酩酊：100，100% 跳过，醉酒值 -50，持续 2 回合（07:73）。</summary>
        DeadDrunk = 3,
    }
}
