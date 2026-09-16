// 职责：一个什么都不做的 ITelemetryScope，给「拿不到埋点服务」的场合顶上，让调用点永远不用判空。
// 为什么新建：波 2 给框架服务接埋点时，这些服务在 EditMode 测试里是直接 new 出来的（没有容器，
//   也就没有 ITelemetryService）。两条现成的路都走不通：
//   1. 复用不行：波 1 的 TelemetryScope 必须绑一个真服务（构造函数里 null 就抛），它做不了空实现；
//      TelemetryOptions.Enabled = false 也不算——那要求先有一个 TelemetryService 实例。
//   2. 扩展不行：塞进 TelemetryScope.cs 会违反「一个文件一个类」（csharp-code.md #命名与结构），
//      而且那个类将来要加模块级采样率，和「恒等于空」的实现混在一起两边都难改。
//   备选做法是每个调用点写 telemetry?.Track(...)，但 BeginSpan 要配 using，可空类型没法 using，
//   于是同一个文件里会出现两套写法。空对象把这件事收在一处：接线的铁律是「埋点绝不改变现有行为」，
//   拿不到服务时最该发生的事就是什么都不发生。

using System;

namespace Game.Core.Telemetry
{
    /// <summary>
    /// 空埋点门面。所有方法都是空操作，<see cref="BeginSpan"/> 返回 <c>default</c> 的
    /// <see cref="TelemetrySpan"/>（它的 Dispose 本来就对空服务免疫）。
    /// <para>用法：<c>telemetry == null ? NullTelemetryScope.Instance : telemetry.Scope("core.ui")</c>。
    /// 无状态，所以全局一个实例就够。</para>
    /// </summary>
    public sealed class NullTelemetryScope : ITelemetryScope
    {
        /// <summary>全局唯一实例。</summary>
        public static readonly NullTelemetryScope Instance = new NullTelemetryScope();

        private NullTelemetryScope()
        {
        }

        /// <summary>空模块名。取它去做字符串拼接也不会炸。</summary>
        public string Module => string.Empty;

        /// <summary>恒为 false：调用方要是自己判了开关，这里就该整段跳过。</summary>
        public bool Enabled => false;

        public void Track(string evt)
        {
        }

        public void Track(string evt, (string Key, PropValue Value) p0)
        {
        }

        public void Track(string evt, (string Key, PropValue Value) p0, (string Key, PropValue Value) p1)
        {
        }

        public void Track(
            string evt,
            (string Key, PropValue Value) p0,
            (string Key, PropValue Value) p1,
            (string Key, PropValue Value) p2)
        {
        }

        public void Track(
            string evt,
            (string Key, PropValue Value) p0,
            (string Key, PropValue Value) p1,
            (string Key, PropValue Value) p2,
            (string Key, PropValue Value) p3)
        {
        }

        public void TrackWarn(string evt, in TelemetryProps props = default)
        {
        }

        public void TrackLevel(TelemetryLevel level, string evt, in TelemetryProps props = default)
        {
        }

        public void TrackError(string evt, Exception error, in TelemetryProps props = default)
        {
        }

        public void TrackError(string evt, string message, in TelemetryProps props = default)
        {
        }

        /// <summary>返回 <c>default</c>：那个结构体的 Dispose 发现自己没有服务就直接返回，不会埋点。</summary>
        public TelemetrySpan BeginSpan(string name) => default;
    }
}
