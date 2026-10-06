// 职责：玩家被晕眩时这一回合怎么过——原文没写，做成配置项。
//
// 出处：`docs/design/features-spotlight/09_BOSS战.md:198`（R47 末句「玩家被晕眩时怎么过回合原文没写」）、
// `待策划拍板问题.md:1269` C91 的可选方向：A 直接跳过 / B 只能防御、用道具 / C 其他。
// 占位 A（整个玩家回合跳过）。

namespace Game.TurnBased
{
    /// <summary>被晕眩的玩家回合的处理方式。</summary>
    public enum StunTurnPolicy
    {
        /// <summary>整个玩家回合跳过（占位口径）。</summary>
        SkipTurn = 0,

        /// <summary>不能出招，但还能用道具（对应 C91 的 B 方向）。</summary>
        ItemsOnly = 1,
    }
}
