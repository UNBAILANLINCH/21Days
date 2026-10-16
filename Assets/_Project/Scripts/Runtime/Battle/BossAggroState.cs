// 职责：BOSS 主动发难（BossAggro）的状态（PRP/combat-iteration D6）。
// 为什么新建：一个类型一个文件；测试与埋点（boss_aggro_state 的 from / to）都按它说话，嵌在组件里会让读方引用整个组件。
// 数值不按出现顺序排：警觉是 2026-10-15 补的，追加在末尾，已有的值不挪（埋点与测试按名字读，不按数值）。
namespace Game.Battle
{
    /// <summary>
    /// BOSS 发难状态：待机 →（玩家进发难半径）警觉 →（愣满反应时间）冲刺 → 接触 → 战斗中 → 解除武装 → 待机；
    /// 冲刺超出拴绳 → 撤回 → 原位后按玩家位置进解除武装或待机。
    /// </summary>
    public enum BossAggroState
    {
        /// <summary>待机：站在原位，玩家进发难半径（以原位为圆心）且守卫全过就警觉（默认值）。</summary>
        Idle = 0,

        /// <summary>冲刺：朝玩家移动；守卫挡住时原地停住，离原位超过拴绳就放弃、撤回原位。</summary>
        Charging = 1,

        /// <summary>接触：已置「BOSS 先动手」并发起交互（嘲讽对白 → Battle 阶段），等交互返回。</summary>
        Contact = 2,

        /// <summary>战斗中：在跟这只 BOSS 打（不论是它冲上来还是玩家按 E）；战斗场景在场时瞬移回原位。</summary>
        InBattle = 3,

        /// <summary>撤回：走回原位；到了之后玩家还在发难半径里就解除武装，否则待机。</summary>
        Returning = 4,

        /// <summary>解除武装：战后 / 交互作废 / 拴绳撤回后不发难（还没回原位就先走回去），直到玩家离开察觉范围才恢复待机。</summary>
        Disarmed = 5,

        /// <summary>警觉：玩家进了发难半径，头顶出提示、开始计反应时间；这段时间里玩家按 E = 正面攻击，离开半径 = 回待机。</summary>
        Alerted = 6,
    }
}
