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
        private readonly IUIService ui;
        private readonly IGameFlow flow;
        private readonly SampleRules rules;
        private readonly SampleConfig config;

        private SampleView view;

        public SampleState(IAssetService assets, IUIService ui, IGameFlow flow, SampleRules rules, SampleConfig config)
            : base(assets)
        {
            this.ui = ui;
            this.flow = flow;
            this.rules = rules;
            this.config = config;
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
            }
            catch (ArgumentOutOfRangeException e)
            {
                line = $"配置有误，算不出价格：{e.Message}";
                Log.Error($"SampleState 算价失败，检查 Data/Sample/SampleConfig.asset：{e}");
            }

            view = await ui.OpenAsync<SampleView>(line, ct);
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
