// 职责：锁定演出面板的三个控件——「自动」标签（自动 / 自动中）与投影镜像、自动 / LOG 键位小字、按钮点击抛 OnAuto / OnHistory、
//   关面板后事件清空；每句字幕抛 OnSubtitleShown；层级上 TapArea 在 SkipRoot / AutoButton / HistoryButton 之下；
//   SkipRoot 挂 UIPointerHold 且能接射线，SkipPointerHeld 跟随按住状态。
// 为什么新建：控件在 PerformanceView（MonoBehaviour + 真预制体），与逐字显示是两个关注点；PerformanceViewTypingTests 专管字幕逐字，
//   塞进去职责说不通，按「被测类 + 关注点 + Tests」另起一个文件（同 PerformanceViewTypingTests 的分法）。
using System.Collections.Generic;
using System.Threading;
using Game.Core.UI;
using Game.Performance;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Tests.EditMode.Performance
{
    public sealed class PerformanceViewControlsTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/UI/PerformanceView.prefab";

        private GameObject canvasObject;
        private PerformanceView view;

        [SetUp]
        public void SetUp()
        {
            canvasObject = new GameObject("PerformanceViewControlsTests_Canvas", typeof(RectTransform), typeof(Canvas));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, "找不到演出面板预制体：" + PrefabPath);
            GameObject instance = Object.Instantiate(prefab, canvasObject.transform, false);
            view = instance.GetComponent<PerformanceView>();
            Assert.That(view, Is.Not.Null, "预制体根上没有 PerformanceView");
        }

        [TearDown]
        public void TearDown()
        {
            if (view != null) Object.DestroyImmediate(view.gameObject);
            if (canvasObject != null) Object.DestroyImmediate(canvasObject);
        }

        // 黑场时长给 0：不起 LitMotion 动画，EditMode 下只测控件。
        private void Open(string autoHint = "A", string historyHint = "H")
        {
            var policy = new PerformancePolicy(true, 1f, true, true);
            var args = new PerformanceViewArgs(policy, "长按 Ctrl", 0f, "▼", 35f, 0.12f, "，。", autoHint, historyHint);
            view.OnOpenAsync(args, CancellationToken.None).GetAwaiter().GetResult();
        }

        private T Field<T>(string name) where T : Object
        {
            using (var so = new SerializedObject(view))
            {
                SerializedProperty property = so.FindProperty(name);
                Assert.That(property, Is.Not.Null, "PerformanceView 没有字段 " + name);
                var value = property.objectReferenceValue as T;
                Assert.That(value, Is.Not.Null, name + " 未接线");
                return value;
            }
        }

        [Test]
        public void Open_WritesKeyHintsAndAutoOffLabel()
        {
            Open("A", "H");

            Assert.That(Field<TMP_Text>("autoHint").text, Is.EqualTo("A"));
            Assert.That(Field<TMP_Text>("historyHint").text, Is.EqualTo("H"));
            Assert.That(Field<TMP_Text>("autoLabel").text, Is.EqualTo("自动"));
            Assert.That(Field<TMP_Text>("autoLabelShadow").text, Is.EqualTo("自动"));
        }

        [Test]
        public void SetAuto_TogglesLabelAndShadow()
        {
            Open();

            view.SetAuto(true);
            Assert.That(Field<TMP_Text>("autoLabel").text, Is.EqualTo("自动中"));
            Assert.That(Field<TMP_Text>("autoLabelShadow").text, Is.EqualTo("自动中"), "投影镜像主标签");

            view.SetAuto(false);
            Assert.That(Field<TMP_Text>("autoLabel").text, Is.EqualTo("自动"));
            Assert.That(Field<TMP_Text>("autoLabelShadow").text, Is.EqualTo("自动"));
        }

        [Test]
        public void Reopen_AfterAutoOn_ResetsLabelToOff()
        {
            Open();
            view.SetAuto(true);
            view.OnCloseAsync(CancellationToken.None).GetAwaiter().GetResult();

            Open();

            Assert.That(Field<TMP_Text>("autoLabel").text, Is.EqualTo("自动"), "每段演出自动复位为关");
        }

        [Test]
        public void ButtonClicks_RaiseOnAutoAndOnHistory_UntilClosed()
        {
            Open();
            int auto = 0;
            int history = 0;
            view.OnAuto += () => auto++;
            view.OnHistory += () => history++;

            Field<Button>("autoButton").onClick.Invoke();
            Field<Button>("historyButton").onClick.Invoke();
            Assert.That(auto, Is.EqualTo(1));
            Assert.That(history, Is.EqualTo(1));

            view.OnCloseAsync(CancellationToken.None).GetAwaiter().GetResult();
            Field<Button>("autoButton").onClick.Invoke();
            Field<Button>("historyButton").onClick.Invoke();
            Assert.That(auto, Is.EqualTo(1), "关面板后退订，不再抛");
            Assert.That(history, Is.EqualTo(1));
        }

        [Test]
        public void ShowSubtitle_RaisesOnSubtitleShown_WithSpeakerAndText()
        {
            Open();
            var shown = new List<string>();
            view.OnSubtitleShown += (speaker, text) => shown.Add(speaker + "|" + text);

            view.ShowSubtitle("阿米娅", "走吧。", null, PerformanceAvatarSide.Left);
            view.ShowSubtitle(null, "风停了。", null, PerformanceAvatarSide.Left);

            Assert.That(shown, Is.EqualTo(new[] { "阿米娅|走吧。", "|风停了。" }), "旁白说话者为空串");
        }

        [Test]
        public void TapArea_IsBelowAllThreeControls()
        {
            Transform root = view.transform;
            Transform tap = root.Find("TapArea");
            Assert.That(tap, Is.Not.Null, "缺 TapArea");
            foreach (string control in new[] { "SkipRoot", "AutoButton", "HistoryButton" })
            {
                Transform node = root.Find(control);
                Assert.That(node, Is.Not.Null, "根下缺 " + control);
                Assert.That(tap.GetSiblingIndex(), Is.LessThan(node.GetSiblingIndex()),
                    $"TapArea 必须在 {control} 之下（层级更靠前），否则会吞掉 {control} 的点击");
            }
        }

        [Test]
        public void SkipRoot_HasPointerHoldAndRaycastImage_AndDrivesSkipPointerHeld()
        {
            Open();
            Transform skipRoot = view.transform.Find("SkipRoot");
            Assert.That(skipRoot, Is.Not.Null, "缺 SkipRoot");
            var hold = skipRoot.GetComponent<UIPointerHold>();
            Assert.That(hold, Is.Not.Null, "SkipRoot 要挂 UIPointerHold");
            Assert.That(Field<UIPointerHold>("skipHold"), Is.SameAs(hold), "skipHold 接的应是 SkipRoot 上那一个");
            var image = skipRoot.GetComponent<Image>();
            Assert.That(image != null && image.raycastTarget, Is.True, "SkipRoot 要有开 raycast 的 Image 才接得到指针");
            Assert.That(image.color.a, Is.EqualTo(0f), "按住区域是透明的");

            Assert.That(view.SkipPointerHeld, Is.False);
            hold.OnPointerDown(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
            Assert.That(view.SkipPointerHeld, Is.True);
            hold.OnPointerUp(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
            Assert.That(view.SkipPointerHeld, Is.False);
        }
    }
}
