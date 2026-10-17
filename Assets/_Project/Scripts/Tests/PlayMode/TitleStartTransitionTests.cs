// 职责：真实帧验证转场取消复位、暂停播放，以及面板覆盖后截图释放。
// 新建原因：截屏依赖帧末和 GPU，EditMode 无法证明其生命周期；用运行时对象，不依赖正式预制体。
using System.Collections;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests.PlayMode
{
    public sealed class TitleStartTransitionTests
    {
        private GameObject root;
        private TitleStartTransition transition;
        private TitleStartConfig config;
        private Material material;
        private RawImage effect;
        private RectTransform mirror;
        private float previousTimeScale;

        [SetUp]
        public void SetUp()
        {
            previousTimeScale = Time.timeScale;
            root = new GameObject("Title", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            transition = root.AddComponent<TitleStartTransition>();
            mirror = (RectTransform)new GameObject("Mirror", typeof(RectTransform)).transform;
            mirror.SetParent(root.transform, false);
            effect = new GameObject("Effect", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            effect.transform.SetParent(root.transform, false); effect.gameObject.SetActive(false);
            config = ScriptableObject.CreateInstance<TitleStartConfig>();
            material = new Material(Shader.Find("21Days/UI/TitleWaterTransition"));
            Set(transition, "config", config); Set(transition, "mirror", mirror);
            Set(transition, "screenEffect", effect); Set(transition, "effectMaterial", material);
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = previousTimeScale;
            Object.Destroy(root); Object.Destroy(config); Object.Destroy(material);
        }

        [UnityTest]
        public IEnumerator LoadProfile_CompletesWithShortTiming_WithoutChangingNewGameTiming() => UniTask.ToCoroutine(async () =>
        {
            Set(config, "mirrorSeconds", 30f); Set(config, "waveSeconds", 30f);
            Set(config, "loadMirrorSeconds", 0.04f); Set(config, "loadWaveSeconds", 0.15f);
            using (var timeout = new CancellationTokenSource())
            {
                timeout.CancelAfterSlim(2000);
                await transition.PlayAsync(timeout.Token, true);
            }
            Assert.That(transition.IsCovered, Is.True);
            Assert.That(transition.HasCapture, Is.False);
            Assert.That(config.MirrorSeconds, Is.EqualTo(30f));
            Assert.That(config.WaveSeconds, Is.EqualTo(30f));
            transition.ResetVisuals();
            Assert.That(root.GetComponent<CanvasGroup>().interactable, Is.True);
        });

        [UnityTest]
        public IEnumerator Cancellation_DuringMirrorTurn_RestoresPoseAndInteraction() => UniTask.ToCoroutine(async () =>
        {
            using (var cancel = new CancellationTokenSource())
            {
                UniTask task = transition.PlayAsync(cancel.Token);
                await UniTask.Delay(100, ignoreTimeScale: true);
                Assert.That(root.GetComponent<CanvasGroup>().interactable, Is.False);
                Assert.That(Quaternion.Angle(mirror.localRotation, Quaternion.identity), Is.GreaterThan(0f));
                cancel.Cancel();
                Assert.That(await task.SuppressCancellationThrow(), Is.True);
                Assert.That(transition.IsRunning, Is.False);
                Assert.That(transition.HasCapture, Is.False);
                Assert.That(effect.gameObject.activeSelf, Is.False);
                Assert.That(mirror.localRotation, Is.EqualTo(Quaternion.identity));
                Assert.That(root.GetComponent<CanvasGroup>().interactable, Is.True);
            }
        });

        [UnityTest]
        public IEnumerator PausedPlayback_CompletesAndCoveredPanelReleasesCapture() => UniTask.ToCoroutine(async () =>
        {
            Set(config, "mirrorSeconds", 0.04f); Set(config, "waveSeconds", 0.15f);
            Time.timeScale = 0f;
            await transition.PlayAsync(CancellationToken.None);
            Assert.That(transition.Progress, Is.EqualTo(1f));
            Assert.That(transition.HasCapture, Is.False, "收成黑幕后立即释放截图");
            Assert.That(transition.IsCovered, Is.True);
            Assert.That(effect.texture, Is.SameAs(Texture2D.blackTexture));
            Assert.That(effect.gameObject.activeSelf, Is.True);
            Assert.That(root.GetComponent<CanvasGroup>().interactable, Is.False);
            root.SetActive(false);
            Assert.That(transition.HasCapture, Is.False);
            Assert.That(effect.texture, Is.Null);
            Assert.That(effect.gameObject.activeSelf, Is.False);
            Assert.That(mirror.localRotation, Is.EqualTo(Quaternion.identity));
            root.SetActive(true);
            Assert.That(root.GetComponent<CanvasGroup>().interactable, Is.True);
            await transition.PlayAsync(CancellationToken.None);
            transition.ResetVisuals();
            Assert.That(transition.HasCapture, Is.False, "再次播放后仍能清理截图");
        });

        [UnityTest]
        public IEnumerator Disable_DuringWaterWarp_CancelsAndReleasesCapturedFrame() => UniTask.ToCoroutine(async () =>
        {
            Set(config, "mirrorSeconds", 0.04f);
            UniTask task = transition.PlayAsync(CancellationToken.None);
            float deadline = Time.realtimeSinceStartup + 3f;
            while (!transition.HasCapture && Time.realtimeSinceStartup < deadline) await UniTask.NextFrame();
            Assert.That(transition.HasCapture, Is.True, "进入水波阶段必须实际取得截图");
            root.SetActive(false);
            Assert.That(await task.SuppressCancellationThrow(), Is.True);
            Assert.That(transition.HasCapture, Is.False);
            Assert.That(effect.texture, Is.Null);
            Assert.That(effect.gameObject.activeSelf, Is.False);
            root.SetActive(true);
            Assert.That(root.GetComponent<CanvasGroup>().interactable, Is.True);
        });

        private static void Set(object instance, string name, object value) => instance.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
    }
}
