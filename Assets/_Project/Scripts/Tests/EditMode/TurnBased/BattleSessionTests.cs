// 职责：整场战斗的回合流程测试——三种进入方式的先手、玩家施放招式后进敌方回合、
// 道具只能在出招前用、醉酒跳过回合的提示事件、晕眩跳过玩家回合、以及两种结束条件。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:13-28`（进入）、:46-58（流程与招式）、
// :70-84（醉酒与 BOSS 招式）。

using System.Linq;
using Game.Core.Simulation;
using Game.TurnBased;
using NUnit.Framework;

namespace Game.Tests.EditMode.TurnBased
{
    public sealed class BattleSessionTests
    {
        private static BattleEntryRequest SneakRequest() =>
            new BattleEntryRequest(true, true, false, false, MonsterAlertState.Unaware, BattleInitiator.PlayerAttacked);

        private static BattleEntryRequest FrontalRequest() =>
            new BattleEntryRequest(false, false, true, false, MonsterAlertState.Unaware, BattleInitiator.PlayerAttacked);

        private static BattleEntryRequest AmbushRequest() =>
            new BattleEntryRequest(false, false, false, true, MonsterAlertState.Unaware, BattleInitiator.BossAttacked);

        private static BattleSession Start(
            in BattleEntryRequest request,
            BattleSettings? settings = null,
            int bossMaxHealth = 100,
            int bossHealth = 100,
            int bossDrunk = 0,
            int playerMaxHealth = 10,
            int playerHealth = 10,
            IRandomStream random = null,
            IBattleItemInventory inventory = null)
        {
            BattleSettings effective = settings ?? BattleSettings.PlaceholderDefault;
            var kernel = new TurnBasedKernel(effective, random ?? new XorShiftRandomStream(1UL), inventory);

            bool started = kernel.TryStartBattle(
                request,
                new PlayerBattleSnapshot(playerMaxHealth, playerHealth),
                new BossBattleSnapshot(bossMaxHealth, bossHealth, bossDrunk),
                out BattleSession session,
                out BattleEntryReject reject);

            Assert.That(started, Is.True, "应该进得去战斗，被拒原因：" + reject);
            return session;
        }

        private static bool HasEvent(BattleSession session, BattleEventKind kind) =>
            session.Events.Any(e => e.Kind == kind);

        private static BattleEvent FirstEvent(BattleSession session, BattleEventKind kind) =>
            session.Events.First(e => e.Kind == kind);

        [Test]
        public void Sneak_BossLosesTwentyPercentAndPlayerActsFirst()
        {
            BattleSession session = Start(SneakRequest());

            // 07:18 偷袭：玩家先手 + BOSS 生命 -20%。
            Assert.That(session.Boss.Health, Is.EqualTo(80));
            Assert.That(session.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            Assert.That(HasEvent(session, BattleEventKind.SneakHealthLoss), Is.True);
            Assert.That(FirstEvent(session, BattleEventKind.SneakHealthLoss).Amount, Is.EqualTo(20));
            Assert.That(session.Player.Rage, Is.EqualTo(0));
        }

        [Test]
        public void SneakDamage_ThatKillsTheBoss_EndsTheBattleImmediately()
        {
            // BOSS 上限 10、当前 2，偷袭扣 20% 上限 = 2 → 直接归零。
            BattleSession session = Start(SneakRequest(), bossMaxHealth: 10, bossHealth: 2);

            Assert.That(session.IsOver, Is.True);
            Assert.That(session.Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(session.ExitKey, Is.EqualTo(BattleExitKeys.Victory));
            Assert.That(HasEvent(session, BattleEventKind.BossDefeated), Is.True);
        }

        [Test]
        public void Frontal_NoHealthLossAndPlayerActsFirst()
        {
            BattleSession session = Start(FrontalRequest());

            Assert.That(session.Boss.Health, Is.EqualTo(100));
            Assert.That(session.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            Assert.That(session.Events.Count, Is.EqualTo(0), "正面攻击没有开战扣血");
        }

        [Test]
        public void Ambush_BossActsFirstAndPlayerCastIsRefused()
        {
            BattleSession session = Start(AmbushRequest());

            // 07:28 被打：BOSS 先手。
            Assert.That(session.Phase, Is.EqualTo(BattlePhase.BossTurn));

            // 负对照：BOSS 先手的第一个回合，玩家出招被拒。
            SkillCastResult refused = session.TryCastSkill(PlayerSkill.Skill1);
            Assert.That(refused.Accepted, Is.False);
            Assert.That(refused.Reject, Is.EqualTo(SkillCastReject.NotPlayerTurn));
            Assert.That(session.Boss.Health, Is.EqualTo(100));
        }

        [Test]
        public void PlayerCast_DamagesTheBossAndHandsTheTurnToTheBoss()
        {
            BattleSession session = Start(FrontalRequest());

            SkillCastResult result = session.TryCastSkill(PlayerSkill.Skill1);

            Assert.That(result.Accepted, Is.True);
            Assert.That(session.Boss.Health, Is.EqualTo(99));
            // 07:56 玩家在施放招式后进入敌方回合。
            Assert.That(session.Phase, Is.EqualTo(BattlePhase.BossTurn));
            Assert.That(HasEvent(session, BattleEventKind.SkillCast), Is.True);
        }

        [Test]
        public void BossTurn_ReturnsTheTurnToThePlayerAndCountsTheRound()
        {
            BattleSession session = Start(FrontalRequest());
            session.TryCastSkill(PlayerSkill.Skill1);

            session.RunBossTurn();

            Assert.That(session.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            Assert.That(session.CompletedRounds, Is.EqualTo(1));
            Assert.That(session.Player.HasCastThisTurn, Is.False, "新回合应该能再出招");
        }

        [Test]
        public void Item_IsUsableBeforeCasting_ButNotAfter()
        {
            var inventory = new FakeBattleItemInventory("item.yao", "item.fu");
            BattleSession session = Start(FrontalRequest(), inventory: inventory);

            // 07:58 玩家可以在施放招式之前使用道具。
            Assert.That(session.TryUseItem("item.yao").Allowed, Is.True);
            Assert.That(HasEvent(session, BattleEventKind.ItemUsed), Is.True);

            session.TryCastSkill(PlayerSkill.Skill1);

            // 负对照：出招之后（已是敌方回合）再用道具被拒。
            ItemUseDecision afterCast = session.TryUseItem("item.fu");
            Assert.That(afterCast.Allowed, Is.False);
            Assert.That(afterCast.Reject, Is.EqualTo(ItemUseReject.WrongPhase));
        }

        [Test]
        public void Item_SecondUseInTheSameBattle_IsRefused_ButAnotherItemStillWorks()
        {
            var inventory = new FakeBattleItemInventory("item.yao", "item.fu");
            BattleSession session = Start(FrontalRequest(), inventory: inventory, random: new XorShiftRandomStream(9UL));

            Assert.That(session.TryUseItem("item.yao").Allowed, Is.True);
            session.TryCastSkill(PlayerSkill.Skill1);
            session.RunBossTurn();

            // 负对照：本场用过的那件道具不能再用。
            Assert.That(session.TryUseItem("item.yao").Reject, Is.EqualTo(ItemUseReject.UsedThisBattle));
            Assert.That(session.TryUseItem("item.fu").Allowed, Is.True);
        }

        [Test]
        public void Item_NotOwned_IsRefused()
        {
            // 负对照：背包里没有的道具不能用（不读背包，只问注入的口）。
            BattleSession session = Start(FrontalRequest(), inventory: new FakeBattleItemInventory("item.yao"));

            Assert.That(session.TryUseItem("item.jinlongdan").Reject, Is.EqualTo(ItemUseReject.NotOwned));
        }

        [Test]
        public void BossDrunkSkip_EmitsTheFlashHintAndTheBossDoesNothing()
        {
            // 微醺（60）掷出 0 < 20 → 跳过（07:71）。
            BattleSession session = Start(
                FrontalRequest(),
                bossDrunk: 60,
                random: FixedRandomStream.ForRolls(0));

            session.TryCastSkill(PlayerSkill.Skill1);
            session.RunBossTurn();

            Assert.That(HasEvent(session, BattleEventKind.BossTurnSkipped), Is.True);
            BattleEvent skip = FirstEvent(session, BattleEventKind.BossTurnSkipped);
            Assert.That(skip.DrunkTier, Is.EqualTo(DrunkTier.Tipsy));
            Assert.That(skip.SkipReason, Is.EqualTo(DrunkSkipReason.Tipsy));
            Assert.That(skip.HintText, Is.EqualTo(BattleHintTexts.TipsySkip), "屏幕中央闪现提示的文案");
            Assert.That(session.Player.Health, Is.EqualTo(10), "跳过的回合不该打人");
            Assert.That(session.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
        }

        [Test]
        public void BossDrunkSkip_WhenTheRollMisses_TheBossActsNormally()
        {
            // 负对照：掷出 99 ≥ 20 → 不跳过，正常出招（选招那次掷出 0 → 权重 6:3:1 命中招式 1）。
            BattleSession session = Start(
                FrontalRequest(),
                bossDrunk: 60,
                random: FixedRandomStream.ForRolls(99, 0));

            session.TryCastSkill(PlayerSkill.Skill1);
            session.RunBossTurn();

            Assert.That(HasEvent(session, BattleEventKind.BossTurnSkipped), Is.False);
            Assert.That(HasEvent(session, BattleEventKind.BossSkillUsed), Is.True);
            Assert.That(session.Player.Health, Is.EqualTo(9), "BOSS 招式 1 打掉 1 点（占位伤害）");
        }

        [Test]
        public void DeadDrunk_SkipsTwoBossTurnsAndDropsFifty()
        {
            // 07:73 酩酊：100% 跳过、醉酒 -50、持续 2 回合；之后回到微醺的概率（掷 99 不跳）。
            BattleSession session = Start(
                FrontalRequest(),
                bossDrunk: 100,
                random: FixedRandomStream.ForRolls(99, 0));

            session.TryCastSkill(PlayerSkill.Skill1);
            session.RunBossTurn();
            Assert.That(HasEvent(session, BattleEventKind.BossTurnSkipped), Is.True);
            Assert.That(session.Boss.DrunkValue, Is.EqualTo(50));
            Assert.That(session.Boss.Drunk.DeadDrunkRoundsRemaining, Is.EqualTo(1));

            session.TryCastSkill(PlayerSkill.Skill1);
            session.RunBossTurn();
            Assert.That(HasEvent(session, BattleEventKind.BossTurnSkipped), Is.True);
            Assert.That(session.Boss.Drunk.DeadDrunkRoundsRemaining, Is.EqualTo(0));

            session.TryCastSkill(PlayerSkill.Skill1);
            session.RunBossTurn();
            Assert.That(HasEvent(session, BattleEventKind.BossTurnSkipped), Is.False, "持续期结束后不再无条件跳过");
            Assert.That(HasEvent(session, BattleEventKind.BossSkillUsed), Is.True);
        }

        [Test]
        public void BossSkill3_InDrunkTier_StunsThePlayerAndSkipsTheNextPlayerTurn()
        {
            // 07:82 薄醉态的招式 3：高额伤害 + 晕眩玩家 1 回合。
            var settings = SettingsWithBossSkills(new BossSkillSettings(1, 30, 10, HealthPercentBase.MaxHealth, 30, 3, 1, 0, 0, 1));
            BattleSession session = Start(
                FrontalRequest(),
                settings: settings,
                bossDrunk: 85,
                random: FixedRandomStream.ForRolls(99, 0, 99, 0));

            session.TryCastSkill(PlayerSkill.Skill1);
            session.RunBossTurn();

            Assert.That(HasEvent(session, BattleEventKind.BossSkillUsed), Is.True);
            Assert.That(HasEvent(session, BattleEventKind.PlayerStunned), Is.True);
            Assert.That(session.Player.Health, Is.EqualTo(7));
            // 晕眩「跳过整个玩家回合」口径（占位）：回合自动交回 BOSS，并且有事件说明跳过了。
            Assert.That(session.Player.CannotActThisTurn, Is.True);
            Assert.That(HasEvent(session, BattleEventKind.PlayerStunnedTurnSkipped), Is.True);
            Assert.That(session.Phase, Is.EqualTo(BattlePhase.BossTurn));

            // 让 BOSS 不再处于薄醉态（招式 3 只在薄醉态晕眩玩家，07:82），
            // 这样下一个玩家回合应当能正常出招——否则它会一直被晕到死。
            session.Boss.Drunk.Set(0);
            session.RunBossTurn();
            Assert.That(session.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            Assert.That(session.Player.CannotActThisTurn, Is.False);
            Assert.That(session.TryCastSkill(PlayerSkill.Skill1).Accepted, Is.True);
        }

        [Test]
        public void BossSkill3_InTipsyTier_DoesNotStunThePlayer()
        {
            // 负对照：微醺态（60）的招式 3 只造成伤害，不晕眩（07:81）。
            var settings = SettingsWithBossSkills(new BossSkillSettings(1, 30, 10, HealthPercentBase.MaxHealth, 30, 3, 1, 0, 0, 1));
            BattleSession session = Start(
                FrontalRequest(),
                settings: settings,
                bossDrunk: 60,
                random: FixedRandomStream.ForRolls(99, 0));

            session.TryCastSkill(PlayerSkill.Skill1);
            session.RunBossTurn();

            Assert.That(HasEvent(session, BattleEventKind.PlayerStunned), Is.False);
            Assert.That(session.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            Assert.That(session.Player.CannotActThisTurn, Is.False);
            Assert.That(session.TryCastSkill(PlayerSkill.Skill1).Accepted, Is.True);
        }

        [Test]
        public void StunPolicy_ItemsOnly_KeepsTheTurnForItemsButRefusesSkills()
        {
            // 把晕眩口径拨到 C91 的 B 方向：不能出招，但还能用道具。
            var settings = new BattleSettings(
                BattleEntrySettings.PlaceholderDefault,
                PlayerSkillSettings.PlaceholderDefault,
                new BossSkillSettings(1, 30, 10, HealthPercentBase.MaxHealth, 30, 3, 1, 0, 0, 1),
                DrunkSettings.PlaceholderDefault,
                new BattleFlowSettings(0, RoundLimitOutcome.CompareHealth, StunTurnPolicy.ItemsOnly),
                true,
                true);

            BattleSession session = Start(
                FrontalRequest(),
                settings: settings,
                bossDrunk: 85,
                random: FixedRandomStream.ForRolls(99, 0),
                inventory: new FakeBattleItemInventory("item.yao"));

            session.TryCastSkill(PlayerSkill.Skill1);
            session.RunBossTurn();

            Assert.That(session.Phase, Is.EqualTo(BattlePhase.PlayerTurn), "ItemsOnly 口径下回合还留在玩家这边");
            Assert.That(session.Player.CannotActThisTurn, Is.True);
            Assert.That(session.TryCastSkill(PlayerSkill.Skill1).Reject, Is.EqualTo(SkillCastReject.Stunned));
            Assert.That(session.TryUseItem("item.yao").Allowed, Is.True);

            session.SkipPlayerTurn();
            Assert.That(session.Phase, Is.EqualTo(BattlePhase.BossTurn));
        }

        [Test]
        public void BossDrink_RaisesDrunkHealsAndArmsTheNextSkill1Bonus()
        {
            // 07:80 招式 2 饮酒：+30 醉酒、回 10% 生命、下次招式 1 +30%。
            var settings = SettingsWithBossSkills(new BossSkillSettings(10, 30, 10, HealthPercentBase.MaxHealth, 30, 3, 1, 0, 1, 0));
            BattleSession session = Start(
                FrontalRequest(),
                settings: settings,
                bossHealth: 50,
                bossDrunk: 60,
                random: FixedRandomStream.ForRolls(99, 0));

            session.TryCastSkill(PlayerSkill.Skill1);
            session.RunBossTurn();

            Assert.That(HasEvent(session, BattleEventKind.BossDrank), Is.True);
            BattleEvent drank = FirstEvent(session, BattleEventKind.BossDrank);
            Assert.That(drank.Amount, Is.EqualTo(30));
            Assert.That(drank.SecondaryAmount, Is.EqualTo(10));
            Assert.That(session.Boss.DrunkValue, Is.EqualTo(90));
            // 50（初始）- 1（玩家那一下招式 1）+ 10（回 10% 上限）= 59。
            Assert.That(session.Boss.Health, Is.EqualTo(59));
            Assert.That(session.Boss.NextSkill1DamageBonusPending, Is.True);
        }

        [Test]
        public void BossSkill1_WithTheArmedBonus_HitsThirtyPercentHarderOnce()
        {
            // 招式 2 挂上的加成由招式 1 吃掉，且只吃一次（用公开接口直接挂一次，隔离出「消耗」这条路径）。
            var settings = SettingsWithBossSkills(new BossSkillSettings(10, 30, 10, HealthPercentBase.MaxHealth, 30, 3, 1, 1, 0, 0));
            BattleSession session = Start(
                FrontalRequest(),
                settings: settings,
                playerMaxHealth: 50,
                playerHealth: 50,
                random: FixedRandomStream.ForRolls(0, 0, 0));

            session.Boss.GrantSkill1DamageBonus();
            session.TryCastSkill(PlayerSkill.Skill1);
            session.RunBossTurn();

            Assert.That(session.Player.Health, Is.EqualTo(50 - 13), "10 + 30% = 13");
            Assert.That(session.Boss.NextSkill1DamageBonusPending, Is.False, "「下次」只用一次");

            // 负对照：第二次普攻回到基础伤害。
            session.TryCastSkill(PlayerSkill.Skill1);
            session.RunBossTurn();
            Assert.That(session.Player.Health, Is.EqualTo(50 - 13 - 10));
        }

        [Test]
        public void BossHealthZero_EndsTheBattleWithVictory()
        {
            BattleSession session = Start(FrontalRequest(), bossHealth: 1);

            session.TryCastSkill(PlayerSkill.Skill1);

            Assert.That(session.Boss.IsDefeated, Is.True);
            Assert.That(session.IsOver, Is.True);
            Assert.That(session.Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(session.ExitKey, Is.EqualTo(BattleExitKeys.Victory));
            Assert.That(HasEvent(session, BattleEventKind.BossDefeated), Is.True);
        }

        [Test]
        public void PlayerHealthZero_EndsTheBattleWithDefeat()
        {
            var settings = SettingsWithBossSkills(new BossSkillSettings(5, 30, 10, HealthPercentBase.MaxHealth, 30, 3, 1, 6, 3, 1));
            BattleSession session = Start(
                AmbushRequest(),
                settings: settings,
                playerHealth: 1,
                random: FixedRandomStream.ForRolls(0));

            session.RunBossTurn();

            Assert.That(session.Player.IsDefeated, Is.True);
            Assert.That(session.Outcome, Is.EqualTo(BattleOutcome.Defeat));
            Assert.That(session.ExitKey, Is.EqualTo(BattleExitKeys.Downed));
            Assert.That(HasEvent(session, BattleEventKind.PlayerDefeated), Is.True);
        }

        [Test]
        public void RoundLimit_CompareHealth_JudgesByRemainingHealthRatio()
        {
            // 让 BOSS 只饮酒且不回血（玩家血量不变），从而把「按剩余生命比例判」这条路径钉死：
            // 玩家满血 10/10 = 1.0，BOSS 挨了一下 99/100 = 0.99 → 判玩家赢。
            var settings = SettingsWithFlowAndBoss(
                new BattleFlowSettings(1, RoundLimitOutcome.CompareHealth, StunTurnPolicy.SkipTurn),
                new BossSkillSettings(1, 30, 0, HealthPercentBase.MaxHealth, 30, 3, 1, 0, 1, 0));

            BattleSession winning = Start(FrontalRequest(), settings: settings, random: FixedRandomStream.ForRolls(0));
            winning.TryCastSkill(PlayerSkill.Skill1);
            winning.RunBossTurn();

            Assert.That(winning.IsOver, Is.True);
            Assert.That(HasEvent(winning, BattleEventKind.RoundLimitReached), Is.True);
            Assert.That(winning.Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(winning.ExitKey, Is.EqualTo(BattleExitKeys.Victory));

            // 负对照：玩家只剩 1 点血（0.1 < 0.99）→ 判负。
            BattleSession losing = Start(
                FrontalRequest(),
                settings: settings,
                playerHealth: 1,
                random: FixedRandomStream.ForRolls(0));
            losing.TryCastSkill(PlayerSkill.Skill1);
            losing.RunBossTurn();

            Assert.That(losing.Outcome, Is.EqualTo(BattleOutcome.Defeat));
            Assert.That(losing.ExitKey, Is.EqualTo(BattleExitKeys.Downed));
        }

        [Test]
        public void RoundLimit_FixedOutcomePolicy_IsRespected()
        {
            // 配置项真的生效：同样的局面，口径拨成「直接判赢」就判赢。
            // BOSS 只饮酒不回血，保证玩家不会先被打死（那样结束原因就不是回合上限了）。
            var settings = SettingsWithFlowAndBoss(
                new BattleFlowSettings(1, RoundLimitOutcome.Victory, StunTurnPolicy.SkipTurn),
                new BossSkillSettings(1, 30, 0, HealthPercentBase.MaxHealth, 30, 3, 1, 0, 1, 0));

            BattleSession session = Start(
                FrontalRequest(),
                settings: settings,
                playerHealth: 1,
                random: FixedRandomStream.ForRolls(0));
            session.TryCastSkill(PlayerSkill.Skill1);
            session.RunBossTurn();

            Assert.That(session.Outcome, Is.EqualTo(BattleOutcome.Victory));
        }

        [Test]
        public void RoundLimit_ZeroMeansNoLimit()
        {
            // 负对照：回合上限配 0 时不该有任何「到点结束」的行为。
            BattleSession session = Start(
                FrontalRequest(),
                playerMaxHealth: 100,
                playerHealth: 100,
                random: new XorShiftRandomStream(5UL));

            for (int i = 0; i < 5; i++)
            {
                session.TryCastSkill(PlayerSkill.Skill1);
                session.RunBossTurn();
            }

            Assert.That(session.IsOver, Is.False);
            Assert.That(session.CompletedRounds, Is.EqualTo(5));
            Assert.That(HasEvent(session, BattleEventKind.RoundLimitReached), Is.False);
        }

        [Test]
        public void AfterTheBattleEnds_FurtherActionsChangeNothing()
        {
            BattleSession session = Start(FrontalRequest(), bossHealth: 1);
            session.TryCastSkill(PlayerSkill.Skill1);
            Assert.That(session.IsOver, Is.True);

            // 负对照：结束之后出招 / 用道具 / 推敌方回合都不该改变状态。
            Assert.That(session.TryCastSkill(PlayerSkill.Skill1).Reject, Is.EqualTo(SkillCastReject.NotPlayerTurn));
            Assert.That(session.TryUseItem("item.yao").Reject, Is.EqualTo(ItemUseReject.WrongPhase));
            session.RunBossTurn();

            Assert.That(session.Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(session.Boss.Health, Is.EqualTo(0));
        }

        [Test]
        public void Events_AreScopedToTheLatestAction()
        {
            BattleSession session = Start(FrontalRequest());

            session.TryCastSkill(PlayerSkill.Skill1);
            Assert.That(HasEvent(session, BattleEventKind.SkillCast), Is.True);

            session.RunBossTurn();

            Assert.That(HasEvent(session, BattleEventKind.SkillCast), Is.False, "事件列表只反映最近一次动作");
        }

        private static BattleSettings SettingsWithBossSkills(in BossSkillSettings bossSkills) =>
            new BattleSettings(
                BattleEntrySettings.PlaceholderDefault,
                PlayerSkillSettings.PlaceholderDefault,
                bossSkills,
                DrunkSettings.PlaceholderDefault,
                BattleFlowSettings.PlaceholderDefault,
                true,
                true);

        private static BattleSettings SettingsWithFlowAndBoss(
            in BattleFlowSettings flow,
            in BossSkillSettings bossSkills) =>
            new BattleSettings(
                BattleEntrySettings.PlaceholderDefault,
                PlayerSkillSettings.PlaceholderDefault,
                bossSkills,
                DrunkSettings.PlaceholderDefault,
                flow,
                true,
                true);
    }
}
