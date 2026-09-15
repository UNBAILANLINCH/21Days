// 职责：ITelemetryScope 的唯一实现——把模块名补上，其余原样转给服务。
// 为什么新建：接口要有实现才能被 Scope() 返回；没有做成结构体是因为它要以接口形式被持有
//   （结构体转接口会装箱，反而比缓存一个类实例更贵）。服务按模块名缓存实例，全局就那么几个对象。
//   没有并进 TelemetryService.cs：一个文件一个类，而且这个转发层将来可能加「模块级采样率」，独立更好改。

using System;

namespace Game.Core.Telemetry
{
    /// <summary>
    /// 绑定模块名的门面。本身不做任何过滤与格式化——那些全在服务里，这里只是省掉调用点的一个参数。
    /// </summary>
    public sealed class TelemetryScope : ITelemetryScope
    {
        private readonly ITelemetryService service;

        internal TelemetryScope(ITelemetryService service, string module)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            Module = module ?? throw new ArgumentNullException(nameof(module));
        }

        public string Module { get; }

        public bool Enabled => service.Enabled;

        public void Track(string evt) => service.Track(Module, evt);

        public void Track(string evt, (string Key, PropValue Value) p0) => service.Track(Module, evt, p0);

        public void Track(string evt, (string Key, PropValue Value) p0, (string Key, PropValue Value) p1)
            => service.Track(Module, evt, p0, p1);

        public void Track(
            string evt,
            (string Key, PropValue Value) p0,
            (string Key, PropValue Value) p1,
            (string Key, PropValue Value) p2)
            => service.Track(Module, evt, p0, p1, p2);

        public void Track(
            string evt,
            (string Key, PropValue Value) p0,
            (string Key, PropValue Value) p1,
            (string Key, PropValue Value) p2,
            (string Key, PropValue Value) p3)
            => service.Track(Module, evt, p0, p1, p2, p3);

        public void TrackWarn(string evt, in TelemetryProps props = default) => service.TrackWarn(Module, evt, in props);

        public void TrackLevel(TelemetryLevel level, string evt, in TelemetryProps props = default)
            => service.TrackLevel(level, Module, evt, in props);

        public void TrackError(string evt, Exception error, in TelemetryProps props = default)
            => service.TrackError(Module, evt, error, in props);

        public void TrackError(string evt, string message, in TelemetryProps props = default)
            => service.TrackError(Module, evt, message, in props);

        public TelemetrySpan BeginSpan(string name) => service.BeginSpan(Module, name);
    }
}
