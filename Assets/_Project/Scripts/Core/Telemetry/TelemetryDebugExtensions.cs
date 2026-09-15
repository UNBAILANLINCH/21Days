// 职责：D 级埋点的调用入口，正式包里整句（含参数求值）被编译器剔除。
// 为什么新建：[Conditional] 不能加在接口成员上（CS0582），所以 TrackDebug 没法直接写进 ITelemetryService；
//   写成实现类的方法又要求调用方持有具体类型（玩法注入的是接口）。扩展方法是唯一能同时满足
//   「按接口调用」和「调用点整句剔除」的写法——和 Log.Debug 的行为对齐（见 Logging/Log.cs）。

using System.Diagnostics;

namespace Game.Core.Telemetry
{
    /// <summary>
    /// D 级埋点。<c>UNITY_EDITOR</c> / <c>DEVELOPMENT_BUILD</c> 之外，这些调用连同实参表达式
    /// 一起被编译器剔除——所以参数里写多贵的计算都不会进正式包，但也**不要写有副作用的实参**
    /// （正式包里那句根本不执行）。
    /// <para>只给到两个属性：D 级是排查期的临时点位，真要带四个属性说明它该升成 I 级正式事件。</para>
    /// </summary>
    public static class TelemetryDebugExtensions
    {
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void TrackDebug(this ITelemetryService service, string module, string evt)
        {
            service?.TrackLevel(TelemetryLevel.Debug, module, evt);
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void TrackDebug(this ITelemetryService service, string module, string evt, (string Key, PropValue Value) p0)
        {
            service?.TrackLevel(TelemetryLevel.Debug, module, evt, TelemetryProps.Of(p0));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void TrackDebug(
            this ITelemetryService service,
            string module,
            string evt,
            (string Key, PropValue Value) p0,
            (string Key, PropValue Value) p1)
        {
            service?.TrackLevel(TelemetryLevel.Debug, module, evt, TelemetryProps.Of(p0, p1));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void TrackDebug(this ITelemetryScope scope, string evt)
        {
            scope?.TrackLevel(TelemetryLevel.Debug, evt);
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void TrackDebug(this ITelemetryScope scope, string evt, (string Key, PropValue Value) p0)
        {
            scope?.TrackLevel(TelemetryLevel.Debug, evt, TelemetryProps.Of(p0));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void TrackDebug(
            this ITelemetryScope scope,
            string evt,
            (string Key, PropValue Value) p0,
            (string Key, PropValue Value) p1)
        {
            scope?.TrackLevel(TelemetryLevel.Debug, evt, TelemetryProps.Of(p0, p1));
        }
    }
}
