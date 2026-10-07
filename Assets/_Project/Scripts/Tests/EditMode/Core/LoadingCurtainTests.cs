// 职责：钉住 LoadingCurtain 的相位规则——首次开面板期间被揭幕不许再淡入、落幕 / 揭幕交叉时谁后到听谁的、
//   落幕被取消后还能揭开、揭幕抛异常后 IsCovered 必须回 false。这几条错了就是永久黑屏或暂停菜单永远打不开。
// 为什么新建：GameFlowTests 用的是假黑幕，测不到真实现的相位；UIServiceTests 测的是面板服务。
//   扩展哪个都会让那个类同时管两套前提，所以单独一个对应 LoadingCurtain 的测试类。
//
// 不给生产代码开接缝：用真的 LoadingView（AddComponent + SerializedObject 接线）和假 IUIService。
// 淡入淡出交给 LitMotion，在编辑器模式下由 EditorApplication.update 驱动；一条用例同步跑完、中间不过帧，
// 所以时长大于 0 的淡入淡出会一直挂着——正好拿来制造「落幕途中 / 揭幕途中」的窗口；
// 时长为 0 时整条链路同步完成。掐断挂着的淡入淡出时，被掐的那次 await 同步正常结束（cancelAwaitOnMotionCanceled = false）。

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
using Game.Core.UI.Views;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests.EditMode.Core
{
    /// <summary><see cref="LoadingCurtain"/> 的 EditMode 测试，视图是真的 <see cref="LoadingView"/>。</summary>
    public sealed class LoadingCurtainTests
    {
        /// <summary>大于 0 的淡入淡出时长：用例里不过帧，这么长的动画一定还挂着。</summary>
        private const float PendingFadeSeconds = 0.25f;

        private GameObject root;
        private LoadingView view;
        private GameObject background;
        private UIConfig config;
        private FakeUIService ui;
        private LoadingCurtain curtain;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("LoadingView", typeof(RectTransform));
            view = root.AddComponent<LoadingView>();
            background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(root.transform, false);
            var hint = new GameObject("Hint", typeof(RectTransform), typeof(CanvasGroup));
            hint.transform.SetParent(root.transform, false);
            var dot = new GameObject("Dot0", typeof(RectTransform), typeof(Image));
            dot.transform.SetParent(hint.transform, false);

            var serialized = new SerializedObject(view);
            serialized.FindProperty("background").objectReferenceValue = background.transform;
            serialized.FindProperty("hintGroup").objectReferenceValue = hint.GetComponent<CanvasGroup>();
            SerializedProperty dots = serialized.FindProperty("hintDots");
            dots.arraySize = 1;
            dots.GetArrayElementAtIndex(0).objectReferenceValue = dot.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            config = ScriptableObject.CreateInstance<UIConfig>();
            SetFadeSeconds(0f);
            ui = new FakeUIService(view);
            curtain = new LoadingCurtain(ui, config);
        }

        [TearDown]
        public void TearDown()
        {
            // 编辑器模式下 LitMotion 的销毁联动（OnDestroy）不会跑：先掐掉挂着的动画，免得之后的编辑器帧去写已销毁的对象。
            if (view != null) view.HideImmediate();
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void RevealAsync_WhileFirstOpenPending_ViewStaysHiddenAfterOpen()
        {
            ui.HoldOpen = true;

            UniTask cover = curtain.CoverAsync(CancellationToken.None);
            Assert.That(curtain.IsCovered, Is.True, "落幕一开始就算在盖（面板还在异步打开）");

            UniTask reveal = curtain.RevealAsync(CancellationToken.None);
            Assert.That(reveal.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(curtain.IsCovered, Is.False);

            ui.ReleaseOpen();

            Assert.That(cover.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(curtain.IsCovered, Is.False, "面板开出来时已经揭过幕：不许再淡入");
            AssertViewHidden("面板开出来时已经揭过幕：视图保持隐藏，否则就是永久黑屏");

            // 之后照常可用。
            curtain.CoverAsync(CancellationToken.None).Forget();
            AssertViewCovering();
            curtain.RevealAsync(CancellationToken.None).Forget();
            AssertViewHidden("再落幕、再揭幕之后");
        }

        [Test]
        public void RevealAsync_WhileCoverFading_RevealWins()
        {
            SetFadeSeconds(PendingFadeSeconds);

            UniTask cover = curtain.CoverAsync(CancellationToken.None);
            Assert.That(cover.Status, Is.EqualTo(UniTaskStatus.Pending), "淡入还挂着");

            UniTask reveal = curtain.RevealAsync(CancellationToken.None);

            Assert.That(cover.Status, Is.EqualTo(UniTaskStatus.Succeeded), "被揭幕掐断的那次落幕正常结束，不抛取消");
            Assert.That(reveal.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(curtain.IsCovered, Is.False);
            AssertViewHidden("落幕途中揭幕：以揭幕为准");
        }

        [Test]
        public void CoverAsync_WhileRevealFading_CoverWins()
        {
            curtain.CoverAsync(CancellationToken.None).Forget();
            AssertViewCovering();

            SetFadeSeconds(PendingFadeSeconds);
            UniTask reveal = curtain.RevealAsync(CancellationToken.None);
            Assert.That(reveal.Status, Is.EqualTo(UniTaskStatus.Pending), "淡出还挂着");
            Assert.That(curtain.IsCovered, Is.True, "淡出途中仍算在盖");

            UniTask cover = curtain.CoverAsync(CancellationToken.None);

            Assert.That(reveal.Status, Is.EqualTo(UniTaskStatus.Succeeded), "被落幕掐断的那次揭幕正常结束，不硬收黑幕");
            Assert.That(cover.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(curtain.IsCovered, Is.True);
            AssertViewCovering();
        }

        [Test]
        public void RevealAsync_AfterCoverCanceled_EndsUncovered()
        {
            SetFadeSeconds(PendingFadeSeconds);
            using (var cts = new CancellationTokenSource())
            {
                UniTask cover = curtain.CoverAsync(cts.Token);
                cts.Cancel();
                Assert.That(cover.Status, Is.EqualTo(UniTaskStatus.Canceled));
            }

            Assert.That(curtain.IsCovered, Is.True, "落幕被取消时黑幕可能停在半透明，交给调用方揭幕");

            UniTask reveal = curtain.RevealAsync(CancellationToken.None);

            Assert.That(reveal.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(curtain.IsCovered, Is.False);
            AssertViewHidden("落幕被取消后再揭幕");
        }

        [Test]
        public void RevealAsync_WhenFadeThrows_EndsUncoveredAndHidden()
        {
            curtain.CoverAsync(CancellationToken.None).Forget();
            AssertViewCovering();

            // 揭幕的淡出抛异常：用已取消的令牌，淡出一建出来就抛 OperationCanceledException。
            // LoadingCurtain 的收尾不分异常类型（finally），所以这条代表「揭幕途中出任何错」。
            SetFadeSeconds(PendingFadeSeconds);
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                UniTask reveal = curtain.RevealAsync(cts.Token);
                Assert.Catch<OperationCanceledException>(() => reveal.GetAwaiter().GetResult(), "异常照抛给调用方");
            }

            Assert.That(curtain.IsCovered, Is.False, "揭幕出错也必须回到没在盖，否则暂停菜单永远打不开");
            AssertViewHidden("揭幕出错时硬收黑幕");
        }

        private void SetFadeSeconds(float seconds)
        {
            var serialized = new SerializedObject(config);
            serialized.FindProperty("loadingFadeSeconds").floatValue = seconds;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private void AssertViewHidden(string because)
        {
            var group = root.GetComponent<CanvasGroup>();
            Assert.That(background.activeSelf, Is.False, because + "：黑底应停用");
            Assert.That(group.alpha, Is.EqualTo(0f), because + "：整块透明");
            Assert.That(group.blocksRaycasts, Is.False, because + "：不挡点击");
        }

        private void AssertViewCovering()
        {
            var group = root.GetComponent<CanvasGroup>();
            Assert.That(background.activeSelf, Is.True, "黑底应激活");
            Assert.That(group.alpha, Is.EqualTo(1f), "全黑");
            Assert.That(group.blocksRaycasts, Is.True, "挡点击");
        }

        /// <summary>
        /// 只认 <see cref="LoadingView"/> 的假面板服务。打开时同 UIService 先走 <see cref="UIView.OnOpenAsync"/>
        /// （LoadingView 在这里把自己摆成隐藏态）；可以让打开挂起，制造「首次打开还在异步实例化」的窗口。
        /// </summary>
        private sealed class FakeUIService : IUIService
        {
            private readonly LoadingView view;
            private UniTaskCompletionSource openGate;

            public FakeUIService(LoadingView view) => this.view = view;

            /// <summary>为 true 时 OpenAsync 挂起，等 <see cref="ReleaseOpen"/>。</summary>
            public bool HoldOpen { get; set; }

            public async UniTask<T> OpenAsync<T>(object arg = null, CancellationToken ct = default) where T : UIView
            {
                if (HoldOpen)
                {
                    openGate = new UniTaskCompletionSource();
                    await openGate.Task;
                }

                await view.OnOpenAsync(arg, ct);
                return (T)(object)view;
            }

            public void ReleaseOpen() => openGate.TrySetResult();

            public UniTask CloseAsync(UIView target, CancellationToken ct = default) => throw new NotSupportedException();

            public UniTask CloseTopAsync(CancellationToken ct = default) => throw new NotSupportedException();

            public T Get<T>() where T : UIView => null;

            public void SetLayerVisible(UILayer layer, bool visible) => throw new NotSupportedException();

            public bool IsLayerVisible(UILayer layer) => true;
        }
    }
}
