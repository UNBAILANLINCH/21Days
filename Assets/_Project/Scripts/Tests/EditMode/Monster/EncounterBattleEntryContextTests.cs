// 职责：钉住 `EncounterStep` 对 `IBattleEntryContext` 的实现——五个快照量怎么从遭遇现场读出来，
//   以及其中**哪一个是推断**（谁先动手）。
// 为什么新建：`Game.TurnBased.BattleEntryRules` 的三条分支早就有内核测试，但「谁把真实的五个量填出来」
//   在生产侧一条覆盖都没有（`BattleSetup` 此前写死「正面攻击、玩家先手」）。本文件覆盖翻译层，
//   `BattleSetupTests` 覆盖「拿这份快照开出了哪一种战斗」。
//
// ⚠️ **站位是这个夹具最容易写错的地方，先读这段**：怪物开局站在 `patrolPoints[0]`，朝向是
//   `normalize(patrolPoints[1] - patrolPoints[0])`。本文件用 `[(3,0), (100,0)]`，所以怪物在 **(3,0) 朝 +X**，
//   玩家出生点 (0,0) 在它**背后**（基线就是「什么都没察觉」）。因此：
//     要「在视锥里」→ 玩家的 x 必须 **> 3**；要进警戒 → 距离落在 (2, 6]；要进红区 → 距离落在 (0, 2]。
//   站到 x < 3 是背后：既不进视锥，只可能命中贴身察觉（且潜行时连贴身也免）。
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Monster;
using Game.Player;
using Game.TurnBased;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Monster
{
    /// <summary>遭遇现场 → 进战斗快照（`07_回合制作战文档.md:13-28` 的五个量）。</summary>
    public sealed class EncounterBattleEntryContextTests
    {
        /// <summary>怪物开局的巡逻起点与朝向（见文件头）。</summary>
        private static readonly Vector2 MonsterStart = new Vector2(3f, 0f);
        private static readonly Vector2 MonsterFacing = new Vector2(1f, 0f);

        /// <summary>正面视野锥里、**橙区**（红区 2 之外、橙区 6 之内）的一个站位。</summary>
        private static readonly Vector2 AlertSpot = MonsterStart + MonsterFacing * 3f;

        /// <summary>正面视野锥里、**红区**（2 之内）的一个站位。</summary>
        private static readonly Vector2 HostileSpot = MonsterStart + MonsterFacing * 1.5f;

        private PlayerConfig playerConfig;
        private MonsterConfig monsterConfig;
        private PlayerModel playerModel;
        private MonsterModel monsterModel;
        private PlayerRules playerRules;
        private MonsterRules monsterRules;
        private EncounterStep step;

        [SetUp]
        public void SetUp()
        {
            playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
            monsterConfig = ScriptableObject.CreateInstance<MonsterConfig>();
            playerModel = new PlayerModel();
            monsterModel = new MonsterModel();
            playerRules = new PlayerRules(playerConfig, playerModel, NullTelemetryScope.Instance);
            monsterRules = new MonsterRules(monsterConfig, monsterModel, new RandomService(5ul), NullTelemetryScope.Instance);
            step = new EncounterStep(playerRules, monsterRules);
            step.Begin(Vector2.zero, new[] { MonsterStart, MonsterStart + new Vector2(100f, 0f) });
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(playerConfig);
            Object.DestroyImmediate(monsterConfig);
        }

        /// <summary>基线：一切都没发生时的五项——未察觉、没潜行、没偷袭、玩家先动手。</summary>
        [Test]
        public void Baseline_NothingHappened_ReportsUnawareAndPlayerAttacked()
        {
            Assert.That(step.PlayerSneaking, Is.False);
            Assert.That(step.SneakAttackLanded, Is.False);
            Assert.That(step.PlayerInAlertZone, Is.False);
            Assert.That(step.PlayerInHostileZone, Is.False);
            Assert.That(step.MonsterState, Is.EqualTo(MonsterAlertState.Unaware));
            Assert.That(step.Initiator, Is.EqualTo(BattleInitiator.PlayerAttacked));
        }

        /// <summary>
        /// 潜行 + 怪物没察觉 → 快照报「潜行中」，且**仍然由玩家先动手**。
        /// 这条是偷袭的输入前提：`BattleEntryRules` 拿到它之后还要 `SneakAttackLanded` 才肯判偷袭
        /// （只有潜行不算偷袭成功——见 <see cref="SneakAttackLanded_StartsFalse"/>）。
        /// </summary>
        [Test]
        public void PlayerSneaking_IsReadFromTheLiveSnapshot()
        {
            Tick(0.1f, sneak: true);

            Assert.That(step.PlayerSneaking, Is.True);
            Assert.That(step.MonsterState, Is.EqualTo(MonsterAlertState.Unaware), "站在背后潜行，它没察觉");
            Assert.That(step.Initiator, Is.EqualTo(BattleInitiator.PlayerAttacked));
        }

        /// <summary>
        /// 负对照：`SneakAttackLanded` 的判据是**已命中**的持久事实（`stealth.assassinated`），
        /// **不是**「此刻能不能下刀」。它一开始必须是 false——否则一潜行就等于「偷袭成功了」，
        /// 偷袭会变成随便就能拿到的 20% 白嫖。
        /// </summary>
        [Test]
        public void SneakAttackLanded_StartsFalse()
        {
            Tick(0.1f, sneak: true);

            Assert.That(step.PlayerSneaking, Is.True, "前置：确实在潜行");
            Assert.That(step.SneakAttackLanded, Is.False, "潜行 ≠ 偷袭命中");
        }

        /// <summary>
        /// 正例：事实集里写进 `stealth.assassinated`（背后处决命中时的写入点）之后，
        /// 快照才开始报「偷袭已命中」。
        /// </summary>
        [Test]
        public void SneakAttackLanded_AfterAssassinatedFact_IsTrue()
        {
            step.FactSink.Set(Game.Stealth.StealthFactKeys.Assassinated, true);

            Assert.That(step.SneakAttackLanded, Is.True);
        }

        /// <summary>玩家走进橙区：怪物进警戒态 → 快照报「在警戒区域内」+ 警觉档位 Alert，玩家动手即正面攻击。</summary>
        [Test]
        public void PlayerInsideAlertZone_ReportsAlertStateAndFrontalContext()
        {
            PlacePlayer(AlertSpot);
            Tick(0.1f);

            Assert.That(monsterModel.Mode, Is.EqualTo(MonsterMode.Alert), "前置：橙区把警戒点亮");
            Assert.That(step.PlayerInAlertZone, Is.True);
            Assert.That(step.PlayerInHostileZone, Is.False);
            Assert.That(step.MonsterState, Is.EqualTo(MonsterAlertState.Alert));
            Assert.That(step.Initiator, Is.EqualTo(BattleInitiator.PlayerAttacked), "警戒态 + 没潜行 → 玩家先动手");
        }

        /// <summary>
        /// 红区：怪物立刻进敌对 → 快照报「在敌对区域内」+ Hostile，且**谁先动手这一项变成 BOSS**
        /// （那一处推断的落点，理由见 `EncounterStep.DeriveInitiator` 的注释）。
        /// </summary>
        [Test]
        public void PlayerInsideHostileZone_ReportsHostileStateAndBossAttacked()
        {
            PlacePlayer(HostileSpot);
            Tick(0.1f);

            Assert.That(monsterModel.Mode, Is.EqualTo(MonsterMode.Hostile), "前置：红区立刻敌对");
            Assert.That(step.PlayerInHostileZone, Is.True);
            Assert.That(step.MonsterState, Is.EqualTo(MonsterAlertState.Hostile));
            Assert.That(step.Initiator, Is.EqualTo(BattleInitiator.BossAttacked), "已经打起来了 → 判作「被打」");
        }

        /// <summary>
        /// 显式钉住那一处推断的边界：**敌对态 + 玩家在潜行**时仍然按「玩家先动手」——
        /// 潜行意味着玩家还在暗处、主动接近，不该被判成「被 BOSS 打」。
        /// （站位仍要在红区里：红区**优先于潜行**，潜行不免红区，见 `06_怪物状态与交互设计文档.md:23`。）
        /// </summary>
        [Test]
        public void HostileButSneaking_StillReportsPlayerAttacked()
        {
            PlacePlayer(HostileSpot);
            Tick(0.1f, sneak: true);

            Assert.That(monsterModel.Mode, Is.EqualTo(MonsterMode.Hostile), "前置：红区不吃潜行这一套");
            Assert.That(step.PlayerInHostileZone, Is.True);
            Assert.That(step.Initiator, Is.EqualTo(BattleInitiator.PlayerAttacked));
        }

        /// <summary>`MonsterMode` → `MonsterAlertState` 的逐值翻译（含巡逻停顿与死亡这两档）。</summary>
        [TestCase(MonsterMode.PatrolWalk, MonsterAlertState.Unaware)]
        [TestCase(MonsterMode.PatrolPause, MonsterAlertState.Unaware)]
        [TestCase(MonsterMode.Dead, MonsterAlertState.Unaware)]
        [TestCase(MonsterMode.Alert, MonsterAlertState.Alert)]
        [TestCase(MonsterMode.Hostile, MonsterAlertState.Hostile)]
        public void ToAlertState_MapsEveryMode(MonsterMode mode, MonsterAlertState expected)
        {
            DriveInto(mode);

            Assert.That(monsterModel.Mode, Is.EqualTo(mode), "前置：现场确实到了这个模式");
            Assert.That(step.MonsterState, Is.EqualTo(expected));
        }

        // ──────────────────────────── 夹具 ────────────────────────────

        /// <summary>把现场推到指定模式（每一档都真的驱动过去，不靠「默认分支大概是对的」）。</summary>
        private void DriveInto(MonsterMode mode)
        {
            switch (mode)
            {
                case MonsterMode.PatrolWalk:
                    // 开局就是巡逻走（玩家在背后，什么都没触发）。
                    break;

                case MonsterMode.PatrolPause:
                    // 巡逻计时达到随机阈值（7–10 秒）才会停步，所以按 0.5 秒一格推进到停步为止。
                    // 玩家留在背后不动，这一段不会被打断；停步本身持续 2 秒，循环上限 60 格（30 秒）足够宽。
                    for (int i = 0; i < 60 && monsterModel.Mode != MonsterMode.PatrolPause; i++)
                    {
                        Tick(0.5f);
                    }

                    break;

                case MonsterMode.Dead:
                    // 走公开的常规击杀入口：`MonsterModel.Health` / `.Mode` 是 internal set，
                    // 而 Game.Runtime 没对测试程序集开 InternalsVisibleTo（工程约定），测试里写不了。
                    var damage = new DamageIntent(monsterConfig.MaxHealth);
                    var attacker = new PlayerSnapshot(Vector2.zero, Vector2.right, false, false, 3);
                    Assert.That(monsterRules.ApplyDamage(in damage, in attacker), Is.True, "前置：这一下要能打死它");
                    break;

                case MonsterMode.Alert:
                    PlacePlayer(AlertSpot);
                    Tick(0.1f);
                    break;

                case MonsterMode.Hostile:
                    PlacePlayer(HostileSpot);
                    Tick(0.1f);
                    break;

                default:
                    // 内核加了新的 MonsterMode 而这里没跟上：当场失败，不要静默算过。
                    Assert.Fail($"DriveInto 没覆盖 {mode}——MonsterAlertState 的映射测试漏了一档");
                    break;
            }
        }

        private void PlacePlayer(Vector2 position) => playerRules.Reset(position);

        private void Tick(float deltaTime, bool sneak = false)
        {
            uint buttons = sneak ? InputCommand.ButtonSneak : 0u;
            var command = new InputCommand(Vector2.zero, Vector2.zero, buttons, Vector2.zero, 0);
            var context = new SimulationContext(0, deltaTime, in command, new RandomService(5ul));
            step.Step(in context);
        }
    }
}
