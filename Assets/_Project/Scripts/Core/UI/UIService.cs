// 职责：IUIService 的唯一实现——建 UIRoot（四层 Canvas + EventSystem）、实例化面板、驱动三段生命周期、粘合 UIStack。
// 为什么新建：IUIService 是契约，实现必须分开放；栈规则已经拆到 UIStack（纯逻辑、可测），
//   这里只剩「跟 Unity 打交道」的部分，没有任何现成文件能承担这个职责。

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Boot;
using Game.Core.Events;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.Telemetry;
using MessagePipe;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Game.Core.UI
{
    /// <summary>
    /// UI 服务。启动时在代码里搭出整套 UI 根节点（不做成预制体：四层 Canvas 的参数全从 UIConfig 来，
    /// 预制体上那份会和配置打架，而且多人协作时预制体是合并冲突高发区）。
    /// <para>
    /// 层级结构：
    /// <code>
    /// UIRoot (DontDestroyOnLoad)
    ///   Canvas_Hud   (sortingOrder 0)   → SafeArea (面板挂这儿)
    ///   Canvas_Panel (sortingOrder 100) → SafeArea
    ///   Canvas_Popup (sortingOrder 200) → SafeArea
    ///   Canvas_Top   (sortingOrder 300) → SafeArea
    ///   EventSystem
    /// </code>
    /// Hud 层的 SafeArea 上额外挂一个 CanvasGroup：Panel 栈里有任一全屏面板时整层被盖住（alpha 0、不吃点击），
    /// 见 <see cref="RefreshHudCover"/>。
    /// </para>
    /// </summary>
    public sealed class UIService : IUIService, IHudVisibility, IGameService, IDisposable
    {
        /// <summary>相邻两层 Canvas 的 sortingOrder 间隔。留出空档给玩法临时插一层。</summary>
        private const int LayerSortingStep = 100;

        private readonly IAssetService assets;
        private readonly IInputService input;
        private readonly UIConfig config;
        private readonly UIStack stack = new UIStack();

        /// <summary>已打开的面板：类型 → 实例。一个类型同时只存在一个实例。</summary>
        private readonly Dictionary<Type, UIView> opened = new Dictionary<Type, UIView>();

        /// <summary>
        /// 正在打开、还没落进 <see cref="opened"/> 的面板：类型 → 这一次打开的完成源。
        /// 打开一个面板中间隔着「等 Addressables 实例化」「等 OnOpenAsync」两次 await，
        /// 这段窗口里同类型的第二次 OpenAsync 在这里排队等结果，而不是自己再开一份。
        /// </summary>
        private readonly Dictionary<Type, UniTaskCompletionSource<UIView>> opening =
            new Dictionary<Type, UniTaskCompletionSource<UIView>>();

        /// <summary>每层的内容根（挂 SafeAreaFitter 的那个 RectTransform），面板生在它下面。</summary>
        private readonly Dictionary<UILayer, Transform> layerRoots = new Dictionary<UILayer, Transform>();

        /// <summary>每层的 Canvas_&lt;layer&gt;，<see cref="SetLayerVisible"/> 切它的 enabled。</summary>
        private readonly Dictionary<UILayer, Canvas> layerCanvases = new Dictionary<UILayer, Canvas>();

        /// <summary>
        /// <see cref="SetLayerVisible"/> 设成隐藏的层（整层开关的唯一记账，<see cref="IsLayerVisible"/> 只读它）。
        /// 单独记一份而不回读 Canvas.enabled：读回值必须只反映「谁调了 SetLayerVisible」，
        /// 与全屏遮盖（<see cref="hudCoverGroup"/>）、沉浸模式（<see cref="SetHudHidden"/>）两套显隐彻底解耦，
        /// 以后哪套机制改成也动 Canvas，这里的语义都不会被污染。建层前设过的值在建层时套用。
        /// </summary>
        private readonly HashSet<UILayer> hiddenLayers = new HashSet<UILayer>();

        private GameObject root;
        private bool disposed;

        /// <summary>
        /// 自己建的那个 EventSystem（<see cref="CreateEventSystem"/>）。默认选中项只往它身上设，
        /// 不用 <c>EventSystem.current</c>：后者在多个 EventSystem 并存或切场景的瞬间可能指向别人。
        /// 没初始化（EditMode 测试）时为 null，选中逻辑整体跳过。
        /// </summary>
        private EventSystem eventSystem;

        private readonly ITelemetryScope telemetry;
        private readonly ITelemetryClock clock;

        /// <summary>沉浸模式切换的事件出口；EditMode 测试可传 null（只是不发布）。</summary>
        private readonly IPublisher<HudVisibilityChangedEvent> hudChanged;

        /// <summary>当前是否沉浸（<see cref="SetHudHidden"/>）。</summary>
        private bool hudHidden;

        /// <summary>
        /// 「被全屏面板盖住」的整层开关：挂在 Canvas_Hud/SafeArea 上的 CanvasGroup，由 <see cref="RefreshHudCover"/> 驱动。
        /// 与另外两套 Hud 显隐互不干扰、可叠加：<see cref="SetLayerVisible"/> 切的是 Canvas.enabled（演出、对白在用，记账在 <see cref="hiddenLayers"/>），
        /// 沉浸模式（<see cref="SetHudHidden"/>）动的是每个 Hud 面板自己根上的 CanvasGroup。
        /// 没初始化（EditMode 测试）时为 null，盖住逻辑整体跳过。
        /// </summary>
        private CanvasGroup hudCoverGroup;

        /// <summary><see cref="hudCoverGroup"/> 当前是否处于盖住状态，状态不变时不重复写。</summary>
        private bool hudCovered;

        /// <summary>
        /// 两个埋点参数与 <paramref name="hudChanged"/> 允许为 null（EditMode 测试里直接 new 出来的 UIService 没有容器）：
        /// 拿不到就整条埋点链路变空操作 / 沉浸切换不发布事件，开关面板的行为一个字节都不变。
        /// </summary>
        public UIService(
            IAssetService assets,
            IInputService input,
            UIConfig config,
            ITelemetryService telemetry,
            ITelemetryClock clock,
            IPublisher<HudVisibilityChangedEvent> hudChanged)
        {
            this.assets = assets ?? throw new ArgumentNullException(nameof(assets));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.config = config;
            this.clock = clock;
            this.hudChanged = hudChanged;
            this.telemetry = telemetry == null
                ? (ITelemetryScope)NullTelemetryScope.Instance
                : telemetry.Scope(TelemetryKeys.Ui);
        }

        public UniTask InitializeAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (root != null)
            {
                return UniTask.CompletedTask;
            }

            root = new GameObject("UIRoot");
            UnityEngine.Object.DontDestroyOnLoad(root);

            Vector2 reference = config == null ? new Vector2(1920f, 1080f) : config.ReferenceResolution;
            float match = config == null ? 0.5f : config.MatchWidthOrHeight;

            CreateLayer(UILayer.Hud, reference, match);
            CreateLayer(UILayer.Panel, reference, match);
            CreateLayer(UILayer.Popup, reference, match);
            CreateLayer(UILayer.Top, reference, match);
            CreateEventSystem();

            // 退出前先把面板还给资源服务。VContainer 按**注册顺序**释放，IAssetService 排在 UIService 前面，
            // 等容器释放到这里时资源服务早就销毁了，它会误报「还有实例没归还」——
            // 那条 Warn 是用来抓真泄漏的，不能让它每次退出播放模式都响一遍。
            Application.quitting += ReleaseAllViews;

            Log.Info("UIService 就绪：四层 Canvas 与 EventSystem 已建立");
            return UniTask.CompletedTask;
        }

        public async UniTask<T> OpenAsync<T>(object arg = null, CancellationToken ct = default)
            where T : UIView
        {
            ThrowIfDisposed();
            Type type = typeof(T);
            long startMs = NowMs;

            // 同类型正在打开中：等第一次的结果，返回同一个实例。
            // 必须先于 opened 检查：淡入期间已登记 view，但尚未交付；取消时排队者也须收到取消。
            if (opening.TryGetValue(type, out UniTaskCompletionSource<UIView> inflight))
            {
                // 这条路不埋：面板是上一次调用开的，那次自己会埋一条 open，这里再埋等于把同一次打开记两遍。
                return (T)await inflight.Task;
            }

            // 已经开完才复用，不能把尚在打开中的半成品交给第二个调用方。
            if (opened.TryGetValue(type, out UIView existing) && existing != null)
            {
                await existing.OnOpenAsync(arg, ct);
                TrackPanel(TelemetryKeys.UiEvents.Open, type.Name, startMs);
                return (T)existing;
            }

            var completion = new UniTaskCompletionSource<UIView>();
            opening[type] = completion;
            try
            {
                T view = await OpenNewAsync<T>(type, arg, ct);
                completion.TrySetResult(view);
                TrackPanel(TelemetryKeys.UiEvents.Open, type.Name, startMs);
                return view;
            }
            catch (OperationCanceledException e)
            {
                // 取消要按取消传给排队的人，包成普通异常会让对方的 catch (OperationCanceledException) 漏掉。
                // 取消是正常路径（状态切走、作用域销毁），不埋——埋了只会让退出播放模式满屏 E。
                completion.TrySetCanceled(e.CancellationToken);
                throw;
            }
            catch (Exception e)
            {
                completion.TrySetException(e);

                // 排队的人（若有）在 TrySetException 里已同步收到异常；没人排队时，UniTaskCompletionSource
                // 的 ExceptionHolder 终结器会在 GC 时把它当「未观察异常」再发布一遍，随机砸中别处（pitfalls.md）。
                // 异常已由下面的 throw 交给本次调用方，这里读一次结果把它标记为已观察。
                try { completion.Task.GetAwaiter().GetResult(); }
                catch (Exception) { /* 就是本次的 e，已在下方原样抛出 */ }

                // 开面板失败的头号原因是 Addressables 地址与类名对不上，所以 panel 必须写进属性；
                // ms 说明是「一上来就炸」还是「等了很久才炸」，两者查的方向完全不同。
                telemetry.TrackError(
                    TelemetryKeys.UiEvents.Open,
                    e,
                    TelemetryProps.Of(
                        (TelemetryKeys.Props.Panel, type.Name),
                        (TelemetryKeys.Props.Ms, NowMs - startMs)));
                throw;
            }
            finally
            {
                opening.Remove(type);
            }
        }

        /// <summary>
        /// 真正开一个新面板：实例化 → 挂层 → 三段生命周期的第一段 → 压栈 → 播淡入。
        /// <para>
        /// 从实例化成功到打开过渡结束整段包 try/catch：生命周期与过渡都可能失败或取消。
        /// 不清理的话字典/栈里留着一个尚未交给调用方、没人关得掉的残骸，实例还挂在场景上，
        /// 之后再 OpenAsync 同一类型会把这个半初始化的面板当成「已经开着」直接复用。
        /// </para>
        /// </summary>
        private async UniTask<T> OpenNewAsync<T>(Type type, object arg, CancellationToken ct)
            where T : UIView
        {
            Transform parent = ResolveLayerRoot(type);
            GameObject instance = await assets.InstantiateAsync(type.Name, parent, ct);
            var view = instance.GetComponent<T>();
            try
            {
                if (view == null)
                {
                    throw new InvalidOperationException(
                        $"Addressables 地址 \"{type.Name}\" 的预制体上没有 {type.Name} 组件。"
                        + "UI 预制体的地址必须等于面板类名，且根节点要挂上那个组件。");
                }

                opened[type] = view;

                // 挂到真正该去的层：ResolveLayerRoot 只能按类型猜（还没实例化就不知道 Layer），
                // 拿到实例后按它自报的 Layer 校正一次。
                Transform actual = GetLayerRoot(view.Layer);
                if (actual != null && view.transform.parent != actual)
                {
                    view.transform.SetParent(actual, false);
                }

                StretchToParent(view.transform as RectTransform);
                view.PrepareForOpen(TransitionSeconds);
                view.gameObject.SetActive(true);

                await view.OnOpenAsync(arg, ct);

                ApplyVisibility(stack.Push(view), false);

                // 全屏面板一压栈 Hud 立刻盖住，不等淡入。
                RefreshHudCover();

                // 沉浸中新开的 Hud 面板：不播淡入（淡入会把 alpha 拉回 1），直接套隐藏。
                if (hudHidden && FollowsHud(view))
                {
                    ApplyHudHidden(view, true);
                    return view;
                }

                await view.PlayOpenTransitionAsync(TransitionSeconds, ct);

                // 淡入途中进了沉浸：淡入收尾会把 alpha 写回 1，这里补一次。
                if (hudHidden && FollowsHud(view))
                {
                    ApplyHudHidden(view, true);
                }

                // 淡入完才选中：淡入途中又叠上来一个面板时，这里的栈顶已经不是它，不抢那个面板的焦点。
                if (stack.Top() == view)
                {
                    ApplySelection(view);
                }

                return view;
            }
            catch
            {
                // 打开尚未返回时调用方拿不到 view，服务必须回滚字典、栈、遮挡与实例。
                bool wasTop = view != null && stack.Top() == view;
                opened.Remove(type);
                ApplyVisibility(stack.Remove(view), true);
                RefreshHudCover();
                if (wasTop) ApplySelection(TopView);
                assets.ReleaseInstance(instance);
                throw;
            }
        }

        public async UniTask CloseAsync(UIView view, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (view == null)
            {
                TrackCloseDenied(string.Empty, "null_view");
                return;
            }

            Type type = view.GetType();
            long startMs = NowMs;
            if (!opened.TryGetValue(type, out UIView tracked) || tracked != view)
            {
                Log.Warn($"CloseAsync 收到的 {type.Name} 不是 UIService 打开的，忽略");
                TrackCloseDenied(type.Name, "not_opened_by_service");
                return;
            }

            await view.PlayCloseTransitionAsync(TransitionSeconds, ct);
            await view.OnCloseAsync(ct);

            // 只有关的是栈顶才动选中：关一个被盖在下面的面板不该把玩家在上层面板里的焦点抢走。
            bool wasTop = stack.Top() == view;

            opened.Remove(type);
            ApplyVisibility(stack.Remove(view), true);

            // 放在淡出之后：面板淡出完成、出栈了 Hud 才恢复，不会在淡出途中透出任务栏。
            RefreshHudCover();

            if (wasTop)
            {
                ApplySelection(TopView);
            }

            assets.ReleaseInstance(view.gameObject);

            // 埋在最后：此时 opened 已经减过，depth 就是「关完之后还开着几个面板」。
            TrackPanel(TelemetryKeys.UiEvents.Close, type.Name, startMs);
        }

        public UniTask CloseTopAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            var top = stack.Top() as UIView;
            return top == null ? UniTask.CompletedTask : CloseAsync(top, ct);
        }

        /// <summary>
        /// 当前栈顶面板（先 Popup 后 Panel），两条栈都空时为 null。Hud / Top 层不进栈，永远不会是它。
        /// 给 <see cref="UICancelRouter"/> 判「Esc 该不该关它」用；不放进 IUIService——玩法只需要
        /// 「关掉最上面那个」（<see cref="CloseTopAsync"/>），不该依赖栈里有谁。
        /// </summary>
        public UIView TopView => stack.Top() as UIView;

        /// <summary>
        /// 栈顶面板该让 EventSystem 选中谁：有 <see cref="UIView.DefaultSelected"/> 就是它的 GameObject，
        /// 否则 null（清空选中，避免焦点留在已经关掉 / 被盖住的控件上）。纯函数，EditMode 可测。
        /// </summary>
        public static GameObject ResolveDefaultSelection(UIView top)
        {
            // UIView / Selectable 都是 UnityEngine.Object，判空只用 == null。
            if (top == null || top.DefaultSelected == null)
            {
                return null;
            }

            return top.DefaultSelected.gameObject;
        }

        public T Get<T>() where T : UIView
        {
            if (!opened.TryGetValue(typeof(T), out UIView view) || view == null)
            {
                return null;
            }

            return (T)view;
        }

        public bool IsLayerVisible(UILayer layer)
        {
            ThrowIfDisposed();
            // 只读 SetLayerVisible 的记账，不看 Canvas.enabled / 全屏遮盖 / 沉浸；从没设过的层默认可见。
            return !hiddenLayers.Contains(layer);
        }

        public void SetLayerVisible(UILayer layer, bool visible)
        {
            ThrowIfDisposed();
            // 先记账：IsLayerVisible 读回的就是这里，与该层 Canvas 建没建好无关。
            bool changed = visible ? hiddenLayers.Remove(layer) : hiddenLayers.Add(layer);
            if (!layerCanvases.TryGetValue(layer, out Canvas canvas) || canvas == null)
            {
                Log.Warn($"SetLayerVisible({layer}) 时 UIService 还没初始化或该层不存在，只记下开关，建层时套用");
                return;
            }

            if (!changed && canvas.enabled == visible)
            {
                return;
            }

            ApplyLayerCanvas(canvas, visible);

            telemetry.Track(
                TelemetryKeys.UiEvents.LayerVisible,
                (TelemetryKeys.Props.Layer, layer.ToString()),
                (TelemetryKeys.Props.Visible, visible));
        }

        /// <summary>
        /// 把整层开关落到 Canvas 上。Canvas.enabled 只管渲染；射线器要一起关，否则层看不见了按钮照样吃点击。
        /// </summary>
        private static void ApplyLayerCanvas(Canvas canvas, bool visible)
        {
            canvas.enabled = visible;
            var raycaster = canvas.GetComponent<GraphicRaycaster>();
            if (raycaster != null)
            {
                raycaster.enabled = visible;
            }
        }

        public bool IsHudHidden => hudHidden;

        /// <summary>
        /// 沉浸模式开关。只遍历自己记账里的 Hud 层面板，<see cref="UIView.VisibleWhenHudHidden"/> 为 true 的不动；
        /// 不关面板、不进出栈、不触发生命周期。状态真正变化时才发布事件。
        /// </summary>
        public void SetHudHidden(bool hidden)
        {
            ThrowIfDisposed();
            if (hudHidden == hidden)
            {
                return;
            }

            hudHidden = hidden;
            foreach (KeyValuePair<Type, UIView> pair in opened)
            {
                UIView view = pair.Value;
                if (FollowsHud(view))
                {
                    ApplyHudHidden(view, hidden);
                }
            }

            if (hudChanged != null)
            {
                hudChanged.Publish(new HudVisibilityChangedEvent(hidden));
            }
        }

        /// <summary>关掉全部面板并销毁 UIRoot。这里不再播过渡动画——作用域都在销毁了，没人看得到。</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Application.quitting -= ReleaseAllViews;

            ReleaseAllViews();
            layerRoots.Clear();
            layerCanvases.Clear();
            hiddenLayers.Clear();
            eventSystem = null;
            hudCoverGroup = null;

            if (root != null)
            {
                UnityEngine.Object.Destroy(root);
                root = null;
            }
        }

        private float TransitionSeconds => config == null ? 0f : config.TransitionSeconds;

        /// <summary>埋点层自己的时钟。拿不到时恒为 0（ms 记成 0），不影响任何业务路径。</summary>
        private long NowMs => clock == null ? 0L : clock.MillisecondsNow;

        /// <summary>
        /// 埋一条面板开 / 关。<c>depth</c> 取的是**这条事件发生之后**还开着几个面板——
        /// 面板一层层叠上去关不掉是 UI 最常见的故障，这条数列直接把它显出来。
        /// </summary>
        private void TrackPanel(string evt, string panel, long startMs)
        {
            telemetry.Track(
                evt,
                (TelemetryKeys.Props.Panel, panel),
                (TelemetryKeys.Props.Ms, NowMs - startMs),
                (TelemetryKeys.Props.Depth, opened.Count));
        }

        /// <summary>关面板被挡回去。两条分支用 reason 区分，聚合时不用去翻文案。</summary>
        private void TrackCloseDenied(string panel, string reason)
        {
            telemetry.TrackWarn(
                TelemetryKeys.UiEvents.Close,
                TelemetryProps.Of(
                    (TelemetryKeys.Props.Panel, panel),
                    (TelemetryKeys.Props.Reason, reason)));
        }

        /// <summary>
        /// 把所有打开的面板还给资源服务并清空记账。不播过渡、不调 OnCloseAsync——
        /// 这是「进程要结束了」的路径，异步回调已经没机会跑完。
        /// </summary>
        private void ReleaseAllViews()
        {
            if (opened.Count > 0)
            {
                foreach (KeyValuePair<Type, UIView> pair in opened)
                {
                    UIView view = pair.Value;
                    if (view != null)
                    {
                        assets.ReleaseInstance(view.gameObject);
                    }
                }

                opened.Clear();
            }

            stack.Clear();
        }

        private void CreateLayer(UILayer layer, Vector2 reference, float match)
        {
            var canvasObject = new GameObject($"Canvas_{layer}", typeof(RectTransform));
            canvasObject.transform.SetParent(root.transform, false);

            // 内置 UI 层正常是 5；万一工程里把它删了，NameToLayer 返回 -1，赋值会报错，退回默认层。
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
            {
                canvasObject.layer = uiLayer;
            }

            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = (int)layer * LayerSortingStep;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = reference;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = match;

            canvasObject.AddComponent<GraphicRaycaster>();

            // 建层前就被 SetLayerVisible(false) 过（时序问题，已记 Warn）：按记账套上，保持读回值与画面一致。
            if (hiddenLayers.Contains(layer))
            {
                ApplyLayerCanvas(canvas, false);
            }

            var contentObject = new GameObject("SafeArea", typeof(RectTransform));
            contentObject.transform.SetParent(canvasObject.transform, false);
            contentObject.layer = canvasObject.layer;
            StretchToParent(contentObject.transform as RectTransform);
            contentObject.AddComponent<SafeAreaFitter>();

            if (layer == UILayer.Hud)
            {
                // 整层盖住开关，含义见 hudCoverGroup 的注释。
                hudCoverGroup = contentObject.AddComponent<CanvasGroup>();
            }

            layerRoots[layer] = contentObject.transform;
            layerCanvases[layer] = canvas;
        }

        private void CreateEventSystem()
        {
            DisableForeignEventSystems();

            // 先建成 inactive 再挂组件：InputSystemUIInputModule 的 OnEnable 发现自己没有动作集
            // 会自动塞一份内置的 DefaultInputActions，之后再换成工程的资产就多绕一圈。
            var eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.SetActive(false);
            eventSystemObject.transform.SetParent(root.transform, false);

            eventSystem = eventSystemObject.AddComponent<EventSystem>();
            var module = eventSystemObject.AddComponent<InputSystemUIInputModule>();

            if (input.Actions == null)
            {
                Log.Warn("InputService 还没初始化，EventSystem 只能用 Input System 内置的默认动作集。"
                         + "检查 GameLifetimeScope 里 Input 是不是排在 UI 前面。");
            }
            else
            {
                // actionsAsset 的 setter 只会拿模块**已有**的动作引用去新资产里找同名的
                // （InputSystemUIInputModule.UpdateReferenceForNewAsset：旧引用为 null 就直接 return null）。
                // 上面故意让模块建成 inactive、不填 DefaultInputActions，也就没了可当模板的旧引用，
                // 于是赋 actionsAsset 是空转、十个引用全是 null——EventSystem、Canvas、按钮看着都正常，
                // 但模块没有任何输入源，点按钮毫无反应且不报任何错。所以必须逐个显式绑定。
                module.actionsAsset = input.Actions.asset;
                BindUIActions(module, input.Actions.asset);
                input.EnableMap(InputService.UIMap);
            }

            eventSystemObject.SetActive(true);
        }

        /// <summary>
        /// 把 UI 动作图里的动作逐个绑到输入模块上。
        /// <para>
        /// 动作名必须和 GameInput 的 UI map 对得上，改了名字这里就绑不上——
        /// 而且是**静默失灵**（UI 收不到点击但不报错），所以找不到时这里会报 Warn。
        /// </para>
        /// <para>
        /// TrackedDevicePosition / TrackedDeviceOrientation 是 XR 才用的，本工程不接 XR，故不绑；
        /// 将来要用按同样的写法补上。
        /// </para>
        /// </summary>
        private static void BindUIActions(InputSystemUIInputModule module, InputActionAsset asset)
        {
            module.point = FindActionReference(asset, "UI/Point");
            module.leftClick = FindActionReference(asset, "UI/Click");
            module.middleClick = FindActionReference(asset, "UI/MiddleClick");
            module.rightClick = FindActionReference(asset, "UI/RightClick");
            module.scrollWheel = FindActionReference(asset, "UI/ScrollWheel");
            module.move = FindActionReference(asset, "UI/Navigate");
            module.submit = FindActionReference(asset, "UI/Submit");
            module.cancel = FindActionReference(asset, "UI/Cancel");
        }

        /// <summary>
        /// 按「动作图/动作」路径取引用。缺一个只是那一路输入失灵，
        /// 不该让整个 UI 起不来，所以报 Warn 而不抛。
        /// </summary>
        private static InputActionReference FindActionReference(InputActionAsset asset, string path)
        {
            InputAction action = asset.FindAction(path);
            if (action == null)
            {
                Log.Warn($"GameInput 里找不到动作「{path}」，这一路 UI 输入会失灵。");
                return null;
            }

            return InputActionReference.Create(action);
        }

        /// <summary>
        /// 关掉场上已经存在的其它 EventSystem。
        /// <para>
        /// Unity 只让**第一个**启用的 EventSystem 处理输入，后来的直接被忽略。
        /// IngameDebugConsole 这类第三方预制体自带一个（还用的是旧输入系统的 StandaloneInputModule），
        /// 它比 UIService 先实例化，结果就是我们这套 UI 一个点击都收不到、而那个模块在新输入系统下还会报错。
        /// 所以这里把别人的关掉、由框架独占 UI 输入——被关掉的那个预制体照样能用我们这个。
        /// </para>
        /// 注意：只处理初始化这一刻存在的；之后 Additive 加载的场景里再带 EventSystem 还是会打架，
        /// 玩法场景里不要放 EventSystem。
        /// </summary>
        private void DisableForeignEventSystems()
        {
            EventSystem[] existing = UnityEngine.Object.FindObjectsOfType<EventSystem>(true);
            for (int i = 0; i < existing.Length; i++)
            {
                EventSystem other = existing[i];
                if (other == null || !other.gameObject.activeSelf)
                {
                    continue;
                }

                other.gameObject.SetActive(false);
                Log.Warn($"场上已有 EventSystem「{other.gameObject.name}」，已关闭——"
                         + "Unity 只认第一个启用的 EventSystem，留着它会让 UI 收不到点击。"
                         + "UI 输入由 UIService 独占，玩法场景里不要再放 EventSystem。", other.gameObject);
            }
        }

        /// <summary>
        /// 还没实例化时只能按类型猜层：拿类型上可能有的默认值不现实，统一先挂 Panel 层，
        /// 实例化之后再按 <see cref="UIView.Layer"/> 校正。父节点只影响一帧内的挂载位置，不影响表现。
        /// </summary>
        private Transform ResolveLayerRoot(Type viewType)
        {
            Transform panel = GetLayerRoot(UILayer.Panel);
            if (panel == null)
            {
                Log.Warn($"UIService 还没初始化就要开 {viewType.Name}，面板会挂在场景根上");
            }

            return panel;
        }

        private Transform GetLayerRoot(UILayer layer)
        {
            return layerRoots.TryGetValue(layer, out Transform transform) ? transform : null;
        }

        /// <summary>把栈顶面板的默认选中项设给自己的 EventSystem（见 <see cref="ResolveDefaultSelection"/>）。</summary>
        private void ApplySelection(UIView top)
        {
            if (eventSystem == null)
            {
                return;
            }

            eventSystem.SetSelectedGameObject(ResolveDefaultSelection(top));
        }

        /// <summary>把 UIStack 返回的条目集合落到实际的显隐上。</summary>
        private void ApplyVisibility(IReadOnlyList<IUIStackEntry> entries, bool visible)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] is UIView view && view != null)
                {
                    view.gameObject.SetActive(visible);
                }
            }
        }

        /// <summary>
        /// 按 Panel 栈同步 Hud 层的整层盖住：有任一全屏面板就 alpha 0 且不吃点击，全关掉后恢复。
        /// 只动 <see cref="hudCoverGroup"/>，不 SetActive 任何 Hud 面板，也不碰沉浸状态与 Canvas.enabled。
        /// </summary>
        private void RefreshHudCover()
        {
            // CanvasGroup 是 UnityEngine.Object，判空只用 == null。
            if (hudCoverGroup == null)
            {
                return;
            }

            bool covered = stack.HasFullScreenPanel;
            if (covered == hudCovered)
            {
                return;
            }

            hudCovered = covered;
            hudCoverGroup.alpha = covered ? 0f : 1f;
            hudCoverGroup.interactable = !covered;
            hudCoverGroup.blocksRaycasts = !covered;
        }

        /// <summary>该面板是否随沉浸模式隐藏：Hud 层且没声明「沉浸中仍显示」。</summary>
        private static bool FollowsHud(UIView view)
        {
            // UIView 是 UnityEngine.Object，判空只用 == null / != null。
            return view != null && view.Layer == UILayer.Hud && !view.VisibleWhenHudHidden;
        }

        /// <summary>
        /// 切一个面板的沉浸显隐：只动 CanvasGroup（alpha / interactable / blocksRaycasts），不 SetActive——
        /// 面板自己的 root 显隐（如对白期间隐藏任务栏）不受影响，恢复时也不会被误开。
        /// </summary>
        private static void ApplyHudHidden(UIView view, bool hidden)
        {
            CanvasGroup group = view.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = view.gameObject.AddComponent<CanvasGroup>();
            }

            group.alpha = hidden ? 0f : 1f;
            group.interactable = !hidden;
            group.blocksRaycasts = !hidden;
        }

        private static void StretchToParent(RectTransform rectTransform)
        {
            if (rectTransform == null)
            {
                return;
            }

            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            rectTransform.localScale = Vector3.one;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(UIService),
                    "UI 服务已销毁，不能再开关面板（多半是作用域已经释放了还有代码在跑）");
            }
        }
    }
}
