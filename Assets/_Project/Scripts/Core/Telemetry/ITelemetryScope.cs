// 职责：已经绑好模块名的埋点门面，玩法模块注入的就是它。
// 为什么新建：让每个调用点都写一遍模块名，迟早出现 "inventory" 与 "Inventory" 混用——
//   而模块名是分析脚本做跨模块关联的键，拼错了不会报错，只会让那一批事件从聚合里消失。
//   没有做成 ITelemetryService 的扩展方法：模块名得有地方存，扩展方法存不了状态。

using System;

namespace Game.Core.Telemetry
{
    /// <summary>
    /// 模块门面。用法：模块的构造函数里 <c>telemetry = service.Scope("inventory");</c>，
    /// 之后埋点就是一行 <c>telemetry.Track("buy_item", ("id", itemId), ("n", count));</c>。
    /// <para>实例由 <see cref="ITelemetryService.Scope"/> 按模块名缓存，不会每次调用都 new。</para>
    /// </summary>
    public interface ITelemetryScope
    {
        /// <summary>绑定的模块名。</summary>
        string Module { get; }

        /// <summary>总开关，透传自服务。</summary>
        bool Enabled { get; }

        /// <summary>埋一条 I 级事件，无属性。</summary>
        void Track(string evt);

        /// <summary>埋一条 I 级事件，一个属性。</summary>
        void Track(string evt, (string Key, PropValue Value) p0);

        /// <summary>埋一条 I 级事件，两个属性。</summary>
        void Track(string evt, (string Key, PropValue Value) p0, (string Key, PropValue Value) p1);

        /// <summary>埋一条 I 级事件，三个属性。</summary>
        void Track(
            string evt,
            (string Key, PropValue Value) p0,
            (string Key, PropValue Value) p1,
            (string Key, PropValue Value) p2);

        /// <summary>埋一条 I 级事件，四个属性（槽位上限）。</summary>
        void Track(
            string evt,
            (string Key, PropValue Value) p0,
            (string Key, PropValue Value) p1,
            (string Key, PropValue Value) p2,
            (string Key, PropValue Value) p3);

        /// <summary>埋一条 W 级事件。</summary>
        void TrackWarn(string evt, in TelemetryProps props = default);

        /// <summary>指定级别埋一条事件。</summary>
        void TrackLevel(TelemetryLevel level, string evt, in TelemetryProps props = default);

        /// <summary>埋一条 E 级事件，取异常的类型名、消息与堆栈。</summary>
        void TrackError(string evt, Exception error, in TelemetryProps props = default);

        /// <summary>埋一条 E 级事件，只有消息没有堆栈。</summary>
        void TrackError(string evt, string message, in TelemetryProps props = default);

        /// <summary>开始一段耗时统计，Dispose 时自动埋一条带 <c>ms</c> 的事件。</summary>
        TelemetrySpan BeginSpan(string name);
    }
}
