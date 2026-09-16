// 职责：埋点服务的契约——埋一条事件、埋一个错误、量一段耗时、领一个绑好模块名的门面。
// 为什么新建：埋点是框架层的一项新能力，工程内没有任何同类接口；Logging/Log.cs 是「给人看的日志」，
//   埋点是「给脚本吃的结构化事实」，两者的调用点、格式、开销取舍都不一样，合成一个接口会两头将就。

using System;

namespace Game.Core.Telemetry
{
    /// <summary>
    /// 埋点服务。玩法模块一般不直接注入它，而是注入 <see cref="Scope"/> 拿到的
    /// <see cref="ITelemetryScope"/>（模块名已经绑好，调用点少写一个参数也不会填错）。
    /// <para>
    /// **零分配约定**：属性走定长的 <see cref="TelemetryProps"/>，重载按属性个数固定实参，
    /// 不用 <c>params</c>（<c>params</c> 每次调用都分配一个数组）。超过
    /// <see cref="TelemetryProps.Capacity"/> 个属性时**拆成两条事件**，别想办法塞——
    /// 一条事件塞四个以上的属性，通常说明它其实是两件事。
    /// </para>
    /// </summary>
    public interface ITelemetryService
    {
        /// <summary>总开关。关掉时所有 Track 直接返回，调用点不用自己判断。</summary>
        bool Enabled { get; }

        /// <summary>
        /// 会话头是否已经写出（<see cref="Boot.IGameService.InitializeAsync"/> 里写）。
        /// EntryPoint 类的调用方（比如性能采样器）靠它避免抢在 <c>session_start</c> 前面埋点——
        /// 分析脚本按 session_start 切段，跑到前面去的事件会被算进上一段会话。
        /// </summary>
        bool SessionStarted { get; }

        /// <summary>埋一条 I 级事件，无属性。</summary>
        void Track(string module, string evt);

        /// <summary>埋一条 I 级事件，一个属性。</summary>
        void Track(string module, string evt, (string Key, PropValue Value) p0);

        /// <summary>埋一条 I 级事件，两个属性。</summary>
        void Track(string module, string evt, (string Key, PropValue Value) p0, (string Key, PropValue Value) p1);

        /// <summary>埋一条 I 级事件，三个属性。</summary>
        void Track(
            string module,
            string evt,
            (string Key, PropValue Value) p0,
            (string Key, PropValue Value) p1,
            (string Key, PropValue Value) p2);

        /// <summary>埋一条 I 级事件，四个属性（槽位上限）。</summary>
        void Track(
            string module,
            string evt,
            (string Key, PropValue Value) p0,
            (string Key, PropValue Value) p1,
            (string Key, PropValue Value) p2,
            (string Key, PropValue Value) p3);

        /// <summary>埋一条 W 级事件。属性多于一个时用 <see cref="TelemetryProps"/> 的 Of 系列构造。</summary>
        void TrackWarn(string module, string evt, in TelemetryProps props = default);

        /// <summary>指定级别埋一条事件。级别要动态决定（比如按失败严重程度分档）时用它。</summary>
        void TrackLevel(TelemetryLevel level, string module, string evt, in TelemetryProps props = default);

        /// <summary>埋一条 E 级事件，<c>err</c> 取异常类型名与消息，<c>st</c> 取异常堆栈。</summary>
        void TrackError(string module, string evt, Exception error, in TelemetryProps props = default);

        /// <summary>埋一条 E 级事件，只有消息没有堆栈（规则判定失败这类「不抛异常的错」用它）。</summary>
        void TrackError(string module, string evt, string message, in TelemetryProps props = default);

        /// <summary>埋一条 E 级事件，消息与堆栈都自己给（日志桥转 Unity 报错时用）。</summary>
        void TrackError(string module, string evt, string message, string stackTrace);

        /// <summary>
        /// 开始一段耗时统计，<c>Dispose</c> 时自动埋一条带 <c>ms</c> 的事件。
        /// 返回的是**结构体**，所以 <c>using</c> 不装箱：<c>using (telemetry.BeginSpan(mod, "load")) { ... }</c>。
        /// </summary>
        TelemetrySpan BeginSpan(string module, string name);

        /// <summary>
        /// 取一个绑好模块名的门面。**同一个模块名每次拿到同一个实例**（内部缓存），
        /// 可以在构造函数里存下来，不必担心每次调用都 new。
        /// </summary>
        ITelemetryScope Scope(string module);
    }
}
