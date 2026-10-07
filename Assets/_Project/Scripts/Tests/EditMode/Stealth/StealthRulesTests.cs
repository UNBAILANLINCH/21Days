// 职责：`Game.Stealth` 纯规则内核的 EditMode 测试。全部是纯逻辑，不依赖场景、物理、帧循环与资产路径。
// 每条判定都配了负对照：构造不满足的输入，断言返回 false / 被拒 / 抛异常。
using System.Collections.Generic;
using Game.Stealth;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Stealth
{
    public sealed class StealthGeometryTests
    {
        [Test]
        public void SegmentIntersectsRectangle_ThroughTheMiddle_IsBlocked()
        {
            Assert.That(StealthGeometry.SegmentIntersectsRectangle(
                new Vector2(-2f, 0f), new Vector2(2f, 0f), Vector2.zero, new Vector2(1f, 1f)), Is.True);
        }

        [Test]
        public void SegmentIntersectsRectangle_PassingAbove_IsNotBlocked()
        {
            // 负对照：线段从矩形上方掠过（y = 2 > 半高 1）。
            Assert.That(StealthGeometry.SegmentIntersectsRectangle(
                new Vector2(-2f, 2f), new Vector2(2f, 2f), Vector2.zero, new Vector2(1f, 1f)), Is.False);
        }

        [Test]
        public void SegmentIntersectsRectangle_EndingBeforeTheBox_IsNotBlocked()
        {
            // 负对照：线段在矩形之前就结束（x 最大只到 -1.5 < -1）。
            Assert.That(StealthGeometry.SegmentIntersectsRectangle(
                new Vector2(-4f, 0f), new Vector2(-1.5f, 0f), Vector2.zero, new Vector2(1f, 1f)), Is.False);
        }

        [Test]
        public void SegmentIntersectsRectangle_StartingInside_IsBlocked()
        {
            Assert.That(StealthGeometry.SegmentIntersectsRectangle(
                new Vector2(0.2f, 0.2f), new Vector2(5f, 0.2f), Vector2.zero, new Vector2(1f, 1f)), Is.True);
        }

        [Test]
        public void SegmentIntersectsRectangle_ExactlyOnTheEdge_IsBlocked()
        {
            // 贴边算挡住：玩法上宁可多算挡住，玩家不会觉得掩体是坏的。
            Assert.That(StealthGeometry.SegmentIntersectsRectangle(
                new Vector2(-2f, 1f), new Vector2(2f, 1f), Vector2.zero, new Vector2(1f, 1f)), Is.True);
        }

        [Test]
        public void SegmentIntersectsRectangle_ZeroLengthSegment_IsNotBlocked()
        {
            // 负对照：退化成点，不算遮挡（自我遮蔽没有意义）。
            Assert.That(StealthGeometry.SegmentIntersectsRectangle(
                Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(1f, 1f)), Is.False);
        }

        [Test]
        public void SegmentIntersectsCircle_ThroughTheMiddle_IsBlocked()
        {
            Assert.That(StealthGeometry.SegmentIntersectsCircle(
                new Vector2(-3f, 0f), new Vector2(3f, 0f), Vector2.zero, 1f), Is.True);
        }

        [Test]
        public void SegmentIntersectsCircle_PassingOutside_IsNotBlocked()
        {
            // 负对照：距离圆心 1.5 > 半径 1。
            Assert.That(StealthGeometry.SegmentIntersectsCircle(
                new Vector2(-3f, 1.5f), new Vector2(3f, 1.5f), Vector2.zero, 1f), Is.False);
        }

        [Test]
        public void SegmentIntersectsCircle_PassingBeyondTheCircle_IsNotBlocked()
        {
            // 负对照：线段在圆之前就结束（最近距离 2 > 半径 1）。
            Assert.That(StealthGeometry.SegmentIntersectsCircle(
                new Vector2(-3f, 0f), new Vector2(-2f, 0f), Vector2.zero, 1f), Is.False);
        }

        [Test]
        public void SegmentIntersectsCircle_Tangent_IsBlocked()
        {
            Assert.That(StealthGeometry.SegmentIntersectsCircle(
                new Vector2(-3f, 1f), new Vector2(3f, 1f), Vector2.zero, 1f), Is.True);
        }

        [Test]
        public void DistancePointToSegment_UsesPerpendicularFoot_WhenProjectionLandsInside()
        {
            Assert.That(StealthGeometry.DistancePointToSegment(
                new Vector2(0f, 3f), new Vector2(-5f, 0f), new Vector2(5f, 0f)), Is.EqualTo(3f).Within(0.0001f));
        }

        [Test]
        public void DistancePointToSegment_ClampsToEndpoint_WhenProjectionLandsOutside()
        {
            Assert.That(StealthGeometry.DistancePointToSegment(
                new Vector2(-8f, 0f), new Vector2(-5f, 0f), new Vector2(5f, 0f)), Is.EqualTo(3f).Within(0.0001f));
        }

        [Test]
        public void MakeRectangle_NonPositiveSize_IsRejected()
        {
            // 负对照：零面积掩体会静默失效，构造期就得炸。
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => StealthOccluder.MakeRectangle(Vector2.zero, new Vector2(0f, 1f)));
        }

        [Test]
        public void MakeCircle_NonPositiveRadius_IsRejected()
        {
            // 负对照：同上。
            Assert.Throws<System.ArgumentOutOfRangeException>(() => StealthOccluder.MakeCircle(Vector2.zero, 0f));
        }
    }

    public sealed class StealthSightTests
    {
        private StealthSight sight;

        [SetUp]
        public void SetUp() => sight = new StealthSight();

        [Test]
        public void TrySight_NoOccluder_SeesTarget()
        {
            sight.SetOccluders(null);
            Assert.That(sight.TrySight(new Vector2(-5f, 0f), new Vector2(5f, 0f)).HasSight, Is.True);
        }

        [Test]
        public void TrySight_SingleOccluderBetween_BlocksAndReportsId()
        {
            sight.SetOccluders(new[] { StealthOccluder.MakeRectangle(Vector2.zero, new Vector2(1f, 4f), 7) });
            SightResult result = sight.TrySight(new Vector2(-5f, 0f), new Vector2(5f, 0f));
            Assert.That(result.HasSight, Is.False);
            Assert.That(result.BlockerCount, Is.EqualTo(1));
            Assert.That(result.FirstBlockerId, Is.EqualTo(7));
        }

        [Test]
        public void TrySight_OccluderOutOfTheWay_StillSeesTarget()
        {
            // 负对照：掩体在旁边（y = 6），视线在 y = 0 上通过。
            sight.SetOccluders(new[] { StealthOccluder.MakeRectangle(new Vector2(0f, 6f), new Vector2(4f, 2f), 1) });
            Assert.That(sight.TrySight(new Vector2(-5f, 0f), new Vector2(5f, 0f)).HasSight, Is.True);
        }

        [Test]
        public void TrySight_MultipleOccluders_ReportsEveryBlockerInOrder()
        {
            sight.SetOccluders(new[]
            {
                StealthOccluder.MakeRectangle(new Vector2(-2f, 0f), new Vector2(0.5f, 2f), 11),
                StealthOccluder.MakeRectangle(new Vector2(2f, 0f), new Vector2(0.5f, 2f), 22),
                StealthOccluder.MakeRectangle(new Vector2(0f, 9f), new Vector2(0.5f, 2f), 33),
            });

            SightResult result = sight.TrySight(new Vector2(-5f, 0f), new Vector2(5f, 0f));
            Assert.That(result.BlockerCount, Is.EqualTo(2));
            Assert.That(result.GetBlockerId(0), Is.EqualTo(11));
            Assert.That(result.GetBlockerId(1), Is.EqualTo(22));
        }

        [Test]
        public void TrySight_ZeroLengthSegment_AlwaysSees()
        {
            // 负对照：站在掩体里不动，不该被判成「被自己挡住」。
            sight.SetOccluders(new[] { StealthOccluder.MakeCircle(Vector2.zero, 2f, 5) });
            Assert.That(sight.TrySight(Vector2.zero, Vector2.zero).HasSight, Is.True);
        }

        [Test]
        public void TrySight_CircleOccluderBetween_Blocks()
        {
            sight.SetOccluders(new[] { StealthOccluder.MakeCircle(Vector2.zero, 1.5f, 3) });
            Assert.That(sight.HasSight(new Vector2(-4f, 0f), new Vector2(4f, 0f)), Is.False);
        }

        [Test]
        public void TrySightNonAlloc_MatchesTrySight_AndReusesBuffer()
        {
            sight.SetOccluders(new[] { StealthOccluder.MakeRectangle(Vector2.zero, new Vector2(1f, 4f), 9) });
            var buffer = new List<int> { 999 };

            Assert.That(sight.TrySightNonAlloc(new Vector2(-5f, 0f), new Vector2(5f, 0f), buffer), Is.False);
            Assert.That(buffer, Is.EqualTo(new[] { 9 }), "缓冲区先清空再写");

            Assert.That(sight.TrySightNonAlloc(new Vector2(-5f, 5f), new Vector2(5f, 5f), buffer), Is.True);
            Assert.That(buffer, Is.Empty);
        }

        [Test]
        public void SetOccluders_DefaultValueOccluder_IsRejected()
        {
            // 负对照：default(StealthOccluder) 不是合法形状，别让它静默等于「永远不挡」。
            Assert.Throws<System.ArgumentException>(() => sight.SetOccluders(new StealthOccluder[1]));
        }

        [Test]
        public void Clear_RemovesEveryOccluder()
        {
            sight.SetOccluders(new[] { StealthOccluder.MakeRectangle(Vector2.zero, new Vector2(1f, 4f), 1) });
            sight.Clear();
            Assert.That(sight.Count, Is.Zero);
            Assert.That(sight.HasSight(new Vector2(-5f, 0f), new Vector2(5f, 0f)), Is.True);
        }
    }

    public sealed class AssassinationRulesTests
    {
        private AssassinationRules rules;

        [SetUp]
        public void SetUp() => rules = new AssassinationRules(new AssassinationSettings(120f, 1.2f, false));

        [Test]
        public void Evaluate_DirectlyBehindAndInRange_IsAllowed()
        {
            // 目标朝 +X，攻方在 -X 侧 = 正后方，距离 1 <= 1.2。
            AssassinationVerdict verdict = rules.Evaluate(Input(new Vector2(-1f, 0f)));
            Assert.That(verdict.Allowed, Is.True);
            Assert.That(verdict.Reject, Is.EqualTo(AssassinationReject.None));
            Assert.That(verdict.FactKey, Is.EqualTo("stealth.assassinated"));
        }

        [Test]
        public void Evaluate_DirectlyInFront_IsRejectedAsNotBehind()
        {
            // 负对照：站在正前方（朝 +X 的目标，攻方在 +X 侧）。
            AssassinationVerdict verdict = rules.Evaluate(Input(new Vector2(1f, 0f)));
            Assert.That(verdict.Allowed, Is.False);
            Assert.That(verdict.Reject, Is.EqualTo(AssassinationReject.NotBehind));
        }

        [Test]
        public void Evaluate_AtTheSide_IsRejectedAsNotBehind()
        {
            // 负对照：正侧面 90°，落在 120° 背后锥（半角 60°）之外。
            AssassinationVerdict verdict = rules.Evaluate(Input(new Vector2(0f, 1f)));
            Assert.That(verdict.Allowed, Is.False);
            Assert.That(verdict.Reject, Is.EqualTo(AssassinationReject.NotBehind));
        }

        [Test]
        public void Evaluate_InsideRearCone_IsAllowed_ButOutsideIsNot()
        {
            // 120° 锥 = 半角 60°：偏离正后方 45° 在锥内，偏离 70° 在锥外。
            Assert.That(rules.Evaluate(Input(Rotate(new Vector2(-1f, 0f), 45f))).Allowed, Is.True);
            Assert.That(rules.Evaluate(Input(Rotate(new Vector2(-1f, 0f), 70f))).Reject,
                Is.EqualTo(AssassinationReject.NotBehind));
        }

        [Test]
        public void Evaluate_BehindButTooFar_IsRejectedAsOutOfRange()
        {
            // 负对照：方向对（正后方）但距离 3 > 1.2。
            AssassinationVerdict verdict = rules.Evaluate(Input(new Vector2(-3f, 0f)));
            Assert.That(verdict.Allowed, Is.False);
            Assert.That(verdict.Reject, Is.EqualTo(AssassinationReject.OutOfRange));
        }

        [Test]
        public void Evaluate_BehindInRangeButTargetAware_IsRejected()
        {
            // 负对照：目标已察觉（警戒 / 敌对），背后处决必须失败。
            AssassinationVerdict verdict = rules.Evaluate(Input(new Vector2(-1f, 0f), targetAware: true));
            Assert.That(verdict.Allowed, Is.False);
            Assert.That(verdict.Reject, Is.EqualTo(AssassinationReject.TargetAware));
        }

        [Test]
        public void Evaluate_TargetAlreadyDead_IsRejected()
        {
            // 负对照：目标不处于可处决状态。
            AssassinationVerdict verdict = rules.Evaluate(Input(new Vector2(-1f, 0f), targetAlive: false));
            Assert.That(verdict.Allowed, Is.False);
            Assert.That(verdict.Reject, Is.EqualTo(AssassinationReject.TargetNotAlive));
        }

        [Test]
        public void Evaluate_SamePositionAsTarget_IsRejected()
        {
            // 负对照：重合时朝向没有意义。
            AssassinationVerdict verdict = rules.Evaluate(Input(Vector2.zero));
            Assert.That(verdict.Allowed, Is.False);
            Assert.That(verdict.Reject, Is.EqualTo(AssassinationReject.SamePosition));
        }

        [Test]
        public void Evaluate_RequiresSneak_RejectsNotSneaking_AndPassesWhenSneaking()
        {
            var sneakRules = new AssassinationRules(new AssassinationSettings(120f, 1.2f, true));
            Assert.That(sneakRules.Evaluate(Input(new Vector2(-1f, 0f))).Reject,
                Is.EqualTo(AssassinationReject.NotSneaking));
            Assert.That(sneakRules.Evaluate(Input(new Vector2(-1f, 0f), sneaking: true)).Allowed, Is.True);
        }

        [Test]
        public void IsBehind_ZeroTargetFacing_IsRejected()
        {
            // 负对照：目标朝向未知（零向量）时任何方向都不算背后。
            Assert.That(rules.IsBehind(new Vector2(-1f, 0f), Vector2.zero, Vector2.zero), Is.False);
        }

        [Test]
        public void IsBehindAndInRange_IgnoresAwareness_UnlikeEvaluate()
        {
            // 绕背判定给附身复用（03:110-113 R11/R12）：它不问察觉到没有，只看几何。
            Assert.That(rules.IsBehindAndInRange(new Vector2(-1f, 0f), Vector2.zero, Vector2.right), Is.True);
            Assert.That(rules.IsBehindAndInRange(new Vector2(-3f, 0f), Vector2.zero, Vector2.right), Is.False);
        }

        [Test]
        public void AssassinationSettings_InvalidThresholds_AreRejected()
        {
            // 负对照：角度越界、距离非正都不许静默接受。
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new AssassinationSettings(0f, 1f, false));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new AssassinationSettings(200f, 1f, false));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new AssassinationSettings(90f, 0f, false));
        }

        private AssassinationInput Input(
            Vector2 attackerPosition, bool targetAware = false, bool targetAlive = true, bool sneaking = false) =>
            new AssassinationInput(
                attackerPosition, Vector2.right, Vector2.zero, Vector2.right, targetAlive, targetAware, sneaking);

        // 把「正后方」向量按目标正面为 +X 转一个角度，用来卡背后锥的边界（纯测试辅助）。
        private static Vector2 Rotate(Vector2 offset, float degrees)
        {
            float radians = degrees * 0.01745329252f;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Vector2(offset.x * cos - offset.y * sin, offset.x * sin + offset.y * cos);
        }
    }

    public sealed class KnockdownRulesTests
    {
        private KnockdownRules rules;

        [SetUp]
        public void SetUp() => rules = new KnockdownRules(
            new KnockdownSettings(1.5f, 3f, 0.35f, true, 0.5f, DownedHitPolicy.KnockdownThenDeath, 3));

        [Test]
        public void Tick_TakeHit_EntersDowned_AndCannotAttack()
        {
            rules.Tick(KnockdownInput.TakeHitNow(3), 0.1f);
            Assert.That(rules.Phase, Is.EqualTo(KnockdownPhase.Downed));
            Assert.That(rules.IsDowned, Is.True);
            Assert.That(rules.CanAttack(true), Is.False, "击倒期间按住攻击键也不能攻击（03:85 R3）");
            Assert.That(rules.MoveSpeedMultiplier, Is.Zero, "倒地期不能移动");
        }

        [Test]
        public void Tick_AdvancesDownedToCrawlingToNone_InOrder()
        {
            rules.Tick(KnockdownInput.TakeHitNow(3), 0.1f);

            rules.Tick(KnockdownInput.Idle(3), 1f);
            Assert.That(rules.Phase, Is.EqualTo(KnockdownPhase.Downed), "1 秒还没到 1.5 秒的倒地时长");

            rules.Tick(KnockdownInput.Idle(3), 1f);
            Assert.That(rules.Phase, Is.EqualTo(KnockdownPhase.Crawling));
            Assert.That(rules.MoveSpeedMultiplier, Is.EqualTo(0.35f).Within(0.0001f), "挣扎期缓慢移动");
            Assert.That(rules.CanAttack(true), Is.False, "挣扎期仍不能攻击");

            rules.Tick(KnockdownInput.Idle(3), 3f);
            Assert.That(rules.Phase, Is.EqualTo(KnockdownPhase.None));
            Assert.That(rules.MoveSpeedMultiplier, Is.EqualTo(1f));
            Assert.That(rules.CanAttack(true), Is.True);
        }

        [Test]
        public void Tick_ThresholdBoundary_TakesFullDurationNotOneTickLess()
        {
            rules.Tick(KnockdownInput.TakeHitNow(3), 0f);
            float remainingAfterHit = rules.RemainingInPhase;
            rules.Tick(KnockdownInput.Idle(3), 1f);
            Assert.That(rules.Phase, Is.EqualTo(KnockdownPhase.Downed),
                $"1 秒还没到 1.5 秒的倒地时长（命中后剩余 {remainingAfterHit}）");
            rules.Tick(KnockdownInput.Idle(3), 0.6f);
            Assert.That(rules.Phase, Is.EqualTo(KnockdownPhase.Crawling), "累计 1.6 >= 1.5 转挣扎");
        }

        [Test]
        public void Tick_KnockdownOnlyPolicy_DoesNotKillEvenAtZeroHealth()
        {
            var onlyKnockdown = new KnockdownRules(
                new KnockdownSettings(1f, 1f, 0.35f, true, 0.5f, DownedHitPolicy.KnockdownOnly, 3));
            onlyKnockdown.Tick(KnockdownInput.TakeHitNow(0), 0.1f);
            Assert.That(onlyKnockdown.Phase, Is.EqualTo(KnockdownPhase.Downed), "sp00 口径：血尽也只是击倒");
        }

        [Test]
        public void Tick_HealthDeathPolicy_KillsAtZeroHealth_WithoutKnockdown()
        {
            var healthDeath = new KnockdownRules(
                new KnockdownSettings(1f, 1f, 0.35f, true, 0.5f, DownedHitPolicy.HealthDeath, 3));
            healthDeath.Tick(KnockdownInput.TakeHitNow(0), 0.1f);
            Assert.That(healthDeath.Phase, Is.EqualTo(KnockdownPhase.Dead));
            Assert.That(healthDeath.CanAttack(true), Is.False);
        }

        [Test]
        public void Tick_KnockdownThenDeathPolicy_KnocksDownWhileAlive_AndKillsWhenHealthRunsOut()
        {
            rules.Tick(KnockdownInput.TakeHitNow(0), 0.1f);
            Assert.That(rules.Phase, Is.EqualTo(KnockdownPhase.Dead), "折中口径：血尽才死");

            var fresh = new KnockdownRules(
                new KnockdownSettings(1f, 1f, 0.35f, true, 0.5f, DownedHitPolicy.KnockdownThenDeath, 3));
            fresh.Tick(KnockdownInput.TakeHitNow(2), 0.1f);
            Assert.That(fresh.Phase, Is.EqualTo(KnockdownPhase.Downed), "折中口径：还有血就只击倒");
        }

        [Test]
        public void Tick_HitWhileDowned_ResetsTimer_WhenConfigured()
        {
            rules.Tick(KnockdownInput.TakeHitNow(3), 0f);
            rules.Tick(KnockdownInput.Idle(3), 1f);
            Assert.That(rules.RemainingInPhase, Is.EqualTo(0.5f).Within(0.0001f));

            rules.Tick(KnockdownInput.TakeHitNow(3), 1f);
            Assert.That(rules.RemainingInPhase, Is.EqualTo(1.5f).Within(0.0001f), "再挨打重置计时");
            Assert.That(rules.KnockdownCount, Is.EqualTo(2));
        }

        [Test]
        public void Tick_HitThrottleWindow_DoesNotDoubleCount()
        {
            rules.Tick(KnockdownInput.TakeHitNow(3), 0f);
            Assert.That(rules.KnockdownCount, Is.EqualTo(1));

            // 节流窗口 0.5 秒内的重复命中只计数不重置（也不额外计数）。
            rules.Tick(KnockdownInput.TakeHitNow(3), 0.1f);
            Assert.That(rules.KnockdownCount, Is.EqualTo(1));
            Assert.That(rules.RemainingInPhase, Is.EqualTo(1.4f).Within(0.0001f),
                "被节流的命中不重置计时（timeSinceHit = 0.1 < 节流窗口 0.5）");
            rules.Tick(KnockdownInput.TakeHitNow(3), 1f);
            Assert.That(rules.KnockdownCount, Is.EqualTo(2));
            Assert.That(rules.RemainingInPhase, Is.EqualTo(1.5f).Within(0.0001f));
        }

        [Test]
        public void Tick_Dead_DoesNotReenterKnockdown()
        {
            var healthDeath = new KnockdownRules(
                new KnockdownSettings(1f, 1f, 0.35f, true, 0.5f, DownedHitPolicy.HealthDeath, 3));
            healthDeath.Tick(KnockdownInput.TakeHitNow(0), 1f);
            Assert.That(healthDeath.Phase, Is.EqualTo(KnockdownPhase.Dead));
            healthDeath.Tick(KnockdownInput.TakeHitNow(0), 10f);
            Assert.That(healthDeath.Phase, Is.EqualTo(KnockdownPhase.Dead), "死人不进击倒循环");
            Assert.That(healthDeath.KnockdownCount, Is.Zero, "死后的命中不重复计数");
        }

        [Test]
        public void Tick_NegativeDeltaTime_IsRejected()
        {
            // 负对照：负步长是调用方 bug，静默吞掉会让回放对不齐。
            Assert.Throws<System.ArgumentOutOfRangeException>(() => rules.Tick(KnockdownInput.Idle(3), -0.1f));
        }

        [Test]
        public void KnockdownSettings_UnknownPolicyOrBadNumbers_AreRejected()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new KnockdownSettings(1f, 1f, 1f, true, 0f, (DownedHitPolicy)99, 3));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new KnockdownSettings(0f, 1f, 1f, true, 0f, DownedHitPolicy.KnockdownOnly, 3));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new KnockdownSettings(1f, 1f, 0f, true, 0f, DownedHitPolicy.KnockdownOnly, 3));
        }

        [Test]
        public void ResolveKnockdownTier_MapsCountsToTiers_AndNoneAtZero()
        {
            Assert.That(StealthFacts.ResolveKnockdownTier(0, 1, 3), Is.EqualTo(KnockdownTier.None));
            Assert.That(StealthFacts.ResolveKnockdownTier(1, 1, 3), Is.EqualTo(KnockdownTier.Low));
            Assert.That(StealthFacts.ResolveKnockdownTier(2, 1, 3), Is.EqualTo(KnockdownTier.Mid));
            Assert.That(StealthFacts.ResolveKnockdownTier(4, 1, 3), Is.EqualTo(KnockdownTier.High));
            Assert.That(StealthFacts.TierKey(KnockdownTier.None), Is.Empty);
            Assert.That(StealthFacts.TierKey(KnockdownTier.High), Is.EqualTo("stealth.knockdownCount.high"));
        }

        [Test]
        public void ResolveKnockdownTier_InvalidThresholds_AreRejected()
        {
            // 负对照：阈值配反了（低档上界 > 中档上界）必须当场炸。
            Assert.Throws<System.ArgumentOutOfRangeException>(() => StealthFacts.ResolveKnockdownTier(1, 3, 1));
        }
    }

    public sealed class ChaseRulesTests
    {
        private ChaseRules rules;

        [SetUp]
        public void SetUp()
        {
            rules = new ChaseRules(ChaseSettings.PlaceholderDefault, ChaseBaseline.ProjectCurrent);
            rules.ConfigureChaseSpeed(3.6f);
        }

        [Test]
        public void VerifyChaseSpeed_ProjectCurrentSpeeds_ReportsTooSlow()
        {
            // 现状问题（04:165）：巡逻 2 × 敌对倍率 1.25 = 2.5，低于玩家步行 3。
            Assert.That(rules.VerifyChaseSpeed(2.5f), Is.EqualTo(ChaseReject.TooSlowToCatch));
            Assert.That(rules.VerifyChaseSpeed(3f), Is.EqualTo(ChaseReject.TooSlowToCatch), "刚好等于步行也是追不上");
        }

        [Test]
        public void VerifyChaseSpeed_ConfiguredSpeed_IsAccepted()
        {
            Assert.That(rules.VerifyChaseSpeed(3.6f), Is.EqualTo(ChaseReject.None));
            Assert.That(rules.VerifyChaseSpeed(), Is.EqualTo(ChaseReject.None));
        }

        [Test]
        public void VerifyChaseSpeed_FasterThanPlayerRun_IsRejectedAsUnfair()
        {
            // 负对照：追兵比玩家奔跑还快会让追逐变成必死（04:175 约束 1）。
            Assert.That(rules.VerifyChaseSpeed(6f), Is.EqualTo(ChaseReject.TooFastToBeFair));
        }

        [Test]
        public void VerifyChaseSpeed_BelowWalk_IsRejected_AndFatalAtTrigger()
        {
            rules.ConfigureChaseSpeed(2.5f);
            ChaseTriggerVerdict verdict = rules.TryStart(
                new ChaseTriggerInput(Vector2.zero, Vector2.one, true, true), ChasePhase.Idle);
            Assert.That(verdict.Triggered, Is.False);
            Assert.That(verdict.Reject, Is.EqualTo(ChaseReject.TooSlowToCatch));
            Assert.That(verdict.Severity, Is.EqualTo(ChaseRejectSeverity.Fatal), "配置错误必须当场炸，不许静默跑");
        }

        [Test]
        public void TryStart_AwareAndFastEnough_StartsChase()
        {
            ChaseTriggerVerdict verdict = rules.TryStart(
                new ChaseTriggerInput(Vector2.zero, new Vector2(3f, 0f), true, true), ChasePhase.Idle);
            Assert.That(verdict.Triggered, Is.True);
            Assert.That(verdict.FactKey, Is.EqualTo("chase.active"));
        }

        [Test]
        public void TryStart_NotAware_SoftReject()
        {
            // 负对照：没察觉就不该起追。
            ChaseTriggerVerdict verdict = rules.TryStart(
                new ChaseTriggerInput(Vector2.zero, Vector2.one, true, false), ChasePhase.Idle);
            Assert.That(verdict.Triggered, Is.False);
            Assert.That(verdict.Reject, Is.EqualTo(ChaseReject.NotAware));
            Assert.That(verdict.Severity, Is.EqualTo(ChaseRejectSeverity.Soft));
        }

        [Test]
        public void TryStart_DeadPursuer_SoftReject()
        {
            ChaseTriggerVerdict verdict = rules.TryStart(
                new ChaseTriggerInput(Vector2.zero, Vector2.one, false, true), ChasePhase.Idle);
            Assert.That(verdict.Triggered, Is.False);
            Assert.That(verdict.Reject, Is.EqualTo(ChaseReject.PursuerNotAlive));
        }

        [Test]
        public void TryStart_OutOfTriggerRadius_SoftReject()
        {
            var limited = new ChaseRules(
                new ChaseSettings(4f, 2f, 8f, 6f, true), ChaseBaseline.ProjectCurrent);
            limited.ConfigureChaseSpeed(3.6f);

            ChaseTriggerVerdict far = limited.TryStart(
                new ChaseTriggerInput(Vector2.zero, new Vector2(5f, 0f), true, true), ChasePhase.Idle);
            Assert.That(far.Triggered, Is.False);
            Assert.That(far.Reject, Is.EqualTo(ChaseReject.OutOfTriggerRadius));

            ChaseTriggerVerdict near = limited.TryStart(
                new ChaseTriggerInput(Vector2.zero, new Vector2(3f, 0f), true, true), ChasePhase.Idle);
            Assert.That(near.Triggered, Is.True);
        }

        [Test]
        public void Tick_SeesTarget_KeepsChasing_AndResetsGrace()
        {
            ChaseVerdict verdict = rules.Tick(
                new ChaseInput(true, 2f, true, true), ChasePhase.Chasing, 0.1f);
            Assert.That(verdict.Phase, Is.EqualTo(ChasePhase.Chasing));
            Assert.That(verdict.GraceAccumulated, Is.Zero);
            Assert.That(verdict.ActiveFactValue, Is.True);
        }

        [Test]
        public void Tick_LostSightButStillClose_KeepsChasing()
        {
            // 负对照：距离不够远，只断视线不算摆脱。
            ChaseVerdict verdict = rules.Tick(new ChaseInput(false, 3f, true, true), ChasePhase.Chasing, 2.5f);
            Assert.That(verdict.EscapedThisTick, Is.False);
            Assert.That(verdict.Phase, Is.EqualTo(ChasePhase.Chasing));
            Assert.That(verdict.GraceAccumulated, Is.EqualTo(2.5f).Within(0.0001f));
        }

        [Test]
        public void Tick_LostSightAndFarEnough_Escapes_AndWritesEscapedKey()
        {
            ChaseVerdict verdict = rules.Tick(new ChaseInput(false, 9f, true, true), ChasePhase.Chasing, 2f);
            Assert.That(verdict.EscapedThisTick, Is.True);
            Assert.That(verdict.Phase, Is.EqualTo(ChasePhase.Idle));
            Assert.That(verdict.EscapedFactKey, Is.EqualTo("chase.escaped"));
            Assert.That(rules.HasEscaped, Is.True);
            Assert.That(rules.EscapeCount, Is.EqualTo(1));
        }

        [Test]
        public void Tick_LostSightForTooShort_EscapesNothing()
        {
            // 负对照：只断视线 1 秒（< 2 秒宽限），即使距离够远也还没摆脱。
            ChaseVerdict verdict = rules.Tick(new ChaseInput(false, 9f, true, true), ChasePhase.Chasing, 1f);
            Assert.That(verdict.EscapedThisTick, Is.False);
            Assert.That(verdict.Phase, Is.EqualTo(ChasePhase.Chasing));
        }

        [Test]
        public void Tick_NotChasing_StaysIdle()
        {
            ChaseVerdict verdict = rules.Tick(new ChaseInput(true, 1f, true, true), ChasePhase.Idle, 0.1f);
            Assert.That(verdict.Phase, Is.EqualTo(ChasePhase.Idle));
            Assert.That(verdict.EscapedThisTick, Is.False);
        }

        [Test]
        public void Tick_DeadTargetOrPursuer_EndsChase()
        {
            Assert.That(rules.Tick(new ChaseInput(true, 1f, false, true), ChasePhase.Chasing, 0.1f).Phase,
                Is.EqualTo(ChasePhase.Idle));
            Assert.That(rules.Tick(new ChaseInput(true, 1f, true, false), ChasePhase.Chasing, 0.1f).Phase,
                Is.EqualTo(ChasePhase.Idle));
        }

        [Test]
        public void Caught_ReportsCaughtFactKey()
        {
            ChaseVerdict verdict = rules.Caught(0.1f);
            Assert.That(verdict.Phase, Is.EqualTo(ChasePhase.Caught));
            Assert.That(verdict.CaughtThisTick, Is.True);
            Assert.That(verdict.CaughtFactKey, Is.EqualTo("chase.caught"));
        }

        [Test]
        public void Tick_NegativeDeltaTimeOrDistance_IsRejected()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => rules.Tick(new ChaseInput(false, 1f, true, true), ChasePhase.Chasing, -0.1f));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => rules.Tick(new ChaseInput(false, -1f, true, true), ChasePhase.Chasing, 0.1f));
        }

        [Test]
        public void ChaseSettings_InvalidNumbers_AreRejected()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ChaseSettings(0f, 0f, 8f, 6f, true));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ChaseSettings(0f, 2f, 0f, 6f, true));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ChaseSettings(-1f, 2f, 8f, 6f, true));
        }
    }

    public sealed class ChaseCaughtRulesTests
    {
        [Test]
        public void Resolve_KnockdownThenDeath_FirstCaughtIsKnockdown_NotFailure()
        {
            var settings = new ChaseOutcomeSettings(ChaseCaughtPolicy.KnockdownThenDeath, 3, 2);
            ChaseOutcome outcome = ChaseCaughtRules.Resolve(in settings, 0);
            Assert.That(outcome.IsFailure, Is.False);
            Assert.That(outcome.EntersKnockdown, Is.True);
            Assert.That(outcome.WritesCaughtFact, Is.False, "还没失败就不写 chase.caught");
        }

        [Test]
        public void Resolve_KnockdownThenDeath_SecondCaughtIsFailure()
        {
            var settings = new ChaseOutcomeSettings(ChaseCaughtPolicy.KnockdownThenDeath, 3, 2);
            ChaseOutcome outcome = ChaseCaughtRules.Resolve(in settings, 1);
            Assert.That(outcome.IsFailure, Is.True);
            Assert.That(outcome.FactKey, Is.EqualTo("chase.caught"));
        }

        [Test]
        public void Resolve_KnockdownThenDeath_ZeroHealthIsFailureImmediately()
        {
            // 负对照：还有「机会」但血已经没了，照样判死。
            var settings = new ChaseOutcomeSettings(ChaseCaughtPolicy.KnockdownThenDeath, 0, 2);
            Assert.That(ChaseCaughtRules.Resolve(in settings, 0).IsFailure, Is.True);
        }

        [Test]
        public void Resolve_DeathPolicy_AlwaysFailure()
        {
            var settings = new ChaseOutcomeSettings(ChaseCaughtPolicy.Death, 3, 2);
            ChaseOutcome outcome = ChaseCaughtRules.Resolve(in settings, 0);
            Assert.That(outcome.IsFailure, Is.True);
            Assert.That(outcome.EntersKnockdown, Is.False);
            Assert.That(outcome.FactKey, Is.EqualTo("chase.caught"));
        }

        [Test]
        public void Resolve_KnockdownPolicy_NeverFailure()
        {
            var settings = new ChaseOutcomeSettings(ChaseCaughtPolicy.Knockdown, 1, 2);
            ChaseOutcome outcome = ChaseCaughtRules.Resolve(in settings, 5);
            Assert.That(outcome.IsFailure, Is.False);
            Assert.That(outcome.EntersKnockdown, Is.True);
            Assert.That(outcome.FactKey, Is.Empty);
        }

        [Test]
        public void Resolve_NonePolicy_DoesNothing()
        {
            var settings = new ChaseOutcomeSettings(ChaseCaughtPolicy.None, 3, 2);
            ChaseOutcome outcome = ChaseCaughtRules.Resolve(in settings, 0);
            Assert.That(outcome.IsFailure, Is.False);
            Assert.That(outcome.WritesCaughtFact, Is.False);
        }

        [Test]
        public void Resolve_UnknownPolicy_IsRejected()
        {
            // 负对照：没登记的口径不许静默通过。
            var settings = new ChaseOutcomeSettings((ChaseCaughtPolicy)99, 3, 2);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => ChaseCaughtRules.Resolve(in settings, 0));
        }

        [Test]
        public void ChaseOutcomeSettings_InvalidNumbers_AreRejected()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new ChaseOutcomeSettings(ChaseCaughtPolicy.Death, -1, 2));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new ChaseOutcomeSettings(ChaseCaughtPolicy.Death, 3, 0));
        }
    }

    public sealed class SummonRulesTests
    {
        private ChaseFormation formation;
        private SummonSettings settings;

        [SetUp]
        public void SetUp()
        {
            settings = SummonSettings.PlaceholderDefault;
            formation = new ChaseFormation(
                1,
                "龙窟巡逻队",
                ChaseRouteKind.SelfDirected,
                false,
                new[]
                {
                    // 第 0 段上站 3 名可召唤成员 + 1 名不可召唤（房间内怪物，04:28 不能跨房间流通）。
                    new FormationMember(101, 1, 0, 0, true),
                    new FormationMember(103, 1, 0, 1, true),
                    new FormationMember(105, 1, 0, 2, true),
                    new FormationMember(107, 1, 0, 3, false),
                    // 第 1 段（中点 x = 10）在第 0 段的召唤半径之外；同一条巡逻路线的另一条腿。
                    new FormationMember(102, 1, 1, 0, true),
                    new FormationMember(104, 1, 1, 1, true),
                    new FormationMember(106, 1, 1, 2, true),
                },
                new[]
                {
                    new PatrolSegment(new Vector2(-2f, 0f), new Vector2(2f, 0f)),
                    new PatrolSegment(new Vector2(8f, 0f), new Vector2(12f, 0f)),
                });
        }

        [Test]
        public void Evaluate_TriggerNearRoute_SpawnsEveryMemberInRadius()
        {
            SummonRequest request = SummonRules.Evaluate(in formation, Vector2.zero, in settings, out SummonReject reject);
            Assert.That(reject, Is.EqualTo(SummonReject.None));
            Assert.That(request.Count, Is.EqualTo(3), "第 0 段半径内只有 3 名可召唤成员，107 不可召唤不算");
            Assert.That(request.GetMonsterId(0), Is.EqualTo(101));
        }

        [Test]
        public void Evaluate_MoreMembersInRadiusThanGroupMax_CapsAtGroupMax()
        {
            // 正对照：半径内 5 名可召唤成员 > 集群上限 4，只召 4 只（04:29「通常 3-4 集群」）。
            var crowded = new ChaseFormation(
                2, "超额巡逻队", ChaseRouteKind.SelfDirected, false,
                new[]
                {
                    new FormationMember(201, 1, 0, 0, true),
                    new FormationMember(202, 1, 0, 1, true),
                    new FormationMember(203, 1, 0, 2, true),
                    new FormationMember(204, 1, 0, 3, true),
                    new FormationMember(205, 1, 0, 4, true),
                },
                formation.Segments);
            SummonRequest request = SummonRules.Evaluate(in crowded, Vector2.zero, in settings, out SummonReject reject);
            Assert.That(reject, Is.EqualTo(SummonReject.None));
            Assert.That(request.Count, Is.EqualTo(4));
        }

        [Test]
        public void Evaluate_TriggerTooFarFromEveryRoute_SpawnsNothing()
        {
            // 负对照：触发点离两段中点都超过召唤半径 8。
            SummonRequest request = SummonRules.Evaluate(
                in formation, new Vector2(100f, 100f), in settings, out SummonReject reject);
            Assert.That(reject, Is.EqualTo(SummonReject.NothingInRadius));
            Assert.That(request.Count, Is.Zero);
        }

        [Test]
        public void Evaluate_AwayFromFirstRoute_IgnoresMembersBeyondRadius()
        {
            // 触发点挪到远处那段附近：第 0 段的三名可召唤成员（距离 10 > 半径 8）不该被「隔空召来」。
            SummonRequest request = SummonRules.Evaluate(
                in formation, new Vector2(10f, 0f), in settings, out SummonReject reject);
            Assert.That(reject, Is.EqualTo(SummonReject.None));
            Assert.That(request.Count, Is.EqualTo(3), "半径内只有第 1 段的三名可召唤成员");
            Assert.That(request.GetMonsterId(0), Is.EqualTo(102));
            Assert.That(request.GetMonsterId(1), Is.EqualTo(104));
            Assert.That(request.GetMonsterId(2), Is.EqualTo(106));
        }

        [Test]
        public void Evaluate_FewerThanGroupMinInRadius_SpawnsNothing()
        {
            // 负对照：「通常 3-4 集群」，凑不满一群就不召，避免被一只只钓出来。
            var thin = new ChaseFormation(
                3, "不满编", ChaseRouteKind.SelfDirected, false,
                new[]
                {
                    new FormationMember(301, 1, 0, 0, true),
                    new FormationMember(302, 1, 0, 1, true),
                    // 这两名在远处那段上，超出召唤半径。
                    new FormationMember(303, 1, 1, 2, true),
                    new FormationMember(304, 1, 1, 3, true),
                },
                formation.Segments);
            SummonRequest request = SummonRules.Evaluate(in thin, Vector2.zero, in settings, out SummonReject reject);
            Assert.That(reject, Is.EqualTo(SummonReject.BelowGroupSize), "半径内只有 2 名 < 集群下限 3");
            Assert.That(request.Count, Is.Zero);
        }

        [Test]
        public void Evaluate_EmptyFormation_IsRejected()
        {
            var empty = new ChaseFormation(0, "空", ChaseRouteKind.SelfDirected, false, null, null);
            Assert.That(SummonRules.Evaluate(in empty, Vector2.zero, in settings, out SummonReject reject).Count, Is.Zero);
            Assert.That(reject, Is.EqualTo(SummonReject.NoMembers));
        }

        [Test]
        public void Evaluate_MembersButNoRoute_IsRejected()
        {
            var noRoute = new ChaseFormation(
                0, "无路线", ChaseRouteKind.SelfDirected, false,
                new[] { new FormationMember(1, 1, 0, 0, true) }, null);
            Assert.That(SummonRules.Evaluate(in noRoute, Vector2.zero, in settings, out SummonReject reject).Count, Is.Zero);
            Assert.That(reject, Is.EqualTo(SummonReject.NoSegments));
        }

        [Test]
        public void SpawnPositionAround_AllInsideRadius_AndDistinct()
        {
            var radius = 8f;
            var seen = new List<Vector2>();
            for (int slot = 0; slot < 4; slot++)
            {
                Vector2 position = SummonRules.SpawnPositionAround(Vector2.zero, slot, 4, radius);
                Assert.That(position.magnitude, Is.LessThan(radius), "落点必须落在召唤半径内");
                for (int i = 0; i < seen.Count; i++)
                {
                    Assert.That(Vector2.Distance(seen[i], position), Is.GreaterThan(0.01f), "落点不许重合");
                }

                seen.Add(position);
            }
        }

        [Test]
        public void SpawnPositionAround_BadSlotOrCount_IsRejected()
        {
            // 负对照：越界槽位、非正数量、非正半径都不许静默接受。
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => SummonRules.SpawnPositionAround(Vector2.zero, 4, 4, 8f));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => SummonRules.SpawnPositionAround(Vector2.zero, 0, 0, 8f));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => SummonRules.SpawnPositionAround(Vector2.zero, 0, 4, 0f));
        }

        [Test]
        public void Validate_HealthyFormation_ReturnsNull()
        {
            // 编队 6 名成员、单条路线 —— 每条路线的可召唤成员会超过集群上限 4，所以这里用放宽的配置校验。
            Assert.That(SummonRules.Validate(in formation, new SummonSettings(8f, 3, 6)), Is.Null);
        }

        [Test]
        public void Validate_SummonableOnOneRouteExceedingGroupMax_IsReported()
        {
            // 负对照：一条路线上塞了 6 名可召唤成员 > 集群上限 4 = 关卡表配错。
            Assert.That(SummonRules.Validate(in formation, in settings), Does.Contain("超过集群上限"));
        }

        [Test]
        public void Validate_DuplicateMemberId_IsReported()
        {
            // 负对照：成员实例 id 重复 = 关卡表配错。
            var duplicated = new ChaseFormation(
                1, "重复", ChaseRouteKind.SelfDirected, false,
                new[]
                {
                    new FormationMember(7, 1, 0, 0, true),
                    new FormationMember(7, 1, 1, 1, true),
                    new FormationMember(8, 1, 0, 2, true),
                },
                formation.Segments);
            Assert.That(SummonRules.Validate(in duplicated, in settings), Does.Contain("重复"));
        }

        [Test]
        public void Validate_SegmentIndexOutOfRange_IsReported()
        {
            // 负对照：成员挂在不存在的段上。
            var bad = new ChaseFormation(
                1, "越界", ChaseRouteKind.SelfDirected, false,
                new[]
                {
                    new FormationMember(1, 1, 5, 0, true),
                    new FormationMember(2, 1, 0, 1, true),
                    new FormationMember(3, 1, 1, 2, true),
                },
                formation.Segments);
            Assert.That(SummonRules.Validate(in bad, in settings), Does.Contain("不存在的路线段"));
        }

        [Test]
        public void CountSummonableOnRoute_SkipsNonSummonableMembers()
        {
            Assert.That(formation.CountSummonableOnRoute(1), Is.EqualTo(6), "107 不可召唤（04:28 房间内怪物不能流通），不计入");
            Assert.That(formation.CountSummonableOnRoute(9), Is.Zero);
        }
    }

    public sealed class FixedChasePlannerTests
    {
        private ChaseFormation scripted;
        private ChaseFormation selfDirected;

        [SetUp]
        public void SetUp()
        {
            var segments = new[]
            {
                new PatrolSegment(Vector2.zero, new Vector2(4f, 0f)),
                new PatrolSegment(new Vector2(4f, 0f), new Vector2(4f, 3f)),
            };
            scripted = new ChaseFormation(2, "账簿固定追逐", ChaseRouteKind.Scripted, true, null, segments);
            selfDirected = new ChaseFormation(3, "自主追击", ChaseRouteKind.SelfDirected, false, null, segments);
        }

        [Test]
        public void IsFixedChase_RequiresScriptedRouteAndScriptedWaypoints()
        {
            Assert.That(new FixedChasePlanner(in scripted).IsFixedChase, Is.True);
            Assert.That(new FixedChasePlanner(in selfDirected).IsFixedChase, Is.False);
        }

        [Test]
        public void FixedFactKey_OnlyForFixedChase()
        {
            Assert.That(new FixedChasePlanner(in scripted).FixedFactKey, Is.EqualTo("chase.fixed"));
            Assert.That(new FixedChasePlanner(in selfDirected).FixedFactKey, Is.Empty);
        }

        [Test]
        public void Advance_CyclesThroughSegments_AndWrapsAround()
        {
            var planner = new FixedChasePlanner(in scripted);
            Assert.That(planner.TryGetCurrentSegment(out PatrolSegment first), Is.True);
            Assert.That(first.From, Is.EqualTo(Vector2.zero));

            PatrolSegment second = planner.Advance();
            Assert.That(second.To, Is.EqualTo(new Vector2(4f, 3f)));
            Assert.That(planner.SegmentIndex, Is.EqualTo(1));

            planner.Advance();
            Assert.That(planner.SegmentIndex, Is.Zero, "默认循环回第一段（04:204 未定，按循环实现）");
        }

        [Test]
        public void HasUsableRoute_FalseForEmptyOrZeroLengthRoute()
        {
            var empty = new ChaseFormation(4, "空", ChaseRouteKind.Scripted, true, null, null);
            Assert.That(new FixedChasePlanner(in empty).HasUsableRoute, Is.False);
            Assert.That(new FixedChasePlanner(in empty).TryGetCurrentSegment(out _), Is.False);

            var zeroLength = new ChaseFormation(
                5, "零长度", ChaseRouteKind.Scripted, true, null,
                new[] { new PatrolSegment(Vector2.one, Vector2.one) });
            Assert.That(new FixedChasePlanner(in zeroLength).HasUsableRoute, Is.False);
        }

        [Test]
        public void Advance_OnUnusableRoute_IsRejected()
        {
            // 负对照：没路线还要推进 = 调用方 bug，抛异常而不是静默停住。
            var broken = new ChaseFormation(6, "坏", ChaseRouteKind.Scripted, true, null, null);
            Assert.Throws<System.InvalidOperationException>(() => new FixedChasePlanner(in broken).Advance());
        }

        [Test]
        public void TotalRouteLength_AndEstimatedSeconds_AreComputed()
        {
            var planner = new FixedChasePlanner(in scripted);
            Assert.That(planner.TotalRouteLength, Is.EqualTo(7f).Within(0.0001f));
            Assert.That(planner.EstimatedRouteSeconds(3.5f), Is.EqualTo(2f).Within(0.0001f));
        }

        [Test]
        public void EstimatedRouteSeconds_NonPositiveSpeed_IsRejected()
        {
            var planner = new FixedChasePlanner(in scripted);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => planner.EstimatedRouteSeconds(0f));
        }

        [Test]
        public void IsAtFinalWaypoint_OnlyNearTheLastSegmentEnd()
        {
            var planner = new FixedChasePlanner(in scripted);
            Assert.That(planner.IsAtFinalWaypoint(new Vector2(4f, 3f), 0.5f), Is.True);
            Assert.That(planner.IsAtFinalWaypoint(Vector2.zero, 0.5f), Is.False, "起点不是终点");
        }

        [Test]
        public void Reset_PutsPlannerBackToFirstSegment()
        {
            var planner = new FixedChasePlanner(in scripted);
            planner.Advance();
            planner.Reset();
            Assert.That(planner.SegmentIndex, Is.Zero);
        }
    }

    public sealed class StealthDecisionGateTests
    {
        [Test]
        public void Evaluate_NoEnemyPerceives_IsHidden()
        {
            StealthGateVerdict verdict = StealthDecisionGate.Evaluate(
                new StealthGateInput(false, false, false, KnockdownPhase.None, false), SightSettings.PlaceholderDefault);
            Assert.That(verdict.Hidden, Is.True);
            Assert.That(verdict.Behind, Is.False);
            Assert.That(verdict.Knockdown, Is.False);
        }

        [Test]
        public void Evaluate_EnemyPerceives_IsNotHidden()
        {
            // 负对照：有敌人察觉就不是「未被察觉」。
            StealthGateVerdict verdict = StealthDecisionGate.Evaluate(
                new StealthGateInput(true, false, false, KnockdownPhase.None, false), SightSettings.PlaceholderDefault);
            Assert.That(verdict.Hidden, Is.False);
        }

        [Test]
        public void Evaluate_CoverBlocksWithoutCoverImpliesHidden_StillDetected()
        {
            // 占位口径：掩体只挡视线，不等于隐身（03:132 R25 未定，由配置决定）。
            StealthGateVerdict verdict = StealthDecisionGate.Evaluate(
                new StealthGateInput(true, false, false, KnockdownPhase.None, true), SightSettings.PlaceholderDefault);
            Assert.That(verdict.Cover, Is.True);
            Assert.That(verdict.Hidden, Is.False, "CoverImpliesHidden = false 时掩体不等于隐身");
        }

        [Test]
        public void Evaluate_CoverImpliesHidden_WhenConfigured_IsHidden()
        {
            var settings = new SightSettings(true, true);
            StealthGateVerdict verdict = StealthDecisionGate.Evaluate(
                new StealthGateInput(true, false, false, KnockdownPhase.None, true), settings);
            Assert.That(verdict.Hidden, Is.True);
        }

        [Test]
        public void Evaluate_WritesCoverFactDisabled_DoesNotRaiseCover()
        {
            // 拍板「掩体不做」时把开关关掉：规则照跑，但不对外写 stealth.cover（字典 §4.2 备注）。
            var settings = new SightSettings(false, false);
            StealthGateVerdict verdict = StealthDecisionGate.Evaluate(
                new StealthGateInput(false, false, false, KnockdownPhase.None, true), settings);
            Assert.That(verdict.Cover, Is.False);
        }

        [Test]
        public void Evaluate_DownedOrCrawling_IsKnockdown()
        {
            Assert.That(StealthDecisionGate.Evaluate(
                new StealthGateInput(false, false, false, KnockdownPhase.Downed, false), SightSettings.PlaceholderDefault).Knockdown,
                Is.True);
            Assert.That(StealthDecisionGate.Evaluate(
                new StealthGateInput(false, false, false, KnockdownPhase.Crawling, false), SightSettings.PlaceholderDefault).Knockdown,
                Is.True);
            Assert.That(StealthDecisionGate.Evaluate(
                new StealthGateInput(false, false, false, KnockdownPhase.None, false), SightSettings.PlaceholderDefault).Knockdown,
                Is.False);
        }

        /// <summary>
        /// 负对照：**「这一刀能不能下」不等于「已经杀过他」**。
        /// 判定门曾经把 <c>AssassinationAllowed</c> 当作 <c>assassinated</c> 传下去，
        /// 于是一站到守卫背后就点亮持久的 `stealth.assassinated`，把内容条件弄假成真。
        /// </summary>
        [Test]
        public void Evaluate_AssassinationAllowed_DoesNotClaimAssassinated()
        {
            // 输入侧摆明「这一刀能下」（第三个参数 = assassinationAllowed）。
            var input = new StealthGateInput(false, true, true, KnockdownPhase.None, false);
            Assert.That(input.AssassinationAllowed, Is.True);

            StealthGateVerdict verdict = StealthDecisionGate.Evaluate(in input, SightSettings.PlaceholderDefault);
            Assert.That(verdict.Behind, Is.True);
            Assert.That(verdict.AlreadyAssassinated, Is.False, "能下刀不代表已经杀过");
        }

        /// <summary>正向：事实由调用方传入时照实带出。</summary>
        [Test]
        public void Evaluate_AlreadyAssassinatedFact_IsCarriedThrough()
        {
            var input = new StealthGateInput(false, true, false, KnockdownPhase.None, false, alreadyAssassinated: true);
            Assert.That(input.AssassinationAllowed, Is.False);

            StealthGateVerdict verdict = StealthDecisionGate.Evaluate(in input, SightSettings.PlaceholderDefault);
            Assert.That(verdict.AlreadyAssassinated, Is.True);
        }

        [Test]
        public void ToFacts_CarriesKeysAndTransientFlags()
        {
            StealthGateVerdict verdict = StealthDecisionGate.Evaluate(
                new StealthGateInput(true, true, false, KnockdownPhase.Downed, true),
                SightSettings.PlaceholderDefault,
                KnockdownTier.Mid);
            StealthFact[] facts = StealthDecisionGate.ToFacts(in verdict);

            Assert.That(facts[0].Key, Is.EqualTo("stealth.hidden"));
            Assert.That(facts[0].Value, Is.False);
            Assert.That(facts[0].Transient, Is.True, "字典 §4.2：hidden 是瞬时态");
            Assert.That(facts[1].Transient, Is.True, "behind 是瞬时态");
            Assert.That(facts[2].Transient, Is.True, "knockdown 是瞬时态");
            Assert.That(facts[3].Key, Is.EqualTo("stealth.cover"));
            Assert.That(facts[4].Key, Is.EqualTo("stealth.assassinated"));
            Assert.That(facts[5].Key, Is.EqualTo("stealth.knockdownCount.mid"));
            Assert.That(facts[5].Value, Is.True);
        }

        [Test]
        public void ToFacts_NoKnockdown_LeavesTierKeyEmpty()
        {
            StealthGateVerdict verdict = StealthDecisionGate.Evaluate(
                new StealthGateInput(false, false, false, KnockdownPhase.None, false), SightSettings.PlaceholderDefault);
            Assert.That(StealthDecisionGate.ToFacts(in verdict)[5].Key, Is.Empty, "档位 None 时没有键可写");
        }

        [Test]
        public void PerceivesThroughCover_NoSightInstance_FallsBackToConeOnly()
        {
            Assert.That(StealthDecisionGate.PerceivesThroughCover(null, Vector2.zero, Vector2.one, true), Is.True);
            Assert.That(StealthDecisionGate.PerceivesThroughCover(null, Vector2.zero, Vector2.one, false), Is.False);
        }

        [Test]
        public void PerceivesThroughCover_OccluderBetween_BlocksPerception()
        {
            var sight = new StealthSight();
            sight.SetOccluders(new[] { StealthOccluder.MakeRectangle(Vector2.zero, new Vector2(1f, 4f), 1) });
            Assert.That(StealthDecisionGate.PerceivesThroughCover(sight, new Vector2(-3f, 0f), new Vector2(3f, 0f), true),
                Is.False);
            Assert.That(StealthDecisionGate.PerceivesThroughCover(sight, new Vector2(-3f, 5f), new Vector2(3f, 5f), true),
                Is.True, "掩体挡不住旁边那条视线");
        }
    }

    public sealed class StealthFactSnapshotTests
    {
        [Test]
        public void Emit_WritesOwnedKeys_AndTierTransitionClearsOldTier()
        {
            var writes = new List<string>();
            var snapshot = new StealthFactSnapshot(true, true, false, true, false, true, 2, 1, 3);
            snapshot.Emit((key, value, write) => writes.Add($"{key}={value}"), KnockdownTier.Low);

            Assert.That(writes, Contains.Item("stealth.hidden=True"));
            Assert.That(writes, Contains.Item("stealth.behind=True"));
            Assert.That(writes, Contains.Item("stealth.knockdown=False"));
            Assert.That(writes, Contains.Item("stealth.cover=True"));
            Assert.That(writes, Contains.Item("stealth.visionmask=False"));
            Assert.That(writes, Contains.Item("stealth.assassinated=True"));
            Assert.That(writes, Contains.Item("stealth.knockdownCount.low=False"), "旧档先清");
            Assert.That(writes, Contains.Item("stealth.knockdownCount.mid=True"), "新档再写");
        }

        [Test]
        public void Emit_SameTier_DoesNotTouchTierKeys()
        {
            // 负对照：档位没变就不该重复写档位键。
            var writes = new List<string>();
            var snapshot = new StealthFactSnapshot(false, false, false, false, false, false, 2, 1, 3);
            snapshot.Emit((key, value, write) => writes.Add(key), KnockdownTier.Mid);
            Assert.That(writes, Does.Not.Contain("stealth.knockdownCount.mid"));
            Assert.That(writes, Does.Not.Contain("stealth.knockdownCount.low"));
        }

        [Test]
        public void Constructor_InvalidTierThresholds_AreRejected()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new StealthFactSnapshot(false, false, false, false, false, false, 1, 3, 1));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new StealthFactSnapshot(false, false, false, false, false, false, -1, 1, 3));
        }

        [Test]
        public void Emit_NullSink_IsRejected()
        {
            var snapshot = new StealthFactSnapshot(false, false, false, false, false, false, 0, 1, 3);
            Assert.Throws<System.ArgumentNullException>(() => snapshot.Emit(null, KnockdownTier.None));
        }

        [Test]
        public void TierChanged_OnlyWhenTierDiffers()
        {
            var snapshot = new StealthFactSnapshot(false, false, false, false, false, false, 4, 1, 3);
            Assert.That(snapshot.Tier, Is.EqualTo(KnockdownTier.High));
            Assert.That(snapshot.TierChanged(KnockdownTier.Mid), Is.True);
            Assert.That(snapshot.TierChanged(KnockdownTier.High), Is.False);
        }
    }

    public sealed class StealthConfigValidationTests
    {
        private KnockdownSettings knockdown;
        private ChaseSettings chase;
        private ChaseBaseline baseline;
        private SummonSettings summon;

        [SetUp]
        public void SetUp()
        {
            knockdown = KnockdownSettings.PlaceholderDefault;
            chase = ChaseSettings.PlaceholderDefault;
            baseline = ChaseBaseline.ProjectCurrent;
            summon = SummonSettings.PlaceholderDefault;
        }

        [Test]
        public void Validate_PlaceholderDefaults_ReturnNull()
        {
            Assert.That(Validate(3.6f, 1, 3, 2), Is.Null);
        }

        [Test]
        public void Validate_CurrentMonsterChaseSpeed_IsReportedAsTooSlow()
        {
            // 现状问题（04:165）：巡逻 2 × 敌对倍率 1.25 = 2.5，低于玩家步行 3。
            Assert.That(Validate(2.5f, 1, 3, 2), Does.Contain("不高于玩家步行"));
        }

        [Test]
        public void Validate_ChaseSpeedEqualToWalk_IsAlsoTooSlow()
        {
            // 负对照：刚好等于步行也是追不上（速度相等永远拉不开，只会僵持）。
            Assert.That(Validate(3f, 1, 3, 2), Does.Contain("不高于玩家步行"));
        }

        [Test]
        public void Validate_ChaseSpeedAbovePlayerRun_IsReportedAsUnfair()
        {
            // 负对照：追兵快过玩家奔跑 = 追逐必死（04:175 约束 1）。
            Assert.That(Validate(6f, 1, 3, 2), Does.Contain("高于玩家奔跑"));
        }

        [Test]
        public void Validate_ZeroOrNegativeChaseSpeed_IsReported()
        {
            Assert.That(Validate(0f, 1, 3, 2), Does.Contain("必须为正数"));
            Assert.That(Validate(-1f, 1, 3, 2), Does.Contain("必须为正数"));
        }

        [Test]
        public void Validate_InvalidTierThresholds_AreReported()
        {
            Assert.That(Validate(3.6f, 3, 1, 2), Does.Contain("档位阈值"));
            Assert.That(Validate(3.6f, -1, 3, 2), Does.Contain("档位阈值"));
        }

        [Test]
        public void Validate_InvalidFailCount_IsReported()
        {
            Assert.That(Validate(3.6f, 1, 3, 0), Does.Contain("被抓次数"));
        }

        private string Validate(float chaseSpeed, int lowMax, int midMax, int failCount) =>
            StealthConfigValidation.Validate(knockdown, chase, baseline, summon, chaseSpeed, lowMax, midMax, failCount);
    }

    public sealed class StealthConfigTests
    {
        [Test]
        public void PlaceholderDefaults_PassValidation_AndChaseBeatsPlayerWalk()
        {
            var config = ScriptableObject.CreateInstance<StealthConfig>();
            try
            {
                Assert.That(config.Validate(), Is.Null);
                Assert.That(config.ChaseSpeed, Is.GreaterThan(config.Baseline.PlayerWalkSpeed),
                    "04:165 的现状问题：追击速度必须高于玩家步行");
                Assert.That(config.ChaseSpeed, Is.LessThanOrEqualTo(config.Baseline.PlayerRunSpeed),
                    "但也不能快过玩家奔跑，否则没有操作空间");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void Kernel_BuildsFromConfig_AndPassesSelfCheck()
        {
            var config = ScriptableObject.CreateInstance<StealthConfig>();
            try
            {
                var kernel = new StealthKernel(config);
                Assert.That(kernel.Validate(), Is.Null);
                Assert.That(kernel.Sight.Count, Is.Zero);
                Assert.That(kernel.Chase.VerifyChaseSpeed(), Is.EqualTo(ChaseReject.None));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void Kernel_NullConfig_IsRejected()
        {
            // 负对照：静默用默认值会让策划改的资产不生效。
            Assert.Throws<System.ArgumentNullException>(() => new StealthKernel(null));
        }

        [Test]
        public void Kernel_ResetForNewLevel_ClearsOccludersAndTimers()
        {
            var config = ScriptableObject.CreateInstance<StealthConfig>();
            try
            {
                var kernel = new StealthKernel(config);
                kernel.Sight.SetOccluders(new[] { StealthOccluder.MakeCircle(Vector2.zero, 2f, 1) });
                kernel.Knockdown.Tick(KnockdownInput.TakeHitNow(2), 0.1f);
                kernel.ResetForNewLevel();

                Assert.That(kernel.Sight.Count, Is.Zero);
                Assert.That(kernel.Knockdown.Phase, Is.EqualTo(KnockdownPhase.None));
                Assert.That(kernel.Chase.EscapeCount, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void Config_Validate_ReportsChaseSpeedProblems()
        {
            var config = ScriptableObject.CreateInstance<StealthConfig>();
            try
            {
                config.hideFlags = HideFlags.None;
                var field = typeof(StealthConfig).GetField(
                    "chaseSpeed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null);

                field.SetValue(config, 2.5f);
                Assert.That(config.Validate(), Does.Contain("不高于玩家步行"), "现状 2.5 必须被判为配置问题");

                field.SetValue(config, 6f);
                Assert.That(config.Validate(), Does.Contain("高于玩家奔跑"));

                field.SetValue(config, 3.6f);
                Assert.That(config.Validate(), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
