// 职责：钉住 UIService 的三条容易回归的粘合规则——打开失败要清干净、同类型并发打开只开一份、
//   关一个不是自己开的面板要容错。UIStack 只覆盖纯栈规则，这三条都在 UIService 里。
// 为什么新建：UIStackTests 测的是 UIStack（不碰 Unity 对象），把要建 GameObject 的用例塞进去
//   会让那个类同时管两套前提；扩展它说不通，只能新建一个对应 UIService 的测试类。

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Events;
using Game.Core.Input;
using Game.Core.UI;
using MessagePipe;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests.EditMode.Core
{
    /// <summary>
    /// UIService 的 EditMode 测试。
    /// <para>
    /// **故意不调 InitializeAsync**：那一步要建四层 Canvas 和 EventSystem（还要一个初始化过的
    /// InputService），在编辑器模式下既慢又和本文件要验的三条规则无关。没初始化时 UIService
    /// 会记一条「面板会挂在场景根上」的 Warn 然后照常走完开关流程——正是我们要测的那条路径，
    /// 所以不需要为了测试给生产代码开任何后门。
    /// </para>
    /// <para>
    /// 资源服务用假实现（<see cref="FakeAssetService"/>），过渡时长为 0（UIConfig 传 null），
    /// 所以整条链路同步完成，断言可以直接做，不需要等帧。
    /// </para>
    /// </summary>
    public sealed class UIServiceTests
    {
        private FakeAssetService assets;
        private FakeHudPublisher hudChanged;
        private UIService service;

        [SetUp]
        public void SetUp()
        {
            assets = new FakeAssetService();
            assets.Register<ThrowingView>();
            assets.Register<PlainView>();
            assets.Register<HudView>();
            assets.Register<ImmersiveHudView>();
            assets.Register<PopupView>();
            assets.Register<LockedView>();
            assets.Register<PendingTransitionView>();

            // InputService 只有在 InitializeAsync 之后才会创建 GameInput；这里只是给构造函数一个非空依赖。
            // 埋点两个参数传 null：UIService 会换成空实现，开关面板的行为和接了埋点时完全一样，
            // 本文件要验的三条规则也就不受埋点影响。
            // 沉浸事件出口用计数假实现，沉浸用例断言「状态变化只发布一次」。
            hudChanged = new FakeHudPublisher();
            service = new UIService(assets, new InputService(), null, null, null, hudChanged);
        }

        [TearDown]
        public void TearDown()
        {
            service.Dispose();
            assets.DestroyRemainingInstances();
        }

        [Test]
        public void OpenAsync_WhenOnOpenAsyncThrows_ReleasesInstanceAndForgetsIt()
        {
            LogAssert.Expect(LogType.Warning, new Regex("还没初始化就要开 ThrowingView"));

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => service.OpenAsync<ThrowingView>().GetAwaiter().GetResult(),
                "OnOpenAsync 抛的异常要原样传给调用方");

            Assert.That(error.Message, Is.EqualTo(ThrowingView.Message));
            Assert.That(service.Get<ThrowingView>(), Is.Null, "打开失败的面板不能留在记账里，否则下次会当成「已开着」复用");
            Assert.That(assets.ReleaseCount, Is.EqualTo(1), "失败路径要把实例还给资源服务，不能留在场景上");
            Assert.That(assets.LiveInstanceCount, Is.EqualTo(0));
        }

        [Test]
        public void OpenAsync_CancelDuringTransition_RollsBackStackAndInstance()
        {
            LogAssert.Expect(LogType.Warning, new Regex("还没初始化就要开 PlainView"));
            PlainView below = service.OpenAsync<PlainView>().GetAwaiter().GetResult();
            LogAssert.Expect(LogType.Warning, new Regex("还没初始化就要开 PendingTransitionView"));
            using var cancellation = new CancellationTokenSource();
            UniTask<PendingTransitionView> opening = service.OpenAsync<PendingTransitionView>(ct: cancellation.Token);
            UniTask<PendingTransitionView> waiting = service.OpenAsync<PendingTransitionView>();
            Assert.That(waiting.Status.IsCompleted(), Is.False, "并发调用不能拿到尚在淡入的半成品");
            Assert.That(service.TopView, Is.TypeOf<PendingTransitionView>());
            Assert.That(below.gameObject.activeSelf, Is.False);

            cancellation.Cancel();
            Assert.Catch<OperationCanceledException>(() => opening.GetAwaiter().GetResult());
            Assert.Catch<OperationCanceledException>(() => waiting.GetAwaiter().GetResult());
            Assert.That(service.Get<PendingTransitionView>(), Is.Null, "淡入取消不能留下未交给调用方的面板");
            Assert.That(service.TopView, Is.SameAs(below));
            Assert.That(below.gameObject.activeSelf, Is.True);
            Assert.That(assets.ReleaseCount, Is.EqualTo(1));
            Assert.That(assets.LiveInstanceCount, Is.EqualTo(1));
        }

        [Test]
        public void OpenAsync_WhenOnOpenAsyncThrows_LeavesNoUnobservedTaskException()
        {
            // 回归：失败路径曾对「并发等待者」用的 UniTaskCompletionSource 调 TrySetException 却无人读取，
            // GC 时终结器把同一个异常当未观察异常再发布一遍，随机砸中后面某条无关用例（pitfalls.md）。
            // 这里接住 UniTaskScheduler 的发布口、就地强制 GC 与终结，确认 ThrowingView 的异常不会再冒出来。
            // Mono 是保守式 GC，对象未必本轮就被回收，所以这条只能「抓到就一定是回归」，抓不到不代表绝对干净。
            LogAssert.Expect(LogType.Warning, new Regex("还没初始化就要开 ThrowingView"));

            var unobserved = new List<Exception>();
            Action<Exception> capture = e =>
            {
                lock (unobserved) unobserved.Add(e);
            };
            bool dispatchToMainThread = UniTaskScheduler.DispatchUnityMainThread;

            // 终结器线程上发布时默认会投递回主线程（下一帧才执行），关掉投递才能在本用例内同步接住。
            UniTaskScheduler.DispatchUnityMainThread = false;
            UniTaskScheduler.UnobservedTaskException += capture;
            try
            {
                Assert.Throws<InvalidOperationException>(
                    () => service.OpenAsync<ThrowingView>().GetAwaiter().GetResult());

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            finally
            {
                UniTaskScheduler.UnobservedTaskException -= capture;
                UniTaskScheduler.DispatchUnityMainThread = dispatchToMainThread;
            }

            lock (unobserved)
            {
                Assert.That(unobserved.FindAll(e => e.Message == ThrowingView.Message), Is.Empty,
                    "打开失败的异常已经抛给调用方，不能再以「未观察异常」的形式延迟发布一遍");
            }
        }

        [Test]
        public void OpenAsync_WhenCalledTwiceConcurrently_ReturnsTheSameInstance()
        {
            LogAssert.Expect(LogType.Warning, new Regex("还没初始化就要开 PlainView"));

            // 扣住实例化，制造「第一次还没开完、第二次就进来了」的窗口（连点按钮就是这个形状）。
            assets.HoldNextInstantiate();
            UniTask<PlainView> first = service.OpenAsync<PlainView>();
            UniTask<PlainView> second = service.OpenAsync<PlainView>();

            assets.ReleaseHeldInstantiate();

            PlainView firstView = first.GetAwaiter().GetResult();
            PlainView secondView = second.GetAwaiter().GetResult();

            Assert.That(assets.InstantiateCount, Is.EqualTo(1), "并发打开同类型只该实例化一份");
            Assert.That(secondView, Is.SameAs(firstView), "后来者要拿到第一次打开的那个实例");
            Assert.That(service.Get<PlainView>(), Is.SameAs(firstView));
        }

        [Test]
        public void CloseAsync_WhenViewWasNotOpenedByService_WarnsAndDoesNothing()
        {
            var foreign = new GameObject("Foreign", typeof(PlainView));
            assets.Track(foreign);
            var view = foreign.GetComponent<PlainView>();

            LogAssert.Expect(LogType.Warning, new Regex("不是 UIService 打开的"));

            Assert.DoesNotThrow(() => service.CloseAsync(view).GetAwaiter().GetResult(),
                "关一个别人建的面板是误用，记 Warn 忽略即可，不该把调用方炸掉");
            Assert.That(assets.ReleaseCount, Is.EqualTo(0), "不是自己开的就不能还给资源服务");
        }

        [Test]
        public void SetLayerVisible_BeforeInitialize_WarnsAndRemembers()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"SetLayerVisible\(Hud\)"));

            Assert.DoesNotThrow(() => service.SetLayerVisible(UILayer.Hud, false),
                "还没建层就切显隐是时序问题，记 Warn 即可，不该把调用方炸掉");
            Assert.That(service.IsLayerVisible(UILayer.Hud), Is.False, "读回的是 SetLayerVisible 设的值，与层建没建好无关");
            Assert.That(service.IsLayerVisible(UILayer.Popup), Is.True, "没设过的层默认可见");
        }

        [Test]
        public void IsLayerVisible_FullScreenPanelOpenOrHudHidden_StillReportsLayerSwitchOnly()
        {
            LogAssert.Expect(LogType.Warning, new Regex("还没初始化就要开 PlainView"));
            PlainView panel = service.OpenAsync<PlainView>().GetAwaiter().GetResult();
            Assert.That(panel.IsFullScreen, Is.True, "前提：Panel 层默认是全屏面板");

            Assert.That(service.IsLayerVisible(UILayer.Hud), Is.True,
                "全屏面板盖住 Hud 是另一套机制，不能污染整层开关的读回值（否则演出 / 对白收尾会把 Hud 恢复成隐藏）");

            service.SetHudHidden(true);
            Assert.That(service.IsLayerVisible(UILayer.Hud), Is.True, "沉浸模式同样不影响整层开关的读回值");

            LogAssert.Expect(LogType.Warning, new Regex(@"SetLayerVisible\(Hud\)"));
            service.SetLayerVisible(UILayer.Hud, false);
            service.CloseAsync(panel).GetAwaiter().GetResult();
            service.SetHudHidden(false);
            Assert.That(service.IsLayerVisible(UILayer.Hud), Is.False, "关掉全屏面板、退出沉浸都不会改动 SetLayerVisible 设的值");
        }

        [Test]
        public void SetHudHidden_True_HidesHudPanelsButKeepsImmersiveOnes()
        {
            LogAssert.Expect(LogType.Warning, new Regex("还没初始化就要开 HudView"));
            LogAssert.Expect(LogType.Warning, new Regex("还没初始化就要开 ImmersiveHudView"));
            HudView hud = service.OpenAsync<HudView>().GetAwaiter().GetResult();
            ImmersiveHudView keep = service.OpenAsync<ImmersiveHudView>().GetAwaiter().GetResult();

            service.SetHudHidden(true);

            CanvasGroup hudGroup = hud.GetComponent<CanvasGroup>();
            Assert.That(service.IsHudHidden, Is.True);
            Assert.That(hudGroup.alpha, Is.EqualTo(0f), "沉浸时普通 Hud 面板要透明");
            Assert.That(hudGroup.interactable, Is.False, "沉浸时普通 Hud 面板不可交互");
            Assert.That(hudGroup.blocksRaycasts, Is.False, "沉浸时普通 Hud 面板不能挡点击");
            CanvasGroup keepGroup = keep.GetComponent<CanvasGroup>();
            Assert.That(keepGroup.alpha, Is.EqualTo(1f), "VisibleWhenHudHidden 的面板不受沉浸影响");
            Assert.That(keepGroup.blocksRaycasts, Is.True);
            Assert.That(hudChanged.Published.Count, Is.EqualTo(1), "进入沉浸发布一次事件");
            Assert.That(hudChanged.Published[0].Hidden, Is.True);
        }

        [Test]
        public void SetHudHidden_FalseAfterTrue_RestoresPanels()
        {
            LogAssert.Expect(LogType.Warning, new Regex("还没初始化就要开 HudView"));
            HudView hud = service.OpenAsync<HudView>().GetAwaiter().GetResult();

            service.SetHudHidden(true);
            service.SetHudHidden(false);

            CanvasGroup group = hud.GetComponent<CanvasGroup>();
            Assert.That(service.IsHudHidden, Is.False);
            Assert.That(group.alpha, Is.EqualTo(1f), "退出沉浸后回到完全不透明");
            Assert.That(group.interactable, Is.True);
            Assert.That(group.blocksRaycasts, Is.True);
            Assert.That(hudChanged.Published.Count, Is.EqualTo(2), "进、出各发布一次");
            Assert.That(hudChanged.Published[1].Hidden, Is.False);
        }

        [Test]
        public void SetHudHidden_SameStateTwice_PublishesOnce()
        {
            service.SetHudHidden(true);
            service.SetHudHidden(true);

            Assert.That(hudChanged.Published.Count, Is.EqualTo(1), "状态没变不重复发布");
        }

        [Test]
        public void OpenAsync_HudPanelWhileHudHidden_OpensHidden()
        {
            LogAssert.Expect(LogType.Warning, new Regex("还没初始化就要开 HudView"));
            service.SetHudHidden(true);

            HudView hud = service.OpenAsync<HudView>().GetAwaiter().GetResult();

            CanvasGroup group = hud.GetComponent<CanvasGroup>();
            Assert.That(group.alpha, Is.EqualTo(0f), "沉浸中新开的 Hud 面板也要套用隐藏");
            Assert.That(group.blocksRaycasts, Is.False);
        }

        [Test]
        public void TopView_AfterOpeningPanelWithDefaultSelected_ResolvesItsDefault()
        {
            LogAssert.Expect(LogType.Warning, new Regex("还没初始化就要开 PlainView"));
            PlainView panel = service.OpenAsync<PlainView>().GetAwaiter().GetResult();
            Button button = AttachDefaultSelected(panel);

            Assert.That(service.TopView, Is.SameAs(panel), "唯一开着的 Panel 就是栈顶");
            Assert.That(UIService.ResolveDefaultSelection(service.TopView), Is.SameAs(button.gameObject),
                "打开后 EventSystem 要选中栈顶面板的默认项");
        }

        [Test]
        public void TopView_AfterClosingPopup_FallsBackToPanelDefault()
        {
            LogAssert.Expect(LogType.Warning, new Regex("还没初始化就要开 PlainView"));
            LogAssert.Expect(LogType.Warning, new Regex("还没初始化就要开 PopupView"));
            PlainView panel = service.OpenAsync<PlainView>().GetAwaiter().GetResult();
            Button panelButton = AttachDefaultSelected(panel);
            PopupView popup = service.OpenAsync<PopupView>().GetAwaiter().GetResult();
            Button popupButton = AttachDefaultSelected(popup);

            Assert.That(service.TopView, Is.SameAs(popup), "Popup 盖在 Panel 上，栈顶先看 Popup");
            Assert.That(UIService.ResolveDefaultSelection(service.TopView), Is.SameAs(popupButton.gameObject));

            service.CloseAsync(popup).GetAwaiter().GetResult();

            Assert.That(service.TopView, Is.SameAs(panel), "关掉弹窗后栈顶回到下层面板");
            Assert.That(UIService.ResolveDefaultSelection(service.TopView), Is.SameAs(panelButton.gameObject),
                "关掉上层后要重新选中下层面板的默认项");
        }

        [Test]
        public void ResolveDefaultSelection_NoTopOrNoDefault_ReturnsNull()
        {
            LogAssert.Expect(LogType.Warning, new Regex("还没初始化就要开 PlainView"));
            Assert.That(UIService.ResolveDefaultSelection(null), Is.Null, "栈空时清空选中");

            PlainView panel = service.OpenAsync<PlainView>().GetAwaiter().GetResult();
            Assert.That(UIService.ResolveDefaultSelection(panel), Is.Null, "没拖默认项的面板清空选中，不把焦点留在旧控件上");
        }

        [Test]
        public void CancelRouter_Decide_CoversAllThreeOutcomes()
        {
            Assert.That(UICancelRouter.Decide(false, false), Is.EqualTo(UICancelRouter.Decision.NothingToClose));
            Assert.That(UICancelRouter.Decide(false, true), Is.EqualTo(UICancelRouter.Decision.NothingToClose),
                "没有栈顶时 CloseOnCancel 无意义");
            Assert.That(UICancelRouter.Decide(true, true), Is.EqualTo(UICancelRouter.Decision.Close));
            Assert.That(UICancelRouter.Decide(true, false), Is.EqualTo(UICancelRouter.Decision.Blocked),
                "栈顶不让关时既不关、也不算「没东西可关」");
        }

        [Test]
        public void CancelRouter_LockedPanelOnTop_IsNotClosed()
        {
            LogAssert.Expect(LogType.Warning, new Regex("还没初始化就要开 PlainView"));
            LogAssert.Expect(LogType.Warning, new Regex("还没初始化就要开 LockedView"));
            service.OpenAsync<PlainView>().GetAwaiter().GetResult();
            LockedView locked = service.OpenAsync<LockedView>().GetAwaiter().GetResult();

            UIView top = service.TopView;
            Assert.That(top, Is.SameAs(locked));
            Assert.That(UICancelRouter.Decide(top != null, top.CloseOnCancel), Is.EqualTo(UICancelRouter.Decision.Blocked));
            Assert.That(service.Get<LockedView>(), Is.SameAs(locked), "CloseOnCancel 为 false 的面板不被路由关掉");
        }

        [Test]
        public void CancelRouter_OnlyHudOpen_HasNothingToClose()
        {
            LogAssert.Expect(LogType.Warning, new Regex("还没初始化就要开 HudView"));
            HudView hud = service.OpenAsync<HudView>().GetAwaiter().GetResult();

            Assert.That(service.TopView, Is.Null, "Hud 层不进栈，不会成为 Esc 的目标");
            Assert.That(UICancelRouter.Decide(service.TopView != null, hud.CloseOnCancel),
                Is.EqualTo(UICancelRouter.Decision.NothingToClose));
            Assert.That(hud.CloseOnCancel, Is.False, "Hud 层默认不可被 Esc 关");
        }

        /// <summary>给面板挂一个子按钮并经序列化写进 defaultSelected（字段是 private，只走 Inspector 那条路）。</summary>
        private static Button AttachDefaultSelected(UIView view)
        {
            var buttonObject = new GameObject("Default", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(view.transform, false);
            var button = buttonObject.GetComponent<Button>();

            var serialized = new SerializedObject(view);
            serialized.FindProperty("defaultSelected").objectReferenceValue = button;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return button;
        }

        /// <summary>OnOpenAsync 直接抛异常的面板，用来覆盖「打开到一半失败」这条路径。</summary>
        private sealed class ThrowingView : UIView
        {
            /// <summary>抛出的异常消息，测试用它确认拿到的就是这一个异常。</summary>
            public const string Message = "ThrowingView 打不开";

            public override UILayer Layer => UILayer.Panel;

            public override UniTask OnOpenAsync(object arg, CancellationToken ct)
            {
                throw new InvalidOperationException(Message);
            }
        }

        /// <summary>只挂起打开过渡，取消令牌触发同步收尾，不依赖 EditMode 帧循环。</summary>
        private sealed class PendingTransitionView : UIView
        {
            public override UILayer Layer => UILayer.Panel;

            protected override async UniTask PlayOpenTransitionAsync(float seconds, CancellationToken ct)
            {
                var pending = new UniTaskCompletionSource();
                using (ct.Register(() => pending.TrySetCanceled(ct))) await pending.Task;
            }
        }

        /// <summary>什么都不做的面板，用来覆盖正常路径。</summary>
        private sealed class PlainView : UIView
        {
            public override UILayer Layer => UILayer.Panel;
        }

        /// <summary>Popup 层面板：盖在 Panel 上，栈顶先看它。</summary>
        private sealed class PopupView : UIView
        {
            public override UILayer Layer => UILayer.Popup;
        }

        /// <summary>不让 Esc 关的 Panel（标题、对白这类）。</summary>
        private sealed class LockedView : UIView
        {
            public override UILayer Layer => UILayer.Panel;
            public override bool CloseOnCancel => false;
        }

        /// <summary>普通 Hud 面板：沉浸时跟着隐藏。</summary>
        private sealed class HudView : UIView
        {
            public override UILayer Layer => UILayer.Hud;
        }

        /// <summary>沉浸中仍显示的 Hud 面板（如沉浸开关按钮）。</summary>
        private sealed class ImmersiveHudView : UIView
        {
            public override UILayer Layer => UILayer.Hud;
            public override bool VisibleWhenHudHidden => true;
        }

        /// <summary>记下每次发布的沉浸事件。</summary>
        private sealed class FakeHudPublisher : IPublisher<HudVisibilityChangedEvent>
        {
            public List<HudVisibilityChangedEvent> Published { get; } = new List<HudVisibilityChangedEvent>();

            public void Publish(HudVisibilityChangedEvent message)
            {
                Published.Add(message);
            }
        }

        /// <summary>
        /// 假资源服务：只实现 UIService 真正会用到的 <see cref="InstantiateAsync"/> 与
        /// <see cref="ReleaseInstance"/>，其余成员一律抛 <see cref="NotSupportedException"/>——
        /// 哪天 UIService 偷偷用上了别的成员，测试会当场炸而不是静默通过。
        /// </summary>
        private sealed class FakeAssetService : IAssetService
        {
            private readonly Dictionary<string, Type> prefabs = new Dictionary<string, Type>(StringComparer.Ordinal);
            private readonly List<GameObject> instances = new List<GameObject>();

            private UniTaskCompletionSource<GameObject> heldCompletion;
            private GameObject heldInstance;
            private bool holdNext;

            /// <summary>实例化过几次（并发去重失效的话这个数会变成 2）。</summary>
            public int InstantiateCount { get; private set; }

            /// <summary>归还过几次。</summary>
            public int ReleaseCount { get; private set; }

            /// <summary>还没归还、也还没销毁的实例数。</summary>
            public int LiveInstanceCount
            {
                get
                {
                    int alive = 0;
                    for (int i = 0; i < instances.Count; i++)
                    {
                        // GameObject 是 UnityEngine.Object，判空只用 != null。
                        if (instances[i] != null)
                        {
                            alive++;
                        }
                    }

                    return alive;
                }
            }

            /// <summary>登记一个「地址 = 类名」的假预制体：实例化时新建一个挂着该组件的 GameObject。</summary>
            public void Register<T>() where T : Component
            {
                prefabs[typeof(T).Name] = typeof(T);
            }

            /// <summary>让下一次实例化挂起，直到 <see cref="ReleaseHeldInstantiate"/>。</summary>
            public void HoldNextInstantiate()
            {
                holdNext = true;
            }

            /// <summary>放行被扣住的那一次实例化。</summary>
            public void ReleaseHeldInstantiate()
            {
                holdNext = false;
                UniTaskCompletionSource<GameObject> completion = heldCompletion;
                GameObject instance = heldInstance;
                heldCompletion = null;
                heldInstance = null;

                if (completion != null)
                {
                    completion.TrySetResult(instance);
                }
            }

            /// <summary>把一个不是本服务造出来的对象也纳入清理范围（测试里手搓的 GameObject）。</summary>
            public void Track(GameObject instance)
            {
                instances.Add(instance);
            }

            /// <summary>TearDown 用：把还活着的实例全部销毁。EditMode 下只能 DestroyImmediate。</summary>
            public void DestroyRemainingInstances()
            {
                for (int i = 0; i < instances.Count; i++)
                {
                    if (instances[i] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(instances[i]);
                    }
                }

                instances.Clear();
            }

            public UniTask<GameObject> InstantiateAsync(string key, Transform parent = null,
                CancellationToken ct = default)
            {
                if (!prefabs.TryGetValue(key, out Type componentType))
                {
                    throw new InvalidOperationException($"假资源服务里没登记地址 {key}，先 Register<T>()");
                }

                InstantiateCount++;
                var instance = new GameObject(key, componentType);
                instances.Add(instance);
                if (parent != null)
                {
                    instance.transform.SetParent(parent, false);
                }

                if (!holdNext)
                {
                    return UniTask.FromResult(instance);
                }

                heldCompletion = new UniTaskCompletionSource<GameObject>();
                heldInstance = instance;
                return heldCompletion.Task;
            }

            public void ReleaseInstance(GameObject instance)
            {
                ReleaseCount++;
                if (instance != null)
                {
                    instances.Remove(instance);
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }

            public UniTask<AssetHandle<T>> LoadAsync<T>(string key, CancellationToken ct = default)
                where T : UnityEngine.Object
            {
                throw new NotSupportedException("UIService 不该用 LoadAsync");
            }

            public UniTask<IReadOnlyList<AssetHandle<T>>> LoadAllAsync<T>(string label,
                CancellationToken ct = default)
                where T : UnityEngine.Object
            {
                throw new NotSupportedException("UIService 不该用 LoadAllAsync");
            }

            public UniTask<SceneHandle> LoadSceneAsync(string key, LoadSceneMode mode,
                CancellationToken ct = default)
            {
                throw new NotSupportedException("UIService 不该用 LoadSceneAsync");
            }
        }
    }
}
