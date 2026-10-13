// 职责：真实帧验证标题被覆盖后的取消复位，以及暂停时仍能完成入场。
// 为什么新建：EditMode 不驱动此 MonoBehaviour 的 Awake/OnDisable，不能用它证明面板覆盖生命周期。
using System.Collections;
using System.Reflection;
using Game.Core.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public sealed class TitleEntranceMotionTests
    {
        private GameObject root;
        private RectTransform part;
        private TitleEntranceMotion motion;
        private TitleEntranceConfig config;
        private float previousTimeScale;

        [SetUp]
        public void SetUp()
        {
            previousTimeScale = Time.timeScale;
            root = new GameObject("Title", typeof(RectTransform), typeof(CanvasGroup));
            motion = root.AddComponent<TitleEntranceMotion>();
            var child = new GameObject("Mirror", typeof(RectTransform), typeof(CanvasGroup));
            child.transform.SetParent(root.transform, false); part = (RectTransform)child.transform;
            config = ScriptableObject.CreateInstance<TitleEntranceConfig>();
            var track = new TitleEntranceConfig.TrackSettings();
            Set(track, "offset", new Vector2(0f, 120f)); Set(track, "duration", 0.12f); Set(track, "fadeSeconds", 0.1f);
            Set(config, "tracks", new[] { track }); Set(motion, "config", config); Set(motion, "targets", new[] { part });
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = previousTimeScale;
            Object.Destroy(root); Object.Destroy(config);
        }

        [UnityTest]
        public IEnumerator Disable_DuringEntrance_RestoresWithoutReplayingWhenCoveredPanelReturns()
        {
            yield return null;
            motion.Begin(); root.SetActive(false);
            Assert.That(motion.IsRunning, Is.False);
            Assert.That(part.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(part.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f));
            Assert.That(root.GetComponent<CanvasGroup>().interactable, Is.True);
            root.SetActive(true); motion.Begin(); yield return null;
            Assert.That(motion.IsRunning, Is.False);
            Assert.That(part.anchoredPosition, Is.EqualTo(Vector2.zero));
        }

        [UnityTest]
        public IEnumerator Begin_WithPausedWorld_CompletesAndRestoresInteraction()
        {
            yield return null;
            Time.timeScale = 0f;
            motion.Begin(); yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(motion.IsRunning, Is.False);
            Assert.That(part.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(root.GetComponent<CanvasGroup>().interactable, Is.True);
        }

        private static void Set(object instance, string name, object value) => instance.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
    }
}
