// 职责：钉住「BOSS 自己的局势」这一层（2026-10-10，③ 方案 2）——
//   ① 判据与巡逻怪**同一套档位**（`MonsterRules.SenseFrom`）：红区 / 橙区 / 贴身三档在 BOSS 上都成立；
//   ② `MonsterAware` 只有「警戒或敌对」才为真（Unaware 不算），因为读方 `BattleSetup.IsEncounterEngaged` 认它；
//   ③ 没接配置 / 没接玩家时**不生效也不抛**（退回原口径），且只 Warn 一次。
// 为什么新建：`BossEntryContext` 是这一波新增的**开战理由来源**，它坏掉的表现是「boss 战又退回用巡逻怪的局势判」
//   ——编译不报、老用例全绿、只有回放里按 E 不开仗（或开仗方式不对）。所以这里既钉正向三档，也钉负对照。
// 2026-10-14（PRP/combat-iteration D6）：再钉两件——④ 开战理由可设置、默认「玩家先动手」不变、可复位（BossAggro 置位 / 复位）；
//   ⑤ 逻辑平面投影：交了 XZ 投影时高度不参与（同波修的既有缺口），没交时仍按 (x, y)（默认值 = 旧行为）。
// 夹具口径：MonoBehaviour 不手 new（Unity 会报），一律建物体 AddComponent + SerializedObject 写私有配置字段
//   （与 `EncounterBattleBridgeTests.BuildContainer` / `MonsterDropSinkWiringTests` 一致）。
using Game.Battle;
using Game.Core.Telemetry;
using Game.Monster;
using Game.Player;
using Game.TurnBased;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Battle
{
    public sealed class BossEntryContextTests
    {
        private MonsterConfig config;
        private PlayerModel player;
        private GameObject bossObject;
        private BossEntryContext context;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<MonsterConfig>();
            player = new PlayerModel();
            player.Restore(new PlayerSaveData { Health = 3 });
            bossObject = new GameObject("BossEntryContextTestBoss");
            context = bossObject.AddComponent<BossEntryContext>();
        }

        [TearDown]
        public void TearDown()
        {
            if (bossObject != null) Object.DestroyImmediate(bossObject);
            bossObject = null;
            context = null;
            if (config != null) Object.DestroyImmediate(config);
            config = null;
            player = null;
        }

        // ────────────────────── 正向：三档 ──────────────────────

        /// <summary>玩家站进红区（敌对半径内 + 在视野锥里）→ 敌对档；这一场按 07:25-28 该是「被打」。</summary>
        [Test]
        public void PlayerInsideHostileRadius_IsHostile()
        {
            Bind();
            MovePlayer(config.HostileRadius * 0.5f);

            Assert.That(context.PlayerInHostileZone, Is.True);
            Assert.That(context.PlayerInAlertZone, Is.False, "红区优先：敌对那一档不重复算警戒");
            Assert.That(context.MonsterState, Is.EqualTo(MonsterAlertState.Hostile));
            Assert.That(context.MonsterAware, Is.True);
        }

        /// <summary>玩家在橙区（警戒半径内、红区外、在视野锥里）→ 警戒档（07:22 正面攻击那一支）。</summary>
        [Test]
        public void PlayerInsideAlertRadius_IsAlert()
        {
            Bind();
            MovePlayer((config.HostileRadius + config.AlertRadius) * 0.5f);

            Assert.That(context.PlayerInAlertZone, Is.True);
            Assert.That(context.PlayerInHostileZone, Is.False);
            Assert.That(context.MonsterState, Is.EqualTo(MonsterAlertState.Alert));
            Assert.That(context.MonsterAware, Is.True);
        }

        /// <summary>三档之外 → 没察觉（负对照；读方据此判「没有发言权」）。</summary>
        [Test]
        public void PlayerOutsideAllRanges_IsUnaware()
        {
            Bind();
            MovePlayer(config.AlertRadius + 5f);

            Assert.That(context.PlayerInAlertZone, Is.False);
            Assert.That(context.PlayerInHostileZone, Is.False);
            Assert.That(context.MonsterState, Is.EqualTo(MonsterAlertState.Unaware));
            Assert.That(context.MonsterAware, Is.False);
            Assert.That(BattleSetup.IsEncounterEngaged(context), Is.False, "没察觉的 BOSS 不该替这一仗找理由");
        }

        /// <summary>
        /// 与巡逻怪**同一份判据**的直接对照：同一个玩家位置，`MonsterRules.SenseFrom` 给什么档位，
        /// BOSS 就该给什么档位。这条是「不出现两套几何」的守卫——将来有人把档位改回实例方法时它会红。
        /// </summary>
        [Test]
        public void SameGeometryAsMonsterRules()
        {
            Bind();
            MovePlayer((config.HostileRadius + config.AlertRadius) * 0.5f);

            PlayerSnapshot target = Snapshot();
            int expected = MonsterRules.SenseFrom(config, null, Vector2.zero, Vector2.zero, in target);

            Assert.That(SenseLevelViaState(), Is.EqualTo(ToLevel(expected)),
                "BOSS 的档位必须等于巡逻怪同一位置的档位（共用 MonsterRules.SenseFrom）");
        }

        // ────────────────────── 负对照：没接就不生效 ──────────────────────

        /// <summary>没接玩家模型：不抛、不算察觉、读方仍能安全取用（退回原口径）。</summary>
        [Test]
        public void WithoutPlayerModel_IsInert()
        {
            context.Configure(config, null);
            context.UseTelemetry(NullTelemetryScope.Instance);
            MovePlayer(config.HostileRadius * 0.5f);

            Assert.That(context.IsReady, Is.False);
            Assert.That(context.MonsterAware, Is.False, "没接玩家 = 不生效，不许凭配置就说察觉了");
            Assert.DoesNotThrow(() => Assert.That(BattleSetup.IsEncounterEngaged(context), Is.False));
        }

        /// <summary>没接 `MonsterConfig`：同样不生效（不拿默认值硬判），也只 Warn 一次。</summary>
        [Test]
        public void WithoutConfig_IsInert()
        {
            context.Configure(null, player);
            context.UseTelemetry(NullTelemetryScope.Instance);
            MovePlayer(0.5f);

            Assert.That(context.IsReady, Is.False);
            Assert.That(context.MonsterAware, Is.False);
            Assert.DoesNotThrow(() => Assert.That(context.PlayerInAlertZone, Is.False));
        }

        // ────────────────────── 开战理由（PRP/combat-iteration D6：可设置、默认不变、可复位） ──────────────────────

        /// <summary>默认「玩家先动手」= 加 BossAggro 之前写死的值：按 E 挑战仍是正面攻击（回归）。</summary>
        [Test]
        public void Initiator_ByDefault_IsPlayerAttacked()
        {
            Bind();

            Assert.That(context.Initiator, Is.EqualTo(BattleInitiator.PlayerAttacked));
        }

        /// <summary>发难组件置为「BOSS 先动手」后，读方（开仗那一刻的 BattleSetup）读到的就是它。</summary>
        [Test]
        public void SetInitiator_BossAttacked_IsReportedToReaders()
        {
            Bind();

            context.SetInitiator(BattleInitiator.BossAttacked);

            Assert.That(context.Initiator, Is.EqualTo(BattleInitiator.BossAttacked));
            Assert.That(((IBattleEntryContext)context).Initiator, Is.EqualTo(BattleInitiator.BossAttacked), "经契约读到的同一个值");
        }

        /// <summary>复位回到默认；重复复位幂等。</summary>
        [Test]
        public void ResetInitiator_AfterBossAttacked_RestoresPlayerAttacked()
        {
            Bind();
            context.SetInitiator(BattleInitiator.BossAttacked);

            context.ResetInitiator();
            context.ResetInitiator();

            Assert.That(context.Initiator, Is.EqualTo(BattleInitiator.PlayerAttacked));
        }

        /// <summary>理由只是转述，不改局势：置了「BOSS 先动手」，档位照旧由距离决定（负对照：远处仍是没察觉）。</summary>
        [Test]
        public void SetInitiator_DoesNotChangeSensedZones()
        {
            Bind();
            MovePlayer(config.AlertRadius + 5f);

            context.SetInitiator(BattleInitiator.BossAttacked);

            Assert.That(context.MonsterAware, Is.False);
            Assert.That(context.PlayerInHostileZone, Is.False);
        }

        // ────────────────────── 逻辑平面投影（同波修的既有缺口：世界场景是 XZ，y 是高度） ──────────────────────

        /// <summary>
        /// 交了 XZ 投影：BOSS 的逻辑位置取 (x, z)，高度不参与。照 SampleScene 的 Npc_SampleBoss（高 4.89、z 8.4），
        /// 玩家站它正南 1.4 米（逻辑 (x, 7.0)）应在红区里。
        /// </summary>
        [Test]
        public void WithXZProjection_UsesWorldXZ_IgnoresHeight()
        {
            Bind();
            context.UseProjection(XZProjection());
            bossObject.transform.position = new Vector3(-4.2f, 4.8884f, 8.4f);
            player.Restore(new PlayerSaveData { Health = 3, PositionX = -4.2f, PositionY = 7.0f });

            Assert.That(context.LogicPosition.x, Is.EqualTo(-4.2f).Within(1e-4f));
            Assert.That(context.LogicPosition.y, Is.EqualTo(8.4f).Within(1e-4f), "逻辑 y = 世界 z");
            Assert.That(context.PlayerInHostileZone, Is.True, "正南 1.4 米 < 敌对半径 2");
        }

        /// <summary>负对照：没交投影 = 改之前的口径 (x, y)（默认值不改旧行为）。同一站位被算成 2.11 米，落在橙区。</summary>
        [Test]
        public void WithoutProjection_UsesWorldXY_AsBefore()
        {
            Bind();
            bossObject.transform.position = new Vector3(-4.2f, 4.8884f, 8.4f);
            player.Restore(new PlayerSaveData { Health = 3, PositionX = -4.2f, PositionY = 7.0f });

            Assert.That(context.LogicPosition.y, Is.EqualTo(4.8884f).Within(1e-4f), "逻辑 y = 世界 y（旧口径）");
            Assert.That(context.PlayerInHostileZone, Is.False);
            Assert.That(context.PlayerInAlertZone, Is.True);
        }

        // ────────────────────── 夹具 ──────────────────────

        /// <summary>一份 XZ 平面的投影（EncounterSceneView 的 useXZPlane 是私有序列化字段，按 Inspector 的接法写）。</summary>
        private EncounterSceneView XZProjection()
        {
            EncounterSceneView view = bossObject.AddComponent<EncounterSceneView>();
            var serialized = new UnityEditor.SerializedObject(view);
            serialized.FindProperty("useXZPlane").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return view;
        }

        private void Bind()
        {
            context.Configure(config, player);
            context.UseTelemetry(NullTelemetryScope.Instance);
        }

        /// <summary>把玩家放到 BOSS 的 <paramref name="distance"/> 米处（BOSS 在原点；判据只比距离，不看朝向）。</summary>
        private void MovePlayer(float distance)
        {
            bossObject.transform.position = Vector3.zero;
            player.Restore(new PlayerSaveData { Health = 3, PositionX = 0f, PositionY = distance });
        }

        private PlayerSnapshot Snapshot() => player.Snapshot;

        private int SenseLevelViaState() => context.MonsterState switch
        {
            MonsterAlertState.Hostile => 2,
            MonsterAlertState.Alert => 1,
            _ => 0,
        };

        private static int ToLevel(int sense) => sense >= 2 ? 2 : sense == 1 ? 1 : 0;
    }
}
