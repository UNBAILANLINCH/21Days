// 职责：进入战斗的三种方式（07 一、进入战斗）。
// 出处：`docs/design/spotlight/07_回合制作战文档.md:13-28`；转写见
// `docs/design/features-spotlight/09_BOSS战.md:178-184`（R40）。

namespace Game.TurnBased
{
    /// <summary>玩家进入战斗的方式。<see cref="None"/> 表示没进入战斗（判定被拒）。</summary>
    public enum BattleEntryKind
    {
        /// <summary>没进入战斗。</summary>
        None = 0,

        /// <summary>偷袭：玩家潜行并偷袭成功、由玩家攻击开战（07:15-18）→ 玩家先手，BOSS 生命 -20%。</summary>
        SneakAttack = 1,

        /// <summary>正面攻击：玩家在警戒 / 敌对区域，或怪物处于警戒 / 敌对态，玩家攻击开战（07:20-23）→ 玩家先手。</summary>
        FrontalAttack = 2,

        /// <summary>被打：同上前提下玩家被 BOSS 攻击开战（07:25-28）→ BOSS 先手。</summary>
        Ambushed = 3,
    }
}
