// 职责：战斗内道具效果（sp07:199-211 的 10 件，2026-10-16 落地）的会话级测试——
//   八种机制各一条正例 + 配套负对照（没配效果的道具不生效、次数/回合用尽后失效、配置写坏被校验拒）。
// 为什么改（不是新建）：原版测的是「一件治疗道具回百分比」的占位（PRP/turnbased-battle D10，等 C91）；
//   2026-10-16 策划把 10 件道具的效果写全，占位换成效果表，本类随之一并重写。

using System.Linq;
using Game.Core.Simulation;
using Game.TurnBased;
using NUnit.Framework;

namespace Game.Tests.EditMode.TurnBased
{
    public sealed class BattleItemEffectTests
    {
        private const string Vitality = "1801";   // 活力药剂 恢复 50 点
        private const string RageCrystal = "1802"; // 狂怒结晶 怒气补满
        private const string IronTalisman = "1803"; // 铁壁护符 2 回合受伤 -40%
        private const string SharpOintment = "1804"; // 锐锋油膏 3 次攻击 +7
        private const string BloodPotion = "1805";  // 浴血药剂 3 回合命中回 12
        private const string BigPotion = "1806";    // 大瓶生命药剂 恢复 70 点
        private const string RageShard = "1807";    // 瞬怒晶石 +2 怒气
        private const string Cloak = "1808";        // 坚韧斗篷 1 层护盾
        private const string QuickPowder = "1809";  // 迅击粉末 本回合多出一招
        private const string RootDew = "1810";      // 固本甘露 3 回合每回合末回 25
        private const string Unknown = "1005";      // 破旧信笺：没配效果

        private static BattleEntryRequest FrontalRequest() =>
            new BattleEntryRequest(false, false, true, false, MonsterAlertState.Unaware, BattleInitiator.PlayerAttacked);

        /// <summary>BOSS 只放招式 1、伤害 5：玩家先挨一下再测道具。</summary>
        private static BattleSettings Settings(in BattleItemSettings items)
        {
            BattleSettings basis = BattleSettings.PlaceholderDefault;
            return new BattleSettings(
                basis.Entry,
                basis.PlayerSkills,
                new BossSkillSettings(5, 30, 10, HealthPercentBase.MaxHealth, 30, 3, 1, 1, 0, 0),
                basis.Drunk,
                basis.Flow,
                basis.ItemOncePerBattle,
                basis.InheritsDrunkValue,
                items);
        }

        private static BattleSettings DefaultSettings() => Settings(BattleItemSettings.PlaceholderDefault);

        /// <summary>开一场仗（玩家 20/20，招式 1 = 18 点）。</summary>
        private static BattleSession Start(in BattleSettings settings, params string[] owned)
        {
            var kernel = new TurnBasedKernel(settings, new XorShiftRandomStream(7UL),
                new FakeBattleItemInventory(owned.Length == 0 ? new[] { Vitality, Unknown } : owned));
            Assert.That(kernel.TryStartBattle(FrontalRequest(), new PlayerBattleSnapshot(20, 20),
                new BossBattleSnapshot(200, 200, 0), out BattleSession session, out _), Is.True);
            return session;
        }

        /// <summary>出一招 → 走一个怪物回合（BOSS 招式 1 打 5）→ 回到玩家回合。返回挨完后的生命。</summary>
        private static int CastAndEatOneHit(BattleSession session)
        {
            Assert.That(session.TryCastSkill(PlayerSkill.Skill1).Accepted, Is.True);
            session.RunBossTurn();
            Assert.That(session.Phase, Is.EqualTo(BattlePhase.PlayerTurn), "应该回到玩家回合");
            return session.Player.Health;
        }

        // ───────────────────────── 即时类：回血 / 怒气 ─────────────────────────

        [Test]
        public void VitalityPotion_HealsFifty_CappedAtMax()
        {
            BattleSession session = Start(DefaultSettings(), Vitality);
            Assert.That(CastAndEatOneHit(session), Is.EqualTo(15), "前置：先挨 5 点");

            Assert.That(session.TryUseItem(Vitality).Allowed, Is.True);

            Assert.That(session.Player.Health, Is.EqualTo(20), "15 + 50 封顶到 20");
            Assert.That(session.Events.Single(e => e.Kind == BattleEventKind.ItemUsed).Amount, Is.EqualTo(5), "事件带实际回复量");
        }

        [Test]
        public void BigPotion_HealsSeventy()
        {
            BattleSession session = Start(DefaultSettings(), BigPotion);
            session.TryCastSkill(PlayerSkill.Skill1);
            session.RunBossTurn();

            session.TryUseItem(BigPotion);

            Assert.That(session.Player.Health, Is.EqualTo(20), "15 + 70 封顶到 20");
            Assert.That(session.Events.Single(e => e.Kind == BattleEventKind.ItemUsed).Amount, Is.EqualTo(5));
        }

        [Test]
        public void RageShard_GainsTwoRage()
        {
            BattleSession session = Start(DefaultSettings(), RageShard);

            session.TryUseItem(RageShard);

            Assert.That(session.Player.Rage, Is.EqualTo(2), "sp07:208 立刻获得 2 点怒气");
        }

        [Test]
        public void RageCrystal_FillsRageToMax()
        {
            BattleSession session = Start(DefaultSettings(), RageCrystal);

            session.TryUseItem(RageCrystal);

            Assert.That(session.Player.Rage, Is.EqualTo(session.Player.RageMax), "sp07:200 补满至 3 点");
        }

        // ───────────────────────── 防护类：护盾 / 减伤 ─────────────────────────

        [Test]
        public void Shield_BlocksOneHitEntirely()
        {
            BattleSession session = Start(DefaultSettings(), Cloak);

            session.TryUseItem(Cloak);
            Assert.That(session.Player.ShieldCharges, Is.EqualTo(1));

            Assert.That(CastAndEatOneHit(session), Is.EqualTo(20), "护盾把这一次伤害整个挡掉");
            Assert.That(session.Player.ShieldCharges, Is.Zero, "挡一次就用掉一层");

            Assert.That(CastAndEatOneHit(session), Is.EqualTo(15), "再挨一下就没有护盾了（负对照）");
        }

        [Test]
        public void DamageReduction_CutsFortyPercentForTwoTurns()
        {
            BattleSession session = Start(DefaultSettings(), IronTalisman);

            session.TryUseItem(IronTalisman);

            Assert.That(CastAndEatOneHit(session), Is.EqualTo(17), "5 点伤害减 40% 向下取整 = 3");
            Assert.That(CastAndEatOneHit(session), Is.EqualTo(14), "第二回合仍减伤");
            Assert.That(CastAndEatOneHit(session), Is.EqualTo(9), "第三回合起失效（负对照：只持续 2 回合）");
        }

        // ───────────────────────── 攻击类：加攻 / 吸血 ─────────────────────────

        [Test]
        public void AttackBonus_AddsSevenForThreeAttacks()
        {
            BattleSession session = Start(DefaultSettings(), SharpOintment);
            int baseDamage = BattleSettings.PlaceholderDefault.PlayerSkills.Skill1Damage; // 18（sp07:139）

            session.TryUseItem(SharpOintment);

            int before = session.Boss.Health;
            session.TryCastSkill(PlayerSkill.Skill1);
            Assert.That(before - session.Boss.Health, Is.EqualTo(baseDamage + 7), "第一击吃到 +7");
            Assert.That(session.Player.AttackBonusCharges, Is.EqualTo(2));

            session.RunBossTurn();
            before = session.Boss.Health;
            session.TryCastSkill(PlayerSkill.Skill1);
            session.RunBossTurn();
            before = session.Boss.Health;
            session.TryCastSkill(PlayerSkill.Skill1);
            Assert.That(before - session.Boss.Health, Is.EqualTo(baseDamage + 7), "第三击仍是 +7");
            Assert.That(session.Player.AttackBonusCharges, Is.Zero);

            session.RunBossTurn();
            before = session.Boss.Health;
            session.TryCastSkill(PlayerSkill.Skill1);
            Assert.That(before - session.Boss.Health, Is.EqualTo(baseDamage), "第四次起回到基础伤害（负对照）");
        }

        [Test]
        public void Lifesteal_HealsOnEveryHitForThreeTurns()
        {
            BattleSession session = Start(DefaultSettings(), BloodPotion);
            Assert.That(CastAndEatOneHit(session), Is.EqualTo(15), "前置：先掉到 15");

            session.TryUseItem(BloodPotion);
            session.TryCastSkill(PlayerSkill.Skill1);

            Assert.That(session.Player.Health, Is.EqualTo(20), "命中后回 12，封顶到 20（15 + 12 > 20）");
        }

        // ───────────────────────── 节奏类：回春 / 额外一招 ─────────────────────────

        [Test]
        public void Regen_HealsAtTurnStartForThreeTurns()
        {
            BattleSession session = Start(DefaultSettings(), RootDew);

            session.TryUseItem(RootDew);
            Assert.That(session.Player.RegenRounds, Is.EqualTo(3), "3 回合");

            // 出一招 → 挨 5 点（15）→ 进新回合时先结算回春（+25 封顶 20），再扣一格。
            Assert.That(CastAndEatOneHit(session), Is.EqualTo(20), "回春把这一下补回来了");
            Assert.That(session.Player.RegenRounds, Is.EqualTo(2), "用掉一格");
        }

        [Test]
        public void ExtraAction_IsRecorded_ButTheTurnModelMovesOnAfterOneCast()
        {
            // **已知边界（= 待拍板 C100）**：原文写「本回合主角可以额外释放一次招式」（sp07:210），
            // 可 07:56 又写「玩家在施放招式后，进入敌方回合」——两者怎么并存原文没写。
            // 本内核照 07:56 执行（出招即转敌方回合），所以额外那一招**当前无处可放**：
            // 效果记下来（ExtraActionPending = true），但第二次出招被拒的理由是 NotPlayerTurn（阶段已转），
            // 不是 AlreadyCastThisTurn。C100 拍板后改这里，本用例就是那条边界的看门人。
            BattleSession session = Start(DefaultSettings(), QuickPowder);

            session.TryUseItem(QuickPowder);
            Assert.That(session.Player.ExtraActionPending, Is.True, "效果记下了");

            Assert.That(session.TryCastSkill(PlayerSkill.Skill1).Accepted, Is.True, "第一招");
            SkillCastResult second = session.TryCastSkill(PlayerSkill.Skill1);

            Assert.That(second.Accepted, Is.False, "同一玩家回合出不了第二招");
            Assert.That(second.Reject, Is.EqualTo(SkillCastReject.NotPlayerTurn),
                "理由是「不是你的回合」（阶段已转敌方），不是 AlreadyCastThisTurn——见 C100");
        }

        // ───────────────────────── 负对照与配置校验 ─────────────────────────

        [Test]
        public void UnknownItem_HasNoEffect()
        {
            // 负对照：没配效果的道具只记账、只置暗。
            BattleSession session = Start(DefaultSettings(), Unknown);
            Assert.That(CastAndEatOneHit(session), Is.EqualTo(15));

            Assert.That(session.TryUseItem(Unknown).Allowed, Is.True);

            Assert.That(session.Player.Health, Is.EqualTo(15));
            Assert.That(session.Events.Single(e => e.Kind == BattleEventKind.ItemUsed).Amount, Is.Zero);
        }

        [Test]
        public void EmptyTable_EveryItemHasNoEffect()
        {
            // 负对照：效果表为空时（删掉全部配置）所有道具都只记账。
            BattleSession session = Start(Settings(new BattleItemSettings(null)), Vitality);
            Assert.That(CastAndEatOneHit(session), Is.EqualTo(15));

            session.TryUseItem(Vitality);

            Assert.That(session.Player.Health, Is.EqualTo(15));
        }

        [Test]
        public void SecondUseInTheSameBattle_IsRefused()
        {
            // 负对照：一场只能用一次（07:42）。
            BattleSession session = Start(DefaultSettings(), Vitality);
            CastAndEatOneHit(session);
            session.TryUseItem(Vitality);
            session.TryCastSkill(PlayerSkill.Skill1);
            session.RunBossTurn();
            int before = session.Player.Health;

            Assert.That(session.TryUseItem(Vitality).Reject, Is.EqualTo(ItemUseReject.UsedThisBattle));
            Assert.That(session.Player.Health, Is.EqualTo(before));
        }

        [Test]
        public void Validate_MalformedEffect_IsReported()
        {
            // 回血写 0 点：数值必须为正。
            var broken = Settings(new BattleItemSettings(new[] { new BattleItemEffect(Vitality, BattleItemEffectKind.HealFixed, 0) }));

            Assert.That(TurnBasedConfigValidation.Validate(broken), Does.Contain("道具效果写坏了"));
        }

        [Test]
        public void Validate_DuplicateItemId_IsReported()
        {
            var broken = Settings(new BattleItemSettings(new[]
            {
                new BattleItemEffect(Vitality, BattleItemEffectKind.HealFixed, 50),
                new BattleItemEffect(Vitality, BattleItemEffectKind.RageAdd, 1),
            }));

            Assert.That(TurnBasedConfigValidation.Validate(broken), Does.Contain("出现了两次"));
        }

        [Test]
        public void Validate_DefaultTable_IsClean()
        {
            Assert.That(TurnBasedConfigValidation.Validate(BattleSettings.PlaceholderDefault), Is.Null, "对照：默认的 10 件道具自洽");
        }
    }
}
