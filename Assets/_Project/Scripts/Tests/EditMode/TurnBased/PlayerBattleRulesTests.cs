// 职责：玩家侧状态机（怒气收支、三招式、额外伤害次数、晕眩）的 EditMode 测试。
// 每条判定都配负对照：怒气不够、同一回合出第二招、晕眩中出招、被打倒后出招都必须被拒。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:48-56`（招式与回合）、:82（晕眩）。

using Game.TurnBased;
using NUnit.Framework;

namespace Game.Tests.EditMode.TurnBased
{
    public sealed class PlayerBattleRulesTests
    {
        private static readonly PlayerSkillSettings Settings = PlayerSkillSettings.PlaceholderDefault;

        private static PlayerBattleRules NewPlayer(int maxHealth = 10, int health = 10) =>
            new PlayerBattleRules(Settings, new PlayerBattleSnapshot(maxHealth, health));

        /// <summary>攒怒气：连打 n 次招式 1（每次之间推进一个回合）。</summary>
        private static void GainRage(PlayerBattleRules player, int casts)
        {
            for (int i = 0; i < casts; i++)
            {
                Assert.That(player.TryCast(PlayerSkill.Skill1).Accepted, Is.True);
                player.BeginTurn();
            }
        }

        [Test]
        public void Skill1_NoCost_GainsOneRageAndDealsDamage()
        {
            PlayerBattleRules player = NewPlayer();

            SkillCastResult result = player.TryCast(PlayerSkill.Skill1);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.RageBefore, Is.EqualTo(0));
            Assert.That(result.RageAfter, Is.EqualTo(1));
            Assert.That(result.Damage, Is.EqualTo(Settings.Skill1Damage));
            Assert.That(player.Rage, Is.EqualTo(1));
        }

        [Test]
        public void Skill1_AtRageMax_DoesNotExceedTheCap()
        {
            // 负对照：怒气上限（占位 3）到了就不再涨。
            PlayerBattleRules player = NewPlayer();
            GainRage(player, 3);
            Assert.That(player.Rage, Is.EqualTo(player.RageMax));

            player.TryCast(PlayerSkill.Skill1);
            Assert.That(player.Rage, Is.EqualTo(player.RageMax));
        }

        [Test]
        public void Skill2_WithEnoughRage_CostsOneAndAppliesHealReduction()
        {
            PlayerBattleRules player = NewPlayer();
            GainRage(player, 1);

            SkillCastResult result = player.TryCast(PlayerSkill.Skill2);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.RageBefore, Is.EqualTo(1));
            Assert.That(result.RageAfter, Is.EqualTo(0));
            Assert.That(result.Damage, Is.EqualTo(Settings.Skill2Damage));
            // 07:52「减少对方50%治疗效果」
            Assert.That(result.HealReductionPercent, Is.EqualTo(50));
        }

        [Test]
        public void Skill2_WithoutRage_IsRefusedAndKeepsRage()
        {
            // 负对照：0 点怒气放不出消耗 1 点的招式 2。
            PlayerBattleRules player = NewPlayer();

            SkillCastResult result = player.TryCast(PlayerSkill.Skill2);

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Reject, Is.EqualTo(SkillCastReject.NotEnoughRage));
            Assert.That(result.RageAfter, Is.EqualTo(0));
            Assert.That(player.HasCastThisTurn, Is.False, "被拒不算出过招");
        }

        [Test]
        public void Skill3_WithTwoRage_IsRefused()
        {
            // 负对照：2 点怒气够放招式 2，但不够放消耗 3 点的招式 3。
            PlayerBattleRules player = NewPlayer();
            GainRage(player, 2);

            SkillCastResult result = player.TryCast(PlayerSkill.Skill3);

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Reject, Is.EqualTo(SkillCastReject.NotEnoughRage));
            Assert.That(player.Rage, Is.EqualTo(2));
            Assert.That(player.ExtraDamageCharges, Is.EqualTo(0), "被拒不该挂上增益");
        }

        [Test]
        public void Skill3_WithThreeRage_CostsThreeAndGrantsThreeExtraDamageAttacks()
        {
            PlayerBattleRules player = NewPlayer();
            GainRage(player, 3);

            SkillCastResult result = player.TryCast(PlayerSkill.Skill3);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.RageBefore, Is.EqualTo(3));
            Assert.That(result.RageAfter, Is.EqualTo(0));
            Assert.That(result.ExtraDamageChargesAfter, Is.EqualTo(3));
            Assert.That(player.ExtraDamageCharges, Is.EqualTo(3));
        }

        [Test]
        public void Skill3_ExtraDamage_AppliesToTheNextThreeAttacksThenStops()
        {
            PlayerBattleRules player = NewPlayer();
            GainRage(player, 3);
            player.TryCast(PlayerSkill.Skill3);
            player.BeginTurn();

            int[] damages = new int[4];
            bool[] applied = new bool[4];
            for (int i = 0; i < 4; i++)
            {
                SkillCastResult result = player.TryCast(PlayerSkill.Skill1);
                damages[i] = result.Damage;
                applied[i] = result.ExtraDamageApplied;
                player.BeginTurn();
            }

            Assert.That(damages[0], Is.EqualTo(Settings.Skill1Damage + Settings.Skill3ExtraDamage));
            Assert.That(damages[1], Is.EqualTo(Settings.Skill1Damage + Settings.Skill3ExtraDamage));
            Assert.That(damages[2], Is.EqualTo(Settings.Skill1Damage + Settings.Skill3ExtraDamage));
            // 负对照：第 4 次攻击不再附带额外伤害。
            Assert.That(damages[3], Is.EqualTo(Settings.Skill1Damage));
            Assert.That(applied[3], Is.False);
            Assert.That(player.ExtraDamageCharges, Is.EqualTo(0));
        }

        [Test]
        public void Skill3_TwiceWithStacking_AddsUpTheCharges()
        {
            var settings = new PlayerSkillSettings(6, 0, 1, 1, 1, 2, 50, 0, 3, 3, 1, 3, true);
            var player = new PlayerBattleRules(settings, new PlayerBattleSnapshot(10, 10));
            GainRage(player, 6);

            SkillCastResult first = player.TryCast(PlayerSkill.Skill3);
            player.BeginTurn();
            SkillCastResult second = player.TryCast(PlayerSkill.Skill3);

            Assert.That(first.ExtraDamageChargesAfter, Is.EqualTo(3));
            // 第二次施放自己吃到一次额外伤害（3 + 1），再补上 3 次 → 5 次。
            Assert.That(second.Damage, Is.EqualTo(settings.Skill3Damage + settings.Skill3ExtraDamage));
            Assert.That(second.ExtraDamageChargesAfter, Is.EqualTo(5));
        }

        [Test]
        public void Skill3_TwiceWithoutStacking_ResetsToThreeCharges()
        {
            // 负对照：把「累加 / 重置」拨到重置一侧，第二次施放后应只剩 3 次。
            var settings = new PlayerSkillSettings(6, 0, 1, 1, 1, 2, 50, 0, 3, 3, 1, 3, false);
            var player = new PlayerBattleRules(settings, new PlayerBattleSnapshot(10, 10));
            GainRage(player, 6);

            player.TryCast(PlayerSkill.Skill3);
            player.BeginTurn();
            SkillCastResult second = player.TryCast(PlayerSkill.Skill3);

            Assert.That(second.ExtraDamageChargesAfter, Is.EqualTo(3));
        }

        [Test]
        public void Cast_TwiceInTheSameTurn_IsRefused()
        {
            // 负对照：07:56 施放招式后进入敌方回合，所以一个玩家回合只能出一招。
            PlayerBattleRules player = NewPlayer();
            player.TryCast(PlayerSkill.Skill1);

            SkillCastResult second = player.TryCast(PlayerSkill.Skill1);

            Assert.That(second.Accepted, Is.False);
            Assert.That(second.Reject, Is.EqualTo(SkillCastReject.AlreadyCastThisTurn));

            // 推进到下一个玩家回合后可以再出招。
            player.BeginTurn();
            Assert.That(player.TryCast(PlayerSkill.Skill1).Accepted, Is.True);
        }

        [Test]
        public void Stun_SkipsTheTurnAndBlocksTheCast()
        {
            // 07:82 薄醉态的 BOSS 招式 3 晕眩玩家 1 回合。
            PlayerBattleRules player = NewPlayer();
            player.ApplyStun(1);

            Assert.That(player.BeginTurn(), Is.False);
            Assert.That(player.CannotActThisTurn, Is.True);

            SkillCastResult refused = player.TryCast(PlayerSkill.Skill1);
            Assert.That(refused.Accepted, Is.False);
            Assert.That(refused.Reject, Is.EqualTo(SkillCastReject.Stunned));

            // 晕眩消耗掉之后恢复正常。
            Assert.That(player.BeginTurn(), Is.True);
            Assert.That(player.CannotActThisTurn, Is.False);
            Assert.That(player.TryCast(PlayerSkill.Skill1).Accepted, Is.True);
        }

        [Test]
        public void Stun_BeforeTheTurnStarts_AlsoBlocksTheCast()
        {
            // 负对照：即使调用方忘了走 BeginTurn，挂着的晕眩也必须拦住出招。
            PlayerBattleRules player = NewPlayer();
            player.ApplyStun(1);

            Assert.That(player.TryCast(PlayerSkill.Skill1).Reject, Is.EqualTo(SkillCastReject.Stunned));
            Assert.That(player.TryCast(PlayerSkill.Skill3).Reject, Is.EqualTo(SkillCastReject.Stunned));
        }

        [Test]
        public void Stun_TwoRounds_ConsumesOneRoundPerTurn()
        {
            PlayerBattleRules player = NewPlayer();
            player.ApplyStun(2);

            Assert.That(player.BeginTurn(), Is.False);
            Assert.That(player.StunRoundsRemaining, Is.EqualTo(1));
            Assert.That(player.BeginTurn(), Is.False);
            Assert.That(player.StunRoundsRemaining, Is.EqualTo(0));
            Assert.That(player.BeginTurn(), Is.True);
        }

        [Test]
        public void Damage_ReducesHealthAndNeverGoesBelowZero()
        {
            PlayerBattleRules player = NewPlayer(10, 10);

            player.ApplyDamage(4);
            Assert.That(player.Health, Is.EqualTo(6));
            Assert.That(player.IsDefeated, Is.False);
            Assert.That(player.TryCast(PlayerSkill.Skill1).Accepted, Is.True);

            player.ApplyDamage(999);
            Assert.That(player.Health, Is.EqualTo(0));
            Assert.That(player.IsDefeated, Is.True);
        }

        [Test]
        public void Cast_WhenDefeated_IsRefused()
        {
            // 负对照：生命归零后不能再出招。
            PlayerBattleRules player = NewPlayer(10, 1);
            player.ApplyDamage(1);

            SkillCastResult result = player.TryCast(PlayerSkill.Skill1);

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Reject, Is.EqualTo(SkillCastReject.Defeated));
        }

        [Test]
        public void Cast_UnknownSkill_IsRefused()
        {
            // 负对照：没给招式（None）不能被当成招式 1 静默处理。
            PlayerBattleRules player = NewPlayer();

            SkillCastResult result = player.TryCast(PlayerSkill.None);

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Reject, Is.EqualTo(SkillCastReject.UnknownSkill));
        }

        [Test]
        public void ApplyDamage_NonPositiveAmount_IsIgnored()
        {
            // 负对照：伤害 0 / 负数不该改血量，也不该被当成治疗。
            PlayerBattleRules player = NewPlayer(10, 10);

            player.ApplyDamage(0);
            player.ApplyDamage(-5);

            Assert.That(player.Health, Is.EqualTo(10));
        }

        [Test]
        public void Snapshot_InvalidValues_Throw()
        {
            // 负对照：脏快照在构造时就该炸，而不是带着非法血量跑到结算里。
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new PlayerBattleSnapshot(0, 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new PlayerBattleSnapshot(10, 11));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new PlayerBattleSnapshot(10, -1));
        }

        [Test]
        public void PlayerSkillRules_TableMatchesTheDocument()
        {
            Assert.That(PlayerSkillRules.IsKnown(PlayerSkill.None), Is.False);
            Assert.That(PlayerSkillRules.IsKnown(PlayerSkill.Skill3), Is.True);

            Assert.That(PlayerSkillRules.RageCost(PlayerSkill.Skill1, Settings), Is.EqualTo(0));
            Assert.That(PlayerSkillRules.RageCost(PlayerSkill.Skill2, Settings), Is.EqualTo(1));
            Assert.That(PlayerSkillRules.RageCost(PlayerSkill.Skill3, Settings), Is.EqualTo(3));

            Assert.That(PlayerSkillRules.RageGain(PlayerSkill.Skill1, Settings), Is.EqualTo(1));
            Assert.That(PlayerSkillRules.RageGain(PlayerSkill.Skill2, Settings), Is.EqualTo(0));

            Assert.That(PlayerSkillRules.HealReductionPercent(PlayerSkill.Skill2, Settings), Is.EqualTo(50));
            Assert.That(PlayerSkillRules.HealReductionPercent(PlayerSkill.Skill1, Settings), Is.EqualTo(0));
            Assert.That(PlayerSkillRules.HealReductionPercent(PlayerSkill.Skill3, Settings), Is.EqualTo(0));

            Assert.That(PlayerSkillRules.GrantedExtraDamageAttacks(PlayerSkill.Skill3, Settings), Is.EqualTo(3));
            Assert.That(PlayerSkillRules.GrantedExtraDamageAttacks(PlayerSkill.Skill1, Settings), Is.EqualTo(0));
        }
    }
}
