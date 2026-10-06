// 职责：取用待处理转场的结果（成功 / 失败二选一），把「为什么没取到」当值带回来，而不是靠抛异常。
// 为什么新建：一个文件一个类（csharp-code.md「一个文件一个类，文件名等于类名」）；
//   失败在这条路上是**正常分支**——目标场景还没实装、表没加载完、玩家连点传送点，
//   这些都不该让进程崩（PRP §2.1）。形状照 SpawnResolution：Success + 值 + 失败档位 + 人话原因。
namespace Game.World
{
    /// <summary>取用待处理转场的结果。<b>消费语义</b>：只要调了取用，待处理转场就已经清掉了（见 <see cref="IWorldTransition.TryConsume"/>）。</summary>
    public readonly struct WorldTransitionResolution
    {
        private WorldTransitionResolution(bool success, WorldTransitionRequest request,
            WorldTransitionFailure failure, string error)
        {
            Success = success;
            Request = request;
            Failure = failure;
            Error = error;
        }

        /// <summary>成功时为 true；<see cref="Request"/> 才是可用的目标。</summary>
        public bool Success { get; }

        /// <summary>
        /// 被消费掉的那个请求。成功时是有效目标；失败时是**被丢弃的请求**（用于埋点与报错），
        /// <see cref="WorldTransitionFailure.NoPending"/> 时为 null。
        /// </summary>
        public WorldTransitionRequest Request { get; }

        /// <summary>失败档位；成功时是 <see cref="WorldTransitionFailure.None"/>。</summary>
        public WorldTransitionFailure Failure { get; }

        /// <summary>失败原因（人话，带「去哪张表改」）；成功时为空串。</summary>
        public string Error { get; }

        /// <summary>成功结果。</summary>
        public static WorldTransitionResolution Consumed(WorldTransitionRequest request) =>
            new WorldTransitionResolution(true, request, WorldTransitionFailure.None, string.Empty);

        /// <summary>失败结果。<paramref name="error"/> 不能为空——失败原因必须能读出来。</summary>
        public static WorldTransitionResolution Rejected(WorldTransitionRequest request,
            WorldTransitionFailure failure, string error) =>
            new WorldTransitionResolution(false, request, failure, error);
    }
}
