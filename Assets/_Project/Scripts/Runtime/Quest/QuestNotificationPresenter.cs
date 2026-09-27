// 职责：把「任务接取 / 任务完成」两个事实事件转成框架通用通知（INotificationService.Show）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：INotificationService 只管显示，不认识任务；QuestService 只发事件，不管表现。
//   2. 扩展不行：塞进 QuestHudPresenter 会让它再多一种职责（它已管 HUD 生命周期、逐帧指引、标记），
//      而且通知与 HUD 生命周期无关——HUD 开不出来时通知照样该弹。单独一个事件订阅者最干净。

using System;
using Game.Core.Events;
using Game.Core.Logging;
using Game.Core.Telemetry;
using Game.Core.UI;
using MessagePipe;
using VContainer.Unity;

namespace Game.Quest
{
    /// <summary>
    /// 任务通知入口点。订阅 <see cref="QuestActivatedEvent"/> / <see cref="QuestCompletedEvent"/>，
    /// 按 <see cref="QuestConfig"/> 的文案格式拼标题后交给 <see cref="INotificationService"/>。
    /// <para>
    /// <b>启动完成（<see cref="BootCompletedEvent"/>）之前的事件不弹</b>：<c>QuestService.InitializeAsync</c> 在启动串行里
    /// 就会激活首条任务，那时还停在标题界面，弹「接取任务」既看不到上下文也不是玩家的动作。
    /// </para>
    /// </summary>
    public sealed class QuestNotificationPresenter : IStartable, IDisposable
    {
        private readonly QuestService service;
        private readonly QuestConfig config;
        private readonly INotificationService notifications;
        private readonly ISubscriber<BootCompletedEvent> bootCompleted;
        private readonly ISubscriber<QuestActivatedEvent> activated;
        private readonly ISubscriber<QuestCompletedEvent> completed;
        private readonly ITelemetryScope telemetry;

        private IDisposable subscription;
        private bool booted;

        public QuestNotificationPresenter(QuestService service, QuestConfig config, INotificationService notifications,
            ISubscriber<BootCompletedEvent> bootCompleted, ISubscriber<QuestActivatedEvent> activated,
            ISubscriber<QuestCompletedEvent> completed, ITelemetryScope telemetry)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            // QuestConfig 是 ScriptableObject，判空只用 ==。
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
            this.notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
            this.bootCompleted = bootCompleted ?? throw new ArgumentNullException(nameof(bootCompleted));
            this.activated = activated ?? throw new ArgumentNullException(nameof(activated));
            this.completed = completed ?? throw new ArgumentNullException(nameof(completed));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        public void Start()
        {
            // 订阅句柄必须托管（EventConventions.cs 第 5 条）。
            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            bootCompleted.Subscribe(_ => booted = true).AddTo(bag);
            activated.Subscribe(e => Notify(e.QuestId, config.ActivatedNotificationFormat, "activated")).AddTo(bag);
            completed.Subscribe(e => Notify(e.QuestId, config.CompletedNotificationFormat, "completed")).AddTo(bag);
            subscription = bag.Build();
        }

        public void Dispose()
        {
            if (subscription == null) return;
            subscription.Dispose();
            subscription = null;
        }

        /// <summary>
        /// 按格式拼通知标题。格式为空时直接用任务标题；格式写坏（如多了 <c>{1}</c>）时退回「格式原文 + 标题」而不抛——
        /// 文案是策划在 Inspector 里改的，写错不该让任务推进路径跟着崩。
        /// </summary>
        public static string Compose(string format, string questTitle)
        {
            string title = questTitle ?? string.Empty;
            if (string.IsNullOrEmpty(format)) return title;
            try
            {
                return string.Format(format, title);
            }
            catch (FormatException)
            {
                return format + title;
            }
        }

        private void Notify(int questId, string format, string kind)
        {
            if (!booted) return;
            if (!service.IsReady || !service.Content.TryGet(questId, out QuestDefinition definition))
            {
                Log.Warn($"QuestNotificationPresenter：收到任务 {questId} 的 {kind} 事件，但查不到任务定义，本条不弹通知。");
                return;
            }

            notifications.Show(Compose(format, definition.Title));
            telemetry.Track("notified", ("id", questId), ("kind", kind));
        }
    }
}
