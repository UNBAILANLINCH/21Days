// 职责：验证遭遇表现层在等距场景中使用 XZ，而不是误把高度 Y 当成逻辑纵轴。
// 为什么新建：MonsterRulesTests 只验证纯规则；场景坐标适配属于独立的表现契约。
using System.Reflection;
using Game.Monster;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Monster
{
    public sealed class EncounterSceneViewTests
    {
        private GameObject host;
        private GameObject spawn;
        private GameObject patrol;
        private EncounterSceneView view;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Encounter View");
            spawn = new GameObject("Player Spawn");
            patrol = new GameObject("Patrol Point");
            view = host.AddComponent<EncounterSceneView>();
            SetField("useXZPlane", true);
            SetField("playerSpawn", spawn.transform);
            SetField("patrolPoints", new[] { patrol.transform });
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(patrol);
            Object.DestroyImmediate(spawn);
            Object.DestroyImmediate(host);
        }

        [Test]
        public void XZPlane_MapsSceneMarkersAndBodiesWithoutChangingHeight()
        {
            spawn.transform.position = new Vector3(1f, 7f, 2f);
            patrol.transform.position = new Vector3(3f, 8f, 4f);

            Assert.That(view.PlayerStart, Is.EqualTo(new Vector2(1f, 2f)));
            Assert.That(view.PatrolPositions(), Is.EqualTo(new[] { new Vector2(3f, 4f) }));

            MethodInfo method = typeof(EncounterSceneView).GetMethod(
                "ToScenePosition", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            var mapped = (Vector3)method.Invoke(view, new object[] { new Vector2(5f, 6f), new Vector3(0f, 9f, 0f) });
            Assert.That(mapped, Is.EqualTo(new Vector3(5f, 9f, 6f)));
        }

        [Test]
        public void ResolveGroundY_FlatGround_KeepsGroundHeight()
        {
            Assert.That(EncounterProjection.ResolveGroundY(4.8884f, 4.8884f, 0.32f), Is.EqualTo(4.8884f));
        }

        [Test]
        public void ResolveGroundY_StairStep_RaisesToStep()
        {
            Assert.That(EncounterProjection.ResolveGroundY(4.8884f, 5.1884f, 0.32f), Is.EqualTo(5.1884f));
        }

        [Test]
        public void ResolveGroundY_WallTop_KeepsCurrentHeight()
        {
            Assert.That(EncounterProjection.ResolveGroundY(4.8884f, 7.8884f, 0.32f), Is.EqualTo(4.8884f));
        }

        [Test]
        public void ResolveGroundY_ExactlyMaxStepHeight_RaisesToStep()
        {
            Assert.That(EncounterProjection.ResolveGroundY(4.8884f, 5.2084f, 0.32f), Is.EqualTo(5.2084f));
        }

        [Test]
        public void ResolveGroundY_Falling_RaisesToLowerGround()
        {
            Assert.That(EncounterProjection.ResolveGroundY(5.2884f, 4.8884f, 0.32f), Is.EqualTo(4.8884f));
        }

        [Test]
        public void ResolveFlipX_MovesLeftBeyondThreshold_ReturnsTrue()
        {
            Assert.That(EncounterProjection.ResolveFlipX(2f, 1.8f, false, 0.1f), Is.True);
        }

        [Test]
        public void ResolveFlipX_MovesRightBeyondThreshold_ReturnsFalse()
        {
            Assert.That(EncounterProjection.ResolveFlipX(2f, 2.2f, true, 0.1f), Is.False);
        }

        [Test]
        public void ResolveFlipX_WithinThreshold_KeepsCurrentFlipTrue()
        {
            Assert.That(EncounterProjection.ResolveFlipX(2f, 2.05f, true, 0.1f), Is.True);
        }

        [Test]
        public void ResolveFlipX_WithinThreshold_KeepsCurrentFlipFalse()
        {
            Assert.That(EncounterProjection.ResolveFlipX(2f, 1.95f, false, 0.1f), Is.False);
        }

        private void SetField(string name, object value)
        {
            FieldInfo field = typeof(EncounterSceneView).GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(view, value);
        }

        // —— 两逻辑 tick 之间的渲染插值（EncounterProjection 纯函数）——

        [Test]
        public void InterpolationAlpha_HalfStep_ReturnsHalf()
        {
            Assert.That(EncounterProjection.InterpolationAlpha(0.5f / 60f, 1f / 60f), Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void InterpolationAlpha_ClampsToZeroAndOne()
        {
            Assert.That(EncounterProjection.InterpolationAlpha(-0.01f, 1f / 60f), Is.EqualTo(0f));
            Assert.That(EncounterProjection.InterpolationAlpha(float.NaN, 1f / 60f), Is.EqualTo(0f));
            Assert.That(EncounterProjection.InterpolationAlpha(0.5f, 1f / 60f), Is.EqualTo(1f));
        }

        [Test]
        public void InterpolationAlpha_NonPositiveStep_ReturnsOne()
        {
            Assert.That(EncounterProjection.InterpolationAlpha(0.01f, 0f), Is.EqualTo(1f));
            Assert.That(EncounterProjection.InterpolationAlpha(0.01f, -1f), Is.EqualTo(1f));
        }

        [Test]
        public void InterpolatePosition_Midway_Lerps()
        {
            EncounterProjection.InterpolatePosition(0f, 0f, 0.1f, -0.2f, 0.25f, 1.5f, out float x, out float y);
            Assert.That(x, Is.EqualTo(0.025f).Within(1e-6f));
            Assert.That(y, Is.EqualTo(-0.05f).Within(1e-6f));
        }

        [Test]
        public void InterpolatePosition_AlphaEdges_ReturnEndpointsExactly()
        {
            EncounterProjection.InterpolatePosition(0.1f, 0.7f, 0.3f, 0.9f, 1f, 1.5f, out float x1, out float y1);
            Assert.That(x1, Is.EqualTo(0.3f));
            Assert.That(y1, Is.EqualTo(0.9f));
            EncounterProjection.InterpolatePosition(0.1f, 0.7f, 0.3f, 0.9f, 0f, 1.5f, out float x0, out float y0);
            Assert.That(x0, Is.EqualTo(0.1f));
            Assert.That(y0, Is.EqualTo(0.7f));
        }

        [Test]
        public void InterpolatePosition_BeyondTeleportDistance_TakesCurrent()
        {
            EncounterProjection.InterpolatePosition(0f, 0f, 3f, 4f, 0.5f, 1.5f, out float x, out float y);
            Assert.That(x, Is.EqualTo(3f));
            Assert.That(y, Is.EqualTo(4f));
        }

        [Test]
        public void ResolveBlockedAxis_BlockedTakesCorrected_FreeKeepsLogic()
        {
            // 被挡：扫掠结果偏离插值点超过容差 → 取修正值。
            Assert.That(EncounterProjection.ResolveBlockedAxis(1.1f, 1.05f, 0.9f, 0.0001f), Is.EqualTo(0.9f));
            // 没被挡（只差浮点舍入）→ 保留逻辑值，不把逻辑位置拉回插值点。
            Assert.That(EncounterProjection.ResolveBlockedAxis(1.1f, 1.05f, 1.05f + 1e-6f, 0.0001f), Is.EqualTo(1.1f));
        }

        [Test]
        public void CorrectPreviousAxis_ChangedTakesCorrected_UnchangedKeepsPrevious()
        {
            Assert.That(EncounterProjection.CorrectPreviousAxis(0f, 0.1f, 0.02f), Is.EqualTo(0.02f));
            Assert.That(EncounterProjection.CorrectPreviousAxis(0f, 0.1f, 0.1f), Is.EqualTo(0f));
        }

        // 视图接线：Bind 给了 alpha 源就在上一 tick 与当前 tick 之间插值；不给时按 1，行为同旧版。
        [Test]
        public void LateUpdate_UsesAlphaSource_AndFallsBackToCurrentWithoutIt()
        {
            var cameraObject = new GameObject("Test Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.AddComponent<Camera>();
            var playerObject = new GameObject("Test Player");
            var monsterObject = new GameObject("Test Monster");
            Sprite sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            var playerConfig = ScriptableObject.CreateInstance<Game.Player.PlayerConfig>();
            var monsterConfig = ScriptableObject.CreateInstance<MonsterConfig>();
            try
            {
                SpriteRenderer playerRenderer = playerObject.AddComponent<SpriteRenderer>();
                SpriteRenderer monsterRenderer = monsterObject.AddComponent<SpriteRenderer>();
                playerRenderer.sprite = sprite;
                monsterRenderer.sprite = sprite;
                SetField("playerBody", playerObject.transform);
                SetField("monsterBody", monsterObject.transform);
                SetField("playerSprite", playerRenderer);
                SetField("monsterSprite", monsterRenderer);

                var playerModel = new Game.Player.PlayerModel();
                var monsterModel = new MonsterModel();
                var playerRules = new Game.Player.PlayerRules(playerConfig, playerModel,
                    Game.Core.Telemetry.NullTelemetryScope.Instance);
                var monsterRules = new MonsterRules(monsterConfig, monsterModel,
                    new Game.Core.Simulation.RandomService(21ul), Game.Core.Telemetry.NullTelemetryScope.Instance);
                playerRules.Reset(Vector2.zero);
                monsterRules.Reset(new[] { new Vector2(10f, 0f) });
                playerRules.Step(new Game.Player.PlayerIntent(Vector2.right, false, false, false, false), 0.1f);
                Vector2 previous = playerModel.PreviousPosition;
                Vector2 current = playerModel.Position;
                Assert.That(current.x, Is.GreaterThan(previous.x));

                MethodInfo lateUpdate = typeof(EncounterSceneView).GetMethod(
                    "LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(lateUpdate, Is.Not.Null);

                view.Bind(playerModel, monsterModel, () => 0.5f);
                lateUpdate.Invoke(view, null);
                Assert.That(playerObject.transform.position.x,
                    Is.EqualTo((previous.x + current.x) * 0.5f).Within(1e-5f));
                Assert.That(playerObject.transform.position.z, Is.EqualTo(current.y).Within(1e-5f));

                view.Bind(playerModel, monsterModel);
                lateUpdate.Invoke(view, null);
                Assert.That(playerObject.transform.position.x, Is.EqualTo(current.x));
            }
            finally
            {
                view.Unbind();
                Object.DestroyImmediate(monsterConfig);
                Object.DestroyImmediate(playerConfig);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(monsterObject);
                Object.DestroyImmediate(playerObject);
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
