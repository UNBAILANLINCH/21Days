// 职责：独立试玩场景就绪后进入音游。Sample 标题路由会与会话路由争用，不能复用；不改变正式 Boot 入口。
using System;
using Cysharp.Threading.Tasks;
using Game.Core.Events;
using Game.Core.Flow;
using MessagePipe;
using VContainer.Unity;
namespace Game.Rhythm
{
    public sealed class RhythmDemoEntry : ITickable, IStartable, IDisposable
    {
        private readonly IGameFlow flow;
        private readonly ISubscriber<TitleStartClickedEvent> startClicked;
        private IDisposable subscription;
        private bool entered;
        public RhythmDemoEntry(IGameFlow flow, ISubscriber<TitleStartClickedEvent> startClicked)
        { this.flow = flow; this.startClicked = startClicked; }
        public void Start() { subscription = startClicked.Subscribe(_ => flow.GoToAsync<RhythmState>().Forget()); }
        public void Dispose() { subscription?.Dispose(); }
        public void Tick()
        {
            if (entered || !(flow.Current is TitleState)) return;
            entered = true;
            flow.GoToAsync<RhythmState>().Forget();
        }
    }
}
