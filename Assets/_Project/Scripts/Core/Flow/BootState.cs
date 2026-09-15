// 职责：启动期占位状态——服务还在初始化、玩家还不能操作时所处的状态。
// 为什么新建：状态机需要一个「什么都还没就绪」的合法起点，否则 Current 在启动期一直是 null，
// 订阅 GameStateChangedEvent 的人要额外处理这个特例。

using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Logging;

namespace Game.Core.Flow
{
    /// <summary>
    /// 启动状态。GameBootstrap 在服务初始化之前进入，初始化完成后切到 TitleState。
    /// 波 3 接 UI 后，启动画面 / Loading 条挂在这里。
    /// </summary>
    public sealed class BootState : GameState
    {
        public override UniTask EnterAsync(CancellationToken ct)
        {
            Log.Info("进入 BootState：开始初始化框架服务");
            return UniTask.CompletedTask;
        }
    }
}
