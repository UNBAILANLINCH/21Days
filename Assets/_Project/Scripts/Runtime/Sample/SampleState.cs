// 职责：示例模块的游戏状态——加载 Sample 场景、开 SampleView 显示折后价、按返回切回标题。
// 为什么新建：状态是玩法的东西，Core 的 BootState / TitleState 是框架自带的两个内置状态，
//   1. 复用不行：TitleState 的职责是「显示标题」，把示例玩法塞进去等于让 Core 认识玩法。
//   2. 扩展不行：SceneGameState 只是基类，必须有子类给出 SceneKey 与场景就绪后的逻辑。

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Flow;
using Game.Core.Logging;
using Game.Core.Telemetry;
using Game.Core.UI;

namespace Game.Sample
{
    /// <summary>
    /// 示例状态。继承 <see cref="SceneGameState"/>：基类负责 Additive 加载 / 卸载 <see cref="SceneKey"/>
    /// 指向的场景（<c>EnterAsync</c> / <c>ExitAsync</c> 是 sealed 的，子类改不了这条生命周期），
    /// 子类只写场景就绪之后的事。
    /// <para>
    /// 进不来这个状态时先查两处：Addressables 里 <c>SampleScene_Game</c> 这个地址在不在（Scenes 组），
    /// 以及 <see cref="SampleInstaller"/> 有没有挂在 Boot 场景的 GameBootstrap 物体上
    /// （没挂的话本状态没被注册进根作用域，<c>GoToAsync&lt;SampleState&gt;()</c> 会解析失败）。
    /// </para>
    /// </summary>
    public sealed class SampleState : SceneGameState
    {
        /// <summary>
        /// 本模块的埋点模块名。玩法层不受 <see cref="TelemetryKeys"/> 约束，但要照同一套规矩起名：
        /// 模块目录的小写，只能出现小写字母、数字、下划线和点。写错不会编译失败，只会让这一批事件
        /// 从分析脚本的聚合里悄悄消失，所以收成一个常量而不是散在调用点。
        /// </summary>
        private const string TelemetryModule = "sample";

        /// <summary>一次购买意图被处理完了。事件名描述**已经发生的事实**，不用 <c>do_buy</c> 这种祈使式。</summary>
        private const string BuyItemEvent = "buy_item";

        /// <summary>购买意图算不出价格。失败单独一个事件名，聚合时不用先按级别过滤一遍。</summary>
        private const string BuyItemFailedEvent = "buy_item_failed";

        /// <summary>折后总价。框架的 Props 里没有对应语义，按契约「新键随便加」的规矩自己起一个。</summary>
        private const string TotalProp = "total";

        /// <summary>折扣。判定失败时它是首要嫌疑人，所以要写进失败事件的属性里。</summary>
        private const string DiscountProp = "discount";

        private readonly IUIService ui;
        private readonly IGameFlow flow;
        private readonly SampleRules rules;
        private readonly SampleConfig config;
        private readonly ITelemetryScope telemetry;

        private SampleView view;

        public SampleState(
            IAssetService assets,
            IUIService ui,
            IGameFlow flow,
            SampleRules rules,
            SampleConfig config,
            ITelemetryService telemetry)
            : base(assets)
        {
            this.ui = ui;
            this.flow = flow;
            this.rules = rules;
            this.config = config;

            // 玩法模块的标准取法：注入 ITelemetryService，在构造函数里换成绑好模块名的门面。
            // Scope 按模块名缓存，不会每次调用都 new。
            this.telemetry = telemetry == null
                ? (ITelemetryScope)NullTelemetryScope.Instance
                : telemetry.Scope(TelemetryModule);
        }

        /// <summary>
        /// 场景的 Addressables 地址（Scenes 组里 <c>Assets/_Project/Scenes/Sample.unity</c> 的 address）。
        /// 故意不叫 <c>Sample</c>：地址是全局唯一的命名空间，和类名 / 预制体地址撞车最难查。
        /// </summary>
        protected override string SceneKey => "SampleScene_Game";

        protected override async UniTask OnSceneReadyAsync(CancellationToken ct)
        {
            // 玩法数据由**规则类**算，状态只负责把结果摆到界面上——
            // 这条分工是整个样板的重点：算的那部分不碰 Unity，才能被 EditMode 测试钉住。
            //
            // 算不出来也要把面板开出来：跑到这里场景已经 Additive 加载完了，异常要是从这儿抛出去，
            // GameFlow 会让 Current 停在已经 Exit 过的上一个状态（见 IGameFlow.Current 的说明），
            // 玩家看到的是「场景在、界面没有、也退不出去」的空屏。所以降级成一行错误提示，
            // 让 EnterAsync 总能正常返回，至少还能按返回键走人。新模块照抄这条。
            string line;
            try
            {
                var intent = new BuyItemIntent(config.ItemId, config.Count);
                global::cfg.Item item = rules.GetItem(intent.ItemId);
                int unitPrice = rules.GetDiscountedPrice(intent.ItemId, config.Discount);
                int total = rules.GetOrderTotal(intent, config.Discount);

                line = $"{item.Name} ×{intent.Count}　原价 {item.Price}／个，"
                       + $"减 {config.Discount:P0} 后 {unitPrice}／个，共 {total}";

                // 埋点尺子第 1 类「意图入口」：BuyItemIntent 被处理完的地方。
                // 玩家（这里是配置）想买什么、买了几个、最后算成多少钱——这是玩法侧唯一的事实来源，
                // 之后所有「为什么扣了这么多钱」的问题都要回到这一条上对。
                telemetry.Track(
                    BuyItemEvent,
                    (TelemetryKeys.Props.Id, intent.ItemId),
                    (TelemetryKeys.Props.N, intent.Count),
                    (TotalProp, total));
            }
            catch (ArgumentOutOfRangeException e)
            {
                line = $"配置有误，算不出价格：{e.Message}";
                Log.Error($"SampleState 算价失败，检查 Data/Sample/SampleConfig.asset：{e}");

                // 埋点尺子第 3 类「失败分支」：**把判定用到的数值一起写进属性**。
                // 只埋一句「算价失败」等于什么都没埋——再查还得去翻当时的配置资产；
                // id / n / discount 三个值在这儿，看日志就能判出是哪个填错了。
                telemetry.TrackError(
                    BuyItemFailedEvent,
                    e,
                    TelemetryProps.Of(
                        (TelemetryKeys.Props.Id, config.ItemId),
                        (TelemetryKeys.Props.N, config.Count),
                        (DiscountProp, config.Discount)));
            }

            // 埋点尺子第 4 类「长耗时操作」：开面板要等 Addressables 实例化预制体，可能跨好几帧。
            // 这种地方用 BeginSpan——struct + using，不装箱，Dispose 时自动埋一条带 ms 的
            // sample/view_ready，不用自己起 Stopwatch 也不会忘记停表。
            using (telemetry.BeginSpan("view_ready"))
            {
                view = await ui.OpenAsync<SampleView>(line, ct);
            }

            view.OnBackClicked += HandleBackClicked;
            Log.Info($"进入 SampleState：{line}");
        }

        protected override async UniTask OnSceneUnloadingAsync(CancellationToken ct)
        {
            // 订阅与退订成对。ExitAsync 可能被调两次（切换失败时，见 IGameFlow.Current 的说明），
            // 所以这段必须幂等——view 置空之后第二次进来直接跳过。
            if (view != null)
            {
                view.OnBackClicked -= HandleBackClicked;
                await ui.CloseAsync(view, ct);
                view = null;
            }

            Log.Info("离开 SampleState");
        }

        /// <summary>
        /// 返回标题。这里不能 await：按钮回调是同步的，而且切换会把本状态 Exit 掉，
        /// 等在自己的 Exit 上就是自己等自己。Forget() 之后异常由 GameFlow 记日志。
        /// </summary>
        private void HandleBackClicked()
        {
            flow.GoToAsync<TitleState>().Forget();
        }
    }
}
