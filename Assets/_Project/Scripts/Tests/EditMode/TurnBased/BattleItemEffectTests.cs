// 职责：道具效果占位（PRP/turnbased-battle D10：1004 治疗药水回复 30% 生命，等 C91）的会话级测试——
//   用了治疗道具真的回血、封顶到上限，配套负对照：别的道具不回血、占位删掉（id 留空）后不回血、配置百分比越界被校验拒。
// 为什么新建：BattleSessionTests 管回合流程，道具效果是本波新加的一条占位，单独一个类便于随 C91 整体替换或删除。

using System.Linq;
using Game.Core.Simulation;
using Game.TurnBased;
using NUnit.Framework;

namespace Game.Tests.EditMode.TurnBased
{
    public sealed class BattleItemEffectTests
    {
        private const string Potion = "1004";
        private const string Letter = "1005";

        private static BattleEntryRequest FrontalRequest() =>
            new BattleEntryRequest(false, false, true, false, MonsterAlertState.Unaware, BattleInitiator.PlayerAttacked);

        /// <summary>BOSS 只放招式 1、伤害 5：玩家先挨一下再测回血。</summary>
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

        /// <summary>开战（玩家 20/20）→ 出一招 → BOSS 回合挨 5 下 → 回到玩家回合（15/20）。</summary>
        private static BattleSession StartHurt(in BattleSettings settings)
        {
            var kernel = new TurnBasedKernel(settings, new XorShiftRandomStream(7UL), new FakeBattleItemInventory(Potion, Letter));
            Assert.That(kernel.TryStartBattle(FrontalRequest(), new PlayerBattleSnapshot(20, 20),
                new BossBattleSnapshot(100, 100, 0), out BattleSession session, out _), Is.True);
            Assert.That(session.TryCastSkill(PlayerSkill.Skill1).Accepted, Is.True);
            session.RunBossTurn();
            Assert.That(session.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            Assert.That(session.Player.Health, Is.EqualTo(15), "前置：玩家先挨了 5 点");
            return session;
        }

        [Test]
        public void TryUseItem_HealPotion_RestoresThirtyPercentOfMaxHealth()
        {
            BattleSession session = StartHurt(Settings(BattleItemSettings.PlaceholderDefault));

            Assert.That(session.TryUseItem(Potion).Allowed, Is.True);

            Assert.That(session.Player.Health, Is.EqualTo(20), "20 的 30% 是 6，回到 21 后封顶 20");
            BattleEvent used = session.Events.Single(e => e.Kind == BattleEventKind.ItemUsed);
            Assert.That(used.Amount, Is.EqualTo(5), "事件里带实际回复量（封顶后是 5）");
        }

        [Test]
        public void TryUseItem_HealPotionAtLowHealth_RestoresExactPercent()
        {
            BattleSession session = StartHurt(Settings(BattleItemSettings.PlaceholderDefault));
            session.TryCastSkill(PlayerSkill.Skill1);
            session.RunBossTurn();
            Assert.That(session.Player.Health, Is.EqualTo(10), "前置：再挨 5 点");

            session.TryUseItem(Potion);

            Assert.That(session.Player.Health, Is.EqualTo(16), "10 + 20×30% = 16");
        }

        [Test]
        public void TryUseItem_OtherItem_DoesNotHeal()
        {
            // 负对照：不是配置里的治疗道具，只记账、不回血。
            BattleSession session = StartHurt(Settings(BattleItemSettings.PlaceholderDefault));

            Assert.That(session.TryUseItem(Letter).Allowed, Is.True);

            Assert.That(session.Player.Health, Is.EqualTo(15));
            Assert.That(session.Events.Single(e => e.Kind == BattleEventKind.ItemUsed).Amount, Is.EqualTo(0));
        }

        [Test]
        public void TryUseItem_PlaceholderRemoved_DoesNotHeal()
        {
            // 负对照：占位删掉（id 留空）后药水只是一件普通道具——占位要能删（D10）。
            BattleSession session = StartHurt(Settings(new BattleItemSettings(string.Empty, 30, HealthPercentBase.MaxHealth)));

            Assert.That(session.TryUseItem(Potion).Allowed, Is.True);

            Assert.That(session.Player.Health, Is.EqualTo(15));
        }

        [Test]
        public void TryUseItem_HealPotionSecondTime_IsRefusedAndDoesNotHealAgain()
        {
            // 负对照：一场只能用一次（07:42），第二次被拒、不回第二次血。
            BattleSession session = StartHurt(Settings(BattleItemSettings.PlaceholderDefault));
            session.TryUseItem(Potion);
            session.TryCastSkill(PlayerSkill.Skill1);
            session.RunBossTurn();
            int before = session.Player.Health;

            Assert.That(session.TryUseItem(Potion).Reject, Is.EqualTo(ItemUseReject.UsedThisBattle));
            Assert.That(session.Player.Health, Is.EqualTo(before));
        }

        [Test]
        public void Validate_HealPercentOutOfRange_IsReported()
        {
            BattleSettings broken = Settings(new BattleItemSettings(Potion, 130, HealthPercentBase.MaxHealth));

            Assert.That(TurnBasedConfigValidation.Validate(broken), Does.Contain("治疗道具"));
            Assert.That(TurnBasedConfigValidation.Validate(BattleSettings.PlaceholderDefault), Is.Null, "对照：占位默认自洽");
        }
    }
}
