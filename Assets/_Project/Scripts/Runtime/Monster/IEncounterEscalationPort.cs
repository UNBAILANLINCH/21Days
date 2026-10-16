// 职责：实时遭遇「升级为回合制」的跨模块契约——遭遇侧（EncounterStep）问「这个种类能不能升级」、发起升级；
//   战斗侧（Game.Battle 的 BattleFlow）回答并负责开一场回合制战斗，打完在收场黑幕下回调
//   EncounterStep.CompleteEscalation 交回胜负与剩余血量（PRP/combat-iteration D5）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：`IBattleEntryContext` 是「主怪的局势快照」、只读无副作用，回答不了「这一下是谁打了谁」，
//      更不能发起开战；剧情那条开战入口（BattleStageEnteredEvent）要先有剧情 Battle 阶段，实时遭遇里没有。
//   2. 扩展不行：给 `IBattleEntryContext` 加「发起开战」会让一个只读契约长出副作用；
//      Monster 不能引用 Game.Battle（battle-module-guide :49、:59；project-root「依赖方向」），
//      所以契约只能声明在 Monster 侧，依赖边仍只有 Battle → Monster 一条（同 IBattleEntryContext / IMonsterDropSink 的接法）。
// 装配：BattleInstaller 的构建回调里与 `flow.BindEntryContext(step)` 同处 `step.BindEscalation(flow)`；
//   不装配（独立原型场景、纯 Monster 测试作用域）时 EncounterStep 的端口为 null，拦截点逐字走原路径。
namespace Game.Monster
{
    /// <summary>
    /// 升级回合制的端口（实现方在战斗侧）。两个方法都是<b>同步</b>回答：遭遇侧要在同一个 tick 里决定这一下伤害吞不吞。
    /// <para>
    /// 拒绝原因（<c>rejectReason</c>）是给埋点用的短码，约定：<c>not_configured</c>（该种类没有战斗数据）、
    /// <c>battle_busy</c>（已有一场仗在途）、<c>rule_rejected</c>（开战规则或配置拒绝）、<c>unavailable</c>（战斗侧不可用）。
    /// 遭遇侧自己判的原因（开关关 <c>disabled</c>、已在途 <c>in_flight</c>）不经过端口。
    /// </para>
    /// </summary>
    public interface IEncounterEscalationPort
    {
        /// <summary>这个种类有没有配回合制战斗数据（0 = 没接种类表，恒为否）。只读、无副作用，可在 tick 里调。</summary>
        bool CanEscalate(int kindId);

        /// <summary>
        /// 发起升级。返回 true = 接受：战斗随后异步进行，打完由实现方恰好调一次
        /// <paramref name="encounter"/> 的 <see cref="EncounterStep.CompleteEscalation"/>（开不成 / 中途失败则调
        /// <see cref="EncounterStep.AbortEscalation"/>）。返回 false = 拒绝，遭遇侧逐字走原路径。
        /// <para>
        /// <b>实现方可能在本调用返回之前就同步跑完整场</b>（测试里的假表现层就是这样）——调用方须在调用前就进入「升级在途」，
        /// 不能等返回 true 再置位，否则那一次完成回调会被当成「不在途」丢掉。
        /// </para>
        /// </summary>
        /// <param name="encounter">发起方；结果交回给它。</param>
        /// <param name="request">这一下的请求快照。</param>
        /// <param name="rejectReason">拒绝时的原因短码；接受时为 null。</param>
        bool TryEscalate(EncounterStep encounter, in EncounterEscalationRequest request, out string rejectReason);
    }
}
