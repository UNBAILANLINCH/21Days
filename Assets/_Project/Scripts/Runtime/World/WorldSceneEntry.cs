// 职责：进入世界场景的判定结果——消费掉的那条转场 + 选中的落点（失败时带档位与原因）。
// 为什么新建：一个文件一个类（csharp-code.md「一个文件一个类，文件名等于类名」）；
//   把「取转场」与「选出生点」两步的结果合成一个能判定的值，调用方（含测试）一次拿全：
//   失败也把**被丢弃的那条请求**带回来，因为失败现场正是最需要知道「本来要去哪」的时候。
namespace Game.World
{
    /// <summary>进入世界场景的判定结果（详见 <see cref="WorldSceneState.ResolveEntry"/>）。</summary>
    public readonly struct WorldSceneEntry
    {
        private WorldSceneEntry(bool success, WorldTransitionRequest request, WorldSpawnTarget spawn,
            WorldSceneEntryFailure failure, string error)
        {
            Success = success;
            Request = request;
            Spawn = spawn;
            Failure = failure;
            Error = error;
        }

        /// <summary>成功时为 true；<see cref="Spawn"/> 才有效。</summary>
        public bool Success { get; }

        /// <summary>本次进入的来源（到达方式 / 传送点）；<see cref="WorldSceneEntryFailure.NoPendingTransition"/> 时为 null。</summary>
        public WorldTransitionRequest Request { get; }

        /// <summary>
        /// 选中的落点。解析不出出生点时是 null；
        /// <b>例外</b>：<see cref="WorldSceneEntryFailure.PlacementFailed"/> 时它**不为 null** ——
        /// 落点已经算出来了，只是没摆成，把「本该摆到哪」带回来才能查（丢了这个信息就只剩一句「摆失败」）。
        /// </summary>
        public WorldSpawnTarget Spawn { get; }

        /// <summary>失败档位；成功时是 <see cref="WorldSceneEntryFailure.None"/>。</summary>
        public WorldSceneEntryFailure Failure { get; }

        /// <summary>失败原因（人话，带差值）；成功时为空串。</summary>
        public string Error { get; }

        /// <summary>成功结果。</summary>
        public static WorldSceneEntry Resolved(WorldTransitionRequest request, WorldSpawnTarget spawn) =>
            new WorldSceneEntry(true, request, spawn, WorldSceneEntryFailure.None, string.Empty);

        /// <summary>失败结果。<paramref name="error"/> 不能为空——失败原因必须能读出来。</summary>
        /// <param name="spawn">放置失败时把已解析出的落点带上（其余失败传 null）。</param>
        public static WorldSceneEntry Failed(WorldSceneEntryFailure failure, WorldTransitionRequest request,
            string error, WorldSpawnTarget spawn = null) =>
            new WorldSceneEntry(false, request, spawn, failure, error);
    }
}
