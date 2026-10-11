// 职责：道具格的三种样子（07:42「可以使用则高亮，用过了则置暗；未拥有或在之前战斗时用过了，则不显示」）。
// 为什么新建：一个类型一个文件；BattleHudRules.ItemLook 的返回值，界面只按它摆格子。
namespace Game.Battle
{
    /// <summary>道具格样子。</summary>
    public enum BattleItemLook
    {
        /// <summary>不显示（未拥有）。</summary>
        Hidden = 0,

        /// <summary>高亮：现在点得了。</summary>
        Lit = 1,

        /// <summary>置暗：本场用过，或现在不是能用道具的时点。</summary>
        Dim = 2,
    }
}
