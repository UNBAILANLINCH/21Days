// 职责：背包键（Gameplay/Inventory：I / 手柄 RB）入口——启动完成后订阅动作，按下且可开时打开背包面板。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：InventoryPanelController 不是入口点，没有「启动完成后」的时机挂动作订阅（同 QuestHudPresenter 的理由）。
//   2. 扩展不行：挂进 QuestHudPresenter / 探索 HUD 会让任务或探索认识背包；本模块没有常驻 HUD 可借。
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

namespace Game.Inventory
{
    /// <summary>
    /// 背包键入口点。面板开着时 Gameplay 图被控制器关掉，背包键只开不关；关闭走 Esc（UICancelRouter）与面板返回按钮。
    /// </summary>
    public sealed class InventoryHotkeyPresenter : IStartable, IDisposable
    {
        private readonly InventoryPanelController panel;
        private readonly DialogueService dialogue;
        private readonly IHudVisibility hudVisibility;
        private readonly IInputService input;
        private readonly ISubscriber<BootCompletedEvent> bootCompleted;
        private readonly ITelemetryScope telemetry;

        private IDisposable subscription;
        private InputAction inventoryAction;
        private bool disposed;

        public InventoryHotkeyPresenter(InventoryPanelController panel, DialogueService dialogue,
            IHudVisibility hudVisibility, IInputService input, ISubscriber<BootCompletedEvent> bootCompleted,
            ITelemetryScope telemetry)
        {
            this.panel = panel ?? throw new ArgumentNullException(nameof(panel));
            this.dialogue = dialogue ?? throw new ArgumentNullException(nameof(dialogue));
            this.hudVisibility = hudVisibility ?? throw new ArgumentNullException(nameof(hudVisibility));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.bootCompleted = bootCompleted ?? throw new ArgumentNullException(nameof(bootCompleted));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        public void Start()
        {
            // 订阅句柄必须托管（EventConventions.cs 第 5 条）。
            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            bootCompleted.Subscribe(_ => HookAction()).AddTo(bag);
            subscription = bag.Build();
        }

        /// <summary>
        /// 按了背包键该不该开面板：不在对白中、不在沉浸模式、面板没开着（含正在开）。纯逻辑，供回调与测试共用。
        /// </summary>
        public static bool ShouldOpen(bool dialogueRunning, bool hudHidden, bool panelBusy)
            => !dialogueRunning && !hudHidden && !panelBusy;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (subscription != null)
            {
                subscription.Dispose();
                subscription = null;
            }

            if (inventoryAction != null)
            {
                inventoryAction.performed -= HandleInventory;
                inventoryAction = null;
            }
        }

        // 经输入服务的动作集取「Gameplay/Inventory」，不读具体按键；只订阅一次。
        private void HookAction()
        {
            if (inventoryAction != null || disposed) return;
            if (input.Actions == null) // lint-ok: UI 层读动作，不进确定性模拟
            {
                Log.Warn("InventoryHotkeyPresenter：启动完成时输入服务还没有动作集，背包键不可用。");
                return;
            }

            inventoryAction = input.Actions.Gameplay.Inventory; // lint-ok: UI 层读动作，不进确定性模拟
            inventoryAction.performed += HandleInventory;
        }

        private void HandleInventory(InputAction.CallbackContext context)
        {
            if (!ShouldOpen(dialogue.IsRunning, hudVisibility.IsHudHidden, panel.IsBusy)) return;
            telemetry.Track("inventory_key");
            OpenPanelAsync().Forget();
        }

        private async UniTaskVoid OpenPanelAsync()
        {
            try
            {
                await panel.OpenAsync();
            }
            catch (OperationCanceledException)
            {
                // 打开途中被取消不是错误。
            }
            catch (Exception e)
            {
                telemetry.TrackError("panel_open_failed", e);
                Log.Error($"InventoryHotkeyPresenter：打开背包面板失败：{e}");
            }
        }
    }
}
