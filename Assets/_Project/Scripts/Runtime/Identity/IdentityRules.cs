// 职责：身份系统的纯规则——借 / 退 / 时限结算、六种露馅判定、后果分派（策略注入）、存档读写。
//   全部方法都是「读输入、改状态、给结果」，不碰 Unity API、不读 Time、不拿系统随机，
//   所以能直接进 EditMode 测试与确定性回放。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：`DisguiseRules` 只有一条判据（AllowsEnemyAttack => !isDisguised），
//      没有身份概念、没有时限与阈值，扩不动；`MonsterRules` 的警戒是逐怪感知，不是身份规则。
//   2. 扩展不行：不能把六种判定塞进 `IdentityState`——状态是数据、规则是行为，
//      合成一个类以后「存档要序列化规则」这种错就会自然长出来。

using System;
using Game.Core.Telemetry;

namespace Game.Identity
{
    /// <summary>
    /// 身份规则。一个实例绑定一份 <see cref="IdentitySettings"/>、一张 <see cref="IdentityCatalog"/>
    /// 与一个露馅后果策略，之后所有方法都作用在调用方传进来的状态对象上（本类自己不持有玩法状态）。
    /// <para>
    /// <b>不硬编的两处</b>：
    /// <list type="bullet">
    /// <item>露馅后果（死亡 vs 追逐）由 <see cref="IExposureOutcomePolicy"/> 注入；不注入时
    /// <see cref="ResolveExposure"/> 给出 <see cref="ExposureOutcome.None"/>，即「判定发生了、后果还没接线」
    /// ——原文矛盾见 <c>docs/design/features-spotlight/00_功能总览.md:286</c>（§5 C1）；</item>
    /// <item>全部数值（时限、冷却、上限、阈值、惩罚）来自 <see cref="IdentitySettings"/>，默认值都是占位。</item>
    /// </list>
    /// </para>
    /// <para>埋点：只埋状态迁移与失败分支（借入 / 退出 / 拒绝 / 露馅），不埋每帧量。</para>
    /// </summary>
    public sealed class IdentityRules
    {
        private readonly IdentitySettings settings;
        private readonly IdentityCatalog catalog;
        private readonly IExposureOutcomePolicy exposurePolicy;
        private readonly ITelemetryScope telemetry;

        /// <param name="settings">数值块，不可为空。</param>
        /// <param name="catalog">身份定义表，不可为空（空表合法：此时任何身份都借不到）。</param>
        /// <param name="exposurePolicy">露馅后果策略；<b>可以为 null</b>——见类注释「不硬编的两处」。</param>
        /// <param name="telemetry">埋点门面；不传则用 <see cref="NullTelemetryScope.Instance"/>。</param>
        public IdentityRules(
            IdentitySettings settings,
            IdentityCatalog catalog,
            IExposureOutcomePolicy exposurePolicy = null,
            ITelemetryScope telemetry = null)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.exposurePolicy = exposurePolicy;
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>当前绑定的数值块（只读，改它请改资产）。</summary>
        public IdentitySettings Settings => settings;

        /// <summary>当前绑定的身份定义表。</summary>
        public IdentityCatalog Catalog => catalog;

        /// <summary>
        /// 借一个身份。<b>无效 id 的明确行为</b>：格式不合法 → <see cref="IdentityEnterResult.InvalidId"/>；
        /// 格式合法但表里没有 → <see cref="IdentityEnterResult.UnknownIdentity"/>；两者都<b>不改任何状态</b>。
        /// <para>
        /// 同一场景可以先后借多个身份（`01_换皮与附身.md:118` R4 的链式附身），所以已在借别的身份时
        /// 直接替换（旧身份按 <see cref="IdentityExitReason.Replaced"/> 结束，<b>不进冷却</b>）；
        /// 已在借同一个身份则返回 <see cref="IdentityEnterResult.AlreadyBorrowing"/>，<b>不刷新时限</b>
        /// （刷新时限原文没写，不替策划决定）。
        /// </para>
        /// <para>
        /// 借用成功会同时记一笔账簿：`01_换皮与附身.md:192` 说明用过的身份要跨阶段保留。
        /// 「以身份行动」的后续记录由接线方按 <see cref="IdentityLedger.Record"/> 补。
        /// </para>
        /// </summary>
        public IdentityEnterResult TryEnter(IdentityState state, IdentityLedger ledger, IdentityId id)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (ledger == null)
            {
                throw new ArgumentNullException(nameof(ledger));
            }

            if (!id.IsValid)
            {
                return Refuse(IdentityEnterResult.InvalidId, id);
            }

            if (!catalog.TryGet(id, out IdentityDefinition definition))
            {
                return Refuse(IdentityEnterResult.UnknownIdentity, id);
            }

            if (state.IsBorrowing && state.Current == id)
            {
                return Refuse(IdentityEnterResult.AlreadyBorrowing, id);
            }

            if (state.CooldownLeft > 0f)
            {
                return Refuse(IdentityEnterResult.OnCooldown, id);
            }

            if (state.IsBorrowing)
            {
                // 先取名字再 End：End 会把 Current 清回本体，取晚了日志里就是一条空身份
                // （这一处是测试 StateTransitions_AreTracked_WithCauseNamesPerExposureKind 抓出来的）。
                string replaced = state.Current.Value;
                state.End(0f);
                telemetry.Track("identity_exit", ("reason", (int)IdentityExitReason.Replaced), ("identity", replaced));
            }

            state.Begin(definition, settings.DefaultDurationSeconds);
            ledger.Record(id);
            telemetry.Track("identity_enter", ("identity", id.Value), ("origin", (int)definition.Origin));
            TrackLedgerOverLimit(ledger);
            return IdentityEnterResult.Entered;
        }

        /// <summary>
        /// 主动退出，回到本体并按 <see cref="IdentitySettings.CooldownSeconds"/> 起冷却。
        /// 原文没写主动退出的操作（`01_换皮与附身.md:165` R26），本波按「可退出、进冷却」处理 [推断]。
        /// 本体时返回 <see cref="IdentityExitReason.None"/>，不改状态。
        /// </summary>
        public IdentityExitReason TryExit(IdentityState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (!state.IsBorrowing)
            {
                return IdentityExitReason.None;
            }

            IdentityId leaving = state.Current;
            state.End(settings.CooldownSeconds);
            telemetry.Track("identity_exit", ("reason", (int)IdentityExitReason.Voluntary), ("identity", leaving.Value));
            return IdentityExitReason.Voluntary;
        }

        /// <summary>
        /// 推进时限与冷却，并在时限归零时结算退出（`01_换皮与附身.md:166` R27：皮有「生效中」时段）。
        /// 返回 <see cref="IdentityExitReason.None"/> 表示这一 tick 什么都没发生。
        /// </summary>
        public IdentityExitReason AdvanceIdentity(IdentityState state, float deltaTime)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (deltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            state.TickTimers(deltaTime);
            if (!state.IsBorrowing || !state.HasTimeLimit || state.RemainingSeconds > 0f)
            {
                return IdentityExitReason.None;
            }

            IdentityId leaving = state.Current;
            state.End(settings.CooldownSeconds);
            telemetry.Track("identity_exit", ("reason", (int)IdentityExitReason.Expired), ("identity", leaving.Value));
            return IdentityExitReason.Expired;
        }

        /// <summary>
        /// 六种露馅判定，返回命中的位掩码（可能多条同时命中，也可能一条都没有）。
        /// <b>本方法不改任何状态</b>——要结算用 <see cref="ResolveExposure"/>。
        /// <para>
        /// 六条逐条对应 <c>docs/design/features-spotlight/02_身份暴露与怀疑.md:147-158</c> 的
        /// 「六种露馅方式一览」表；每条的行号写在下面各自的分支注释里。
        /// </para>
        /// </summary>
        public ExposureCause EvaluateExposure(
            IdentityState state,
            IdentityLedger ledger,
            SuspicionState suspicion,
            in ExposureSignals signals)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (ledger == null)
            {
                throw new ArgumentNullException(nameof(ledger));
            }

            if (suspicion == null)
            {
                throw new ArgumentNullException(nameof(suspicion));
            }

            ExposureCause cause = ExposureCause.None;

            // ① 人物特性：只在该身份「生效中」、且禁区限制的正是当前身份时才成立。
            //    真源 docs/design/features-spotlight/02_身份暴露与怀疑.md:96-97（R4/R5：
            //    所借身份是乐正 + 在音乐解谜区 + 被其他人发现，三条件同时成立）；表行 :153。
            if (state.IsInEffect
                && signals.PersonaRestrictedIdentity == state.Current
                && signals.InPersonaRestrictedArea
                && signals.SeenByOther)
            {
                cause |= ExposureCause.PersonaTrait;
            }

            // ② 身份核验关口：正在过关且核验没过。真源 02_身份暴露与怀疑.md:107-110（R12–R15）；
            //    核验失败会怎样原文没写（:110 R15），所以这里只报「没过」，后果交给策略。
            if (signals.CheckpointRequiresIdentity && !signals.CheckpointAccepted)
            {
                cause |= ExposureCause.Checkpoint;
            }

            // ③ 账簿超量。真源 02_身份暴露与怀疑.md:114-118（R16–R20）；表行 :155。
            if (ledger.IsOverLimit(settings.LedgerMode, settings.LedgerLimit))
            {
                cause |= ExposureCause.Ledger;
            }

            // ④ 怀疑度到上限。真源 02_身份暴露与怀疑.md:122-127（R21–R26：灰衣收入累计怀疑度、
            //    到达上限触发追逐战）；表行 :156。
            if (suspicion.IsAtLimit)
            {
                cause |= ExposureCause.Suspicion;
            }

            // ⑤ 揭露（阶段九龙族、按规律发生）。真源 02_身份暴露与怀疑.md:131-134（R27–R30）；
            //    蜃师面具挡一次（:132 R28）；表行 :157。本体没有身份可揭露，故先要 IsBorrowing。
            if (signals.RevealedByDragon && !signals.RevealBlocked && state.IsBorrowing)
            {
                cause |= ExposureCause.Reveal;
            }

            // ⑥ 警戒值（视线层）：红区一律命中；橙区只在身份不生效时命中
            //    ——「伪装免于橙区警戒」（mai「验收 4」，02_身份暴露与怀疑.md:144 R34）；
            //    表行 :158。橙区升满警戒不是立刻露馅（:142 R32 写的 4 秒升满），
            //    「满警戒怎么算」属 Monster 的判定，这里只报「视线层已经把你记上了」。
            if (signals.InRedZone || (signals.InOrangeZone && !state.IsInEffect))
            {
                cause |= ExposureCause.Alertness;
            }

            return cause;
        }

        /// <summary>
        /// 结算一次露馅：判定 → 走注入策略 → 需要时让身份失效 → 记露馅次数 → 给出时长。
        /// <para>
        /// <b>身份会不会失效</b>由 <see cref="LosesIdentity"/> 决定：规则层五种会失效，
        /// 只有警戒值一层不会（原文只写「满了转敌对」`02_身份暴露与怀疑.md:142` R32，
        /// 没写会夺走身份；两层怎么叠加见 :145 R35 / :240 Q11 待定 [推断]）。
        /// </para>
        /// </summary>
        public ExposureResolution ResolveExposure(
            IdentityState state,
            IdentityLedger ledger,
            SuspicionState suspicion,
            in ExposureSignals signals)
        {
            ExposureCause cause = EvaluateExposure(state, ledger, suspicion, in signals);
            if (cause == ExposureCause.None)
            {
                return ExposureResolution.None;
            }

            ExposureOutcome outcome = exposurePolicy == null ? ExposureOutcome.None : exposurePolicy.Decide(cause);
            bool losesIdentity = LosesIdentity(cause);
            IdentityId wearing = state.Current;
            if (losesIdentity)
            {
                state.End(settings.ExposureLockoutSeconds);
                state.RecordExposure();
                telemetry.Track("identity_exit", ("reason", (int)IdentityExitReason.Exposed), ("identity", wearing.Value));
            }

            if (outcome == ExposureOutcome.Death)
            {
                state.MarkDead();
            }

            // 六种露馅各记一条（命中几种就几条），这样日志里能按方式统计；
            // 事件很少发生（露馅是节点性事件），不存在刷屏问题。
            TrackExposureCauses(cause, outcome, wearing);
            if (outcome == ExposureOutcome.Death)
            {
                telemetry.Track("identity_death", ("identity", wearing.Value), ("cause", (int)cause));
            }
            else if (outcome == ExposureOutcome.Chase)
            {
                telemetry.Track("identity_chase", ("identity", wearing.Value), ("cause", (int)cause),
                    ("seconds", settings.ExposureChaseDurationSeconds));
            }

            float punishment = outcome == ExposureOutcome.Chase
                ? settings.ExposureChaseDurationSeconds
                : outcome == ExposureOutcome.Death ? settings.ExposureDeathDelaySeconds : 0f;
            return new ExposureResolution(cause, outcome, losesIdentity, punishment);
        }

        /// <summary>
        /// 这条露馅会不会让身份失效。规则层五种（人物特性 / 核验 / 账簿 / 怀疑度 / 揭露）会——
        /// 原文写的是「变回原主」（`02_身份暴露与怀疑.md:90` R1）；警戒值一层不会（见 <see cref="ResolveExposure"/>）。
        /// </summary>
        public static bool LosesIdentity(ExposureCause cause)
        {
            const ExposureCause ruleLayer = ExposureCause.PersonaTrait | ExposureCause.Checkpoint
                | ExposureCause.Ledger | ExposureCause.Suspicion | ExposureCause.Reveal;
            return (cause & ruleLayer) != ExposureCause.None;
        }

        /// <summary>
        /// 把状态、账簿、怀疑度写进存档分区。分区里<b>只放原始量</b>，不放派生出来的剧情事实键
        /// （档位键每次变化重算，见 <see cref="IdentityFactSnapshot"/>）。
        /// </summary>
        public void Capture(IdentityState state, IdentityLedger ledger, SuspicionState suspicion, IdentitySaveData data)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (ledger == null)
            {
                throw new ArgumentNullException(nameof(ledger));
            }

            if (suspicion == null)
            {
                throw new ArgumentNullException(nameof(suspicion));
            }

            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            state.Capture(data);

            data.UsedIdentityIds.Clear();
            for (int i = 0; i < ledger.UsedIdentities.Count; i++)
            {
                data.UsedIdentityIds.Add(ledger.UsedIdentities[i].Value);
            }

            data.UsedIdentityTotalUses = ledger.TotalUses;
            data.Suspicion = suspicion.Value;
        }

        /// <summary>
        /// 从存档分区恢复。返回 true 表示存档里的当前身份仍然有效；
        /// <b>false 表示它已被丢弃</b>（表里没有 / id 不合法），此时状态是「本体 + 其余计数照常恢复」，
        /// 而不是整份读档失败——内容改过 id 的旧档不该让玩家开不了游戏。
        /// </summary>
        public bool Restore(IdentityState state, IdentityLedger ledger, SuspicionState suspicion, IdentitySaveData data)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (ledger == null)
            {
                throw new ArgumentNullException(nameof(ledger));
            }

            if (suspicion == null)
            {
                throw new ArgumentNullException(nameof(suspicion));
            }

            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            state.Restore(data);

            bool currentRestored = state.Current.IsValid && catalog.TryGet(state.Current, out _);
            if (!currentRestored)
            {
                state.DropCurrentIdentity();
            }

            var identities = new IdentityId[data.UsedIdentityIds.Count];
            for (int i = 0; i < identities.Length; i++)
            {
                string raw = data.UsedIdentityIds[i];
                // 存档里的 id 可能来自旧内容或被改坏：认不出来的那条<b>跳过</b>，
                // 不让整个槽位打不开（整个槽位打不开的代价远大于少记一个身份）。
                if (!IdentityId.TryParse(raw, out identities[i]) && !string.IsNullOrWhiteSpace(raw))
                {
                    telemetry.TrackWarn("restore_skipped", TelemetryProps.Of(("identity", raw)));
                }
            }

            ledger.Restore(identities, data.UsedIdentityTotalUses);
            suspicion.RestoreValue(data.Suspicion);
            return currentRestored;
        }

        /// <summary>六种露馅逐种记一条（事件名 <c>exposed</c>，属性 <c>cause</c> 是方式名、<c>identity</c>、<c>outcome</c>）。</summary>
        private void TrackExposureCauses(ExposureCause cause, ExposureOutcome outcome, IdentityId identity)
        {
            TrackExposureCause(cause, ExposureCause.PersonaTrait, outcome, identity);
            TrackExposureCause(cause, ExposureCause.Checkpoint, outcome, identity);
            TrackExposureCause(cause, ExposureCause.Ledger, outcome, identity);
            TrackExposureCause(cause, ExposureCause.Suspicion, outcome, identity);
            TrackExposureCause(cause, ExposureCause.Reveal, outcome, identity);
            TrackExposureCause(cause, ExposureCause.Alertness, outcome, identity);
        }

        private void TrackExposureCause(ExposureCause cause, ExposureCause single, ExposureOutcome outcome, IdentityId identity)
        {
            if ((cause & single) == ExposureCause.None)
            {
                return;
            }

            telemetry.Track("exposed", ("cause", CauseName(single)), ("identity", identity.Value), ("outcome", (int)outcome));
        }

        /// <summary>
        /// 账簿越界时记一条。<b>越界是持续状态</b>，所以之后每次借身份都会再记一条——
        /// 这是有意的：重复触发追逐的判定在追逐侧（`04_追逐.md`），不在这里；
        /// 日志里重复出现正好说明「越界之后玩家还在继续借身份」。
        /// </summary>
        private void TrackLedgerOverLimit(IdentityLedger ledger)
        {
            if (!ledger.IsOverLimit(settings.LedgerMode, settings.LedgerLimit))
            {
                return;
            }

            telemetry.Track("ledger_over", ("mode", (int)settings.LedgerMode), ("limit", settings.LedgerLimit),
                ("count", ledger.CountUnder(settings.LedgerMode)));
        }

        private static string CauseName(ExposureCause single)
        {
            switch (single)
            {
                case ExposureCause.PersonaTrait: return "persona_trait";
                case ExposureCause.Checkpoint: return "checkpoint";
                case ExposureCause.Ledger: return "ledger";
                case ExposureCause.Suspicion: return "suspicion";
                case ExposureCause.Reveal: return "reveal";
                default: return "alertness";
            }
        }

        private IdentityEnterResult Refuse(IdentityEnterResult result, IdentityId id)
        {
            telemetry.Track("identity_enter_refused", ("result", (int)result), ("identity", id.Value));
            return result;
        }
    }
}
