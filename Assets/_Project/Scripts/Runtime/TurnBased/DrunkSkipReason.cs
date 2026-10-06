// 职责：怪物回合为什么会跳过——给表现层（屏幕中央闪现提示）一个机器可读的原因。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:71-73`（三种文案各不相同，所以原因要分得开）。

namespace Game.TurnBased
{
    /// <summary>怪物跳过回合的原因。</summary>
    public enum DrunkSkipReason
    {
        /// <summary>没跳过。</summary>
        None = 0,

        /// <summary>微醺，20% 概率命中（07:71）。</summary>
        Tipsy = 1,

        /// <summary>薄醉，40% 概率命中（07:72）。</summary>
        Drunk = 2,

        /// <summary>酩酊，100% 跳过（07:73）。</summary>
        DeadDrunk = 3,
    }
}
