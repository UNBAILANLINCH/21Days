// 职责：范围交互焦点——每帧在已登记的可交互物体里选离玩家最近且可交互的一个为焦点，
//   按焦点显隐底部居中的交互提示 HUD，并把交互键（Gameplay/Interact）/ HUD 点击转成 Current.Interact()（PRP 8.1、roadmap H4）。
// 为什么新建：DialogueSceneBinder 只管「场景加载时登记与注入」，是事件驱动的；焦点是逐帧的选择 + HUD 驱动，
//   塞进 Binder 会让它变成 ITickable 并依赖 UI 与输入。DialogueInteractable 是单个 NPC 的组件，看不到别的 NPC，做不了「选最近」。
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.Core.Events;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.Telemetry;
using Game.Core.UI;
using MessagePipe;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using VContainer.Unity;

namespace Game.Dialogue
{
    /// <summary>
    /// 交互焦点入口点。玩家标记来自 <see cref="DialogueSceneBinder.Actor"/>；对白进行中或没有玩家标记时无焦点。
    /// HUD 在启动完成（<see cref="BootCompletedEvent"/>）后打开并常驻——入口点 Start 早于 UIService 初始化，那时开不了面板。
    /// </summary>
    public sealed class DialogueInteractionFocus : ITickable, IStartable, IDisposable
    {
        // 键盘绑定路径前缀；只用于挑出「提示文字」该显示哪条绑定，不读键值。
        private const string KeyboardPathPrefix = "<Keyboard>";

        private readonly DialogueSceneBinder binder;
        private readonly DialogueService service;
        private readonly IUIService ui;
        private readonly IHudVisibility hudVisibility;
        private readonly IInputService input;
        private readonly ISubscriber<BootCompletedEvent> bootCompleted;
        private readonly ITelemetryScope telemetry;
        private IDisposable bootSubscription;
        private DialogueInteractHudView hud;
        private bool disposed;

        public DialogueInteractionFocus(DialogueSceneBinder binder, DialogueService service, IUIService ui,
            IHudVisibility hudVisibility, IInputService input, ISubscriber<BootCompletedEvent> bootCompleted,
            ITelemetryScope telemetry)
        {
            this.binder = binder ?? throw new ArgumentNullException(nameof(binder));
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            this.hudVisibility = hudVisibility ?? throw new ArgumentNullException(nameof(hudVisibility));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.bootCompleted = bootCompleted ?? throw new ArgumentNullException(nameof(bootCompleted));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>当前焦点；没有时为 null。</summary>
        public DialogueInteractable Current { get; private set; }

        /// <summary>焦点变化（含变为 null）。</summary>
        public event Action<DialogueInteractable> OnFocusChanged;

        public void Start()
        {
            // 订阅句柄必须托管（EventConventions.cs 第 5 条）。
            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            bootCompleted.Subscribe(_ => OpenHudAsync().Forget()).AddTo(bag);
            bootSubscription = bag.Build();
        }

        public void Tick()
        {
            DialogueInteractionActor actor = binder.Actor;
            // 沉浸模式下不选焦点：Current 为 null，交互键与 HUD 点击随之不响应。
            DialogueInteractable next = actor == null || service.IsRunning || hudVisibility.IsHudHidden
                ? null
                : SelectNearest(actor.Anchor.position, binder.Bound);
            if (next != Current) SetFocus(next);

            // 交互走独立的 Interact 动作（E / F / 手柄 A），不再复用 Confirm：Confirm 仍是确定性输入位，由 LiveInputSource 采样。
            // 对白进行中 Gameplay 图已关、且上面已把焦点清空，不会重入。
            if (Current != null && input.Actions != null && input.Actions.Gameplay.Interact.WasPressedThisFrame()) // lint-ok: 触发对话不进逻辑帧、不影响回放，交互键只在这里读（PRP 8.1、roadmap H4）
            {
                Current.Interact();
            }
        }

        /// <summary>
        /// 在候选里选离 <paramref name="origin"/> 最近、激活且 <see cref="DialogueInteractable.CanInteract"/> 的一个；没有返回 null。
        /// 纯选择逻辑，无分配，供 Tick 与测试共用。
        /// </summary>
        public static DialogueInteractable SelectNearest(Vector3 origin, IReadOnlyList<DialogueInteractable> candidates)
        {
            DialogueInteractable best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                DialogueInteractable candidate = candidates[i];
                if (candidate == null || !candidate.enabled || !candidate.gameObject.activeInHierarchy || !candidate.CanInteract) continue;
                float sqr = (candidate.transform.position - origin).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = candidate;
                }
            }
            return best;
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
            if (Current != null) Current.Focused = false;
            Current = null;
            if (hud != null)
            {
                hud.OnInteract -= HandleHudInteract;
                CloseHudAsync(hud).Forget();
            }
            hud = null;
        }

        // 只在焦点变化时走到，不每帧埋点。
        private void SetFocus(DialogueInteractable next)
        {
            // 旧焦点可能已随场景销毁（伪空），== null 时跳过。
            if (Current != null) Current.Focused = false;
            Current = next;
            if (next != null) next.Focused = true;

            if (hud != null)
            {
                if (next != null) hud.Show(next.DisplayName);
                else hud.Hide();
            }

            telemetry.Track("focus_changed", ("target", next == null ? string.Empty : next.name),
                ("id", next == null ? 0 : next.DialogueId));
            OnFocusChanged?.Invoke(next);
        }

        private async UniTaskVoid OpenHudAsync()
        {
            try
            {
                DialogueInteractHudView opened = await ui.OpenAsync<DialogueInteractHudView>();
                // await 期间 Dispose 可能已执行：hud 字段此时还没赋值，若不在这里收尾，
                // 新开出来的面板既没记进 hud 也没被 Dispose 关掉，会成为关不掉的孤儿实例。
                if (disposed)
                {
                    await ui.CloseAsync(opened);
                    return;
                }
                hud = opened;
                hud.OnInteract -= HandleHudInteract;
                hud.OnInteract += HandleHudInteract;
                // 键位徽章只在打开时赋一次（改键 UI 尚无，运行中不会变）。
                hud.SetKeyText(input.Actions != null ? ReadKeyboardDisplay(input.Actions.Gameplay.Interact) : null); // lint-ok: 只读绑定显示串做提示文字，不读输入值
                if (Current != null) hud.Show(Current.DisplayName);
                else hud.Hide();
            }
            catch (Exception e)
            {
                // HUD 只是快捷入口，开不出来/关不掉都不影响交互键与点击 NPC，记 Error 不崩。
                Log.Error($"DialogueInteractionFocus：打开交互 HUD 失败：{e}");
            }
        }

        // 作用域销毁时 UIService 往往已先释放（按注册顺序），关面板会抛 ObjectDisposedException——
        // 那时 UIRoot 连同 HUD 已由 UIService 一并销毁，静默即可。
        private async UniTaskVoid CloseHudAsync(DialogueInteractHudView view)
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
                Log.Warn($"DialogueInteractionFocus：关闭交互 HUD 失败：{e.Message}");
            }
        }

        private void HandleHudInteract()
        {
            if (Current != null) Current.Interact();
        }

        /// <summary>
        /// 取动作第一条键盘绑定的显示串（如「E」）；没有键盘绑定返回空串，由 HUD 回退「E」。
        /// 只显示键盘串：判「最近一次输入来自手柄」要跟踪设备切换，暂不做，手柄玩家看到的仍是键盘键位。
        /// </summary>
        private static string ReadKeyboardDisplay(InputAction action)
        {
            if (action == null) return string.Empty;
            ReadOnlyArray<InputBinding> bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                InputBinding binding = bindings[i];
                if (binding.isComposite || binding.isPartOfComposite) continue;
                string path = binding.effectivePath;
                if (path != null && path.StartsWith(KeyboardPathPrefix, StringComparison.Ordinal))
                    return action.GetBindingDisplayString(i);
            }
            return string.Empty;
        }
    }
}
