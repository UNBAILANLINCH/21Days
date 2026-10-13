// 职责：进战斗那一刻的**局势快照**——把遭遇侧（Monster / Stealth）才知道的几件事翻译成
//   `Game.TurnBased.BattleEntryRequest` 要的五个量，供战斗侧开仗前填请求。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：`BattleEntryRequest` 是**纯值快照**（`Runtime/TurnBased/BattleEntryRequest.cs` 文件头明写
//      「由接线侧从潜行 / 感知 / 区域查询的结果翻译过来，本模块不查任何东西」），它不认识 `MonsterRules`；
//      而「玩家潜没潜行、这一下算不算偷袭、怪物在哪一档警觉」只有遭遇侧知道。
//   2. 扩展不行：把 `MonsterRules` / `EncounterStep` 直接塞进 `Game.TurnBased` 会让纯规则内核认识玩法类型
//      （turnbased-module-guide「依赖方向」明令禁止）；让 `BattleSetup` 反向去认识 `EncounterStep`
//      更是把「开仗装配」变成「遭遇流程的读者」。
//   3. 新建：以上两条都不成立，故新建这个**只读契约**。
// 契约方向（同 `IMonsterDropSink` / `IStealthFactSink` 的既有接法）：
//   接口声明在**生产方**这一侧（Game.Monster，由 `EncounterStep` 实现），消费方（Game.Battle 的
//   `BattleSetup.TryCreate`）反向认识它——与「接口声明在调用方」的写法方向相反是**有意的**：
//   这里生产方是遭遇，消费方是战斗，而战斗模块指南明写 `Game.Battle` 不许被 `Game.Monster` 反向引用
//   （battle-module-guide「反向禁止」）。把契约放在 Monster 侧，依赖边就只有 Battle → Monster 一条。
// 装配方式：`BattleInstaller` 的构建回调里 `TryResolve<EncounterStep>()` 之后 `flow.BindEntryContext(step)`；
//   **拿不到 context 时 `BattleSetup.TryCreate` 退回「正面攻击、玩家先手」硬编码请求，
//   与接线前逐字一致**（独立原型场景与纯 Battle 测试作用域走的正是那一支，有负对照测试钉住）。
//
// ⚠️ 与剧情身份**不是同一套 id**：本契约只喂「进战斗判定」的五个快照量，不提供
//   `BattleStageEnteredEvent` 的 generation / activationId / targetId。遭遇侧的 `EncounterId` / `ActivationId`
//   是遭遇流程自己的编号（`EncounterStep.StartBattle` 登记），**不要**拿它们去 `NarrativeRules.CompleteBattle`
//   （后者校验剧情侧那三项，对不上直接返回 false）。理由与实测见 `PRP/battle-to-narrative/prp.md` 与
//   `BattleStageEnteredEvent.cs:11-12` 的注释。
using Game.TurnBased;

namespace Game.Monster
{
    /// <summary>
    /// 一场战斗开打前的局势快照来源。实现方是遭遇侧（<see cref="EncounterStep"/>），
    /// 读方是战斗侧（`Game.Battle.BattleSetup.TryCreate`）。
    /// <para>
    /// <b>只读、无副作用</b>：读这些属性不该推进任何状态。实现方按「开仗的那一刻」现取即可。
    /// </para>
    /// </summary>
    public interface IBattleEntryContext
    {
        /// <summary>玩家此刻是否处于潜行（`07_回合制作战文档.md:17` 偷袭触发条件的一半）。</summary>
        bool PlayerSneaking { get; }

        /// <summary>
        /// 这一场里**已经**偷袭命中过目标（另一半）。判据是持久事实 `stealth.assassinated`
        /// （字典 §4.2「已用背后暗杀解决过目标，命中即写」），**不是**「此刻能不能下刀」——
        /// 后者会把「站在守卫背后」写成「已经杀过他」（`StealthGateInput.AlreadyAssassinated` 的注释）。
        /// </summary>
        bool SneakAttackLanded { get; }

        /// <summary>玩家是否处于这只怪的警戒区域内（`07:22`、`:27`）。</summary>
        bool PlayerInAlertZone { get; }

        /// <summary>玩家是否处于这只怪的敌对区域内（`07:22`、`:27`）。</summary>
        bool PlayerInHostileZone { get; }

        /// <summary>怪物当前的警觉档位（`07:22`、`:27`「怪物正处于警戒状态或敌对状态」）。</summary>
        MonsterAlertState MonsterState { get; }

        /// <summary>
        /// 快照来源这一侧的「这只对手此刻察觉到玩家了吗」（＝它自己处于警戒 / 敌对态）。
        /// <para>
        /// 2026-10-10（③ 方案 2）新增：巡逻怪那边等价于 <see cref="MonsterState"/> 是警戒 / 敌对，
        /// 而**没有状态机的那一侧**（剧情 BOSS：`Game.Battle.BossEntryContext`）只有「这一刻的感知结果」，
        /// 没有持久档位可报——拿 <see cref="MonsterState"/> 表达会让读方以为存在一个状态机。
        /// 读方 `BattleSetup.IsEncounterEngaged` 认这一条：本侧察觉了，遭遇就有发言权。
        /// </para>
        /// </summary>
        bool MonsterAware { get; }

        /// <summary>谁先动的手（`07:18`、`:28`）。</summary>
        BattleInitiator Initiator { get; }
    }
}
