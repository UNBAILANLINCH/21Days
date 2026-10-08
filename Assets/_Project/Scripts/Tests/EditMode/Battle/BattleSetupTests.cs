// 职责：钉住开仗装配——玩家生命取 BOSS 定义的「本场玩家生命」（满血，与探索血量无关，W2a 主窗口定），BOSS 满血 + 继承战斗外醉酒值，
//   正面攻击玩家先手；本场玩家生命非正时拒绝开仗（负对照）；回放测试口 OverrideSettings 撤销后下一场用回原值。
// 为什么新建：BattleSetup 在 W2a 改了玩家血量来源；一个被测类一个测试类（W1 只经 BattleFlowTests 间接覆盖）。
using System;
using Game.Battle;
using Game.TurnBased;
using NUnit.Framework;

namespace Game.Tests.EditMode.Battle
{
    public sealed class BattleSetupTests
    {
        [Test]
        public void TryCreate_PlayerHealthComesFromBossDefinition_FullHealth()
        {
            var setup = NewSetup();

            Assert.That(setup.TryCreate(new BossDefinition("b", "B", 12, 50), out BattleSession session, out string error), Is.True, error);

            Assert.That(session.Player.MaxHealth, Is.EqualTo(BossDefinition.PlaceholderPlayerHealth), "默认占位 10（等 C91）");
            Assert.That(session.Player.Health, Is.EqualTo(session.Player.MaxHealth), "开战满血");
            Assert.That(session.Boss.MaxHealth, Is.EqualTo(12));
            Assert.That(session.Boss.Health, Is.EqualTo(12));
            Assert.That(session.Boss.DrunkValue, Is.EqualTo(50), "继承战斗外醉酒值（07:66）");
            Assert.That(session.Phase, Is.EqualTo(BattlePhase.PlayerTurn), "正面攻击玩家先手");
        }

        [Test]
        public void TryCreate_CustomPlayerHealth_IsUsed()
        {
            var setup = NewSetup();

            setup.TryCreate(new BossDefinition("b", "B", 12, 0, playerHealth: 7), out BattleSession session, out _);

            Assert.That(session.Player.MaxHealth, Is.EqualTo(7));
            Assert.That(session.Player.Health, Is.EqualTo(7));
        }

        [Test]
        public void TryCreate_NonPositivePlayerHealth_Refuses()
        {
            var setup = NewSetup();

            bool ok = setup.TryCreate(new BossDefinition("b", "B", 12, 0, playerHealth: 0), out BattleSession session, out string error);

            Assert.That(ok, Is.False);
            Assert.That(session, Is.Null);
            Assert.That(error, Does.Contain("本场玩家生命"));
        }

        [Test]
        public void OverrideSettings_ThenDispose_NextBattleUsesOriginalSettings()
        {
            // 回放测试口：换上的数值只管换着的那段时间，撤销后下一场用回装配时的数值。
            // 观测点选「是否继承战斗外醉酒值」：开仗当场就体现在 BOSS 醉酒值上，不用推回合。
            var setup = NewSetup(); // 占位默认：继承醉酒值
            var boss = new BossDefinition("b", "B", 12, 50);
            BattleSettings basis = setup.Settings;
            IDisposable handle = setup.OverrideSettings(new BattleSettings(basis.Entry, basis.PlayerSkills, basis.BossSkills,
                basis.Drunk, basis.Flow, basis.ItemOncePerBattle, false, basis.Items));

            Assert.That(setup.IsOverridden, Is.True);
            Assert.That(setup.TryCreate(boss, out BattleSession during, out string error), Is.True, error);
            Assert.That(during.Boss.DrunkValue, Is.EqualTo(0), "前置（负对照）：换着的时候用的是换上的数值（不继承醉酒值）");

            handle.Dispose();
            handle.Dispose(); // 幂等

            Assert.That(setup.IsOverridden, Is.False);
            Assert.That(setup.TryCreate(boss, out BattleSession after, out error), Is.True, error);
            Assert.That(after.Boss.DrunkValue, Is.EqualTo(50), "撤销后下一场用回原值（继承醉酒值 50）");
        }

        private static BattleSetup NewSetup() =>
            new BattleSetup(BattleSettings.PlaceholderDefault, () => 3UL, new BattleItemInventory(new BattleFakes.Backpack()));
    }
}
