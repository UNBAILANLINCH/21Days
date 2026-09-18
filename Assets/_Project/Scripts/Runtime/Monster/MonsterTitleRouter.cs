// 职责：把标题的开始事件路由到怪物遭遇场景；Core 与 Sample 路由不应知道本模块。
using System;
using Cysharp.Threading.Tasks;
using Game.Core.Events;
using Game.Core.Flow;
using MessagePipe;
using VContainer.Unity;

namespace Game.Monster
{
    public sealed class MonsterTitleRouter : IStartable, IDisposable
    {
        private readonly ISubscriber<TitleStartClickedEvent> startClicked;
        private readonly IGameFlow flow;
        private IDisposable subscription;

        public MonsterTitleRouter(ISubscriber<TitleStartClickedEvent> startClicked, IGameFlow flow)
        {
            this.startClicked = startClicked;
            this.flow = flow;
        }

        public void Start()
        {
            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            startClicked.Subscribe(_ => flow.GoToAsync<MonsterEncounterState>().Forget()).AddTo(bag);
            subscription = bag.Build();
        }

        public void Dispose()
        {
            subscription?.Dispose();
            subscription = null;
        }
    }
}
