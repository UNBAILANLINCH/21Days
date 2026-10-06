// 职责：进入一张世界场景时的判定失败原因（机器可判的档位）。
// 为什么新建：与 WorldTransitionFailure 分开——那一份是「转场服务自己」的失败，
//   这一份是「状态侧进入这张图」的失败（它还多一步出生点解析）。两处都保留档位而不是合并成一个 bool：
//   失败原因决定谁来修（加表行 / 做场景 / 补地址 / 补出生点），合成一句话就分不出来了。
namespace Game.World
{
    /// <summary>进入世界场景的判定失败原因。</summary>
    public enum WorldSceneEntryFailure
    {
        /// <summary>没失败。</summary>
        None = 0,

        /// <summary>没有待处理转场（<see cref="WorldTransitionFailure.NoPending"/> 的映射）。</summary>
        NoPendingTransition = 1,

        /// <summary>目标场景不可加载：不在表里 / 未实装 / 没写地址 / 表没就绪（其余转场失败的映射）。</summary>
        SceneNotLoadable = 2,

        /// <summary>场景可加载，但出生点解析不出来（<see cref="WorldRules.TryResolveSpawn"/> 失败）。</summary>
        SpawnUnresolved = 3,

        /// <summary>出生点解析出来了，但放置策略没摆成（场景侧缺锚点 / 策略没接线）。</summary>
        PlacementFailed = 4,
    }
}
