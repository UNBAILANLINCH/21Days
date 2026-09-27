// 职责：把 UI/Cancel（Esc / 手柄 B）路由成「关掉栈顶面板」——栈顶声明可关（UIView.CloseOnCancel）才关；
//   没有任何面板可关时抛 OnCancelWithNothingToClose，由 PauseMenuController 订阅（沉浸中忽略）。
// 为什么新建：
//   1. 复用不行：UIService 只管开关与记账，不读输入；InputSystemUIInputModule 的 cancel 只发给「当前选中的控件」，
//      选中项为空或是普通 Button 时没人接，做不成「Esc 关最上面的面板」这条全局规则。
//   2. 扩展不行：塞进 UIService 会让它在构造 / 初始化之外再管一条输入订阅的生命周期，
//      而且 UIService 是 IGameService 不是入口点，拿不到 BootCompletedEvent 之后的时机；职责说不通。
//   所以单独一个入口点，只做「读 UI/Cancel → 查栈顶 → 调 CloseTopAsync」。

using System;
using Cysharp.Threading.Tasks;
using Game.Core.Events;
using Game.Core.Input;
using Game.Core.Logging;
using MessagePipe;
using UnityEngine.InputSystem;
using VContainer.Unity;

namespace Game.Core.UI
{
    /// <summary>
    /// UI 取消键路由（根作用域入口点）。
    /// <para>
    /// <b>为什么订阅 <c>performed</c> 而不是 ITickable 每帧轮询</b>：取消键一局按不了几次，轮询是每帧白跑一次判断；
    /// 订阅是零每帧开销，且与 <c>ExplorationHudPresenter</c> 订阅 Gameplay/Cancel 的写法一致。
    /// 动作集在 <c>InputService.InitializeAsync</c> 之后才存在，而入口点 Start 早于服务初始化，
    /// 所以在 <see cref="BootCompletedEvent"/> 之后才挂订阅（同 <c>ExplorationHudPresenter.HookCancel</c>）。
    /// </para>
    /// <para>
    /// 只读 <b>UI 图</b>的 Cancel：UI 图由 UIService 建 EventSystem 时启用并常开；Gameplay 图在面板开着时会被玩法关掉，
    /// 读它的话面板一开 Esc 就失灵。
    /// </para>
    /// </summary>
    public sealed class UICancelRouter : IStartable, IDisposable
    {
        /// <summary>一次取消键按下该怎么处理。</summary>
        public enum Decision
        {
            /// <summary>两条栈都空：没有可关的面板，交给 <see cref="OnCancelWithNothingToClose"/>。</summary>
            NothingToClose,

            /// <summary>栈顶声明可关：调 <see cref="IUIService.CloseTopAsync"/>。</summary>
            Close,

            /// <summary>栈顶声明不可关（标题、对白）：什么都不做，也不当成「没东西可关」。</summary>
            Blocked,
        }

        private readonly UIService ui;
        private readonly IInputService input;
        private readonly ISubscriber<BootCompletedEvent> bootCompleted;

        private IDisposable subscription;
        private InputAction cancelAction;
        private bool closing;
        private bool disposed;

        public UICancelRouter(UIService ui, IInputService input, ISubscriber<BootCompletedEvent> bootCompleted)
        {
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.bootCompleted = bootCompleted ?? throw new ArgumentNullException(nameof(bootCompleted));
        }

        /// <summary>
        /// 按了取消键但没有任何面板可关（两条栈都空）。由 <c>PauseMenuController</c> 订阅（沉浸中忽略）。
        /// 栈顶面板不可关（<see cref="Decision.Blocked"/>）时<b>不</b>触发——那是「有面板但它不让关」，不是「没东西」。
        /// </summary>
        public event Action OnCancelWithNothingToClose;

        /// <summary>
        /// 纯判定：给「栈顶有没有面板 + 它的 <see cref="UIView.CloseOnCancel"/>」，返回该怎么处理。EditMode 可测。
        /// </summary>
        public static Decision Decide(bool hasTop, bool topCloseOnCancel)
        {
            if (!hasTop)
            {
                return Decision.NothingToClose;
            }

            return topCloseOnCancel ? Decision.Close : Decision.Blocked;
        }

        public void Start()
        {
            // 订阅句柄必须托管（EventConventions.cs 第 5 条）。
            subscription = bootCompleted.Subscribe(_ => HookCancel());
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (subscription != null)
            {
                subscription.Dispose();
                subscription = null;
            }

            if (cancelAction != null)
            {
                cancelAction.performed -= HandleCancel;
                cancelAction = null;
            }
        }

        // 只挂一次；动作集为空（Input 没初始化成功）时记 Warn，Esc 关面板不可用但「< 返回」按钮照常。
        private void HookCancel()
        {
            if (cancelAction != null || disposed)
            {
                return;
            }

            if (input.Actions == null) // lint-ok: UI 层读动作，不进确定性模拟
            {
                Log.Warn("UICancelRouter：启动完成时输入服务还没有动作集，Esc 关面板不可用（面板上的返回按钮仍可用）。");
                return;
            }

            cancelAction = input.Actions.UI.Cancel; // lint-ok: UI 层读动作，不进确定性模拟
            cancelAction.performed += HandleCancel;
        }

        private void HandleCancel(InputAction.CallbackContext context)
        {
            // 上一次关闭还在播淡出：此时栈顶还是同一个面板，再关一次会让它的 OnCloseAsync 跑两遍、实例还两次。
            if (closing || disposed)
            {
                return;
            }

            UIView top = ui.TopView;

            // UIView 是 UnityEngine.Object，判空只用 != null。
            bool hasTop = top != null;
            switch (Decide(hasTop, hasTop && top.CloseOnCancel))
            {
                case Decision.Close:
                    CloseTopAsync().Forget();
                    break;
                case Decision.NothingToClose:
                    OnCancelWithNothingToClose?.Invoke();
                    break;
                case Decision.Blocked:
                    break;
            }
        }

        private async UniTaskVoid CloseTopAsync()
        {
            closing = true;
            try
            {
                await ui.CloseTopAsync();
            }
            catch (ObjectDisposedException)
            {
                // 作用域销毁途中按了 Esc：UIService 已释放，面板随 UIRoot 一起没了，静默即可。
            }
            catch (Exception e)
            {
                Log.Error($"UICancelRouter：Esc 关闭栈顶面板失败：{e}");
            }
            finally
            {
                closing = false;
            }
        }
    }
}
