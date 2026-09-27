// 职责：驱动探索 HUD 与沉浸模式——启动后打开常驻 ExplorationHudView；按钮切换沉浸，
//   沉浸键（Gameplay/Immersive）切换，沉浸中按取消键（且没有对白）或对白开始时退出沉浸；按钮文字随 HudVisibilityChangedEvent 刷新；埋点 immersive_changed。
// 为什么新建：ExplorationHudView 只显示不注入服务；沉浸开关的 Core 能力（IHudVisibility）不认识玩法输入与对白，
//   「何时退出沉浸」属于探索玩法，现有入口点（对白焦点、任务 HUD）职责都对不上。
using System;
using Cysharp.Threading.Tasks;
using Game.Core.Events;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.Telemetry;
using Game.Core.UI;
using Game.Dialogue;
using MessagePipe;
using UnityEngine.InputSystem;
using VContainer.Unity;

namespace Game.IsometricExploration
{
    /// <summary>
    /// 探索 HUD 入口点。HUD 在 <see cref="BootCompletedEvent"/> 后打开并常驻——入口点 Start 早于 UIService 初始化，那时开不了面板；
    /// 取消键也在那时订阅（输入服务的动作集初始化前为 null），只订阅一次。
    /// </summary>
    public sealed class ExplorationHudPresenter : IStartable, IDisposable
    {
        private const string SourceButton = "button";
        private const string SourceCancel = "cancel";
        private const string SourceKey = "key";
        private const string SourceDialogue = "dialogue";

        private readonly IUIService ui;
        private readonly IHudVisibility hudVisibility;
        private readonly IInputService input;
        private readonly DialogueService dialogue;
        private readonly ISubscriber<BootCompletedEvent> bootCompleted;
        private readonly ISubscriber<HudVisibilityChangedEvent> hudChanged;
        private readonly ITelemetryScope telemetry;

        private IDisposable subscription;
        private ExplorationHudView hud;
        private InputAction cancelAction;
        private InputAction immersiveAction;
        private bool dialogueHooked;
        private bool disposed;

        public ExplorationHudPresenter(IUIService ui, IHudVisibility hudVisibility, IInputService input,
            DialogueService dialogue, ISubscriber<BootCompletedEvent> bootCompleted,
            ISubscriber<HudVisibilityChangedEvent> hudChanged, ITelemetryScope telemetry)
        {
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            this.hudVisibility = hudVisibility ?? throw new ArgumentNullException(nameof(hudVisibility));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.dialogue = dialogue ?? throw new ArgumentNullException(nameof(dialogue));
            this.bootCompleted = bootCompleted ?? throw new ArgumentNullException(nameof(bootCompleted));
            this.hudChanged = hudChanged ?? throw new ArgumentNullException(nameof(hudChanged));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>
        /// 取消键是否应当退出沉浸：只在沉浸中、且没有对白在跑时（对白里的取消键归对白自己用）。
        /// 纯逻辑，供回调与测试共用。
        /// </summary>
        public static bool ShouldExitOnCancel(bool hudHidden, bool dialogueRunning) => hudHidden && !dialogueRunning;

        public void Start()
        {
            // 订阅句柄必须托管（EventConventions.cs 第 5 条）。
            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            bootCompleted.Subscribe(_ => HandleBootCompleted()).AddTo(bag);
            hudChanged.Subscribe(e => RefreshButton(e.Hidden)).AddTo(bag);
            subscription = bag.Build();

            dialogue.OnStarted += HandleDialogueStarted;
            dialogueHooked = true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (subscription != null)
            {
                subscription.Dispose();
                subscription = null;
            }

            if (dialogueHooked)
            {
                dialogue.OnStarted -= HandleDialogueStarted;
                dialogueHooked = false;
            }

            if (cancelAction != null)
            {
                cancelAction.performed -= HandleCancel;
                cancelAction = null;
            }

            if (immersiveAction != null)
            {
                immersiveAction.performed -= HandleImmersiveKey;
                immersiveAction = null;
            }

            if (hud != null)
            {
                hud.OnImmersiveToggle -= HandleToggle;
                CloseHudAsync(hud).Forget();
            }

            hud = null;
        }

        private void HandleBootCompleted()
        {
            HookCancel();
            OpenHudAsync().Forget();
        }

        // 经输入服务的动作集取「Gameplay/Cancel」动作，不读具体按键；只订阅一次。
        // 沉浸模式是纯表现开关，不进确定性模拟、不影响回放，所以允许直接订阅动作（同 DialogueInteractionFocus 读确认键）。
        private void HookCancel()
        {
            if (cancelAction != null || immersiveAction != null || disposed) return;
            if (input.Actions == null) // lint-ok: 沉浸是纯表现开关，不进确定性模拟与回放
            {
                Log.Warn("ExplorationHudPresenter：启动完成时输入服务还没有动作集，取消键退出沉浸不可用（按钮仍可用）。");
                return;
            }

            cancelAction = input.Actions.Gameplay.Cancel; // lint-ok: 沉浸是纯表现开关，不进确定性模拟与回放
            cancelAction.performed += HandleCancel;
            // 「Gameplay/Immersive」动作（键鼠 H / 手柄右摇杆按下）：直接切换沉浸。
            immersiveAction = input.Actions.Gameplay.Immersive; // lint-ok: 沉浸是纯表现开关，不进确定性模拟与回放
            immersiveAction.performed += HandleImmersiveKey;
        }

        private void HandleCancel(InputAction.CallbackContext context)
        {
            if (ShouldExitOnCancel(hudVisibility.IsHudHidden, dialogue.IsRunning)) SetImmersive(false, SourceCancel);
        }

        // 对白进行中 Gameplay 图已关，一般收不到；仍兜底判一次，避免对白里切出沉浸。
        private void HandleImmersiveKey(InputAction.CallbackContext context)
        {
            if (!dialogue.IsRunning) SetImmersive(!hudVisibility.IsHudHidden, SourceKey);
        }

        private void HandleDialogueStarted(DialogueStartedEvent e)
        {
            if (hudVisibility.IsHudHidden) SetImmersive(false, SourceDialogue);
        }

        private void HandleToggle() => SetImmersive(!hudVisibility.IsHudHidden, SourceButton);

        // 唯一的切换入口：状态真正变化才调服务与埋点；按钮文字由 HudVisibilityChangedEvent 回调刷新。
        private void SetImmersive(bool hidden, string source)
        {
            if (hudVisibility.IsHudHidden == hidden) return;
            try
            {
                hudVisibility.SetHudHidden(hidden);
            }
            catch (ObjectDisposedException)
            {
                // 作用域销毁途中 UIService 已释放，静默。
                return;
            }

            telemetry.Track("immersive_changed", ("hidden", hidden), ("source", source));
        }

        private void RefreshButton(bool hidden)
        {
            if (hud != null) hud.SetImmersive(hidden);
        }

        private async UniTaskVoid OpenHudAsync()
        {
            try
            {
                ExplorationHudView opened = await ui.OpenAsync<ExplorationHudView>();
                // await 期间 Dispose 可能已执行：此时 hud 还没赋值，不在这里收尾会留下关不掉的孤儿面板。
                if (disposed)
                {
                    await ui.CloseAsync(opened);
                    return;
                }

                hud = opened;
                hud.OnImmersiveToggle -= HandleToggle;
                hud.OnImmersiveToggle += HandleToggle;
                hud.SetImmersive(hudVisibility.IsHudHidden);
            }
            catch (Exception e)
            {
                // 沉浸按钮只是表现开关，开不出来不影响玩法，记 Error 不崩。
                telemetry.TrackError("hud_open_failed", e);
                Log.Error($"ExplorationHudPresenter：打开探索 HUD 失败：{e}");
            }
        }

        // 作用域销毁时 UIService 往往已先释放（按注册顺序），关面板会抛 ObjectDisposedException——
        // 那时 UIRoot 连同 HUD 已由 UIService 一并销毁，静默即可。
        private async UniTaskVoid CloseHudAsync(ExplorationHudView view)
        {
            try
            {
                await ui.CloseAsync(view);
            }
            catch (ObjectDisposedException)
            {
                // 见方法注释。
            }
            catch (Exception e)
            {
                Log.Warn($"ExplorationHudPresenter：关闭探索 HUD 失败：{e.Message}");
            }
        }
    }
}
