// 职责：统一交互焦点（PRP/interaction D2–D4）——每帧：让位判定 → 在登记表里选焦点（InteractionSelector）→ 焦点变化时回调对象、
//   切底部提示 HUD、埋点 → 读交互键（全工程唯一一处）→ 本帧焦点没变化才调 Current.Interact()。HUD 点击走同一个判定。
// 为什么新建：原来对白与物资箱各有一个焦点类（已删），各选各的焦点、各读一次交互键，一次 E 可能同时拉对白又开箱；
//   两者都只认自己的组件类型，扩展任何一个都会让另一模块反向依赖它（Loot 已经因此依赖了对白焦点）。
//   合并成一个只认 IInteractable 的焦点，放在 Interaction 模块，Dialogue / Loot 只做实现方。
using System;
using Cysharp.Threading.Tasks;
using Game.Core.Events;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using MessagePipe;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using VContainer.Unity;

namespace Game.Interaction
{
    /// <summary>
    /// 交互焦点入口点。玩家标记取 <see cref="IInteractionRegistry.Actor"/>；候选取 <see cref="IInteractionRegistry.Candidates"/>。
    /// 让位（<see cref="ShouldYield"/>）时焦点为空、按键与 HUD 点击都不响应：没有玩家标记、Gameplay 动作图未启用（对白 / 演出 / 任务面板 /
    /// 镜碎页等都会关它）、沉浸模式、世界暂停。HUD 在启动完成（<see cref="BootCompletedEvent"/>）后打开并常驻——入口点 Start 早于 UIService 初始化。
    /// </summary>
    public sealed class InteractionFocus : IInteractionFocus, ITickable, IStartable, IDisposable
    {
        // 键盘绑定路径前缀；只用于挑出「提示文字」该显示哪条绑定，不读键值。
        private const string KeyboardPathPrefix = "<Keyboard>";

        private readonly IInteractionRegistry registry;
        private readonly IUIService ui;
        private readonly IHudVisibility hudVisibility;
        private readonly IWorldPauseService worldPause;
        private readonly IInputService input;
        private readonly ISubscriber<BootCompletedEvent> bootCompleted;
        private readonly ITelemetryScope telemetry;
        private readonly Func<int> frameCounter;
        private IDisposable bootSubscription;
        private InteractPromptHudView hud;
        private int focusChangedFrame = int.MinValue;
        private bool disposed;

        /// <param name="frameCounter">当前帧号来源，判「焦点本帧刚变」用；为 null 时取 <see cref="Time.frameCount"/>（测试注入可控的帧号）。</param>
        public InteractionFocus(IInteractionRegistry registry, IUIService ui, IHudVisibility hudVisibility,
            IWorldPauseService worldPause, IInputService input, ISubscriber<BootCompletedEvent> bootCompleted,
            ITelemetryScope telemetry, Func<int> frameCounter = null)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            this.hudVisibility = hudVisibility ?? throw new ArgumentNullException(nameof(hudVisibility));
            this.worldPause = worldPause ?? throw new ArgumentNullException(nameof(worldPause));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.bootCompleted = bootCompleted ?? throw new ArgumentNullException(nameof(bootCompleted));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
            this.frameCounter = frameCounter ?? CurrentFrame;
        }

        public IInteractable Current { get; private set; }

        public event Action<IInteractable> OnFocusChanged;

        public event Action<IInteractable> OnInteracted;

        /// <summary>
        /// 让位条件（D3）：任一成立时焦点为空、按键不响应。纯函数，供 <see cref="Advance"/> 与测试共用。
        /// Dialogue 原来不看世界暂停，这次补齐；不认识对白服务，「玩家此刻不能操作世界」统一看 Gameplay 图。
        /// </summary>
        public static bool ShouldYield(bool hasActor, bool gameplayMapEnabled, bool hudHidden, bool worldPaused)
        {
            return !hasActor || !gameplayMapEnabled || hudHidden || worldPaused;
        }

        public void Start()
        {
            // 订阅句柄必须托管（EventConventions.cs 第 5 条）。
            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            bootCompleted.Subscribe(_ => OpenHudAsync().Forget()).AddTo(bag);
            bootSubscription = bag.Build();
        }

        public void Tick()
        {
            if (disposed) return;
            GameInput actions = input.Actions; // lint-ok: 只取动作集判 Gameplay 图启用与交互键，交互不进逻辑帧、不影响回放（PRP/interaction §5 第 7 条）
            bool gameplayEnabled = actions != null && actions.Gameplay.enabled;
            InputAction interact = InteractAction(actions);
            bool pressed = gameplayEnabled && interact.WasPressedThisFrame(); // lint-ok: 交互键全工程只在这里读（PRP/interaction D1），不进 InputCommand、不复用 Confirm
            Advance(gameplayEnabled, pressed);
        }

        /// <summary>
        /// 一帧的完整判定：让位 → 选焦点 → 焦点变化 → 按键。<see cref="Tick"/> 读完输入后调它；测试直接调它喂「Gameplay 图开没开、按没按」。
        /// 焦点在本帧变化时，本帧的按键不响应（D4：防止关掉对白的那一下顺手开箱）。
        /// </summary>
        public void Advance(bool gameplayMapEnabled, bool interactPressed)
        {
            if (disposed) return;
            InteractionActor actor = registry.Actor;
            bool yielding = ShouldYield(actor != null, gameplayMapEnabled, hudVisibility.IsHudHidden, worldPause.IsPaused);
            IInteractable next = yielding ? null : InteractionSelector.Select(actor.Anchor.position, registry.Candidates);
            // 引用比较：旧焦点随场景销毁成伪空时，Unity 的 == 会把它和 null 判成相等，订阅者就收不到「焦点清空」。
            if (!ReferenceEquals(next, Current)) SetFocus(next);
            if (interactPressed) TryInteract("key");
        }

        /// <summary>
        /// 点了底部交互提示（或别的 UI 入口）：与交互键同一判定——让位中、没有焦点、焦点本帧刚变都不响应。
        /// </summary>
        public void RequestInteract()
        {
            if (disposed) return;
            GameInput actions = input.Actions; // lint-ok: 只判 Gameplay 图启用状态，不读设备输入、不影响回放
            bool gameplayEnabled = actions != null && actions.Gameplay.enabled;
            if (ShouldYield(registry.Actor != null, gameplayEnabled, hudVisibility.IsHudHidden, worldPause.IsPaused)) return;
            TryInteract("hud");
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
            IInteractable old = Current;
            Current = null;
            if (old != null && !IsDestroyed(old)) old.OnFocusChanged(false);
            if (hud != null)
            {
                hud.OnInteract -= RequestInteract;
                CloseHudAsync(hud).Forget();
            }
            hud = null;
            OnFocusChanged = null;
            OnInteracted = null;
        }

        // 只在焦点变化时走到，不每帧埋点。
        private void SetFocus(IInteractable next)
        {
            IInteractable old = Current;
            Current = next;
            focusChangedFrame = frameCounter();
            // 旧焦点可能已随场景销毁（伪空），这时不回调。
            if (old != null && !IsDestroyed(old)) old.OnFocusChanged(false);
            if (next != null) next.OnFocusChanged(true);

            if (hud != null)
            {
                if (next != null) hud.Show(next.Prompt);
                else hud.Hide();
            }

            telemetry.Track("focus_changed", ("target", Describe(next)), ("verb", next == null ? string.Empty : next.Prompt.Verb ?? string.Empty));
            OnFocusChanged?.Invoke(next);
        }

        private void TryInteract(string via)
        {
            IInteractable target = Current;
            if (target == null || IsDestroyed(target)) return;
            if (frameCounter() == focusChangedFrame)
            {
                // 焦点本帧刚变：不响应（D4）。只在按键 / 点击的那一帧走到，不是每帧。
                telemetry.Track("interact_ignored", ("target", Describe(target)), ("via", via), ("reason", "focus_changed_this_frame"));
                return;
            }
            telemetry.Track("interacted", ("target", Describe(target)), ("verb", target.Prompt.Verb ?? string.Empty), ("via", via));
            target.Interact();
            OnInteracted?.Invoke(target);
        }

        private async UniTaskVoid OpenHudAsync()
        {
            try
            {
                InteractPromptHudView opened = await ui.OpenAsync<InteractPromptHudView>();
                // await 期间 Dispose 可能已执行：hud 字段此时还没赋值，若不在这里收尾，
                // 新开出来的面板既没记进 hud 也没被 Dispose 关掉，会成为关不掉的孤儿实例。
                if (disposed)
                {
                    await ui.CloseAsync(opened);
                    return;
                }
                hud = opened;
                hud.OnInteract -= RequestInteract;
                hud.OnInteract += RequestInteract;
                // 键位徽章只在打开时赋一次（改键 UI 尚无，运行中不会变）。
                hud.SetKeyText(ReadKeyboardDisplay(InteractAction(input.Actions))); // lint-ok: 只读绑定显示串做提示文字，不读输入值
                if (Current != null) hud.Show(Current.Prompt);
                else hud.Hide();
            }
            catch (Exception e)
            {
                // HUD 只是快捷入口，开不出来 / 关不掉都不影响交互键与点击 NPC，记 Error 不崩。
                telemetry.TrackError("hud_open_failed", e);
                Log.Error($"InteractionFocus：打开交互提示 HUD 失败：{e}");
            }
        }

        // 作用域销毁时 UIService 往往已先释放（按注册顺序），关面板会抛 ObjectDisposedException——
        // 那时 UIRoot 连同 HUD 已由 UIService 一并销毁，静默即可。
        private async UniTaskVoid CloseHudAsync(InteractPromptHudView view)
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
                Log.Warn($"InteractionFocus：关闭交互提示 HUD 失败：{e.Message}");
            }
        }

        // 全工程唯一取交互键动作（Gameplay/Interact，E / F / 手柄 A）的地方：按键与键位徽章都经这里取。
        // 不复用 Confirm（它是 LiveInputSource 采样的确定性输入位，PRP/interaction §5 第 7 条）。
        private static InputAction InteractAction(GameInput actions) => actions == null ? null : actions.Gameplay.Interact; // lint-ok: 交互不进逻辑帧、不影响回放，交互键只在这里取

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

        private static bool IsDestroyed(IInteractable interactable) => interactable is UnityEngine.Object unityObject && unityObject == null;

        // 埋点用的对象名：组件取物体名，纯 C# 实现取类型名。只在焦点变化 / 按键时调用。
        private static string Describe(IInteractable interactable)
        {
            if (interactable == null) return string.Empty;
            if (interactable is Component component) return component != null ? component.name : string.Empty;
            return interactable.GetType().Name;
        }

        private static int CurrentFrame() => Time.frameCount;
    }
}
