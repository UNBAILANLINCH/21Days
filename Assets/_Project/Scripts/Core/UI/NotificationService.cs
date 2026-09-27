// 职责：INotificationService 的实现——持 NotificationQueue，首次 Show 时经 IUIService 打开常驻的 NotificationView，
//   逐帧喂 unscaled 时间推进队列，按队列的「当前项变了」换卡片或收起。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：UIService 只开关面板，不排队不计时；NotificationQueue 是纯逻辑，不碰视图与帧循环。
//   2. 扩展不行：写进 UIService 会让面板服务背上通知调度（它还是 IGameService、参与启动串行），职责不同；
//      写进 NotificationView 又回到「视图持队列」，服务侧没法换表现、玩法侧得先拿到视图实例才能塞通知。

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Logging;
using Game.Core.Timing;
using Game.Core.UI.Views;

namespace Game.Core.UI
{
    /// <summary>
    /// 通知服务。根作用域单例，<b>不是</b> IGameService（不参与启动串行）：第一次 <see cref="Show"/> 时才开视图，
    /// 那时 UIService 早已初始化完。
    /// <para>
    /// 计时用 <see cref="IClock.UnscaledDeltaTime"/>：世界暂停（timeScale = 0）时通知照样走完，不会卡在屏幕上。
    /// 显示循环只在队列非空时存在，空闲时不占每帧开销。
    /// </para>
    /// </summary>
    public sealed class NotificationService : INotificationService, IDisposable
    {
        private readonly IUIService ui;
        private readonly UIConfig config;
        private readonly IClock clock;
        private readonly NotificationQueue queue = new NotificationQueue(true);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();

        private NotificationView view;
        private bool pumping;
        private bool disposed;

        public NotificationService(IUIService ui, UIConfig config, IClock clock)
        {
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            // UIConfig 是 ScriptableObject，判空只用 ==。
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public void Show(string title, string body = null, float seconds = 0f)
        {
            if (disposed) return;
            float hold = seconds > 0f ? seconds : config.NotificationSeconds;
            if (!queue.Enqueue(title, body, hold)) return;
            if (pumping) return;
            PumpAsync(lifetime.Token).Forget();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            queue.Clear();
            lifetime.Cancel();
            lifetime.Dispose();
            // 不在这里关视图：根作用域销毁时 UIService 会连同 UIRoot 一并回收全部面板，
            // 而且它可能已先于本服务释放（按注册顺序），再调 CloseAsync 只会抛 ObjectDisposedException。
            view = null;
        }

        private async UniTaskVoid PumpAsync(CancellationToken ct)
        {
            pumping = true;
            try
            {
                NotificationView target = await EnsureViewAsync(ct);
                // 空闲时取出第一条。
                queue.Advance(0f);
                while (queue.HasCurrent)
                {
                    NotificationEntry entry = queue.Current;
                    target.ShowCard(entry.Title, entry.Body);

                    // 等到当前项变了（到时出队）为止；这段不分配。
                    do
                    {
                        await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    }
                    while (!queue.Advance(clock.UnscaledDeltaTime));
                }

                target.HideCard();
            }
            catch (OperationCanceledException)
            {
                // 服务释放：正常收尾，不外抛（外抛就成了未观察异常）。
            }
            catch (Exception e)
            {
                // 通知只是提示，开不出来不影响玩法：清掉队列、记 Error，下一次 Show 再试着开。
                queue.Clear();
                view = null;
                Log.Error($"NotificationService：显示通知失败（检查 Prefabs/UI/NotificationView.prefab 与 Addressables 地址 NotificationView）：{e}");
            }
            finally
            {
                pumping = false;
            }
        }

        private async UniTask<NotificationView> EnsureViewAsync(CancellationToken ct)
        {
            // 视图被外部关掉（伪空）或还没开：重新开。OpenAsync 对已开的面板直接返回同一实例。
            if (view == null)
            {
                view = await ui.OpenAsync<NotificationView>(null, ct);
            }

            return view;
        }
    }
}
