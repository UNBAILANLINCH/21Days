// 职责：演出阶段机的阶段枚举（Idle → Playing ⇄ Holding → Finished），供规则、服务与测试共用。
// 为什么新建（复用 → 扩展 → 新建）：PerformanceRules 对外要暴露 Phase 属性，嵌套同名枚举会与属性撞名（CS0102）；
//   工程里没有可复用的演出阶段类型，Core 不许出现玩法名词，只能在本模块单列一个文件。

namespace Game.Performance
{
    /// <summary>演出阶段。</summary>
    public enum PerformancePhase
    {
        /// <summary>从未开始（或刚构造）。</summary>
        Idle,

        /// <summary>时间轴在播放。</summary>
        Playing,

        /// <summary>停在 HoldMarker 上等玩家确认。</summary>
        Holding,

        /// <summary>已结束（结果见 Outcome）；可再次 Start。</summary>
        Finished
    }
}
