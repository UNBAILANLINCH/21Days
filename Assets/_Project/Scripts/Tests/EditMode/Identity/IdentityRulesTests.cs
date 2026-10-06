// 职责：钉住身份规则——六种露馅各一条（含「非当前身份不触发」）、时限归零自动退出、
//   无效身份 id 的明确行为、露馅后果由注入策略决定、跨档埋点在状态迁移处各一条。
// 为什么新建：这六条是 `02_身份暴露与怀疑.md` §3.9 那张表的可执行版本；规则一旦漂移，
//   玩家看到的是「莫名其妙地追逐 / 死亡」，而日志里什么都没有。

using System;
using Game.Core.Telemetry;
using Game.Identity;
using Game.Tests.EditMode.Telemetry;
using NUnit.Framework;
using static Game.Tests.EditMode.Identity.IdentityTestContent;

namespace Game.Tests.EditMode.Identity
{
    public sealed class IdentityRulesTests
    {
        private IdentitySettings settings;
        private IdentityRules rules;
        private IdentityState state;
        private IdentityLedger ledger;
        private SuspicionState suspicion;

        [SetUp]
        public void SetUp()
        {
            settings = NewSettings();
            rules = new IdentityRules(settings, NewCatalog());
            state = new IdentityState();
            ledger = new IdentityLedger();
            suspicion = SuspicionState.FromSettings(settings);
        }

        // ── 借 / 退 / 时限 ──────────────────────────────────────────────────────────

        [Test]
        public void TryEnter_ValidIdentity_BecomesBorrowingAndRecordsLedger()
        {
            Assert.That(rules.TryEnter(state, ledger, MusicianId), Is.EqualTo(IdentityEnterResult.Entered));

            Assert.That(state.Current.Value, Is.EqualTo(Musician));
            Assert.That(state.CurrentOrigin, Is.EqualTo(IdentityOrigin.Possession));
            Assert.That(state.IsInEffect, Is.True, "无时限（DefaultDurationSeconds = 0）时借到就生效");
            Assert.That(state.MemoryRecorded, Is.True, "附身 / 用皮成功即记 memory（story-facts.md §4.1）");
            Assert.That(ledger.DistinctCount, Is.EqualTo(1));
        }

        /// <summary>格式不合法的文本根本进不了规则层——拦住它的是 <see cref="IdentityId"/> 自己。</summary>
        [Test]
        public void IdentityId_FromMalformedText_Throws()
        {
            Assert.Throws<FormatException>(() => IdentityId.From("Bad Id"));
            Assert.Throws<FormatException>(() => IdentityId.From("identity.with.dot"));
            Assert.That(IdentityId.TryParse("Bad Id", out IdentityId parsed), Is.False);
            Assert.That(parsed, Is.EqualTo(IdentityId.None));
        }

        /// <summary>无效身份 id 的明确行为：<see cref="IdentityId.None"/>（本体）借不出东西，状态一动不动。</summary>
        [Test]
        public void TryEnter_InvalidIdFormat_LeavesBodyUntouched()
        {
            Assert.That(rules.TryEnter(state, ledger, IdentityId.None), Is.EqualTo(IdentityEnterResult.InvalidId));

            Assert.That(state.IsBorrowing, Is.False);
            Assert.That(ledger.TotalUses, Is.Zero, "被拒的借用不进账簿");
        }

        [Test]
        public void TryEnter_UnknownIdentity_LeavesBodyUntouched()
        {
            Assert.That(rules.TryEnter(state, ledger, IdentityId.From("nobody_here")),
                Is.EqualTo(IdentityEnterResult.UnknownIdentity));

            Assert.That(state.IsBorrowing, Is.False);
            Assert.That(state.Current, Is.EqualTo(IdentityId.None));
            Assert.That(ledger.TotalUses, Is.Zero);
        }

        [Test]
        public void TryEnter_SameIdentityTwice_ReturnsAlreadyBorrowingWithoutRefreshingLimit()
        {
            settings.DefaultDurationSeconds = 10f;
            Assert.That(rules.TryEnter(state, ledger, MusicianId), Is.EqualTo(IdentityEnterResult.Entered));
            rules.AdvanceIdentity(state, 4f);

            Assert.That(rules.TryEnter(state, ledger, MusicianId), Is.EqualTo(IdentityEnterResult.AlreadyBorrowing));
            Assert.That(state.RemainingSeconds, Is.EqualTo(6f).Within(0.001f), "重复借同一个身份不刷新时限");
            Assert.That(ledger.TotalUses, Is.EqualTo(1));
        }

        [Test]
        public void TryEnter_AnotherIdentity_ReplacesWithoutCooldown()
        {
            settings.CooldownSeconds = 5f;
            rules.TryEnter(state, ledger, MusicianId);

            Assert.That(rules.TryEnter(state, ledger, StewardId), Is.EqualTo(IdentityEnterResult.Entered), "同一场景可以先后借多个身份（01 R4）");
            Assert.That(state.Current.Value, Is.EqualTo(Steward));
            Assert.That(ledger.DistinctCount, Is.EqualTo(2));
        }

        [Test]
        public void AdvanceIdentity_WhenTimeLimitExpires_ExitsAndStartsCooldown()
        {
            settings.DefaultDurationSeconds = 5f;
            settings.CooldownSeconds = 2f;
            rules.TryEnter(state, ledger, StewardId);

            Assert.That(rules.AdvanceIdentity(state, 4f), Is.EqualTo(IdentityExitReason.None), "没到点不退出");
            Assert.That(state.IsInEffect, Is.True);

            Assert.That(rules.AdvanceIdentity(state, 1f), Is.EqualTo(IdentityExitReason.Expired));
            Assert.That(state.Current, Is.EqualTo(IdentityId.None));
            Assert.That(state.IsInEffect, Is.False);
            Assert.That(state.CooldownLeft, Is.EqualTo(2f).Within(0.001f));
            Assert.That(rules.TryEnter(state, ledger, MusicianId), Is.EqualTo(IdentityEnterResult.OnCooldown), "冷却期内借不了");
        }

        [Test]
        public void TryExit_WhileBorrowing_ReturnsBodyWithCooldown()
        {
            settings.CooldownSeconds = 3f;
            rules.TryEnter(state, ledger, StewardId);

            Assert.That(rules.TryExit(state), Is.EqualTo(IdentityExitReason.Voluntary));
            Assert.That(state.IsBorrowing, Is.False);
            Assert.That(state.CooldownLeft, Is.EqualTo(3f).Within(0.001f));
            Assert.That(rules.TryExit(state), Is.EqualTo(IdentityExitReason.None), "本体时退出什么都不做");
        }

        // ── 六种露馅，逐条 ──────────────────────────────────────────────────────────

        /// <summary>① 人物特性：`02_身份暴露与怀疑.md:96-97` R4/R5（所借身份 + 禁区 + 被看见，三条件齐）。</summary>
        [Test]
        public void EvaluateExposure_PersonaTrait_WhenRestrictedIdentityInAreaAndSeen()
        {
            rules.TryEnter(state, ledger, MusicianId);

            ExposureCause cause = rules.EvaluateExposure(state, ledger, suspicion,
                ExposureSignals.Persona(IdentityId.From(Musician), true, true));

            Assert.That(cause, Is.EqualTo(ExposureCause.PersonaTrait));
        }

        /// <summary>非当前身份不触发：禁区限制的是乐正，玩家借的是都知——按 `02:97` R5「少一个就不暴露」。</summary>
        [Test]
        public void EvaluateExposure_PersonaTrait_WhenRestrictedIdentityIsNotCurrent_DoesNotTrigger()
        {
            rules.TryEnter(state, ledger, StewardId);

            ExposureCause cause = rules.EvaluateExposure(state, ledger, suspicion,
                ExposureSignals.Persona(IdentityId.From(Musician), true, true));

            Assert.That(cause, Is.EqualTo(ExposureCause.None));
        }

        /// <summary>人物特性还要求「身份生效中」：时限归零后人在禁区被看见也不算（`02:97` R5 的「所借身份是乐正」）。</summary>
        [Test]
        public void EvaluateExposure_PersonaTrait_AfterTimeLimitExpired_DoesNotTrigger()
        {
            settings.DefaultDurationSeconds = 1f;
            rules.TryEnter(state, ledger, MusicianId);
            rules.AdvanceIdentity(state, 1f);

            ExposureCause cause = rules.EvaluateExposure(state, ledger, suspicion,
                ExposureSignals.Persona(IdentityId.From(Musician), true, true));

            Assert.That(cause, Is.EqualTo(ExposureCause.None));
        }

        /// <summary>② 身份核验关口：`02_身份暴露与怀疑.md:107-110` R12–R15。</summary>
        [Test]
        public void EvaluateExposure_CheckpointNotAccepted_ReportsCheckpoint()
        {
            ExposureCause refused = rules.EvaluateExposure(state, ledger, suspicion, ExposureSignals.Checkpoint(false));
            ExposureCause accepted = rules.EvaluateExposure(state, ledger, suspicion, ExposureSignals.Checkpoint(true));

            Assert.That(refused, Is.EqualTo(ExposureCause.Checkpoint));
            Assert.That(accepted, Is.EqualTo(ExposureCause.None));
        }

        /// <summary>③ 账簿超量：`02_身份暴露与怀疑.md:114-118` R16–R20。</summary>
        [Test]
        public void EvaluateExposure_LedgerOverLimit_ReportsLedger()
        {
            settings.LedgerMode = LedgerCountingMode.DistinctIdentities;
            settings.LedgerLimit = 2;
            ledger.Record(IdentityId.From(Musician));
            ledger.Record(IdentityId.From(Steward));

            Assert.That(rules.EvaluateExposure(state, ledger, suspicion, ExposureSignals.None), Is.EqualTo(ExposureCause.None),
                "恰好等于上限不算超量（原文是「超过一定数量」）");

            ledger.Record(IdentityId.From(Clerk));
            Assert.That(rules.EvaluateExposure(state, ledger, suspicion, ExposureSignals.None), Is.EqualTo(ExposureCause.Ledger));
        }

        /// <summary>④ 怀疑度到上限：`02_身份暴露与怀疑.md:122-127` R21–R26。</summary>
        [Test]
        public void EvaluateExposure_SuspicionAtLimit_ReportsSuspicion()
        {
            settings.SuspicionLimit = 10f;
            suspicion = SuspicionState.FromSettings(settings);
            suspicion.Add(9f);

            Assert.That(rules.EvaluateExposure(state, ledger, suspicion, ExposureSignals.None), Is.EqualTo(ExposureCause.None));

            suspicion.Add(1f);
            Assert.That(rules.EvaluateExposure(state, ledger, suspicion, ExposureSignals.None), Is.EqualTo(ExposureCause.Suspicion));
        }

        /// <summary>⑤ 揭露：`02_身份暴露与怀疑.md:131-134` R27–R30；蜃师面具挡一次（`:132` R28）。</summary>
        [Test]
        public void EvaluateExposure_Reveal_ReportsRevealUnlessBlocked()
        {
            rules.TryEnter(state, ledger, ApprenticeId);

            Assert.That(rules.EvaluateExposure(state, ledger, suspicion, ExposureSignals.Reveal(false)),
                Is.EqualTo(ExposureCause.Reveal));
            Assert.That(rules.EvaluateExposure(state, ledger, suspicion, ExposureSignals.Reveal(true)),
                Is.EqualTo(ExposureCause.None), "挡下的一次不算露馅");
        }

        /// <summary>⑥ 警戒值：红区一律、橙区只在身份不生效时（`02:142-144` R32/R34）。</summary>
        [Test]
        public void EvaluateExposure_Alertness_OrangeZoneOnlyWhenIdentityNotInEffect()
        {
            Assert.That(rules.EvaluateExposure(state, ledger, suspicion, ExposureSignals.Alert(true, false)),
                Is.EqualTo(ExposureCause.Alertness), "本体在红区：直接命中");

            rules.TryEnter(state, ledger, StewardId);
            Assert.That(rules.EvaluateExposure(state, ledger, suspicion, ExposureSignals.Alert(false, true)),
                Is.EqualTo(ExposureCause.None), "身份生效中免橙区警戒（mai「验收 4」）");
            Assert.That(rules.EvaluateExposure(state, ledger, suspicion, ExposureSignals.Alert(true, true)),
                Is.EqualTo(ExposureCause.Alertness), "红区不免");
        }

        [Test]
        public void EvaluateExposure_MultipleKindsAtOnce_ReportsAllOfThem()
        {
            settings.LedgerLimit = 1;
            settings.SuspicionLimit = 5f;
            suspicion = SuspicionState.FromSettings(settings);
            rules.TryEnter(state, ledger, MusicianId);
            ledger.Record(IdentityId.From(Steward));
            suspicion.Add(5f);

            var signals = new ExposureSignals(IdentityId.From(Musician), true, true,
                false, false, false, false, false, false);
            ExposureCause cause = rules.EvaluateExposure(state, ledger, suspicion, in signals);

            Assert.That(cause & ExposureCause.PersonaTrait, Is.EqualTo(ExposureCause.PersonaTrait));
            Assert.That(cause & ExposureCause.Ledger, Is.EqualTo(ExposureCause.Ledger));
            Assert.That(cause & ExposureCause.Suspicion, Is.EqualTo(ExposureCause.Suspicion));
        }

        // ── 结果分派：策略注入 ──────────────────────────────────────────────────────

        [Test]
        public void ResolveExposure_WithDeathPolicy_LosesIdentityAndMarksDead()
        {
            var ruleSet = new IdentityRules(settings, NewCatalog(), new FixedPolicy(ExposureOutcome.Death));
            ruleSet.TryEnter(state, ledger, MusicianId);

            ExposureResolution result = ruleSet.ResolveExposure(state, ledger, suspicion,
                ExposureSignals.Persona(IdentityId.From(Musician), true, true));

            Assert.That(result.Cause, Is.EqualTo(ExposureCause.PersonaTrait));
            Assert.That(result.Outcome, Is.EqualTo(ExposureOutcome.Death), "后果来自注入的策略，不是规则层硬编");
            Assert.That(result.IdentityLost, Is.True);
            Assert.That(state.IsBorrowing, Is.False);
            Assert.That(state.Dead, Is.True);
            Assert.That(state.ExposureCount, Is.EqualTo(1));
        }

        [Test]
        public void ResolveExposure_WithChasePolicy_KeepsPlayerAliveAndReportsPunishment()
        {
            settings.ExposureChaseDurationSeconds = 30f;
            var ruleSet = new IdentityRules(settings, NewCatalog(), new FixedPolicy(ExposureOutcome.Chase));
            ruleSet.TryEnter(state, ledger, StewardId);

            ExposureResolution result = ruleSet.ResolveExposure(state, ledger, suspicion, ExposureSignals.Checkpoint(false));

            Assert.That(result.Outcome, Is.EqualTo(ExposureOutcome.Chase));
            Assert.That(result.PunishmentSeconds, Is.EqualTo(30f).Within(0.001f));
            Assert.That(state.Dead, Is.False);
        }

        /// <summary>没有注入策略时不得替策划选一种后果——`00_功能总览.md:286` §5 C1 是原文矛盾。</summary>
        [Test]
        public void ResolveExposure_WithoutPolicy_ReportsNoOutcomeButStillLosesIdentity()
        {
            rules.TryEnter(state, ledger, MusicianId);

            ExposureResolution result = rules.ResolveExposure(state, ledger, suspicion,
                ExposureSignals.Persona(IdentityId.From(Musician), true, true));

            Assert.That(result.Outcome, Is.EqualTo(ExposureOutcome.None));
            Assert.That(result.PunishmentSeconds, Is.Zero);
            Assert.That(state.IsBorrowing, Is.False, "身份被看穿本身与后果无关，规则层五种都会失效");
            Assert.That(state.Dead, Is.False);
        }

        /// <summary>警戒值一层不会夺走身份（原文只写「满了转敌对」，`02:142` R32；两层叠加见 `02:145` R35 待定）。</summary>
        [Test]
        public void ResolveExposure_AlertnessOnly_DoesNotLoseIdentity()
        {
            rules.TryEnter(state, ledger, StewardId);

            ExposureResolution result = rules.ResolveExposure(state, ledger, suspicion, ExposureSignals.Alert(true, false));

            Assert.That(result.Cause, Is.EqualTo(ExposureCause.Alertness));
            Assert.That(result.IdentityLost, Is.False);
            Assert.That(state.IsInEffect, Is.True);
            Assert.That(state.ExposureCount, Is.Zero, "只有规则层露馅才记 identity.exposed 的次数");
        }

        // ── 埋点（状态迁移处各一条，不埋每帧量）─────────────────────────────────────

        [Test]
        public void StateTransitions_AreTracked_WithCauseNamesPerExposureKind()
        {
            var clock = new FakeTelemetryClock();
            var sink = new RecordingTelemetrySink();
            var service = new TelemetryService(TelemetryOptions.Default, clock, sink);
            try
            {
                var ruleSet = new IdentityRules(settings, NewCatalog(), new FixedPolicy(ExposureOutcome.Chase),
                    service.Scope("identity"));
                settings.LedgerLimit = 1;
                ruleSet.TryEnter(state, ledger, MusicianId);
                // 人物特性要「禁区限制的身份 == 当前身份」，所以先按乐正判一次露馅，再换成都知把账簿撑过界。
                ruleSet.ResolveExposure(state, ledger, suspicion, ExposureSignals.Persona(IdentityId.From(Musician), true, true));
                ruleSet.TryEnter(state, ledger, StewardId);
                ruleSet.TryEnter(state, ledger, IdentityId.From("nobody_here"));

                string log = string.Join("\n", sink.Lines);
                Assert.That(log, Does.Contain("identity/identity_enter"));
                Assert.That(log, Does.Contain("identity/exposed"));
                Assert.That(log, Does.Contain("persona_trait"), "每种露馅各记一条，属性带方式名");
                Assert.That(log, Does.Contain("identity/identity_chase"), "政策判追逐时另记一条");
                Assert.That(log, Does.Contain("identity/identity_enter_refused"), "失败分支要埋点");
                Assert.That(log, Does.Contain("identity/ledger_over"), "账簿越界另记一条");
                Assert.That(log, Does.Contain("identity/identity_exit"), "露馅导致的身份失效要留下退出记录");
                Assert.That(log, Does.Contain("\"reason\":3"), "退出原因是 Exposed");
                Assert.That(log, Does.Not.Contain("\"identity\":\"\""), "退出记录里的身份不能是空的");
            }
            finally
            {
                service.Dispose();
            }
        }

        /// <summary>测试用固定策略：不替规则层做决定，只回报测试要求的那一种后果。</summary>
        private sealed class FixedPolicy : IExposureOutcomePolicy
        {
            private readonly ExposureOutcome outcome;

            public FixedPolicy(ExposureOutcome outcome)
            {
                this.outcome = outcome;
            }

            public ExposureOutcome Decide(ExposureCause cause) => outcome;
        }
    }
}
