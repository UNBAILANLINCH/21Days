// 职责：钉住开局操作说明闸门的两条边界——面板开不出来（预制体没接好 / 地址缺失）时不许挡住开局；
//   等待被取消时面板一定要收掉（不能因为 ct 已经取消就留在屏上）。
// 为什么新建：LoadingCurtainTests 测的是切场景黑幕的相位、UIServiceTests 测的是面板服务本身，
//   扩展哪个都会让那个类同时管两套前提。
//
// 视图用真的 TutorialView（AddComponent + SerializedObject 接线），面板服务用假 IUIService——
// 与 LoadingCurtainTests 同一套做法，不给生产代码开接缝。happy path（3 秒后真按键关掉）要真帧循环与真输入设备，
// 归 Showcase / 编辑器实测，不在 EditMode 里造。

using System;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Telemetry;
using Game.Core.UI;
using Game.Core.UI.Views;
using Game.Session;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests.EditMode.Session
{
    /// <summary><see cref="NewGameTutorial"/> 的 EditMode 测试，视图是真的 <see cref="TutorialView"/>。</summary>
    public sealed class NewGameTutorialTests
    {
        private GameObject root;
        private TutorialView view;
        private UIConfig config;
        private FakeUIService ui;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("TutorialView", typeof(RectTransform));
            view = root.AddComponent<TutorialView>();
            var content = new GameObject("Content", typeof(RectTransform), typeof(Image));
            content.transform.SetParent(root.transform, false);

            var serialized = new SerializedObject(view);
            serialized.FindProperty("content").objectReferenceValue = content.GetComponent<Image>();
            // 不给 hintGroup 接线：这条用例不需要淡入，也就不会留下编辑器模式下没人收的 LitMotion 句柄。
            serialized.ApplyModifiedPropertiesWithoutUndo();

            config = ScriptableObject.CreateInstance<UIConfig>();
            SetTutorialSeconds(0f);
            ui = new FakeUIService(view);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void ShowAsync_WhenViewFailsToOpen_DoesNotThrow()
        {
            // fail-open 会打一条 Error（这是它的正常行为），得先用 LogAssert 认领，否则测试框架把这条日志判成用例失败。
            LogAssert.Expect(LogType.Error, new Regex("开局操作说明没能显示出来"));
            ui.OpenThrows = new InvalidOperationException("Addressables 地址 TutorialView 找不到预制体");

            UniTask show = new NewGameTutorial(ui, config, null, null).ShowAsync(CancellationToken.None);

            Assert.That(show.Status, Is.EqualTo(UniTaskStatus.Succeeded),
                "教程是提示不是闸门：显示失败必须照常返回，否则玩家永远开不了新游戏");
            Assert.That(ui.ClosedView, Is.Null, "面板压根没开出来，没有什么可关的");
        }

        [Test]
        public void ShowAsync_WhenCanceledWhileWaiting_ClosesViewAndRethrows()
        {
            var tutorial = new NewGameTutorial(ui, config, null, null);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            UniTask show = tutorial.ShowAsync(cts.Token);

            Assert.Catch<OperationCanceledException>(() => show.GetAwaiter().GetResult(), "取消照抛给调用方");
            Assert.That(ui.OpenedView, Is.SameAs(view), "取消前面板已经打开");
            Assert.That(ui.ClosedView, Is.SameAs(view),
                "取消时也必须收掉面板——关面板那一步若带上已取消的令牌，教程图会永久盖在屏上");
        }

        [Test]
        public void ShowAsync_WhenTelemetryDependenciesMissing_StillRuns()
        {
            // 埋点与时钟允许为 null（EditMode 直接 new 出来测）：拿不到就把 ms 记成 0，业务行为不变。
            var tutorial = new NewGameTutorial(ui, config, NullTelemetryScope.Instance, null);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            UniTask show = tutorial.ShowAsync(cts.Token);

            Assert.Catch<OperationCanceledException>(() => show.GetAwaiter().GetResult());
            Assert.That(ui.ClosedView, Is.SameAs(view));
        }

        private void SetTutorialSeconds(float seconds)
        {
            var serialized = new SerializedObject(config);
            serialized.FindProperty("tutorialSeconds").floatValue = seconds;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 只认 <see cref="TutorialView"/> 的假面板服务：打开时同 UIService 先走 <see cref="UIView.OnOpenAsync"/>，
        /// 记录开 / 关了两个什么。可以让打开直接抛异常，代表「预制体 / 地址缺失」。
        /// </summary>
        private sealed class FakeUIService : IUIService
        {
            private readonly TutorialView view;

            public FakeUIService(TutorialView view) => this.view = view;

            /// <summary>非空时 OpenAsync 抛这个异常。</summary>
            public Exception OpenThrows { get; set; }

            public TutorialView OpenedView { get; private set; }

            public TutorialView ClosedView { get; private set; }

            public async UniTask<T> OpenAsync<T>(object arg = null, CancellationToken ct = default) where T : UIView
            {
                if (OpenThrows != null)
                {
                    throw OpenThrows;
                }

                await view.OnOpenAsync(arg, ct);
                OpenedView = view;
                return (T)(object)view;
            }

            public UniTask CloseAsync(UIView target, CancellationToken ct = default)
            {
                ClosedView = target as TutorialView;
                return UniTask.CompletedTask;
            }

            public UniTask CloseTopAsync(CancellationToken ct = default) => throw new NotSupportedException();

            public T Get<T>() where T : UIView => null;

            public void SetLayerVisible(UILayer layer, bool visible) => throw new NotSupportedException();

            public bool IsLayerVisible(UILayer layer) => true;
        }
    }
}
