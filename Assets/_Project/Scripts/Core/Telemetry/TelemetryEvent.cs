// 职责：一条埋点的完整快照——级别 / 模块 / 事件 / t / s / f / p / err / st，字段与契约一一对应。
// 为什么新建：把「一条事件」定成一个值类型之后，格式化才能写成一个纯函数（TelemetryFormat.Write），
//   测试可以直接造一条事件去比对契约正则，不用先跑起半个服务。工程内没有同类结构可复用；
//   也没有并进 TelemetryService.cs：那里管策略（过滤、限流、序号、生命周期），这里只是数据。

namespace Game.Core.Telemetry
{
    /// <summary>
    /// 一条埋点事件。字段对应 docs/telemetry.md 第 1 节的表：
    /// <c>[Game][T] &lt;级别&gt; &lt;模块&gt;/&lt;事件&gt; | {"t":..,"s":..,"f":..,"p":{..},"err":"..","st":".."}</c>。
    /// <para>不可变值类型：造好之后只会被读一次（格式化），传参一律用 <c>in</c>，不产生拷贝也不产生堆对象。</para>
    /// </summary>
    public readonly struct TelemetryEvent
    {
        /// <summary>常规事件（没有 err / st）。</summary>
        public TelemetryEvent(
            TelemetryLevel level,
            string module,
            string name,
            long timeMs,
            int sequence,
            int frame,
            in TelemetryProps props)
            : this(level, module, name, timeMs, sequence, frame, in props, null, null, null)
        {
        }

        /// <summary>全字段。<paramref name="rawProps"/> 只给会话头这种属性数超过槽位的固定行用。</summary>
        public TelemetryEvent(
            TelemetryLevel level,
            string module,
            string name,
            long timeMs,
            int sequence,
            int frame,
            in TelemetryProps props,
            string error,
            string stack,
            string rawProps)
        {
            Level = level;
            Module = module;
            Event = name;
            TimeMs = timeMs;
            Sequence = sequence;
            Frame = frame;
            Props = props;
            Error = error;
            Stack = stack;
            RawProps = rawProps;
        }

        /// <summary>级别，决定行首字符与最终走 Debug.Log / LogWarning / LogError 哪一个。</summary>
        public TelemetryLevel Level { get; }

        /// <summary>模块名。框架层用 <c>core.*</c>，玩法层用模块名小写，见 <see cref="TelemetryKeys"/>。</summary>
        public string Module { get; }

        /// <summary>事件名，snake_case，描述已经发生的事实。</summary>
        public string Event { get; }

        /// <summary>契约字段 <c>t</c>：自会话开始的毫秒数。</summary>
        public long TimeMs { get; }

        /// <summary>契约字段 <c>s</c>：会话内自增序号，用来在日志被重排后恢复真实顺序。</summary>
        public int Sequence { get; }

        /// <summary>契约字段 <c>f</c>：<c>Time.frameCount</c>，用来判断「是不是同一帧内连续发生」。</summary>
        public int Frame { get; }

        /// <summary>契约字段 <c>p</c>：事件属性，扁平一层。</summary>
        public TelemetryProps Props { get; }

        /// <summary>契约字段 <c>err</c>：异常消息，只有 E 级有。</summary>
        public string Error { get; }

        /// <summary>契约字段 <c>st</c>：堆栈，只有 E 级有；格式化时换行会被压成一行。</summary>
        public string Stack { get; }

        /// <summary>
        /// 已经拼好的 <c>p</c> 对象内容（**不含外层花括号**，调用方自己保证是合法 JSON）。
        /// 非空时优先于 <see cref="Props"/>。只有会话头这种「字段固定但超过 4 个」的行才用它，
        /// 其余一律走 <see cref="TelemetryProps"/>——那条路才是零分配的。
        /// </summary>
        public string RawProps { get; }
    }
}
