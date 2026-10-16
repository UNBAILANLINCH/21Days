// 职责：实时遭遇「升级为回合制」的三种触发情形（PRP/combat-iteration D5，用户 2026-10-14 拍板「三种触发全要」）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：`Game.TurnBased.BattleInitiator` 只分「谁先动手」两值，答不了「玩家打中的怪察觉了没有」；
//      `MonsterMode` 是怪物自己的状态，不是「这一下是谁打了谁」。
//   2. 扩展不行：给 `BattleInitiator` 加值会让纯规则内核认识实时遭遇（turnbased-module-guide「依赖方向」），
//      而 Monster 不能引用 Game.Battle，契约只能声明在 Monster 侧（同 `IBattleEntryContext` 的既有接法）。
//   所以新建一个枚举，作为 `EncounterEscalationRequest` 的一个字段；三值到开战请求的翻译归战斗侧（`BattleSetup`）。
namespace Game.Monster
{
    /// <summary>实时遭遇升级为回合制的触发情形（「这一下是谁打了谁」）。</summary>
    public enum EncounterEscalationTrigger : byte
    {
        /// <summary>没有触发（默认值，不会出现在被接受的请求里）。</summary>
        None = 0,

        /// <summary>玩家打中了<b>未察觉</b>的怪（巡逻走 / 巡逻停）→ 偷袭：玩家先手、怪物开战前先掉 20%。</summary>
        PlayerHitUnaware = 1,

        /// <summary>玩家打中了<b>已警戒 / 敌对</b>的怪 → 正面攻击：玩家先手。</summary>
        PlayerHitAware = 2,

        /// <summary>怪打中了玩家（含追逐中追上）→ 被打：怪物先手。</summary>
        MonsterHitPlayer = 3,
    }
}
