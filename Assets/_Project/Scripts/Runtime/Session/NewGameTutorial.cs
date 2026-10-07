// 职责：开始新游戏前的「操作说明」闸门——打开 TutorialView、等满最短展示时长并等到任意键、再把面板收掉，然后才让调用方进游戏。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：LoadingCurtain 是切场景黑幕（按相位记账、自己管盖 / 揭，不等待输入），NotificationService 是排队卡片，
//      工程里没有「展示满 N 秒后等玩家按任意键」这套语义的现成类型。
//   2. 扩展不行：写进 SessionTitleRouter，选槽面板那条新游戏路径（SaveSlotsController）就得抄一份同样的开关面板代码；
//      写进 GameSession 则是把 UI 表现塞进只认存档分区的门面（它连 IUIService 都不依赖，只发通知）。
//   所以新开一个只做这一件事的闸门，两条新游戏入口各调一次；「开始新游戏」将来再多一个入口也只需再调一次。
// 失败不挡开局：面板开不出来（预制体 / Addressables 地址缺失、UIService 已释放）时记错误日志后照常往下走——
//   教程是提示不是闸门，卡在标题页比不显示严重得多。

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Logging;
using Game.Core.Telemetry;
using Game.Core.UI;
using Game.Core.UI.Views;

namespace Game.Session
{
    /// <summary>
    /// 开局操作说明的展示闸门（根作用域单例）。<see cref="ShowAsync"/> 会一直等到玩家按键才返回；
    /// 调用方在它返回之后再真正开新游戏（<c>GameSession.NewGameAsync</c>）。
    /// <para>
    /// 时长取 <see cref="UIConfig.TutorialSeconds"/>（教程是 UI 框架侧的展示策略，和存档参数不混在一个配置里）。
    /// </para>
    /// </summary>
    public sealed class NewGameTutorial
    {
        private readonly IUIService ui;
        private readonly UIConfig config;
        private readonly ITelemetryScope telemetry;
        private readonly ITelemetryClock clock;

        /// <remarks>两个与埋点有关的依赖允许为 null（EditMode 直接 new 出来测）：拿不到就把 ms 记成 0，业务行为一个字节不变。</remarks>
        public NewGameTutorial(IUIService ui, UIConfig config, ITelemetryScope telemetry, ITelemetryClock clock)
        {
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            // UIConfig 是 ScriptableObject，判空只用 ==。
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
            this.clock = clock;
        }

        /// <summary>
        /// 展示开局操作说明并等玩家关掉它。取消（<paramref name="ct"/>）按原样抛出，其它异常只记日志、不往上抛。
        /// </summary>
        public async UniTask ShowAsync(CancellationToken ct = default)
        {
            long startMs = NowMs;
            TutorialView view = null;
            try
            {
                view = await ui.OpenAsync<TutorialView>(null, ct);
                telemetry.Track("tutorial_shown", (TelemetryKeys.Props.Panel, nameof(TutorialView)));
                await view.WaitForDismissAsync(config.TutorialSeconds, ct);
                telemetry.Track(
                    "tutorial_dismissed",
                    (TelemetryKeys.Props.Ms, NowMs - startMs),
                    ("elapsed_seconds", view.ElapsedSeconds));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                telemetry.TrackError("tutorial_failed", e);
                Log.Error($"开局操作说明没能显示出来，直接进入游戏：{e}");
            }
            finally
            {
                // 关面板用不带 ct 的重载：走到这里可能正是因为 ct 被取消，带上它等于关不掉、面板留在屏上。
                await CloseAsync(view);
            }
        }

        private async UniTask CloseAsync(TutorialView view)
        {
            // UIView 是 MonoBehaviour，判空只用 != null（面板可能压根没开出来）。
            if (view == null)
            {
                return;
            }

            try
            {
                await ui.CloseAsync(view);
            }
            catch (ObjectDisposedException)
            {
                // 作用域销毁时 UIService 往往已先释放，面板随 UIRoot 一起没了——静默（同 SaveSlotsController）。
            }
            catch (Exception e)
            {
                // 关不掉只是留一块盖屏的图，不该把开局流程整条带崩；但玩家会卡在这张图前，
                // 症状是「进不去游戏」，所以埋一条带异常的事件——只靠 Warn 日志查不动。
                telemetry.TrackError("tutorial_close_failed", e);
                Log.Warn($"开局操作说明关闭失败：{e.Message}");
            }
        }

        private long NowMs => clock == null ? 0L : clock.MillisecondsNow;
    }
}
