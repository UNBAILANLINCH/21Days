// 职责：验证巡逻、感知优先级、警戒时间、战斗与独立快照恢复；以及按种类数值的接入
//   （种类优先于全局、旧路径不传种类时行为不变）。
using Game.Core.Config;
using Game.Core.Replay;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Mirror;
using Game.Monster;
using Game.Player;
using Game.Tests.EditMode.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;

namespace Game.Tests.EditMode.Monster
{
    public sealed class MonsterRulesTests
    {
        // 与 Tables/Data/monster_species/1002.json 一致（井边妇人：血 2、橙区 4.5、更警觉、更执着）。
        private const int WellWomanKindId = 1002;

        private MonsterConfig config;
        private MonsterKind wellWoman;
        private MonsterKindCatalog catalog;
        private MonsterRules rules;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<MonsterConfig>();
            // 从真实生成的种类表取一条：走「表 → 目录 → 规则」这条真链路，不手搓数值。
            var tables = ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes());
            var stub = new StubConfigService(tables);
            catalog = new MonsterKindCatalog(stub, new YaoCatalog(stub), config.HostileRadius);
            wellWoman = catalog.Get(WellWomanKindId);
            rules = NewRules();
            rules.Reset(new[] { Vector2.zero, Vector2.right * 100f });
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        [Test]
        public void Patrol_PausesAfterSevenToTenSeconds_ForTwoSeconds()
        {
            for (int i = 0; i < 6; i++)
            {
                Step(NoTarget(), 1f);
            }

            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.PatrolWalk));
            for (int i = 0; i < 4 && rules.Model.Mode == MonsterMode.PatrolWalk; i++)
            {
                Step(NoTarget(), 1f);
            }

            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.PatrolPause));
            Vector2 stoppedAt = rules.Model.Position;
            Step(NoTarget(), 1f);
            Assert.That(rules.Model.Position, Is.EqualTo(stoppedAt));
            Step(NoTarget(), 1f);
            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.PatrolWalk));
        }

        [Test]
        public void Perception_PrioritizesRed_AndRespectsDisguiseSneakAndRearProximity()
        {
            Step(Target(new Vector2(4f, 0f), disguised: true), 0f);
            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.PatrolWalk));
            Step(Target(new Vector2(-1f, 0f), sneaking: true), 0f);
            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.PatrolWalk));
            Step(Target(new Vector2(-1f, 0f)), 0f);
            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.Alert));

            rules.Reset(new[] { Vector2.zero, Vector2.right * 100f });
            Step(Target(Vector2.right, sneaking: true, disguised: true), 0f);
            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.Hostile), "红区优先于伪装与潜行");
        }

        [Test]
        public void Alert_FillsInFourSeconds_DecaysInSix_AfterHostileLossInTwo()
        {
            for (int i = 0; i < 4; i++)
            {
                Step(Target(rules.Model.Position + Vector2.right * 4f), 1f);
            }

            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.Hostile));
            Assert.That(rules.Model.Alert, Is.EqualTo(1f));
            Step(NoTarget(), 1f);
            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.Hostile));
            Step(NoTarget(), 1f);
            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.Alert));
            Assert.That(rules.Model.Alert, Is.EqualTo(1f));
            Step(NoTarget(), 3f);
            Assert.That(rules.Model.Alert, Is.EqualTo(0.75f).Within(0.001f));
            Step(NoTarget(), 3f);
            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.PatrolWalk));
        }

        [Test]
        public void Combat_HitsAndDies_ThenCannotAct()
        {
            Assert.That(Step(Target(Vector2.right), 0f), Is.False);
            Assert.That(Step(Target(Vector2.right), 0.5f), Is.True);
            var damage = new DamageIntent(3);
            PlayerSnapshot attacker = Target(Vector2.right);
            rules.ApplyDamage(in damage, in attacker);
            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.Dead));
            Assert.That(rules.Model.Health, Is.Zero);
            Assert.That(Step(attacker, 1f), Is.False);
        }

        [Test]
        public void ReplaySnapshot_RestoresPatrolPointsTimersAndRandomState()
        {
            Step(NoTarget(), 1f);
            var buffer = new StateBuffer();
            rules.Serialize(buffer);

            MonsterRules restored = NewRules();
            buffer.SeekToStart();
            restored.Deserialize(buffer);
            Assert.That(buffer.Remaining, Is.Zero);
            Assert.That(restored.Model.Position, Is.EqualTo(rules.Model.Position));
            Assert.That(restored.Model.NextPauseAfter, Is.EqualTo(rules.Model.NextPauseAfter));

            for (int i = 0; i < 10; i++)
            {
                var intent = new MonsterIntent(NoTarget(), 1f);
                rules.Step(in intent);
                restored.Step(in intent);
            }

            Assert.That(restored.Model.Position, Is.EqualTo(rules.Model.Position));
            Assert.That(restored.Model.Mode, Is.EqualTo(rules.Model.Mode));
            Assert.That(restored.Model.NextPauseAfter, Is.EqualTo(rules.Model.NextPauseAfter));
        }

        // 旧路径回归：不传种类（kind = null，也就是「这份资产没接种类表」）时，生命与感知全部取全局默认值，
        // 行为与拆分前逐位一致。这条是向后兼容的守门用例，改动 Resolve* 系列时它会先亮。
        [Test]
        public void Rules_WithoutKind_UsesGlobalDefaults()
        {
            Assert.That(rules.Kind, Is.Null);
            Assert.That(rules.IsKillable, Is.True, "没接种类表时保持旧行为：可以被打死");
            Assert.That(rules.DropItemIds, Is.Empty);

            rules.Reset(new[] { Vector2.zero, Vector2.right * 100f });
            Assert.That(rules.Model.Health, Is.EqualTo(config.MaxHealth), "生命取全局默认");
            Assert.That(rules.AttackDamage, Is.EqualTo(config.AttackDamage), "伤害取全局默认");

            // 全局橙区半径 6：距离 5 在前方扇区内应进入警戒（旧行为）。
            Step(Target(Vector2.right * 5f), 0f);
            Assert.That(rules.Model.Mode, Is.EqualTo(MonsterMode.Alert));
        }

        // 按种类：种类给的值盖过全局。井边妇人橙区 4.5 < 全局 6，所以距离 5 处不再进警戒。
        [Test]
        public void Rules_WithKind_UsesKindValues_OverGlobal()
        {
            MonsterRules rulesOfKind = NewRules(wellWoman);
            rulesOfKind.Reset(new[] { Vector2.zero, Vector2.right * 100f });

            Assert.That(rulesOfKind.Kind, Is.Not.Null);
            Assert.That(rulesOfKind.Kind.KindId, Is.EqualTo(WellWomanKindId));
            Assert.That(rulesOfKind.Model.Health, Is.EqualTo(2), "生命用种类值（全局是 3）");

            // 距离 5：全局橙区 6 会进警戒，种类橙区 4.5 不会——这一条同时证明种类真的生效。
            var far = new MonsterIntent(Target(Vector2.right * 5f), 0f);
            rulesOfKind.Step(in far);
            Assert.That(rulesOfKind.Model.Mode, Is.EqualTo(MonsterMode.PatrolWalk), "种类橙区 4.5 够不到距离 5");

            // 距离 4：在种类橙区内，仍应进入警戒。
            var near = new MonsterIntent(Target(Vector2.right * 4f), 0f);
            rulesOfKind.Step(in near);
            Assert.That(rulesOfKind.Model.Mode, Is.EqualTo(MonsterMode.Alert));
        }

        // 红区仍用全局敌对半径：改种类不动「红区优先于伪装与潜行」的语义（MonsterConfig.hostileRadius 注释）。
        [Test]
        public void Rules_WithKind_RedZoneStillUsesGlobalHostileRadius()
        {
            MonsterRules rulesOfKind = NewRules(wellWoman);
            rulesOfKind.Reset(new[] { Vector2.zero, Vector2.right * 100f });

            var intent = new MonsterIntent(Target(Vector2.right * 1.5f, sneaking: true, disguised: true), 0f);
            rulesOfKind.Step(in intent);

            Assert.That(rulesOfKind.Model.Mode, Is.EqualTo(MonsterMode.Hostile), "红区（全局 2）先于伪装与潜行");
        }

        // killable = false 的种类不吃常规伤害：06_怪物分层.md:121 R9。
        [Test]
        public void ApplyDamage_WhenKindNotKillable_IsRejected()
        {
            var damage = new DamageIntent(1);
            PlayerSnapshot attacker = Target(Vector2.right);

            Assert.That(rules.ApplyDamage(in damage, in attacker), Is.True, "没接种类表时照旧掉血");

            // 妖物表不可用时 IsKillable 为 false，正好就是「不可常规击杀」那一支（籍中吏「不可击杀」）。
            MonsterRules immortal = NewRules(new MonsterKind(9001, 2, "测试用不可击杀",
                3, 1, 0.8f, 1f, 75f, 6f, 1.5f, 4f, 2f, null));
            immortal.Reset(new[] { Vector2.zero, Vector2.right * 100f });

            Assert.That(immortal.IsKillable, Is.False);
            Assert.That(immortal.ApplyDamage(in damage, in attacker), Is.False);
            Assert.That(immortal.Model.Health, Is.EqualTo(3), "被拒的伤害不掉血");
            Assert.That(immortal.Model.Mode, Is.Not.EqualTo(MonsterMode.Dead));
        }

        // 伤害也按种类走：种类填的值盖过全局（全局 1，种类 3）。
        [Test]
        public void Rules_WithKind_TakesKindAttackDamageOverGlobal()
        {
            MonsterRules heavy = NewRules(new MonsterKind(9002, 2, "测试用重击",
                3, 3, 0.8f, 1f, 75f, 6f, 1.5f, 4f, 2f, null));
            heavy.Reset(new[] { Vector2.zero, Vector2.right * 100f });

            Assert.That(heavy.AttackDamage, Is.EqualTo(3), "种类伤害盖过全局");
            Assert.That(rules.AttackDamage, Is.EqualTo(config.AttackDamage), "没种类时仍取全局");
        }

        // 正式接线那一支：规则只拿到「目录 + 种类 id」，种类在 Reset 时才查。
        // 这条同时是启动顺序的守门用例——如果把查询挪回构造函数（容器构建期，配置表还没加载），
        // kind 会是 null、血量回落到全局的 3，下面第一条断言就会亮。
        [Test]
        public void Rules_WithKindCatalog_ResolvesKindOnReset()
        {
            MonsterRules viaCatalog = new MonsterRules(config, new MonsterModel(),
                new RandomService(123ul), NullTelemetryScope.Instance,
                kind: null, kindCatalog: catalog, kindId: WellWomanKindId);

            Assert.That(viaCatalog.Kind, Is.Null, "构造时不查表");
            viaCatalog.Reset(new[] { Vector2.zero, Vector2.right * 100f });

            Assert.That(viaCatalog.Kind, Is.Not.Null, "Reset 时按种类 id 查到");
            Assert.That(viaCatalog.Kind.KindId, Is.EqualTo(WellWomanKindId));
            Assert.That(viaCatalog.Model.Health, Is.EqualTo(2), "血量用种类值，不是全局的 3");
        }

        // kindId = 0 的语义是「用表里第一条」，也是场景没显式指定种类时的默认。
        [Test]
        public void Rules_WithKindCatalogAndZeroKindId_UsesFirstRow()
        {
            MonsterRules first = new MonsterRules(config, new MonsterModel(),
                new RandomService(123ul), NullTelemetryScope.Instance,
                kind: null, kindCatalog: catalog, kindId: 0);
            first.Reset(new[] { Vector2.zero, Vector2.right * 100f });

            Assert.That(first.Kind, Is.Not.Null);
            Assert.That(first.Kind.KindId, Is.EqualTo(catalog.All[0].KindId));
        }

        // 目录在但表里没有这个种类：兜底成全局默认，且不抛（抛了整场遭遇就进不去）。
        // 兜底会写一条 Error，所以要用 LogAssert 认领它——否则 Unity 测试框架会把这条日志判成用例失败
        // （也正因为它默认会让测试红，这条兜底不会被静默吞掉）。
        [Test]
        public void Rules_WithUnknownKindId_FallsBackToGlobalWithoutThrowing()
        {
            MonsterRules unknown = new MonsterRules(config, new MonsterModel(),
                new RandomService(123ul), NullTelemetryScope.Instance,
                kind: null, kindCatalog: catalog, kindId: 987654);

            LogAssert.Expect(LogType.Error, new Regex("monster_species 表里没有种类 987654"));
            Assert.DoesNotThrow(() => unknown.Reset(new[] { Vector2.zero, Vector2.right * 100f }));
            Assert.That(unknown.Kind, Is.Null);
            Assert.That(unknown.Model.Health, Is.EqualTo(config.MaxHealth), "回落到全局默认");
        }

        private MonsterRules NewRules() => new MonsterRules(config, new MonsterModel(),
            new RandomService(123ul), NullTelemetryScope.Instance);

        private MonsterRules NewRules(MonsterKind kind) => new MonsterRules(config, new MonsterModel(),
            new RandomService(123ul), NullTelemetryScope.Instance, kind);

        /// <summary>只递一份现成的 <c>cfg.Tables</c>。</summary>
        private sealed class StubConfigService : IConfigService
        {
            public StubConfigService(global::cfg.Tables tables) => Tables = tables;
            public global::cfg.Tables Tables { get; }
            public ulong ContentHash => throw new System.NotSupportedException("假配置服务不提供内容指纹");
        }

        private bool Step(PlayerSnapshot target, float deltaTime)
        {
            var intent = new MonsterIntent(target, deltaTime);
            return rules.Step(in intent);
        }

        private static PlayerSnapshot NoTarget() => new PlayerSnapshot(Vector2.right * 1000f,
            Vector2.right, false, false, 3);

        private static PlayerSnapshot Target(Vector2 position, bool sneaking = false, bool disguised = false) =>
            new PlayerSnapshot(position, Vector2.left, sneaking, disguised, 3);

        // 渲染插值：每 tick 推进前记下上一 tick 位置；Reset / 快照恢复 / 存档恢复时对齐，不跨瞬移插值。
        [Test]
        public void Step_RecordsPreviousPosition_AndResetRestoreAlignIt()
        {
            Assert.That(rules.Model.PreviousPosition, Is.EqualTo(rules.Model.Position), "Reset 后对齐");
            Vector2 before = rules.Model.Position;
            Step(NoTarget(), 1f);
            Assert.That(rules.Model.PreviousPosition, Is.EqualTo(before));
            Assert.That(rules.Model.Position, Is.Not.EqualTo(before));

            MonsterSaveData saved = rules.Capture();
            var buffer = new StateBuffer();
            rules.Serialize(buffer);

            MonsterRules restored = NewRules();
            buffer.SeekToStart();
            restored.Deserialize(buffer);
            Assert.That(buffer.Remaining, Is.Zero, "PreviousPosition 不进快照，字节布局不变");
            Assert.That(restored.Model.PreviousPosition, Is.EqualTo(restored.Model.Position));

            MonsterRules loaded = NewRules();
            loaded.Restore(saved);
            Assert.That(loaded.Model.PreviousPosition, Is.EqualTo(loaded.Model.Position));
        }

        // 驯服接管的移动入口同样先记上一 tick 位置。
        [Test]
        public void MoveControlled_RecordsPreviousPositionBeforeMoving()
        {
            Vector2 before = rules.Model.Position;
            rules.MoveControlled(Vector2.up, 1f);
            Assert.That(rules.Model.PreviousPosition, Is.EqualTo(before));
            Assert.That(rules.Model.Position.y, Is.GreaterThan(before.y));
        }
    }
}
