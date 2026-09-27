// 职责：演出管线对外的服务契约——拉起一段演出、查询播放状态、查询是否已播过。
// 为什么新建（复用 → 扩展 → 新建）：工程里没有任何「按时间轴编排的演出」概念可复用；
// Dialogue 是内容驱动推进机、Core 不许出现玩法名词，扩展不了，只能新建。

using System.Threading;
using Cysharp.Threading.Tasks;

namespace Game.Performance
{
    public interface IPerformanceService
    {
        /// <summary>是否有演出正在播放（同一时刻只允许一段）。</summary>
        bool IsRunning { get; }

        /// <summary>正在播放的演出 id；空闲时为 null。</summary>
        string CurrentId { get; }

        /// <summary>该 id 是否已经完整播过或被跳过（读存档分区，用于「只播一次」）。</summary>
        bool HasPlayed(string id);

        /// <summary>按 Addressables 地址拉起一段演出并等它结束。进行中再调抛 InvalidOperationException；id 为空抛 ArgumentException。</summary>
        UniTask<PerformanceResult> PlayAsync(string id, CancellationToken ct = default);

        /// <summary>
        /// 同上，并指定摆放：世界模式（<see cref="PerformanceStageMode.World"/>）的演出实例化后摆到 <paramref name="placement"/> 的世界位姿；
        /// 传 <see cref="PerformancePlacement.None"/> 等价于两参重载。叠加模式忽略摆放并埋一条告警。
        /// </summary>
        UniTask<PerformanceResult> PlayAsync(string id, PerformancePlacement placement, CancellationToken ct = default);

        /// <summary>
        /// 代码确认继续：正在停顿（Holding）时等价于玩家按了确认（继续播放、收起 ▼），否则无事。
        /// 给回放 / 编辑器试播 / 将来触屏按钮用；在下一帧的播放循环里生效。
        /// </summary>
        void Confirm();

        /// <summary>
        /// 代码跳过：正在播放（Playing / Holding）时等价于长按满（结果 Skipped，埋 skipped 的 source = code），否则无事。
        /// 不看舞台的可跳过开关（开关只约束玩家长按）。给回放 / 编辑器试播 / 将来触屏跳过按钮用；在下一帧的播放循环里生效。
        /// </summary>
        void Skip();
    }
}
