// 职责：验证标题入场被主题切换或面板覆盖打断时，部件复位且交还交互。
// 为什么新建：现有标题测试只覆盖换图；时间轴的取消与坐标恢复需要独立的生命周期检查。
using System;
using System.Reflection;
using Game.Core.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests.EditMode.Core
{
    public sealed class TitleEntranceMotionTests
    {
        private GameObject root;
        private RectTransform part;
        private TitleEntranceMotion motion;
        private TitleEntranceConfig config;
        private Vector2 rest;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Title", typeof(RectTransform), typeof(CanvasGroup));
            motion = root.AddComponent<TitleEntranceMotion>();
            var child = new GameObject("Mirror", typeof(RectTransform), typeof(CanvasGroup));
            child.transform.SetParent(root.transform, false);
            part = (RectTransform)child.transform;
            rest = new Vector2(20f, 30f); part.anchoredPosition = rest;
            config = ScriptableObject.CreateInstance<TitleEntranceConfig>();
            var track = new TitleEntranceConfig.TrackSettings();
            Set(track, "offset", new Vector2(0f, 120f));
            Set(track, "scale", 0.8f); Set(track, "rotation", -30f);
            Set(config, "tracks", new[] { track });
            Set(motion, "config", config); Set(motion, "targets", new[] { part });
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void SetAlternate_DuringEntrance_CancelsAndRestoresRestPoseAndInteraction()
        {
            motion.Begin();
            Assert.That(motion.IsRunning, Is.True);
            Assert.That(part.anchoredPosition, Is.EqualTo(rest + new Vector2(0f, 120f)));
            Assert.That(root.GetComponent<CanvasGroup>().interactable, Is.False);
            motion.SetAlternate(true);
            AssertRestored();
        }

        [Test]
        public void VerticalUnroll_KeepsWidthAndTopFixedAndRestoresHeightWhenCanceled()
        {
            part.pivot = new Vector2(0.5f, 1f);
            part.sizeDelta = new Vector2(200f, 400f);
            part.gameObject.AddComponent<RectMask2D>();
            var track = config.Tracks[0];
            Set(track, "unrollVertical", true);
            Set(track, "scale", 0.06f); Set(track, "offset", Vector2.zero); Set(track, "rotation", 0f);
            motion.Begin();
            Assert.That(part.sizeDelta.x, Is.EqualTo(200f));
            Assert.That(part.sizeDelta.y, Is.EqualTo(24f).Within(0.01f));
            Assert.That(part.localScale, Is.EqualTo(Vector3.one));
            Assert.That(part.anchoredPosition, Is.EqualTo(rest), "顶部不得跟随整幅下落");
            Assert.That(part.GetComponent<RectMask2D>().padding, Is.EqualTo(Vector4.zero), "纸面遮罩不应按丝带横向揭露");
            motion.SetAlternate(true);
            Assert.That(part.sizeDelta, Is.EqualTo(new Vector2(200f, 400f)));
        }

        [Test]
        public void Begin_WithMissingTrack_RejectsBeforeDisablingInteraction()
        {
            Set(motion, "targets", Array.Empty<RectTransform>());
            Assert.Throws<InvalidOperationException>(() => motion.Begin());
            Assert.That(root.GetComponent<CanvasGroup>().interactable, Is.True);
        }

        private void AssertRestored()
        {
            Assert.That(motion.IsRunning, Is.False);
            Assert.That(part.anchoredPosition, Is.EqualTo(rest));
            Assert.That(part.localScale, Is.EqualTo(Vector3.one));
            Assert.That(Quaternion.Angle(part.localRotation, Quaternion.identity), Is.LessThan(0.01f));
            Assert.That(part.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f));
            Assert.That(root.GetComponent<CanvasGroup>().interactable, Is.True);
        }

        private static void Set(object instance, string name, object value) => instance.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
    }
}
