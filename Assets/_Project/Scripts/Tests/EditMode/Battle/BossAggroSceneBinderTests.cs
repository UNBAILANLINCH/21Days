// 职责：钉住 BOSS 发难的**真实场景扫描**（PRP/combat-iteration D6；2026-10-16 代码审查指出还差这一层）——
//   `BossAggroSceneBinder.BindScene(scene)` 扫一个**已加载的场景**时做三件事，缺任一件在现场都表现为
//   「BOSS 站着不动、一句错也不报」：
//   ① 同一个场景的 `BossEntryContext` 接上数值来源 / 玩家模型 / 逻辑平面投影（发难每帧要问它局势，不能等开仗才接）；
//   ② 场景里的 `BossAggro` 拿到名册里那一行 + 本对象（外部世界），此后 State 才会离开待机；
//   ③ 顺序：局势先接好再绑发难（发难绑定时要看 `IsReady` 决定武不武装）。
// 与 `BattleInstallerTests` 的分工（两处不重复）：那一组钉容器里那个入口点 `Start` 与名册 / 配置的负对照；
//   本组只调 `BindScene`，覆盖 `Start` 那条路走不到的：重复扫描幂等、原位只记一次、场景里没有发难组件。
// 为什么新建：`BossAggroTests` 是**手工 Bind**（直接调 aggro.Bind）驱动状态机，证明不了「场景里的组件会被扫到」
//   ——那正是接线缺口最常见的形状（pitfalls.md:225「测试自己把依赖装上，接线缺口全程不报」）。
// 场景从哪来：**用测试运行器当前那个场景**（EditMode 里造不出来——`SceneManager.CreateScene` 只在 play mode 可用，
//   `EditorSceneManager.NewScene(Additive)` 会因为「当前是未保存的未命名场景」直接抛
//   `Cannot create a new scene additively with an untitled scene unsaved`）。用例造出来的物体全部登记在
//   `spawned` 里，TearDown 逐个销毁；测试运行器每跑一条用例都会重开场景，所以不留残留。
// 真实服务：roster / flow / arena 用真的（BattleFakes 补齐下游），保证绑定时读的是生产判据。
using System.Collections.Generic;
using System.Threading;
using Game.Battle;
using Game.Core.Input;
using Game.Core.Telemetry;
using Game.Monster;
using Game.Player;
using Game.Tests.EditMode.Telemetry;
using Game.TurnBased;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using NarrativeTrigger = Game.Narrative.NarrativeTrigger;

namespace Game.Tests.EditMode.Battle
{
    public sealed class BossAggroSceneBinderTests
    {
        private const string BossId = "sample_boss";
        private const float AggroRadius = 5f;

        private readonly List<GameObject> spawned = new List<GameObject>();
        private MonsterConfig monsterConfig;
        private PlayerModel player;
        private BossAggroSceneBinder binder;
        private BattleArena arena;
        private BattleFlow flow;
        private InputService input;
        private RecordingTelemetrySink sink;
        private TelemetryService telemetryService;

        [SetUp]
        public void SetUp()
        {
            monsterConfig = ScriptableObject.CreateInstance<MonsterConfig>();
            player = new PlayerModel();
            sink = new RecordingTelemetrySink();
            telemetryService = new TelemetryService(TelemetryOptions.Default, new FakeTelemetryClock(), sink);
            NewBinder(AggroRoster(aggroEnabled: true));
        }

        [TearDown]
        public void TearDown()
        {
            binder?.Dispose();
            binder = null;
            flow?.Dispose();
            flow = null;
            input?.Dispose();
            input = null;
            telemetryService?.Dispose();
            telemetryService = null;
            foreach (GameObject go in spawned)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            spawned.Clear();
            if (monsterConfig != null) Object.DestroyImmediate(monsterConfig);
            monsterConfig = null;
        }

        // ────────────────────── 扫到了、接上了、武装了 ──────────────────────

        /// <summary>
        /// 场上有按 Inspector 接法挂好的 BOSS（BossEntryContext + BossAggro + 剧情入口 + 同场景投影），
        /// 只调 <see cref="BossAggroSceneBinder.BindScene"/>：三件事都要发生。
        /// 这一条红了就是「场景里配了也没人接」——现场表现是 BOSS 站着不动、控制台一片安静。
        /// </summary>
        [Test]
        public void BindScene_BossWithAggro_GetsBoundAndArmed()
        {
            BossAggro aggro = SpawnBoss(withProjection: true);
            BossEntryContext context = aggro.GetComponent<BossEntryContext>();

            int bound = binder.BindScene(aggro.gameObject.scene);

            Assert.That(bound, Is.EqualTo(1), "场上那一个 BossAggro 应被扫到一次");
            Assert.That(aggro.IsBound, Is.True, "没拿到外部世界 = 绑没接上");
            Assert.That(context.IsReady, Is.True, "局势该在场景加载时接上 MonsterConfig 与 PlayerModel（不等开仗）");
            Assert.That(aggro.IsArmed, Is.True, "名册这一行开了发难、局势也接上了，绑定的第一帧就该武装");
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Idle));
            Assert.That(aggro.Home, Is.EqualTo(aggro.transform.position), "原位在绑定那一刻记下");
        }

        /// <summary>
        /// 投影在不在只影响「逻辑位置怎么算」，不影响武装：**故意不挂** `EncounterSceneView`，
        /// 局势仍要接上数值来源与玩家模型（少了投影退回按 (x, y) 取，是 D6 之前的既定退路）。
        /// </summary>
        [Test]
        public void BindScene_WithoutProjection_StillArmsAggro()
        {
            BossAggro aggro = SpawnBoss(withProjection: false);
            BossEntryContext context = aggro.GetComponent<BossEntryContext>();

            binder.BindScene(aggro.gameObject.scene);

            Assert.That(context.IsReady, Is.True, "投影缺位不该连累局势接配置");
            Assert.That(aggro.IsArmed, Is.True);
        }

        /// <summary>
        /// 幂等：同一个场景扫两次（`Start` 扫一遍 + 之后 `sceneLoaded` 再来一次）不重复计数、不动原位、不回退状态。
        /// 原位只记一次这条尤其要紧——重复绑定若把原位挪到「当前位置」，BOSS 撤回就会撤到它冲出去的那一点。
        /// </summary>
        [Test]
        public void BindScene_Twice_IsIdempotent()
        {
            BossAggro aggro = SpawnBoss(withProjection: true);

            binder.BindScene(aggro.gameObject.scene);
            Vector3 home = aggro.Home;
            int boundAgain = binder.BindScene(aggro.gameObject.scene);

            Assert.That(boundAgain, Is.EqualTo(1), "每次扫描都返回扫到的个数（判重是调用方的事）");
            Assert.That(aggro.IsArmed, Is.True);
            Assert.That(aggro.Home, Is.EqualTo(home), "原位只在第一次绑定时记");
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Idle), "没推进过 tick，状态不许自己变");
        }

        /// <summary>负对照：场景里没有发难组件时扫出 0 个、不抛（战斗舞台等叠加场景会被扫到，里面本来就没有它）。</summary>
        [Test]
        public void BindScene_SceneWithoutAggro_ReturnsZero()
        {
            // 场景里放一个别的物体（有投影、没有 BossAggro）——证明扫的是组件类型，不是「场景里有东西就报 1」。
            var plain = new GameObject("BossAggroSceneBinderTests_Crate");
            spawned.Add(plain);
            AddXZProjection(plain);

            Assert.That(binder.BindScene(plain.scene), Is.Zero, "本组自己不留 BossAggro 残留（TearDown 逐个销毁）");
        }

        /// <summary>负对照：名册里那一行没开发难 → 组件照样被接上（IsBound）但不武装、不动。</summary>
        [Test]
        public void BindScene_AggroDisabledInRoster_BindsButStaysDisarmed()
        {
            NewBinder(AggroRoster(aggroEnabled: false));
            BossAggro aggro = SpawnBoss(withProjection: true);

            binder.BindScene(aggro.gameObject.scene);

            Assert.That(aggro.IsBound, Is.True);
            Assert.That(aggro.IsArmed, Is.False, "名册没开发难 = 加组件之前的行为");
        }

        // ────────────────────── 夹具 ──────────────────────

        /// <summary>名册一行 sample_boss：发难半径 5 米、反应时间 3 秒（拴绳 0 = 按半径 × 2 算）。</summary>
        private static BossRoster AggroRoster(bool aggroEnabled) => new BossRoster(new[]
        {
            new BossDefinition(BossId, "示例 BOSS", 10, 0, playerHealth: 10,
                aggroEnabled: aggroEnabled, aggroRadius: AggroRadius, aggroReactionSeconds: 3f,
                aggroChargeSpeed: BossDefinition.PlaceholderAggroChargeSpeed, aggroLeashDistance: 0f),
        });

        /// <summary>
        /// 照 SampleScene 的 `Npc_SampleBoss` 摆一只（坐标也是挪位后的 (-4, -8)、高 4.8884）：
        /// `BossEntryContext` 与 `BossAggro` 都填 bossId = sample_boss，`NarrativeTrigger` 用默认交互半径 2 米
        /// （&lt; 发难半径 5 米，所以绑定时不会因为「玩家没有走近按 E 的空档」告警）。
        /// </summary>
        private BossAggro SpawnBoss(bool withProjection)
        {
            var boss = new GameObject("BossAggroSceneBinderTests_Npc_SampleBoss");
            spawned.Add(boss);
            boss.transform.position = new Vector3(-4f, 4.8884f, -8f);
            boss.AddComponent<BossEntryContext>();
            boss.AddComponent<NarrativeTrigger>();
            BossAggro aggro = boss.AddComponent<BossAggro>();
            SetField(aggro, "bossId", property => property.stringValue = BossId);
            if (withProjection) AddXZProjection(boss);
            return aggro;
        }

        /// <summary>同场景的逻辑平面投影（SampleScene 的 `EncounterSceneView` 勾了 useXZPlane）。</summary>
        private static void AddXZProjection(GameObject target)
        {
            EncounterSceneView view = target.AddComponent<EncounterSceneView>();
            SetField(view, "useXZPlane", property => property.boolValue = true);
        }

        /// <summary>按 Inspector 的接法写私有序列化字段（不给生产代码开 setter，同 BossAggroTests）。</summary>
        private static void SetField(Object target, string field, System.Action<SerializedProperty> write)
        {
            var serialized = new SerializedObject(target);
            write(serialized.FindProperty(field));
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>换一份名册重建绑定器：真实 BattleFlow + 真战斗场景宿主，其余口用 BattleFakes。</summary>
        private void NewBinder(BossRoster roster)
        {
            input = new InputService();
            input.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            var items = new BattleItemInventory(new BattleFakes.Backpack());
            arena = new BattleArena(new BattleFakes.Curtain(), new BattleFakes.Assets(), NullTelemetryScope.Instance);
            flow = new BattleFlow(new BattleFakes.Bus<Game.Narrative.BattleStageEnteredEvent>(), roster,
                new BattleFakes.Narrative(), new BattleFakes.Gate(),
                new BattleWorldLock(new BattleFakes.Pause(), input, new BattleFakes.UI()), arena,
                new BattleSetup(BattleSettings.PlaceholderDefault, () => 42UL, items), items,
                new BattleFakes.Presenter(), NullTelemetryScope.Instance);
            binder = new BossAggroSceneBinder(roster, flow, arena, new BattleFakes.Pause(),
                telemetryService.Scope("battle"), monsterConfig: monsterConfig, player: player);
        }
    }
}
