// 职责：钉住 S3 视线遮挡这一波的接线——场景里显式登记的遮挡体，转成纯数据几何再喂进 `EncounterStep.Sight`，
//   能真的把视线挡掉（`stealth.hidden` / `stealth.cover` 为真）；没登记时是空数组 = 视线不被遮挡。
// 为什么新建：`EncounterStealthTests` 守的是「遮挡体已经在 Sight 里之后」的判定，而这一波新接的那一步
//   （`MonsterEncounterState` 里的 `view.CollectSightOccluders()` → `step.Sight.SetOccluders(...)`）
//   在此以前**没有任何调用方**，`CollectSightOccluders` 的转换规则（跳过空引用、非正尺寸跳过并警告、
//   id 按收集顺序递增）也一条都没测过。两个类分开：那个测结算，这个测「场景 → 纯数据 → 结算」这条链。
using System.Text.RegularExpressions;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Monster;
using Game.Player;
using Game.Stealth;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.EditMode.Monster
{
    public sealed class EncounterSightOccluderWiringTests
    {
        private PlayerConfig playerConfig;
        private MonsterConfig monsterConfig;
        private GameObject viewHost;
        private EncounterSceneView view;

        [SetUp]
        public void SetUp()
        {
            playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
            monsterConfig = ScriptableObject.CreateInstance<MonsterConfig>();
            viewHost = new GameObject("EncounterSceneView(测试)");
            view = viewHost.AddComponent<EncounterSceneView>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(viewHost);
            Object.DestroyImmediate(playerConfig);
            Object.DestroyImmediate(monsterConfig);
        }

        /// <summary>四条登记里只有两条能成：空引用跳过、尺寸非正数跳过并警告（不许静默失效）。</summary>
        [Test]
        public void CollectSightOccluders_SkipsEmptyAndMalformedEntries()
        {
            LogAssert.Expect(LogType.Warning, new Regex("尺寸非正数"));
            Transform pillar = NewMarker("Pillar", new Vector2(1.5f, 0f));
            Transform well = NewMarker("Well", new Vector2(5f, 0f));
            SetOccluderEntries(
                new[] { pillar, well, null, pillar },
                new[] { false, true, false, false },
                new[] { new Vector2(0.5f, 2f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 2f) });

            StealthOccluder[] occluders = view.CollectSightOccluders();

            Assert.That(occluders.Length, Is.EqualTo(2), "空引用与非正尺寸的两条要被跳过");
            Assert.That(occluders[0].Kind, Is.EqualTo(StealthOccluderKind.Rectangle));
            Assert.That(occluders[0].Center, Is.EqualTo(new Vector2(1.5f, 0f)));
            Assert.That(occluders[0].HalfSize, Is.EqualTo(new Vector2(0.25f, 1f)), "场景给的是全长全宽，几何存半宽半高");
            Assert.That(occluders[0].Id, Is.EqualTo((short)1), "id 按收集顺序从 1 递增");
            Assert.That(occluders[1].Kind, Is.EqualTo(StealthOccluderKind.Circle));
            // 2026-10-07 口径已统一：圆的 size.x 是**直径**（与 Tooltip `:78/:80` 和矩形分支的「给全长、存半」一致）。
            // 此前实现把 size.x 当半径传、比 Tooltip 大一倍，本用例曾按「实现现状」钉 Radius == 1f 并留了提醒注释；
            // 现在按统一口径钉 **0.5f**（size.x = 1 的直径 → 半径 0.5）。改这条时同步改了 `EncounterSceneView.cs`。
            Assert.That(occluders[1].Radius, Is.EqualTo(0.5f), "size.x = 1 是直径，半径应为 0.5");
            Assert.That(occluders[1].Id, Is.EqualTo((short)2));
        }

        /// <summary>没登记任何遮挡体：返回空数组（喂进 Sight 等于清空），视线判定与接线前一致。</summary>
        [Test]
        public void CollectSightOccluders_WithoutEntries_ReturnsEmpty()
        {
            Assert.That(view.CollectSightOccluders(), Is.Empty);
        }

        /// <summary>
        /// 接线正向：柱子在玩家与怪物之间时，把清单喂进 Sight 之后这一 tick 就是「看不见 + 处于掩体遮挡下」。
        /// 喂的这一步就是 `MonsterEncounterState.OnSceneReadyAsync` 里新加的那一行。
        /// </summary>
        [Test]
        public void OccludersFromView_WhenFedIntoStepSight_MakePlayerHidden()
        {
            Transform pillar = NewMarker("Pillar", new Vector2(1.5f, 0f));
            SetOccluderEntries(new[] { pillar }, new[] { false }, new[] { new Vector2(0.5f, 2f) });
            Rig rig = NewRig(Vector2.zero, new Vector2(3f, 0f), new Vector2(0f, 0f));
            Assert.That(rig.MonsterRules.Detects(rig.PlayerModel.Snapshot), Is.True, "距离与夹角上都够得着");

            rig.Step.Sight.SetOccluders(view.CollectSightOccluders());
            Tick(rig, 0f);

            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.Hidden), Is.True, "视线被挡 = 未被察觉");
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.Cover), Is.True, "处于掩体遮挡下");
        }

        /// <summary>负对照：同一份场景、同一个位置，只是清单为空（柱子没登记）——照样被看见。</summary>
        [Test]
        public void WithoutOccluders_SameSetup_PlayerIsSeen()
        {
            Rig rig = NewRig(Vector2.zero, new Vector2(3f, 0f), new Vector2(0f, 0f));

            rig.Step.Sight.SetOccluders(view.CollectSightOccluders());
            Tick(rig, 0f);

            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.Hidden), Is.False);
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.Cover), Is.False);
        }

        // ──────────────────────────── 夹具 ────────────────────────────

        /// <summary>场景里登记一条遮挡体：私有序列化数组按序列化名写（同 SampleSceneObstacleWiringTests 的读法）。</summary>
        private void SetOccluderEntries(Transform[] sources, bool[] circles, Vector2[] sizes)
        {
            var serialized = new SerializedObject(view);
            SerializedProperty array = serialized.FindProperty("sightOccluders");
            array.arraySize = sources.Length;
            for (int i = 0; i < sources.Length; i++)
            {
                SerializedProperty entry = array.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("source").objectReferenceValue = sources[i];
                entry.FindPropertyRelative("circle").boolValue = circles[i];
                entry.FindPropertyRelative("size").vector2Value = sizes[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>按逻辑 XY 摆一个场景标记（本视图未开 XZ 模式，投影取 (x, y)）。</summary>
        private Transform NewMarker(string name, Vector2 logicPosition)
        {
            var marker = new GameObject(name);
            marker.transform.SetParent(viewHost.transform, false);
            marker.transform.position = new Vector3(logicPosition.x, logicPosition.y, 0f);
            return marker.transform;
        }

        private sealed class Rig
        {
            public PlayerModel PlayerModel { get; set; }
            public MonsterModel MonsterModel { get; set; }
            public MonsterRules MonsterRules { get; set; }
            public EncounterStep Step { get; set; }
            public RandomService Random { get; set; }
            public int Tick { get; set; }
        }

        private Rig NewRig(Vector2 playerSpawn, params Vector2[] patrolPoints)
        {
            var rig = new Rig
            {
                PlayerModel = new PlayerModel(),
                MonsterModel = new MonsterModel(),
                Random = new RandomService(5ul),
            };
            var playerRules = new PlayerRules(playerConfig, rig.PlayerModel, NullTelemetryScope.Instance);
            rig.MonsterRules = new MonsterRules(monsterConfig, rig.MonsterModel, new RandomService(5ul),
                NullTelemetryScope.Instance);
            rig.Step = new EncounterStep(playerRules, rig.MonsterRules);
            rig.Step.Begin(playerSpawn, patrolPoints);
            return rig;
        }

        private static void Tick(Rig rig, float deltaTime)
        {
            var command = new InputCommand(Vector2.zero, Vector2.zero, 0u, Vector2.zero, 0);
            var context = new SimulationContext(rig.Tick, deltaTime, in command, rig.Random);
            rig.Tick++;
            rig.Step.Step(in context);
        }
    }
}
