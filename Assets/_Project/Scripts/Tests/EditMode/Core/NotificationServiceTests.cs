// 职责：钉住 NotificationService 的两路分离——Show 走队列卡片、ShowCornerHint 走右下角小字且不进队列；
//   以及角落小字的空串忽略、重复调用以最后一次为准、只出小字时不顶一张空卡片、服务释放后不再显示。
// 为什么新建：NotificationQueueTests 只测纯队列；「保存提示不再挤占玩法通知队列」是 roadmap E1 记下的契约，
//   必须有 EditMode 守着，否则改动后容易悄悄回到单队列（回放里只表现为「获得物资」晚出现几秒，看不出来）。
//
// 不给生产代码开接缝：用真的 NotificationView（AddComponent + SerializedObject 接线）和假 IUIService。
// 动画时长置 0，淡入淡出同步完成；一条用例同步跑完、中间不过帧，
// 所以队列推进（UniTask.Yield）与小字计时（UniTask.Delay）都停在第一次 await 上，用例正好观察「刚调完」的状态。

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Core.UI.Views;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests.EditMode.Core
{
    /// <summary><see cref="NotificationService"/> 的 EditMode 测试，视图是真的 <see cref="NotificationView"/>。</summary>
    public sealed class NotificationServiceTests
    {
        private const string RewardTitle = "获得物资";
        private const string RewardBody = "破旧信笺 ×1";
        private const string SavedTitle = "已保存";

        private GameObject root;
        private NotificationView view;
        private RectTransform card;
        private CanvasGroup cardGroup;
        private TMP_Text titleLabel;
        private TMP_Text bodyLabel;
        private CanvasGroup cornerGroup;
        private TMP_Text cornerLabel;
        private UIConfig config;
        private FakeUIService ui;
        private NotificationService service;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("NotificationView", typeof(RectTransform));
            view = root.AddComponent<NotificationView>();

            card = new GameObject("Card", typeof(RectTransform)).GetComponent<RectTransform>();
            card.SetParent(root.transform, false);
            cardGroup = card.gameObject.AddComponent<CanvasGroup>();
            titleLabel = AddText("Title", card);
            bodyLabel = AddText("Body", card);

            var corner = new GameObject("Corner", typeof(RectTransform));
            corner.transform.SetParent(root.transform, false);
            cornerGroup = corner.AddComponent<CanvasGroup>();
            cornerLabel = AddText("CornerLabel", corner.transform);

            var serialized = new SerializedObject(view);
            serialized.FindProperty("card").objectReferenceValue = card;
            serialized.FindProperty("cardGroup").objectReferenceValue = cardGroup;
            serialized.FindProperty("titleLabel").objectReferenceValue = titleLabel;
            serialized.FindProperty("bodyLabel").objectReferenceValue = bodyLabel;
            serialized.FindProperty("cornerGroup").objectReferenceValue = cornerGroup;
            serialized.FindProperty("cornerLabel").objectReferenceValue = cornerLabel;
            serialized.FindProperty("cardSeconds").floatValue = 0f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            config = ScriptableObject.CreateInstance<UIConfig>();
            ui = new FakeUIService(view);
            service = new NotificationService(ui, config, new FixedClock());
            view.OnOpenAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            // 先释放服务：把挂着的队列推进与小字计时取消掉，免得之后的编辑器帧去写已销毁的对象。
            service.Dispose();
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void ShowCornerHint_WhileCardShowing_ShowsBothWithoutQueueing()
        {
            service.Show(RewardTitle, RewardBody);
            Assert.That(view.IsCardShown, Is.True, "队列里的奖励卡片先上屏");

            service.ShowCornerHint(SavedTitle);

            Assert.That(view.IsCornerShown, Is.True, "角落小字立刻显示，不排在卡片后面");
            Assert.That(cornerLabel.text, Is.EqualTo(SavedTitle));
            Assert.That(cornerGroup.alpha, Is.EqualTo(1f));
            Assert.That(view.IsCardShown, Is.True, "卡片照旧在屏上，没被小字顶掉");
            Assert.That(titleLabel.text, Is.EqualTo(RewardTitle));
            Assert.That(bodyLabel.text, Is.EqualTo(RewardBody));
        }

        [Test]
        public void Show_WhileCornerHintShowing_StillShowsCard()
        {
            service.ShowCornerHint(SavedTitle);

            service.Show(RewardTitle, RewardBody);

            Assert.That(view.IsCardShown, Is.True, "小字在屏上不影响卡片队列出队");
            Assert.That(titleLabel.text, Is.EqualTo(RewardTitle));
            Assert.That(view.IsCornerShown, Is.True, "小字照旧显示");
        }

        [Test]
        public void ShowCornerHint_Repeated_KeepsLastText()
        {
            service.ShowCornerHint("已保存");

            service.ShowCornerHint("已保存（槽 2）");

            Assert.That(cornerLabel.text, Is.EqualTo("已保存（槽 2）"), "重复调用以最后一次为准，不叠成两条");
            Assert.That(view.IsCornerShown, Is.True);
        }

        [Test]
        public void ShowCornerHint_WithoutAnyCard_ShowsOnlyCorner()
        {
            service.ShowCornerHint(SavedTitle);

            Assert.That(ui.OpenCount, Is.EqualTo(1), "第一次小字自己把常驻视图开出来");
            Assert.That(view.IsCornerShown, Is.True);
            Assert.That(view.IsCardShown, Is.False, "只出小字，不顶一张没有内容的卡片");
        }

        [Test]
        public void ShowCornerHint_EmptyText_DoesNothing()
        {
            service.ShowCornerHint(string.Empty);
            service.ShowCornerHint(null);

            Assert.That(view.IsCornerShown, Is.False, "空串忽略");
            Assert.That(ui.OpenCount, Is.Zero, "空串连面板都不开");
        }

        [Test]
        public void Show_LeavesCornerAlone()
        {
            service.Show(RewardTitle, RewardBody);

            Assert.That(view.IsCornerShown, Is.False, "队列卡片不碰角落小字");
        }

        [Test]
        public void ShowCornerHint_AfterDispose_DoesNothing()
        {
            service.Dispose();

            service.ShowCornerHint(SavedTitle);

            Assert.That(view.IsCornerShown, Is.False, "服务释放后不再显示");
            Assert.That(ui.OpenCount, Is.Zero);
        }

        private static TMP_Text AddText(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<TextMeshProUGUI>();
        }

        /// <summary>固定时钟：本文件不需要时间推进（队列与小字计时都停在 await 上）。</summary>
        private sealed class FixedClock : IClock
        {
            public DateTime UtcNow => new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
            public float GameTime => 0f;
            public float UnscaledTime => 0f;
            public float DeltaTime => 0f;
            public float UnscaledDeltaTime => 0f;
        }

        /// <summary>
        /// 只认 <see cref="NotificationView"/> 的假面板服务。打开时同 UIService 先走 <see cref="UIView.OnOpenAsync"/>
        /// （NotificationView 在这里把自己摆成隐藏态）。
        /// </summary>
        private sealed class FakeUIService : IUIService
        {
            private readonly NotificationView view;

            public FakeUIService(NotificationView view) => this.view = view;

            /// <summary>被打开过几次（含已开着时的重复 OpenAsync）。</summary>
            public int OpenCount { get; private set; }

            public async UniTask<T> OpenAsync<T>(object arg = null, CancellationToken ct = default) where T : UIView
            {
                OpenCount++;
                await view.OnOpenAsync(arg, ct);
                return (T)(object)view;
            }

            public UniTask CloseAsync(UIView target, CancellationToken ct = default) => throw new NotSupportedException();

            public UniTask CloseTopAsync(CancellationToken ct = default) => throw new NotSupportedException();

            public T Get<T>() where T : UIView => null;

            public void SetLayerVisible(UILayer layer, bool visible) => throw new NotSupportedException();

            public bool IsLayerVisible(UILayer layer) => true;
        }
    }
}
