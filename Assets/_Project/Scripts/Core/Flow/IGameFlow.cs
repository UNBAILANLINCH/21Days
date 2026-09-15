// 职责：状态切换的对外接口。
// 为什么新建：玩法与 UI 只该看到「切到某状态」这一个动作，不该拿到 GameFlow 的内部队列；
// 工程内没有同类接口。

using System.Threading;
using Cysharp.Threading.Tasks;

namespace Game.Core.Flow
{
    /// <summary>
    /// 游戏状态流。切换串行执行：先 Exit 当前状态，再 Enter 目标状态；
    /// 切换进行中再次请求会排队，按请求顺序依次执行，不会插队也不会并发。
    /// </summary>
    public interface IGameFlow
    {
        /// <summary>
        /// 最近一个**成功进入**的状态；还没进入过任何状态时为 null。
        /// 切换失败（目标状态的 EnterAsync 抛异常）时这里仍指向前一个状态，
        /// 而前一个状态的 ExitAsync 已经执行过——即处于「前一状态已退出、目标未进入」的空档。
        /// 因此状态的 ExitAsync 必须幂等：失败那次和下一次切换会各调一遍。
        /// </summary>
        GameState Current { get; }

        /// <summary>切到 TState。返回的 UniTask 在**这一次**切换真正完成时才结束（排队等待也算在内）。</summary>
        UniTask GoToAsync<TState>(CancellationToken ct = default) where TState : GameState;
    }
}
