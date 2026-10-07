// 职责：出生点解析的结果（成功 / 失败二选一），替调用方把「失败的原因」当成值带回来，而不是靠抛异常。
// 为什么新建：一个文件一个类（csharp-code.md「一个文件一个类，文件名等于类名」）；
//   失败在这条路上是**正常分支**（表里写了未实装场景、策划还没定出生点），
//   抛异常会把「数据还没写好」变成崩溃，所以要有能判定的返回值类型。
namespace Game.World
{
    /// <summary>出生点选择的结果。</summary>
    public readonly struct SpawnResolution
    {
        private SpawnResolution(bool success, WorldSpawnTarget target, string error)
        {
            Success = success;
            Target = target;
            Error = error;
        }

        /// <summary>成功时为 true；<see cref="Target"/> 才是有效的。</summary>
        public bool Success { get; }

        /// <summary>解析出来的落点；失败时为 null。</summary>
        public WorldSpawnTarget Target { get; }

        /// <summary>失败原因（人话，带差值）；成功时为空串。</summary>
        public string Error { get; }

        /// <summary>成功结果。<paramref name="usedFallback"/> = 指名出生点不可用、用的是默认出生点。</summary>
        public static SpawnResolution Resolved(string sceneKey, string spawnId, bool usedFallback) =>
            new SpawnResolution(true, new WorldSpawnTarget(sceneKey, spawnId, usedFallback), string.Empty);

        /// <summary>失败结果。<paramref name="error"/> 不能为空——失败原因必须能读出来。</summary>
        public static SpawnResolution Failed(string error) => new SpawnResolution(false, null, error);
    }
}
