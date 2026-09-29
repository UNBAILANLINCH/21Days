// 职责：锁定演出字幕的逐字显示——开始从 0 字打、推进到全可见、整句补全（CompleteTyping）、打字中 ▼ 不出现打完才出现、速度 0 整句直出、收字幕重置。
//   面板只管「补全」这个动作；玩家要连点几下才补全（与对白同一三连点规则）由服务计数，见 PerformanceServiceWorldTests 的 PlayerTap_* 用例。
// 为什么新建：逐字状态在 PerformanceView（MonoBehaviour + TMP），要从真预制体实例化到 Canvas 下才能出 textInfo；
//   已有的 Performance EditMode 测试都是纯逻辑 / 舞台，放不进去，按「被测类 + Tests」单独成文件。
using System.Threading;
using Game.Performance;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Performance
{
    public sealed class PerformanceViewTypingTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/UI/PerformanceView.prefab";
        private const string Line = "你好，旅人。";

        private GameObject canvasObject;
        private PerformanceView view;
        private TMP_Text body;
        private GameObject holdPrompt;

        [SetUp]
        public void SetUp()
        {
            canvasObject = new GameObject("PerformanceViewTypingTests_Canvas", typeof(RectTransform), typeof(Canvas));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, "找不到演出面板预制体：" + PrefabPath);
            GameObject instance = Object.Instantiate(prefab, canvasObject.transform, false);
            view = instance.GetComponent<PerformanceView>();
            Assert.That(view, Is.Not.Null, "预制体根上没有 PerformanceView");
            var serialized = new SerializedObject(view);
            body = (TMP_Text)serialized.FindProperty("body").objectReferenceValue;
            holdPrompt = ((TMP_Text)serialized.FindProperty("holdPrompt").objectReferenceValue).gameObject;
        }

        [TearDown]
        public void TearDown()
        {
            if (view != null) Object.DestroyImmediate(view.gameObject);
            if (canvasObject != null) Object.DestroyImmediate(canvasObject);
        }

        // 黑场时长给 0：不起 LitMotion 动画，EditMode 下只测字幕。
        private void Open(float charactersPerSecond)
        {
            var policy = new PerformancePolicy(true, 1f, true, true);
            var args = new PerformanceViewArgs(policy, "长按 Ctrl", 0f, "▼", charactersPerSecond, 0.12f,
                "，。！？…；：、,.!?");
            view.OnOpenAsync(args, CancellationToken.None).GetAwaiter().GetResult();
        }

        private int VisibleLength()
        {
            body.ForceMeshUpdate();
            return body.textInfo.characterCount;
        }

        [Test]
        public void ShowSubtitle_WithSpeed_StartsTypingFromZero()
        {
            Open(35f);
            view.ShowSubtitle("阿米娅", Line, null, PerformanceAvatarSide.Left);
            Assert.That(view.IsTyping, Is.True);
            Assert.That(body.maxVisibleCharacters, Is.EqualTo(0));
        }

        [Test]
        public void TickTyping_WithLargeDelta_RevealsAllAndStops()
        {
            Open(35f);
            view.ShowSubtitle("阿米娅", Line, null, PerformanceAvatarSide.Left);
            view.TickTyping(10f);
            Assert.That(view.IsTyping, Is.False);
            Assert.That(body.maxVisibleCharacters, Is.GreaterThanOrEqualTo(VisibleLength()));
        }

        [Test]
        public void TickTyping_WithSmallDelta_RevealsPartially()
        {
            Open(35f);
            view.ShowSubtitle("阿米娅", Line, null, PerformanceAvatarSide.Left);
            view.TickTyping(1.5f / 35f);
            Assert.That(view.IsTyping, Is.True);
            Assert.That(body.maxVisibleCharacters, Is.EqualTo(1));
        }

        [Test]
        public void CompleteTyping_WhileTyping_RevealsAllImmediately()
        {
            Open(35f);
            view.ShowSubtitle("阿米娅", Line, null, PerformanceAvatarSide.Left);
            view.CompleteTyping();
            Assert.That(view.IsTyping, Is.False);
            Assert.That(body.maxVisibleCharacters, Is.GreaterThanOrEqualTo(VisibleLength()));
        }

        [Test]
        public void SetHoldPromptVisible_WhileTyping_ShowsOnlyAfterTypingFinishes()
        {
            Open(35f);
            view.ShowSubtitle("阿米娅", Line, null, PerformanceAvatarSide.Left);
            view.SetHoldPromptVisible(true);
            Assert.That(holdPrompt.activeSelf, Is.False, "打字中 ▼ 不出现");
            view.TickTyping(10f);
            Assert.That(holdPrompt.activeSelf, Is.True, "打完后 ▼ 出现");
            view.SetHoldPromptVisible(false);
            Assert.That(holdPrompt.activeSelf, Is.False);
        }

        [Test]
        public void SetHoldPromptVisible_WhenTypingCompleted_ShowsImmediately()
        {
            Open(35f);
            view.ShowSubtitle("阿米娅", Line, null, PerformanceAvatarSide.Left);
            view.SetHoldPromptVisible(true);
            view.CompleteTyping();
            Assert.That(holdPrompt.activeSelf, Is.True);
        }

        [Test]
        public void ShowSubtitle_WithZeroSpeed_ShowsWholeLineWithoutTyping()
        {
            Open(0f);
            view.ShowSubtitle("阿米娅", Line, null, PerformanceAvatarSide.Left);
            Assert.That(view.IsTyping, Is.False);
            Assert.That(body.maxVisibleCharacters, Is.GreaterThanOrEqualTo(VisibleLength()));
            view.SetHoldPromptVisible(true);
            Assert.That(holdPrompt.activeSelf, Is.True);
        }

        [Test]
        public void HideSubtitle_WhileTyping_ResetsTypingState()
        {
            Open(35f);
            view.ShowSubtitle("阿米娅", Line, null, PerformanceAvatarSide.Left);
            view.TickTyping(1.5f / 35f);
            view.HideSubtitle();
            Assert.That(view.IsTyping, Is.False);
            Assert.That(body.maxVisibleCharacters, Is.GreaterThanOrEqualTo(VisibleLength()), "收字幕后不再限字数");
            view.TickTyping(10f);
            Assert.That(view.IsTyping, Is.False);
        }
    }
}
