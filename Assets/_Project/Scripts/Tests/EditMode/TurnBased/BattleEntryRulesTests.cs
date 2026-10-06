// 职责：进入战斗判定（07 一、进入战斗）的 EditMode 测试。
// 每条判定都配负对照：条件少一条、或输入自相矛盾时，必须被拒并点名原因。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:13-28`。

using Game.TurnBased;
using NUnit.Framework;

namespace Game.Tests.EditMode.TurnBased
{
    public sealed class BattleEntryRulesTests
    {
        private static readonly BattleEntrySettings Settings = BattleEntrySettings.PlaceholderDefault;

        private static BattleEntryRequest Sneak(bool sneaking, bool landed) =>
            new BattleEntryRequest(sneaking, landed, false, false, MonsterAlertState.Unaware, BattleInitiator.PlayerAttacked);

        private static BattleEntryRequest Frontal(
            bool alertZone,
            bool hostileZone,
            MonsterAlertState state,
            BattleInitiator initiator) =>
            new BattleEntryRequest(false, false, alertZone, hostileZone, state, initiator);

        [Test]
        public void Sneak_SneakingAndStrikeLanded_PlayerInitiativeAndBossLosesTwentyPercent()
        {
            BattleEntryDecision decision = BattleEntryRules.Decide(Sneak(true, true), Settings);

            Assert.That(decision.Accepted, Is.True, decision.Describe());
            Assert.That(decision.Kind, Is.EqualTo(BattleEntryKind.SneakAttack));
            Assert.That(decision.Initiative, Is.EqualTo(BattleInitiative.Player));
            // 07:18「BOSS生命值减少20%」
            Assert.That(decision.BossHealthLossPercent, Is.EqualTo(20));
            Assert.That(decision.BossHealthLossBase, Is.EqualTo(HealthPercentBase.MaxHealth));
            Assert.That(decision.Reject, Is.EqualTo(BattleEntryReject.None));
        }

        [Test]
        public void Sneak_SneakingButStrikeMissed_IsRefused()
        {
            // 负对照：潜行了但偷袭没打中（07:17 要求「偷袭怪物成功」）。
            BattleEntryDecision decision = BattleEntryRules.Decide(Sneak(true, false), Settings);

            Assert.That(decision.Accepted, Is.False);
            Assert.That(decision.Kind, Is.EqualTo(BattleEntryKind.None));
            Assert.That(decision.Initiative, Is.EqualTo(BattleInitiative.None));
            Assert.That(decision.BossHealthLossPercent, Is.EqualTo(0));
            Assert.That(decision.Reject, Is.EqualTo(BattleEntryReject.SneakStrikeNotLanded));
        }

        [Test]
        public void Sneak_NotSneakingButStrikeSucceeded_IsRefused()
        {
            // 负对照：偷袭命中却不在潜行（07:17 的两个条件缺一不可）。
            BattleEntryDecision decision = BattleEntryRules.Decide(Sneak(false, true), Settings);

            Assert.That(decision.Accepted, Is.False);
            Assert.That(decision.Reject, Is.EqualTo(BattleEntryReject.NoTrigger));
        }

        [Test]
        public void Sneak_StrikeSucceededButBossAttackedFirst_IsRefusedAsInconsistent()
        {
            // 负对照：偷袭的定义是「玩家攻击 BOSS」，不可能同时又由 BOSS 先动手（07:18 与 07:28 冲突）。
            var request = new BattleEntryRequest(
                true, true, true, true, MonsterAlertState.Hostile, BattleInitiator.BossAttacked);

            BattleEntryDecision decision = BattleEntryRules.Decide(request, Settings);

            Assert.That(decision.Accepted, Is.False);
            Assert.That(decision.Reject, Is.EqualTo(BattleEntryReject.InconsistentInitiator));
        }

        [Test]
        public void Sneak_DoesNotRequireAlertOrHostileZone()
        {
            // 07:15-18 的触发条件里没有「在警戒 / 敌对区域内」这一条。
            BattleEntryDecision decision = BattleEntryRules.Decide(Sneak(true, true), Settings);
            Assert.That(decision.Accepted, Is.True);
            Assert.That(decision.Kind, Is.EqualTo(BattleEntryKind.SneakAttack));
        }

        [Test]
        public void Frontal_PlayerAttacksInsideAlertZone_PlayerInitiative()
        {
            BattleEntryDecision decision = BattleEntryRules.Decide(
                Frontal(true, false, MonsterAlertState.Unaware, BattleInitiator.PlayerAttacked), Settings);

            Assert.That(decision.Accepted, Is.True);
            Assert.That(decision.Kind, Is.EqualTo(BattleEntryKind.FrontalAttack));
            Assert.That(decision.Initiative, Is.EqualTo(BattleInitiative.Player));
            // 正面攻击不掉 BOSS 血（07:23 只写「玩家获得先手攻击权」）。
            Assert.That(decision.BossHealthLossPercent, Is.EqualTo(0));
        }

        [Test]
        public void Frontal_PlayerAttacksInsideHostileZone_PlayerInitiative()
        {
            BattleEntryDecision decision = BattleEntryRules.Decide(
                Frontal(false, true, MonsterAlertState.Unaware, BattleInitiator.PlayerAttacked), Settings);

            Assert.That(decision.Accepted, Is.True);
            Assert.That(decision.Kind, Is.EqualTo(BattleEntryKind.FrontalAttack));
        }

        [Test]
        public void Frontal_PlayerAttacksMonsterInAlertState_PlayerInitiative()
        {
            BattleEntryDecision decision = BattleEntryRules.Decide(
                Frontal(false, false, MonsterAlertState.Alert, BattleInitiator.PlayerAttacked), Settings);

            Assert.That(decision.Accepted, Is.True);
            Assert.That(decision.Kind, Is.EqualTo(BattleEntryKind.FrontalAttack));
            Assert.That(decision.Initiative, Is.EqualTo(BattleInitiative.Player));
        }

        [Test]
        public void Frontal_PlayerAttacksMonsterInHostileState_PlayerInitiative()
        {
            BattleEntryDecision decision = BattleEntryRules.Decide(
                Frontal(false, false, MonsterAlertState.Hostile, BattleInitiator.PlayerAttacked), Settings);

            Assert.That(decision.Accepted, Is.True);
            Assert.That(decision.Kind, Is.EqualTo(BattleEntryKind.FrontalAttack));
        }

        [Test]
        public void Frontal_OutsideZonesAndMonsterUnaware_IsRefused()
        {
            // 负对照：不在任何区域内、怪物也没察觉，玩家主动攻击不构成「进入战斗」的触发条件。
            BattleEntryDecision decision = BattleEntryRules.Decide(
                Frontal(false, false, MonsterAlertState.Unaware, BattleInitiator.PlayerAttacked), Settings);

            Assert.That(decision.Accepted, Is.False);
            Assert.That(decision.Kind, Is.EqualTo(BattleEntryKind.None));
            Assert.That(decision.Reject, Is.EqualTo(BattleEntryReject.NoTrigger));
        }

        [Test]
        public void Ambush_BossAttacksInsideAlertZone_BossInitiative()
        {
            BattleEntryDecision decision = BattleEntryRules.Decide(
                Frontal(true, false, MonsterAlertState.Unaware, BattleInitiator.BossAttacked), Settings);

            Assert.That(decision.Accepted, Is.True);
            Assert.That(decision.Kind, Is.EqualTo(BattleEntryKind.Ambushed));
            Assert.That(decision.Initiative, Is.EqualTo(BattleInitiative.Boss));
            Assert.That(decision.BossHealthLossPercent, Is.EqualTo(0));
        }

        [Test]
        public void Ambush_BossAttacksMonsterInHostileState_BossInitiative()
        {
            BattleEntryDecision decision = BattleEntryRules.Decide(
                Frontal(false, false, MonsterAlertState.Hostile, BattleInitiator.BossAttacked), Settings);

            Assert.That(decision.Accepted, Is.True);
            Assert.That(decision.Kind, Is.EqualTo(BattleEntryKind.Ambushed));
            Assert.That(decision.Initiative, Is.EqualTo(BattleInitiative.Boss));
        }

        [Test]
        public void Ambush_OutsideZonesAndMonsterUnaware_IsRefused()
        {
            // 负对照：挨打也要有前提（07:27「触发条件：玩家处于怪物警戒区域或敌对区域内……」）。
            BattleEntryDecision decision = BattleEntryRules.Decide(
                Frontal(false, false, MonsterAlertState.Unaware, BattleInitiator.BossAttacked), Settings);

            Assert.That(decision.Accepted, Is.False);
            Assert.That(decision.Reject, Is.EqualTo(BattleEntryReject.NoTrigger));
        }

        [Test]
        public void Sneak_LossBaseFollowsSettings()
        {
            var settings = new BattleEntrySettings(20, HealthPercentBase.CurrentHealth);
            BattleEntryDecision decision = BattleEntryRules.Decide(Sneak(true, true), settings);

            Assert.That(decision.BossHealthLossBase, Is.EqualTo(HealthPercentBase.CurrentHealth));
        }

        [Test]
        public void Sneak_LossPercentZeroInSettings_KeepsBossAtFullHealth()
        {
            // 负对照：扣血比例是可配的，配成 0 时判定仍然通过但不扣血。
            var settings = new BattleEntrySettings(0, HealthPercentBase.MaxHealth);
            BattleEntryDecision decision = BattleEntryRules.Decide(Sneak(true, true), settings);

            Assert.That(decision.Accepted, Is.True);
            Assert.That(decision.BossHealthLossPercent, Is.EqualTo(0));
        }

        [Test]
        public void Describe_RefusedAndAccepted_NamesTheSituation()
        {
            Assert.That(BattleEntryRules.Decide(Sneak(true, true), Settings).Describe(), Does.Contain("偷袭"));
            Assert.That(BattleEntryRules.Decide(Sneak(true, true), Settings).Describe(), Does.Contain("玩家先手"));
            Assert.That(BattleEntryRules.Decide(Sneak(true, true), Settings).Describe(), Does.Contain("-20%"));

            BattleEntryDecision refused = BattleEntryRules.Decide(
                Frontal(false, false, MonsterAlertState.Unaware, BattleInitiator.PlayerAttacked), Settings);
            Assert.That(refused.Describe(), Does.Contain("未进入战斗"));
        }
    }
}
