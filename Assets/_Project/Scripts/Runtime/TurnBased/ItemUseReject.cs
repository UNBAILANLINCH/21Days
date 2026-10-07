// 职责：道具使用请求被拒的原因。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:42`「道具如可以使用则高亮，用过了则置暗；
// 如果未拥有或在之前战斗时用过了，则不显示」；
// 转写：`docs/design/features-spotlight/09_BOSS战.md:193`（R45「一次战斗内每件道具只能用一次」）。

namespace Game.TurnBased
{
    /// <summary>道具使用请求被拒的原因。</summary>
    public enum ItemUseReject
    {
        /// <summary>没被拒（可以用）。</summary>
        None = 0,

        /// <summary>玩家没有这件道具（07:42「未拥有……则不显示」）。</summary>
        NotOwned = 1,

        /// <summary>本场战斗已经用过这件道具了（07:42「用过了则置暗」）。</summary>
        UsedThisBattle = 2,

        /// <summary>现在不是「施放招式之前」的时点（07:58「玩家可以在施放招式之前使用道具」）。</summary>
        WrongPhase = 3,

        /// <summary>道具 id 为空。</summary>
        InvalidItem = 4,
    }
}
