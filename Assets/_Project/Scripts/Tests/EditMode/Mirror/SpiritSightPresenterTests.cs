// 职责：验证通灵视使用当前渲染主相机，切镜、禁用、销毁及释放订阅后不会沿用旧朝向。
// 为什么新建：现有 SpiritSightRulesTests 只验证纯规则，无法覆盖呈现器的渲染回调与生命周期；一个被测类一个测试类。
using System.Collections.Generic;
using System.Reflection;
using Game.Mirror;
using Game.Monster;
using Game.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Game.Tests.EditMode.Mirror
{
    public sealed class SpiritSightPresenterTests
    {
        private readonly List<Object> created = new List<Object>();
        private SpiritSightPresenter presenter;
        private SpriteRenderer hint;

        [SetUp]
        public void SetUp()
        {
            var config = ScriptableObject.CreateInstance<MirrorConfig>();
            created.Add(config);
            presenter = new SpiritSightPresenter(new MirrorSceneBinder(new MonsterModel(), null), config,
                new EncounterStep(null, null), new PlayerModel(), null);
            var go = new GameObject("SpiritSightTestHint");
            created.Add(go);
            hint = go.AddComponent<SpriteRenderer>();
            // 池的生成已由回放覆盖，这里只提供一个在显示中的提示，隔离相机时序。
            var pool = (List<SpriteRenderer>)typeof(SpiritSightPresenter)
                .GetField("pool", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(presenter);
            pool.Add(hint);
            typeof(SpiritSightPresenter).GetField("shownCount", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(presenter, 1);
        }

        [TearDown]
        public void TearDown()
        {
            presenter.Dispose();
            foreach (Object value in created)
            {
                if (value != null) Object.DestroyImmediate(value);
            }
            created.Clear();
        }

        [Test]
        public void Render_MainCameraChanges_UsesNewCameraWithoutWaitingForSceneEvent()
        {
            Camera first = CreateCamera(10f);
            Camera second = CreateCamera(70f);
            CameraEvents.Render(first);
            AssertRotation(first.transform.rotation);

            first.tag = "Untagged"; // 旧相机仍启用：只检查 enabled 的缓存会失效。
            CameraEvents.Render(second);
            AssertRotation(second.transform.rotation);
            CameraEvents.Render(first);
            AssertRotation(second.transform.rotation);

            Object.DestroyImmediate(second.gameObject);
            first.tag = "MainCamera";
            CameraEvents.Render(first);
            AssertRotation(first.transform.rotation);
        }

        [Test]
        public void Render_DisabledInactiveOrNonGameCamera_DoesNotChangeHints()
        {
            Camera camera = CreateCamera(30f);
            camera.enabled = false;
            CameraEvents.Render(camera);
            AssertRotation(Quaternion.identity);
            camera.enabled = true;
            camera.gameObject.SetActive(false);
            CameraEvents.Render(camera);
            AssertRotation(Quaternion.identity);
            camera.gameObject.SetActive(true);
            camera.cameraType = CameraType.SceneView;
            CameraEvents.Render(camera);
            AssertRotation(Quaternion.identity);
            Object.DestroyImmediate(camera.gameObject);
            CameraEvents.Render(camera);
            CameraEvents.Render(null);
            AssertRotation(Quaternion.identity);
        }

        [Test]
        public void Dispose_RemovesRenderingSubscription_AndIsIdempotent()
        {
            Camera camera = CreateCamera(50f);
            presenter.Dispose();
            presenter.Dispose();
            CameraEvents.Render(camera);
            AssertRotation(Quaternion.identity);
            var callback = (System.Delegate)typeof(RenderPipelineManager)
                .GetField("beginCameraRendering", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            if (callback == null) return;
            foreach (System.Delegate subscriber in callback.GetInvocationList())
                Assert.That(subscriber.Target, Is.Not.SameAs(presenter), "静态事件不能保留已释放的呈现器");
        }

        private Camera CreateCamera(float angle)
        {
            var go = new GameObject("SpiritSightTestCamera");
            created.Add(go);
            Camera camera = go.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.rotation = Quaternion.Euler(0f, angle, 0f);
            return camera;
        }

        private void AssertRotation(Quaternion expected) =>
            Assert.That(Quaternion.Angle(hint.transform.rotation, expected), Is.LessThan(0.01f));

        // 发真实 SRP 事件，验证构造订阅与 Dispose 退订，不启动渲染或改项目的 URP 设置。
        private sealed class CameraEvents : RenderPipeline
        {
            public static void Render(Camera camera) => BeginCameraRendering(default, camera);
            protected override void Render(ScriptableRenderContext context, Camera[] cameras) { }
        }
    }
}
