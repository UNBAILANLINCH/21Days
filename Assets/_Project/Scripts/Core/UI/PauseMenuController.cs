// 职责：暂停菜单的会话控制——决定 Esc / P 何时开暂停菜单、开着期间持世界暂停令牌并关 Gameplay 输入图、把四个按钮转成动作。
// 为什么新建：复用——UICancelRouter 只做「Esc 关栈顶」，它已经把「没东西可关」留成事件等暂停菜单订阅；
//   扩展——塞进 UICancelRouter 会让它认识流程状态、暂停令牌、设置面板，职责从「路由」变成「玩法菜单」；
//   PauseMenuView 按约定不注入服务。所以单独一个根作用域入口点（roadmap E5 / H11）。
//
// ===== Esc 优先级（H11）：一次 Esc 只做下表第一条命中的事 =====
//   1. 栈顶有面板且 CloseOnCancel = true（任务面板、暂停菜单、设置面板…） → UICancelRouter 关掉它（暂停菜单被关 = 继续）。
//   2. 栈顶面板 CloseOnCancel = false（对白、标题）                         → 路由判 Blocked，什么都不做。
//   3. 没有面板、处于沉浸模式（IHudVisibility.IsHudHidden）                  → 退出沉浸（由沉浸按钮那一侧处理），本控制器不开菜单。
//      Esc 同时触发 Gameplay/Cancel（退沉浸）与 UI/Cancel（路由）两条动作，谁先回调取决于动作图顺序；
//      若退沉浸先跑，轮到这里时 IsHudHidden 已是 false——所以「本帧刚退出沉浸」也按沉浸中处理（见 unhideFrame）。
//   4. 以上都不是、且处于玩法状态（不是 BootState / TitleState）            → 打开暂停菜单。
//   P 键 / 手柄 Start（Gameplay/Pause）只开不关：开着时再按无效；Gameplay 图在菜单开着时本来就被关掉了。

using System;
using Cysharp.Threading.Tasks;
using Game.Core.Boot;
using Game.Core.Events;
using Game.Core.Flow;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.Platform;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI.Views;
using MessagePipe;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer.Unity;

namespace Game.Core.UI
{
    /// <summary>
    /// 暂停菜单控制器（根作用域入口点）。打开条件见 <see cref="ShouldOpen"/>，Esc 优先级见文件头。
    /// <para>
    /// 触发源两个：<see cref="UICancelRouter.OnCancelWithNothingToClose"/>（Esc 且无面板可关）与 <c>Gameplay/Pause.performed</c>（P / Start）。
    /// 都在 <see cref="BootCompletedEvent"/> 之后才挂——动作集要等 InputService 初始化完才存在（同 UICancelRouter）。
    /// </para>
    /// <para>
    /// 关闭有三条路：「继续」按钮、Esc 被路由关掉、其它代码从外部关掉；统一走 <see cref="ReleaseSession"/> 收尾：
    /// 释放暂停令牌、进来前 Gameplay 图是开的才重新打开。
    /// </para>
    /// </summary>
    public sealed class PauseMenuController : IStartable, IDisposable
    {
        private const string OpenedEvent = "pause_opened";
        private const string ClosedEvent = "pause_closed";
        private const string SourceEsc = "esc";
        private const string SourceKey = "key";
        private const string ReasonResume = "resume";
        private const string ReasonTitle = "title";
        private const string ReasonExternal = "external";
        private const string ReasonDispose = "dispose";

        private readonly IUIService ui;
        private readonly UICancelRouter cancelRouter;
        private readonly IHudVisibility hud;
        private readonly IGameFlow flow;
        private readonly IWorldPauseService pause;
        private readonly IInputService input;
        private readonly IPlatformService platform;
        private readonly SettingsController settingsController;
        private readonly ISubscriber<BootCompletedEvent> bootCompleted;
        private readonly ISubscriber<HudVisibilityChangedEvent> hudChanged;
        private readonly ITelemetryScope telemetry;

        private IDisposable bootSubscription;
        private IDisposable hudSubscription;

        /// <summary>最近一次退出沉浸发生在哪一帧；-1 表示还没有过。只用于同帧 Esc 的去重。</summary>
        private int unhideFrame = -1;
        private InputAction pauseAction;
        private bool routerHooked;
        private bool bootDone;

        private PauseMenuView view;
        private IDisposable pauseToken;
        private bool gameplayWasEnabled;
        private bool opening;
        private bool closing;
        private bool disposed;

        public PauseMenuController(IUIService ui, UICancelRouter cancelRouter, IHudVisibility hud, IGameFlow flow,
            IWorldPauseService pause, IInputService input, IPlatformService platform,
            SettingsController settingsController, ISubscriber<BootCompletedEvent> bootCompleted,
            ISubscriber<HudVisibilityChangedEvent> hudChanged, ITelemetryService telemetry)
        {
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            this.cancelRouter = cancelRouter ?? throw new ArgumentNullException(nameof(cancelRouter));
            this.hud = hud ?? throw new ArgumentNullException(nameof(hud));
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.pause = pause ?? throw new ArgumentNullException(nameof(pause));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
            this.settingsController = settingsController ?? throw new ArgumentNullException(nameof(settingsController));
            this.bootCompleted = bootCompleted ?? throw new ArgumentNullException(nameof(bootCompleted));
            this.hudChanged = hudChanged ?? throw new ArgumentNullException(nameof(hudChanged));
            this.telemetry = telemetry == null
                ? (ITelemetryScope)NullTelemetryScope.Instance
                : telemetry.Scope(TelemetryKeys.Ui);
        }

        /// <summary>暂停菜单是否开着。</summary>
        public bool IsOpen => view != null;

        /// <summary>
        /// 纯判定：这次 Esc / P 该不该开暂停菜单。EditMode 可测。
        /// </summary>
        /// <param name="bootCompleted">启动流程已完成。</param>
        /// <param name="hudHidden">处于沉浸模式——这次 Esc 归「退出沉浸」，不开菜单。</param>
        /// <param name="isTitle">当前不在玩法状态（BootState / TitleState / 还没进任何状态）。</param>
        /// <param name="alreadyOpen">菜单已开或正在开。</param>
        public static bool ShouldOpen(bool bootCompleted, bool hudHidden, bool isTitle, bool alreadyOpen)
        {
            return bootCompleted && !hudHidden && !isTitle && !alreadyOpen;
        }

        public void Start()
        {
            // 订阅句柄必须托管（EventConventions.cs 第 5 条）。
            bootSubscription = bootCompleted.Subscribe(_ => HookTriggers());
            hudSubscription = hudChanged.Subscribe(e =>
            {
                if (!e.Hidden) unhideFrame = Time.frameCount;
            });
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            if (bootSubscription != null)
            {
                bootSubscription.Dispose();
                bootSubscription = null;
            }

            if (hudSubscription != null)
            {
                hudSubscription.Dispose();
                hudSubscription = null;
            }

            if (routerHooked)
            {
                cancelRouter.OnCancelWithNothingToClose -= HandleEscNothingToClose;
                routerHooked = false;
            }

            if (pauseAction != null)
            {
                pauseAction.performed -= HandlePausePerformed;
                pauseAction = null;
            }

            PauseMenuView target = view;
            if (target != null)
            {
                Detach(target);
                ReleaseSession(ReasonDispose);
                CloseViewAsync(target).Forget();
            }
        }

        // 只挂一次。动作集为空时 P 键不可用，Esc 那一路照常（它由 UICancelRouter 读 UI 图）。
        private void HookTriggers()
        {
            if (disposed || bootDone) return;
            bootDone = true;

            cancelRouter.OnCancelWithNothingToClose += HandleEscNothingToClose;
            routerHooked = true;

            if (input.Actions == null) // lint-ok: UI 层读动作，不进确定性模拟
            {
                Log.Warn("PauseMenuController：启动完成时输入服务还没有动作集，P / Start 开暂停菜单不可用（Esc 仍可用）。");
                return;
            }

            pauseAction = input.Actions.Gameplay.Pause; // lint-ok: UI 层读动作，不进确定性模拟
            pauseAction.performed += HandlePausePerformed;
        }

        private void HandleEscNothingToClose() => TryOpen(SourceEsc);

        private void HandlePausePerformed(InputAction.CallbackContext context) => TryOpen(SourceKey);

        private void TryOpen(string source)
        {
            if (disposed) return;
            GameState current = flow.Current;
            bool isTitle = current == null || current is TitleState || current is BootState;
            // Esc 这一路：本帧刚被 Gameplay/Cancel 退出沉浸的，这次 Esc 已经「用掉」了（见文件头第 3 条）。
            bool hudHidden = hud.IsHudHidden || (source == SourceEsc && unhideFrame == Time.frameCount);
            if (!ShouldOpen(bootDone, hudHidden, isTitle, view != null || opening || closing)) return;
            OpenAsync(source).Forget();
        }

        private async UniTaskVoid OpenAsync(string source)
        {
            opening = true;
            try
            {
                pauseToken = pause.Acquire(this);
                // 只恢复进来之前的状态：Gameplay 图本来就关着（过场中）时，关菜单后不擅自打开。
                bool hasInput = input.Actions != null; // lint-ok: 只判动作集是否已创建，不读设备输入、不影响回放
                gameplayWasEnabled = hasInput && input.Actions.Gameplay.enabled; // lint-ok: 只读动作图启用状态用于收尾恢复，不影响回放
                if (hasInput) input.DisableMap(InputService.GameplayMap);

                PauseMenuView opened;
                try
                {
                    opened = await ui.OpenAsync<PauseMenuView>();
                }
                catch
                {
                    RestoreWorld();
                    throw;
                }

                // await 期间 Dispose 可能已执行：此时不收尾就会留下没人关的面板。
                if (disposed)
                {
                    RestoreWorld();
                    await ui.CloseAsync(opened);
                    return;
                }

                view = opened;
                view.SetQuitVisible(!platform.IsTouchPrimary);
                view.OnResume += HandleResume;
                view.OnSettings += HandleSettings;
                view.OnTitle += HandleTitle;
                view.OnQuit += HandleQuit;
                view.OnClosed += HandleViewClosed;
                telemetry.Track(OpenedEvent, ("source", source));
            }
            catch (Exception e)
            {
                telemetry.TrackError(OpenedEvent, e);
                Log.Error($"PauseMenuController：打开暂停菜单失败：{e}");
            }
            finally
            {
                opening = false;
            }
        }

        /// <summary>关掉暂停菜单（「继续」或「回标题」前）。没开时空操作。</summary>
        private async UniTask CloseAsync(string reason)
        {
            if (view == null || closing) return;
            closing = true;
            PauseMenuView target = view;
            Detach(target);
            try
            {
                await ui.CloseAsync(target);
            }
            finally
            {
                ReleaseSession(reason);
                closing = false;
            }
        }

        private void HandleResume() => ResumeAsync().Forget();

        private async UniTaskVoid ResumeAsync()
        {
            try
            {
                await CloseAsync(ReasonResume);
            }
            catch (Exception e)
            {
                Log.Error($"PauseMenuController：关闭暂停菜单失败：{e}");
            }
        }

        private void HandleSettings() => OpenSettingsAsync().Forget();

        private async UniTaskVoid OpenSettingsAsync()
        {
            try
            {
                // 设置面板叠在暂停菜单上（全屏，会把菜单暂时隐藏）；关掉设置回到菜单，暂停令牌一直由本控制器持有。
                await settingsController.OpenAsync();
            }
            catch (Exception e)
            {
                Log.Error($"PauseMenuController：打开设置面板失败：{e}");
            }
        }

        private void HandleTitle() => BackToTitleAsync().Forget();

        private async UniTaskVoid BackToTitleAsync()
        {
            try
            {
                // 先关自己（释放暂停令牌、恢复输入图），再切状态：否则标题界面在 timeScale = 0 下打开。
                await CloseAsync(ReasonTitle);
                await flow.GoToAsync<TitleState>();
            }
            catch (Exception e)
            {
                Log.Error($"PauseMenuController：回标题失败：{e}");
            }
        }

        // 退出实现与标题界面共用，见 Boot/GameQuit。
        private void HandleQuit() => GameQuit.Quit("暂停菜单");

        // 菜单被 UIService 从外部关掉（Esc 走 CloseTopAsync，或别的代码关的）：只收尾会话，不再调 ui.CloseAsync。
        private void HandleViewClosed()
        {
            if (closing || view == null) return;
            Detach(view);
            ReleaseSession(ReasonExternal);
        }

        private void Detach(PauseMenuView target)
        {
            target.OnResume -= HandleResume;
            target.OnSettings -= HandleSettings;
            target.OnTitle -= HandleTitle;
            target.OnQuit -= HandleQuit;
            target.OnClosed -= HandleViewClosed;
        }

        // 关闭的同步部分（幂等）：释放暂停令牌、恢复输入图、埋点。
        private void ReleaseSession(string reason)
        {
            bool wasOpen = view != null;
            view = null;
            RestoreWorld();
            if (wasOpen) telemetry.Track(ClosedEvent, (TelemetryKeys.Props.Reason, reason));
        }

        private void RestoreWorld()
        {
            if (gameplayWasEnabled) input.EnableMap(InputService.GameplayMap);
            gameplayWasEnabled = false;
            if (pauseToken != null)
            {
                pauseToken.Dispose();
                pauseToken = null;
            }
        }

        // 作用域销毁时 UIService 往往已先释放，关面板会抛 ObjectDisposedException——面板已随 UIRoot 销毁，静默即可。
        private async UniTaskVoid CloseViewAsync(PauseMenuView target)
        {
            try
            {
                await ui.CloseAsync(target);
            }
            catch (ObjectDisposedException)
            {
                // 见方法注释。
            }
            catch (Exception e)
            {
                Log.Warn($"PauseMenuController：关闭暂停菜单失败：{e.Message}");
            }
        }
    }
}
