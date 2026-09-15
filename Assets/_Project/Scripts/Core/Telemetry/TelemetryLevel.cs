// 职责：埋点级别枚举，对应契约（docs/telemetry.md 第 1 节）行首那一个字符 D / I / W / E。
// 为什么新建：Logging/Log.cs 的四个级别是四个静态方法，没有可复用的枚举类型；
// 把这个枚举塞进 Log.cs 会让日志门面凭空多出一个与它自己无关的公开类型（埋点与日志是两条通道）。

namespace Game.Core.Telemetry
{
    /// <summary>
    /// 埋点级别。**顺序即严重程度**：过滤时按 <c>级别 &gt;= 最低级别</c> 比较，所以底下的数值不能乱改。
    /// </summary>
    public enum TelemetryLevel
    {
        /// <summary>调试。正式包里整句剔除（同 <see cref="Logging.Log.Debug"/>），行首字符 <c>D</c>。</summary>
        Debug = 0,

        /// <summary>常规事实。埋点的默认级别，行首字符 <c>I</c>。</summary>
        Info = 1,

        /// <summary>可继续运行但需要留意，行首字符 <c>W</c>。</summary>
        Warn = 2,

        /// <summary>出错。带 <c>err</c> / <c>st</c> 两个字段，行首字符 <c>E</c>。</summary>
        Error = 3,
    }
}
