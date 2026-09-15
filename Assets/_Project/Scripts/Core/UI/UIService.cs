// 职责：IUIService 的唯一实现——建 UIRoot（四层 Canvas + EventSystem）、实例化面板、驱动三段生命周期、粘合 UIStack。
// 为什么新建：IUIService 是契约，实现必须分开放；栈规则已经拆到 UIStack（纯逻辑、可测），
//   这里只剩「跟 Unity 打交道」的部分，没有任何现成文件能承担这个职责。

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Boot;
using Game.Core.Input;
using Game.Core.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
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
    /// </para>
    /// </summary>
    public sealed class UIService : IUIService, IGameService, IDisposable
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

        private GameObject root;
        private bool disposed;

        public UIService(IAssetService assets, IInputService input, UIConfig config)
        {
            this.assets = assets ?? throw new ArgumentNullException(nameof(assets));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.config = config;
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

            // 已经开着就复用：面板是有状态的，开两份会出现「关掉一个另一个还在」的幽灵界面。
            if (opened.TryGetValue(type, out UIView existing) && existing != null)
            {
                await existing.OnOpenAsync(arg, ct);
                return (T)existing;
            }

            // 同类型正在打开中：等第一次的结果，返回同一个实例。
            // 上面那次查表到 opened 落值之间隔着 await，连点两下按钮会两次都查到「没开」，
            // 各实例化一份出来——第二份不在栈里也不在字典里，成了关不掉的幽灵面板。
            if (opening.TryGetValue(type, out UniTaskCompletionSource<UIView> inflight))
            {
                return (T)await inflight.Task;
            }

            var completion = new UniTaskCompletionSource<UIView>();
            opening[type] = completion;
            try
            {
                T view = await OpenNewAsync<T>(type, arg, ct);
                completion.TrySetResult(view);
                return view;
            }
            catch (OperationCanceledException e)
            {
                // 取消要按取消传给排队的人，包成普通异常会让对方的 catch (OperationCanceledException) 漏掉。
                completion.TrySetCanceled(e.CancellationToken);
                throw;
            }
            catch (Exception e)
            {
                completion.TrySetException(e);
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
        /// 从实例化成功到压栈之间整段包 try/catch：<see cref="UIView.OnOpenAsync"/> 是玩法自己写的，
        /// 抛异常并不罕见。不清理的话字典里留着一个没入栈、没人关得掉的残骸，实例还挂在场景上，
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
            }
            catch
            {
                // 记账和实例一起回滚。opened 里没登记过时 Remove 是空操作（GetComponent 为 null 那条分支）。
                opened.Remove(type);
                assets.ReleaseInstance(instance);
                throw;
            }

            ApplyVisibility(stack.Push(view), false);
            await view.PlayOpenTransitionAsync(TransitionSeconds, ct);
            return view;
        }

        public async UniTask CloseAsync(UIView view, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (view == null)
            {
                return;
            }

            Type type = view.GetType();
            if (!opened.TryGetValue(type, out UIView tracked) || tracked != view)
            {
                Log.Warn($"CloseAsync 收到的 {type.Name} 不是 UIService 打开的，忽略");
                return;
            }

            await view.PlayCloseTransitionAsync(TransitionSeconds, ct);
            await view.OnCloseAsync(ct);

            opened.Remove(type);
            ApplyVisibility(stack.Remove(view), true);

            assets.ReleaseInstance(view.gameObject);
        }

        public UniTask CloseTopAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            var top = stack.Top() as UIView;
            return top == null ? UniTask.CompletedTask : CloseAsync(top, ct);
        }

        public T Get<T>() where T : UIView
        {
            if (!opened.TryGetValue(typeof(T), out UIView view) || view == null)
            {
                return null;
            }

            return (T)view;
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

            if (root != null)
            {
                UnityEngine.Object.Destroy(root);
                root = null;
            }
        }

        private float TransitionSeconds => config == null ? 0f : config.TransitionSeconds;

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

            var contentObject = new GameObject("SafeArea", typeof(RectTransform));
            contentObject.transform.SetParent(canvasObject.transform, false);
            contentObject.layer = canvasObject.layer;
            StretchToParent(contentObject.transform as RectTransform);
            contentObject.AddComponent<SafeAreaFitter>();

            layerRoots[layer] = contentObject.transform;
        }

        private void CreateEventSystem()
        {
            DisableForeignEventSystems();

            // 先建成 inactive 再挂组件：InputSystemUIInputModule 的 OnEnable 发现自己没有动作集
            // 会自动塞一份内置的 DefaultInputActions，之后再换成工程的资产就多绕一圈。
            var eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.SetActive(false);
            eventSystemObject.transform.SetParent(root.transform, false);

            eventSystemObject.AddComponent<EventSystem>();
            var module = eventSystemObject.AddComponent<InputSystemUIInputModule>();

            if (input.Actions == null)
            {
                Log.Warn("InputService 还没初始化，EventSystem 只能用 Input System 内置的默认动作集。"
                         + "检查 GameLifetimeScope 里 Input 是不是排在 UI 前面。");
            }
            else
            {
                // 赋 actionsAsset 时模块会按「同名动作图 + 同名动作」重新绑定 Point / Click / Submit……
                // 所以 GameInput 的 UI map 里那几个动作名不能改（Navigate / Submit / Cancel / Point / Click / …）。
                module.actionsAsset = input.Actions.asset;
                input.EnableMap(InputService.UIMap);
            }

            eventSystemObject.SetActive(true);
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
