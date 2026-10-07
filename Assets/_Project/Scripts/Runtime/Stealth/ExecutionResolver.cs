// 职责：把「一次通过的处决判定」结算成三件事（PRP/stealth-execution §2.3，缺一不可）：
//   1. 写 `stealth.assassinated`（**只在执行真的成功之后**，见该类文档）；
//   2. 让目标死亡（走 MonsterRules 的显式处决入口，不是 ApplyDamage）；
//   3. 把这场遭遇以处决收束这件事承载成 BattleResult(Victory)（只承载，不推进剧情）。
//   另外埋三个点（§2.6）并留一个表现钩子（`06:63` 的被处决动画，美术未定）。
//
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：工程里没有任何「执行处决」的落点——判定（AssassinationRules / ExecutionRules）
//      只回答「能不能」，事实键只有常量，谁都不负责把一次判定落成后果。
//   2. 扩展不行：塞进 ExecutionRules 会让纯判定认识 MonsterRules / EncounterStep / 埋点，
//      从此没法用纯逻辑测门槛；塞进 MonsterRules 会让怪物模块反向认识潜行语义与剧情事实。
//   3. 新建：以上两条都不成立，故新建「判定之后的结算」这一层。
//
// 依赖方向说明：本文件（连同 ExecutionInteractor）是本模块**唯一**允许认识 Monster / Narrative 的落点，
//   因为「对谁下刀、结果交给谁承载」本来就跨模块；内核文件（AssassinationRules 等）仍只依赖 Game.Core。
//   方向是潜行 → 怪物（消费方自己拉），不是怪物 → 潜行，与 `stealth-module-guide.md` 的依赖方向不冲突。
using System;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Monster;
using Game.Narrative;
using UnityEngine;

namespace Game.Stealth
{
    /// <summary>
    /// `stealth.assassinated` 的**写入口**。
    /// <para>
    /// 只开「写一个键」这一个口子：处决交互不必认识 <c>EncounterStep</c> 或它的内存事实集
    /// （读侧契约 <c>IEncounterFacts</c> 在 Narrative，写侧契约在本模块，各归其主）。
    /// 实现方是遭遇结算的内存事实集 <c>Game.Monster.EncounterFactLog</c>。
    /// </para>
    /// </summary>
    public interface IStealthFactSink
    {
        /// <summary>写一个事实键。键名一律取自 <see cref="StealthFactKeys"/>，不要手打字符串。</summary>
        void Set(string key, bool value);
    }

    /// <summary>
    /// 处决命中的表现载荷（`06_怪物状态与交互设计文档.md:63`「怪物被玩家处决秒杀之后会进入被处决的动画」）。
    /// <para>
    /// 只带表现要的东西（位置 / 朝向 / 是哪只妖），不把规则对象交出去——动画规格还没定（`roadmap.md` D6 等 Spine），
    /// 本波只留钩子。勾子为空时行为与「没有表现」完全一致。
    /// </para>
    /// </summary>
    public readonly struct ExecutionPresentation
    {
        public ExecutionPresentation(Vector2 position, Vector2 facing, int yaoId)
        {
            Position = position;
            Facing = facing;
            YaoId = yaoId;
        }

        /// <summary>目标倒地时的逻辑位置。</summary>
        public Vector2 Position { get; }

        /// <summary>目标倒地时的朝向（动画朝向用）。</summary>
        public Vector2 Facing { get; }

        /// <summary>目标的妖物 id（选动画集用）；没接种类表时为 0。</summary>
        public int YaoId { get; }
    }

    /// <summary>
    /// 处决结算器：判定通过之后干三件事，并把「玩家按了没反应」的现场埋进日志。
    /// </summary>
    public sealed class ExecutionResolver
    {
        private readonly AssassinationRules assassination;
        private readonly IStealthFactSink facts;
        private readonly EncounterStep encounter;
        private readonly ITelemetryScope telemetry;

        /// <param name="assassination">条件 ① 的判定内核（与 <c>EncounterStep</c> 用同一个 StealthKernel 出来的那份）。</param>
        /// <param name="facts">`stealth.assassinated` 的写入点；必填——写事实是这类存在的理由。</param>
        /// <param name="encounter">
        /// 遭遇结算，用来承载 <c>BattleResult</c>；可为 null（独立原型场景没有遭遇上下文），
        /// 为 null 时跳过第 3 件事，其余行为不变。
        /// </param>
        /// <param name="telemetry">埋点作用域（模块名 <c>stealth</c>）；可为 null（用空实现）。</param>
        public ExecutionResolver(AssassinationRules assassination, IStealthFactSink facts,
            EncounterStep encounter = null, ITelemetryScope telemetry = null)
        {
            this.assassination = assassination ?? throw new ArgumentNullException(nameof(assassination));
            this.facts = facts ?? throw new ArgumentNullException(nameof(facts));
            this.encounter = encounter;
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>
        /// 表现钩子：处决命中（目标已死、事实已写）之后回调一次。**默认为空**，为空时什么都不做
        /// ——美术未定（`roadmap.md` D6 等 Spine），接线方订阅它播被处决动画即可，本类不硬编表现。
        /// </summary>
        public event Action<ExecutionPresentation> OnExecuted;

        /// <summary>
        /// 对一个**已经选定**的目标结算一次处决。返回判定结果：<see cref="ExecutionVerdict.Allowed"/> 为 false 时
        /// 什么都没做（没写事实、没杀人、没结算战斗结果），埋点里有一条 <c>stealth_execute_rejected</c> 带原因。
        /// <para>
        /// <b>三件事的顺序（与 PRP §2.3 的编号有一处刻意的调换，理由在此）</b>：PRP 把「写事实」列在第 1、
        /// 「让目标死亡」列在第 2，但 §2.5 的硬要求是「**只在真的执行成功之后写**」。执行这一侧唯一可能失败的
        /// 一步就是 <see cref="MonsterRules.ApplyExecution"/>（目标在同一帧里已经死了），所以把它提到写事实之前：
        /// 实际顺序是 **执行 → 写事实 → 承载战斗结果**。写事实仍然严格在「执行成功」之后，没有把
        /// 「能不能下刀」当成「已经杀了他」。
        /// </para>
        /// </summary>
        public ExecutionVerdict TryExecute(in ExecutionInput input, MonsterRules target)
        {
            if (target == null)
            {
                return RejectNoTarget();
            }

            float distance = GameMath.Distance(input.Target.AttackerPosition, input.Target.TargetPosition);
            string defeatMethod = DefeatMethodOf(target);
            ExecutionVerdict verdict = ExecutionRules.Evaluate(in input, assassination);
            if (!verdict.Allowed)
            {
                TrackRejected(in verdict, distance, defeatMethod, target.IsKillable);
                return verdict;
            }

            // 「允许」与「执行」分开埋：前者是「这一刀本来能下」，后者是「真的下了」。
            // 两者之间还夹着一次 ApplyExecution，失败时只会看到 allowed 而没有 executed——这正是想要的现场。
            telemetry.Track("stealth_execute_allowed",
                ("distance", distance), ("defeat_method", defeatMethod), ("killable", target.IsKillable));

            // 第 2 件事（提到前面）：让目标死亡。defeat_method = 暗杀 的怪通常 killable = false，
            // ApplyDamage 对它们拒伤，所以必须走这条显式入口（理由写在 MonsterRules.ApplyExecution 的注释里）。
            if (!target.ApplyExecution())
            {
                // 同一帧内目标已经死了：不算一次处决——不写事实、不埋 stealth_executed、不承载胜利。
                var dead = new ExecutionVerdict(false, ExecutionReject.TargetNotAlive);
                TrackRejected(in dead, distance, defeatMethod, target.IsKillable);
                return dead;
            }

            // 第 1 件事：写 stealth.assassinated。**只在这一刻**——判定通过不等于杀过（PRP §2.5、
            // 字典 §4.2「命中即写，持久」）。键名来自 verdict，不在调用点手打。
            facts.Set(verdict.FactKey, true);

            // 第 3 件事：把战斗结果承载上去。只调 SettleBattle（PRP/battle-to-narrative §2.1 的唯一写入方），
            // **不碰 Session / Narrative**——战斗结果→剧情的消费侧目前仍无落点，本波只负责承载。
            // 不在遭遇里（未 Begin / 已 End）时不承载：那种情况下没有「这场遭遇」可言。
            bool settled = false;
            if (encounter != null && encounter.IsActive)
            {
                settled = encounter.SettleBattle(new BattleResult(BattleOutcome.Victory));
            }

            telemetry.Track("stealth_executed",
                ("distance", distance), ("defeat_method", defeatMethod), ("killable", target.IsKillable),
                ("settled", settled));

            // 表现钩子：默认为空 → 这里什么都不发生（美术未定，只留接口）。
            Action<ExecutionPresentation> hook = OnExecuted;
            if (hook != null)
            {
                hook(new ExecutionPresentation(target.Model.Position, target.Model.Facing, YaoIdOf(target)));
            }

            return verdict;
        }

        /// <summary>
        /// 一个候选目标都没有时也留一条拒绝埋点：**「对着空气按 F」是玩家最常见的「按了没反应」**，
        /// 不埋它就没法把「没目标」和「功能坏了」在现场分开。它不写任何事实、不改任何状态。
        /// <para>
        /// 只带 <c>reason</c> 一个属性：没有目标时距离、物种、可否击杀都无从谈起，塞 0 进去只会骗人。
        /// </para>
        /// </summary>
        public ExecutionVerdict RejectNoTarget()
        {
            var verdict = new ExecutionVerdict(false, ExecutionReject.NoTarget);
            telemetry.Track("stealth_execute_rejected", ("reason", verdict.ReasonCode));
            return verdict;
        }

        // 拒绝路径是「玩家按了没反应」的唯一现场，所以判定用到的数值一起写进去（`docs/telemetry.md` §2.2 第 3 类）。
        // **属性上限是 4**（`ITelemetryScope.Track` 的重载最多四个槽位、`TelemetryProps.Capacity = 4`），
        // 这里刻意挑的四个与砍掉的两个，理由如下（下一个人想加字段时先看这段）：
        //   · reason（留）——唯一能把「不在背后 / 超距 / 已察觉 / 物种不可处决」分开的东西，是整个事件的目的；
        //   · distance（留）——「超距」是差一点点还是差很远，只有它能说；调阈值时全靠它；
        //   · defeat_method（留）——比一个 species 布尔有用：能看出是白名单里**哪一种**非暗杀值
        //     （可击杀（方式没写） / 特殊条件 / 需收服 / 不可杀），内容侧补数据时直接照着它查；
        //   · killable（留）——「绕背会不会变成万能解」的现场证据：killable = true 的怪被拒 = 门槛在起作用；
        //   · aware（砍）——察觉在几何之前就被检查（AssassinationRules.Evaluate 的顺序），
        //     所以 reason = target_aware 时它恒为 true、其余 reason 下它必然为 false，纯冗余；
        //   · behind（砍）——上一版这里填的是 `TargetAlive && !TargetAware`，那**不是「在背后」**，是个错名的派生值；
        //     真要报背后得让 ExecutionInput 再带一个夹角，本波不加，而 reason = not_behind 已经指名了这种情况。
        // 也不为多塞属性改用 TrackWarn——那会把 I 级事件降成 W 级，级别本身是筛选维度。
        private void TrackRejected(in ExecutionVerdict verdict, float distance, string defeatMethod, bool killable)
        {
            telemetry.Track("stealth_execute_rejected",
                ("reason", verdict.ReasonCode),
                ("distance", distance),
                ("defeat_method", defeatMethod ?? string.Empty),
                ("killable", killable));
        }

        // 没接种类表时 DefeatMethod 为 null（MonsterKind 的文档），埋点里写成空串，别写 "null"。
        private static string DefeatMethodOf(MonsterRules target) =>
            target.Kind == null ? string.Empty : target.Kind.DefeatMethod;

        private static int YaoIdOf(MonsterRules target) => target.Kind == null ? 0 : target.Kind.YaoId;
    }
}
