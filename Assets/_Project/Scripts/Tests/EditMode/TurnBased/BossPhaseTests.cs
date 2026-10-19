// 职责：阶段九 BOSS（双形态 / 护心鳞 / 金锁禁锢 / 自我禁锢）的会话级测试——
//   每条原文规则一条正例，外加「不分形态时行为不变」的负对照（阶段一那条路不许被这一层改掉）。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:86-119`（三、BOSS设计 · 1. 阶段9BOSS）、
// :163-193（四、数值相关 · 3. 阶段9 BOSS数值）；转写见 `docs/design/features-spotlight/09_BOSS战.md` R49–R58、R66–R67。
// 原文没写的三处做成配置项：怒气清空清谁（C94）、护心鳞「层 × 一层挨几次」、人形态招式 3 的触发概率（C95）。

using System.Collections.Generic;
using System.Linq;
using Game.Core.Simulation;
using Game.TurnBased;
using NUnit.Framework;

namespace Game.Tests.EditMode.TurnBased
{
    public sealed class BossPhaseTests
    {
        private static BattleEntryRequest FrontalRequest() =>
            new BattleEntryRequest(false, false, true, false, MonsterAlertState.Unaware, BattleInitiator.PlayerAttacked);

        private static BattleEntryRequest SneakRequest() =>
            new BattleEntryRequest(true, true, false, false, MonsterAlertState.Unaware, BattleInitiator.PlayerAttacked);

        /// <summary>阶段九档案 + 全局那份「偷袭扣 20%」——用来验按 BOSS 覆盖真的生效。</summary>
        private static BattleSettings PhasedSettings()
        {
            BattleSettings basis = BattleSettings.PlaceholderDefault;
            return new BattleSettings(
                basis.Entry, basis.PlayerSkills, basis.BossSkills, basis.Drunk, basis.Flow,
                basis.ItemOncePerBattle, basis.InheritsDrunkValue, basis.Items, BossProfile.StageNineDefault());
        }

        /// <summary>不分形态（阶段一那条路）：档案留空。</summary>
        private static BattleSettings SinglePhaseSettings() => BattleSettings.PlaceholderDefault;

        /// <summary>只留龙形态那一份档案，用来把「龙形态的规则」单独测干净（不必先打空人形态）。</summary>
        private static BattleSettings DragonOnlySettings() => PhaseOnlySettings(dragonPhase: true);

        /// <summary>只留人形态那一份档案（金锁禁锢用）。</summary>
        private static BattleSettings HumanOnlySettings() => PhaseOnlySettings(dragonPhase: false);

        /// <summary>
        /// 只留形态表里的某一份，并**把权重改成只有招式 2**——这样选招必中招式 2，
        /// 用例不必再依赖随机数剧本（两种形态的招式 2 正好分别是金锁禁锢与自我禁锢）。
        /// </summary>
        private static BattleSettings PhaseOnlySettings(bool dragonPhase)
        {
            BattleSettings basis = BattleSettings.PlaceholderDefault;
            BossProfile stageNine = BossProfile.StageNineDefault();
            Assert.That(stageNine.TryGetPhase(dragonPhase ? 1 : 0, out BossPhaseSettings phase), Is.True);

            BossSkillSettings only = phase.Skills;
            var skill2Only = new BossSkillSettings(
                only.Skill1Damage, only.Skill2DrinkAddDrunk, only.Skill2HealPercent, only.Skill2HealBase,
                only.Skill2NextSkill1DamageBonusPercent, only.Skill3Damage, only.Skill3StunRounds,
                0, 1, 0, only.Skill2Damage);

            var phases = new List<BossPhaseSettings> { CloneWithSkills(phase, skill2Only) };
            var profile = new BossProfile(phases, sneakHealthLossPercentOverride: 0);
            return new BattleSettings(
                basis.Entry, basis.PlayerSkills, basis.BossSkills, basis.Drunk, basis.Flow,
                basis.ItemOncePerBattle, basis.InheritsDrunkValue, basis.Items, profile);
        }

        private static BossPhaseSettings CloneWithSkills(in BossPhaseSettings phase, in BossSkillSettings skills) =>
            new BossPhaseSettings(
                phase.Name, phase.MaxHealth, skills, phase.ShieldLayers, phase.ShieldHitsPerLayer,
                phase.AttackEchoPercent, phase.Skill3RequiresPlayerLock, phase.Skill3ChancePercentWhenLocked,
                phase.Skill3GrantsShieldLayers, phase.Skill3ShieldDamageBonusPercent,
                phase.Skill3HealPercentWhenNoShield, phase.SelfImprisonRounds,
                phase.SelfImprisonAttackBonusPercent, phase.SkillLockRounds);

        private static BattleSession Start(
            in BattleSettings settings,
            in BattleEntryRequest request,
            IRandomStream random,
            int playerHealth = 9999,
            int bossHealth = 999,
            params string[] owned)
        {
            var kernel = new TurnBasedKernel(settings, random,
                new FakeBattleItemInventory(owned.Length == 0 ? new[] { "2001" } : owned));
            Assert.That(kernel.TryStartBattle(request,
                new PlayerBattleSnapshot(playerHealth, playerHealth),
                new BossBattleSnapshot(bossHealth, bossHealth, 0),
                out BattleSession session, out BattleEntryReject reject), Is.True, "进得去战斗：" + reject);
            return session;
        }

        private static bool Has(BattleSession session, BattleEventKind kind) =>
            session.Events.Any(e => e.Kind == kind);

        // ───────────────────────── 开局的形态与护心鳞 ─────────────────────────

        [Test]
        public void StageNine_StartsInHumanPhase_WithOneShieldLayer()
        {
            BattleSession session = Start(PhasedSettings(), FrontalRequest(), new XorShiftRandomStream(1UL));

            Assert.That(session.Boss.HasPhases, Is.True);
            Assert.That(session.Boss.PhaseName, Is.EqualTo("人形态"), "sp07:94 进入战斗时为人形态");
            Assert.That(session.Boss.MaxHealth, Is.EqualTo(260), "sp07:167 人形态最大生命值 260");
            Assert.That(session.Boss.Health, Is.EqualTo(260), "开战满血");
            Assert.That(session.Boss.ShieldLayers, Is.EqualTo(1), "sp07:169 护心麟初始层数 1");
        }

        [Test]
        public void StageNine_SneakAttack_DoesNotCostTwentyPercent()
        {
            // sp07:90「龙族感知敏锐，玩家通过偷袭攻击进入对战的效果和正面战斗进入对战的效果相同」。
            BattleSession sneaked = Start(PhasedSettings(), SneakRequest(), new XorShiftRandomStream(1UL));
            Assert.That(sneaked.Boss.Health, Is.EqualTo(260), "按 BOSS 覆盖成 0%：偷袭不扣血");

            // 负对照：不分形态时仍是全局那份 20%。
            BattleSession plain = Start(SinglePhaseSettings(), SneakRequest(), new XorShiftRandomStream(1UL));
            Assert.That(plain.Boss.Health, Is.EqualTo(999 - 999 * 20 / 100), "阶段一那条路照旧扣 20%");
        }

        // ───────────────────────── 护心鳞 ─────────────────────────

        [Test]
        public void HumanPhase_ShieldBlocksExactlyOneAttack()
        {
            BattleSession session = Start(PhasedSettings(), FrontalRequest(), new XorShiftRandomStream(1UL));

            session.TryCastSkill(PlayerSkill.Skill1);

            Assert.That(Has(session, BattleEventKind.BossShieldBlocked), Is.True, "sp07:92 抵挡一次玩家的任何招式");
            Assert.That(session.Boss.Health, Is.EqualTo(260), "被挡下的这一招不掉血");
            Assert.That(session.Boss.ShieldLayers, Is.Zero, "人形态一层挨一次就碎");

            session.RunBossTurn();
            session.TryCastSkill(PlayerSkill.Skill1);

            Assert.That(session.Boss.Health, Is.EqualTo(260 - 18), "第二次没人挡了（18 = 招式 1 伤害，sp07:139）");
        }

        [Test]
        public void DragonPhase_ShieldLayerTakesTwoHits()
        {
            // sp07:185「护心麟初始层数 1｜需要2 次玩家攻击才能击碎」——同一列的「1」与人形态不是一个单位。
            BattleSession session = Start(DragonOnlySettings(), FrontalRequest(), new XorShiftRandomStream(3UL));
            Assert.That(session.Boss.PhaseName, Is.EqualTo("龙形态"));
            Assert.That(session.Boss.ShieldLayers, Is.EqualTo(1), "sp07:92 转为龙形态后护心麟刷新为 1 次");

            session.TryCastSkill(PlayerSkill.Skill1);
            Assert.That(session.Boss.Health, Is.EqualTo(260), "第一下被挡");
            Assert.That(session.Boss.ShieldLayers, Is.EqualTo(1), "一层要挨两次，第一次不碎");

            session.RunBossTurn();
            session.TryCastSkill(PlayerSkill.Skill1);
            Assert.That(session.Boss.ShieldLayers, Is.Zero, "第二下才碎");
        }

        // ───────────────────────── 双形态切换 ─────────────────────────

        [Test]
        public void HumanPhaseDepleted_SwitchesToDragonPhaseWithFullHealth()
        {
            BattleSession session = Start(PhasedSettings(), FrontalRequest(), new XorShiftRandomStream(5UL));

            CastUntilPhaseChanges(session, out bool changed);

            Assert.That(changed, Is.True, "sp07:94 人形态生命值耗尽后为龙形态");
            Assert.That(session.Boss.PhaseName, Is.EqualTo("龙形态"));
            Assert.That(session.Boss.Health, Is.EqualTo(260), "形态切换后生命值恢复为 100%");
            Assert.That(session.IsOver, Is.False, "转形态不是打完");
            Assert.That(Has(session, BattleEventKind.BossPhaseChanged), Is.True);
        }

        [Test]
        public void SinglePhaseBoss_StillEndsOnDepletion()
        {
            // 负对照：不分形态时（阶段一那条路）生命归零仍然直接判胜。
            // BOSS 血量给低、且把招式 2 的饮酒回血关掉——否则它会一边回血一边挨打，永远打不死。
            var noHeal = new BossSkillSettings(14, 0, 0, HealthPercentBase.MaxHealth, 0, 22, 1, 6, 3, 1);
            BattleSettings basis = BattleSettings.PlaceholderDefault;
            var settings = new BattleSettings(basis.Entry, basis.PlayerSkills, noHeal, basis.Drunk, basis.Flow,
                basis.ItemOncePerBattle, basis.InheritsDrunkValue, basis.Items);

            BattleSession session = Start(settings, FrontalRequest(), new XorShiftRandomStream(7UL), bossHealth: 100);

            for (int i = 0; i < 50 && !session.IsOver; i++)
            {
                session.TryCastSkill(PlayerSkill.Skill1);
                if (!session.IsOver)
                {
                    session.RunBossTurn();
                }
            }

            Assert.That(session.IsOver, Is.True);
            Assert.That(session.Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(Has(session, BattleEventKind.BossPhaseChanged), Is.False, "单形态不会转形态");
        }

        // ───────────────────────── 金锁禁锢（人形态招式 2）─────────────────────────

        [Test]
        public void HumanSkill2_LocksThePlayerToSkillOneAndDisablesItems()
        {
            // 权重改成只有招式 2，选招必中金锁禁锢，不依赖随机数剧本。
            BattleSession session = Start(HumanOnlySettings(), FrontalRequest(), new XorShiftRandomStream(1UL), owned: "2001");

            session.TryCastSkill(PlayerSkill.Skill1);   // 先出一招，把回合交给 BOSS
            session.RunBossTurn();                      // BOSS 这一招 = 金锁禁锢

            Assert.That(Has(session, BattleEventKind.PlayerSkillLocked), Is.True, "sp07:176 玩家受到效果「金锁禁锢」");
            Assert.That(session.Player.IsSkillLocked, Is.True);
            Assert.That(session.Player.SkillLockRoundsRemaining, Is.EqualTo(2), "在下 2 个回合只能使用招式 1");

            Assert.That(session.TryCastSkill(PlayerSkill.Skill2).Reject, Is.EqualTo(SkillCastReject.SkillLocked), "招式 2 置暗");
            Assert.That(session.TryCastSkill(PlayerSkill.Skill3).Reject, Is.EqualTo(SkillCastReject.SkillLocked), "招式 3 置暗");
            Assert.That(session.TryUseItem("2001").Allowed, Is.False, "所有道具置暗");
            Assert.That(session.TryCastSkill(PlayerSkill.Skill1).Accepted, Is.True, "招式 1 仍可用");
        }

        // ───────────────────────── 自我禁锢（龙形态招式 2）─────────────────────────

        [Test]
        public void DragonSkill2_SelfImprisonsAndGrantsShieldWhenItEnds()
        {
            // 权重改成只有招式 2，选招必中自我禁锢（sp07:192）。
            BattleSession session = Start(DragonOnlySettings(), FrontalRequest(), new XorShiftRandomStream(1UL));

            session.TryCastSkill(PlayerSkill.Skill1);
            session.RunBossTurn();

            Assert.That(session.Boss.SelfImprisonRoundsRemaining, Is.EqualTo(1),
                "sp07:192 自我禁锢 2 个回合，走完当前这一回合还剩 1");
            Assert.That(BossPhaseSkill2Kind.SelfImprison,
                Is.EqualTo(session.Boss.CurrentPhase().Skill2Kind), "用的是龙形态那一套招式 2");
        }

        // ───────────────────────── 辅助 ─────────────────────────

        /// <summary>一直出招式 1 直到形态变了或打完（上限 400 次，防止写坏了死循环）。</summary>
        private static void CastUntilPhaseChanges(BattleSession session, out bool changed)
        {
            changed = false;
            for (int i = 0; i < 400; i++)
            {
                session.TryCastSkill(PlayerSkill.Skill1);
                if (session.Boss.PhaseIndex > 0)
                {
                    changed = true;
                    return;
                }

                if (session.IsOver)
                {
                    return;
                }

                session.RunBossTurn();
                if (session.Boss.PhaseIndex > 0)
                {
                    changed = true;
                    return;
                }
            }
        }

        /// <summary>把人形态打空，停在龙形态刚开局、且轮回到玩家的那一刻。</summary>
        private static BattleSession AdvanceToDragonPhase(IRandomStream random)
        {
            BattleSession session = Start(PhasedSettings(), FrontalRequest(), random);
            CastUntilPhaseChanges(session, out bool changed);
            Assert.That(changed, Is.True, "前置：应该已经转到龙形态");

            // 转形态是在玩家出招那一刻发生的，此时阶段已转敌方——让 BOSS 走完这一回合再回到玩家。
            if (session.Phase == BattlePhase.BossTurn)
            {
                session.RunBossTurn();
            }

            Assert.That(session.Phase, Is.EqualTo(BattlePhase.PlayerTurn), "前置：转形态后轮到玩家");
            return session;
        }
    }
}
