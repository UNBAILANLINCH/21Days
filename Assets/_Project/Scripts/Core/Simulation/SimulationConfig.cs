// 职责：确定性内核的可调数值资产——逻辑步长、单帧追帧上限、主随机种子。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：工程内没有和「逻辑推进」相关的配置资产。TelemetryConfig 管埋点开关与采样，
//      AudioConfig 管声部与音量，UIConfig 管面板栈，换个参数都变不成 tick 参数。
//   2. 扩展不行：这三个值跟任何一份现成配置都不是同一件事，塞进去只会让那份配置的名字说不通；
//      而按 csharp-code.md「数值配置进 ScriptableObject」，又不能把 60 / 5 写死在 SimulationRunner 里
//      ——步长和追帧上限恰恰是要在真机上按表现调的两个数。

using UnityEngine;

namespace Game.Core.Simulation
{
    /// <summary>
    /// 确定性内核配置。资产建好后拖到 GameLifetimeScope 上（接线是后续任务，本文件不建 .asset）。
    /// <para>
    /// 运行时只读：<see cref="SimulationRunner"/> 在构造函数里把这几个值抄进自己的只读字段，
    /// 之后再改资产不影响本次运行——换步长等于换一套逻辑，中途改会让前后两段 tick 对不上。
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "SimulationConfig", menuName = "21Days/Core/Simulation Config")]
    public sealed class SimulationConfig : ScriptableObject
    {
        [Header("逻辑步长")]
        [Tooltip("每秒多少个逻辑 tick。60 表示步长 1/60 秒。录制与重放必须用同一个值，改了以前的录像就废了。")]
        [Range(1, 240)]
        [SerializeField] private int tickRate = 60;

        [Tooltip("一个渲染帧内最多补几个 tick。卡顿后攒下的时间超过这么多就整段丢弃，"
                 + "并埋一条 core.sim/tick_dropped。不设上限会「卡顿→补帧→更卡」滚成死亡螺旋。")]
        [Min(1)]
        [SerializeField] private int maxCatchUpTicks = 5;

        [Header("随机")]
        [Tooltip("主随机种子。0 表示运行时现取一个（真随机开局）；复现 bug 时填录像里记下的那个数。")]
        [SerializeField] private ulong masterSeed;

        /// <summary>每秒 tick 数。</summary>
        public int TickRate => tickRate;

        /// <summary>一个渲染帧内最多补几个 tick。</summary>
        public int MaxCatchUpTicks => maxCatchUpTicks;

        /// <summary>主随机种子，0 表示运行时随机取。</summary>
        public ulong MasterSeed => masterSeed;

        /// <summary>由 <see cref="TickRate"/> 算出的固定步长秒数，省得每个调用点自己写 1f / tickRate。</summary>
        public float FixedDeltaTime => 1f / tickRate;
    }
}
