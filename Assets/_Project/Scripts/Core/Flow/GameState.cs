// 职责：游戏状态的抽象基类，只定义进入/退出两段异步生命周期。
// 为什么新建：状态机是波 1 的新能力，工程里没有任何状态基类可复用或扩展。

using System.Threading;
using Cysharp.Threading.Tasks;

namespace Game.Core.Flow
{
    /// <summary>
    /// 一个游戏状态（标题、关卡、结算……）。由容器解析，因此可以构造注入任何已注册的服务。
    /// 生命周期只有两段：EnterAsync / ExitAsync，都是异步的——加载、淡入淡出都能 await。
    /// 状态里不要放每帧逻辑，每帧实现 VContainer 的 ITickable。
    /// </summary>
    public abstract class GameState
    {
        /// <summary>进入本状态。GameFlow 在上一个状态 ExitAsync 返回之后才会调用。</summary>
        public virtual UniTask EnterAsync(CancellationToken ct) => UniTask.CompletedTask;

        /// <summary>退出本状态。在下一个状态 EnterAsync 之前调用，做清理与资源释放。</summary>
        public virtual UniTask ExitAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
