// 职责：把框架的「标题界面点了开始」事件接住，切到本模块的 SampleState。
// 为什么新建：Game.Core 不许引用 Game.Runtime（asmdef 依赖方向），所以「开始之后去哪」
//   必须由玩法层自己决定。
//   1. 复用不行：工程里没有任何订阅框架事件的玩法侧对象。
//   2. 扩展不行：塞进 SampleState 不行——状态只在被切进去之后才活着，而这个订阅必须从启动起
//      就一直在；塞进 SampleInstaller 也不行——Install 是容器**构建期间**调的，那时不能 Resolve。

using System;
using Cysharp.Threading.Tasks;
using Game.Core.Events;
using Game.Core.Flow;
using Game.Core.Logging;
using MessagePipe;
using VContainer.Unity;

namespace Game.Sample
{
    /// <summary>
    /// 标题 → 玩法的路由。由 <see cref="SampleInstaller"/> 用
    /// <c>builder.RegisterEntryPoint&lt;SampleTitleRouter&gt;()</c> 注册进根作用域，
    /// 容器建好后 VContainer 会调一次 <see cref="Start"/>，作用域销毁时调 <see cref="Dispose"/>。
    /// <para>
    /// 这是「玩法把自己接到框架上」的标准做法：框架只发出事实
    /// （<see cref="TitleStartClickedEvent"/>），由谁接、接了去哪都在玩法这一侧。
    /// 再加一个玩法模块时，各写各的 Router，互不影响。
    /// </para>
    /// </summary>
    public sealed class SampleTitleRouter : IStartable, IDisposable
    {
        private readonly ISubscriber<TitleStartClickedEvent> startClicked;
        private readonly IGameFlow flow;

        private IDisposable subscription;

        public SampleTitleRouter(ISubscriber<TitleStartClickedEvent> startClicked, IGameFlow flow)
        {
            this.startClicked = startClicked ?? throw new ArgumentNullException(nameof(startClicked));
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
        }

        public void Start()
        {
            // 订阅句柄必须托管，禁止裸订阅（EventConventions.cs 第 5 条）：
            // 句柄丢了就退订不掉，作用域销毁后回调还在跑。
            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            startClicked.Subscribe(_ => HandleStartClicked()).AddTo(bag);
            subscription = bag.Build();

            Log.Info("SampleTitleRouter 就绪：标题界面的「开始」将进入 SampleState");
        }

        public void Dispose()
        {
            if (subscription != null)
            {
                subscription.Dispose();
                subscription = null;
            }
        }

        /// <summary>
        /// 不 await：发布方（TitleState 的按钮回调）是同步的，而这次切换要先把 TitleState Exit 掉，
        /// 等在这里就是让标题状态等自己退出。Forget() 之后异常由 GameFlow 记日志。
        /// </summary>
        private void HandleStartClicked()
        {
            flow.GoToAsync<SampleState>().Forget();
        }
    }
}
