// 职责：把埋点行收进列表的假 sink，供埋点相关的 EditMode 测试断言输出。
// 为什么新建：真实 sink 走 UnityEngine.Debug，测试里既读不回内容，打出 Error 还会让 LogAssert 判失败。
//   没有做成某个测试类的私有嵌套类（工程里 FakeClock / FakePlatformService 是那么写的）：
//   这份假 sink 被 TelemetryServiceTests 与 UnityLogTelemetryBridgeTests 两个文件共用，嵌套就得抄两遍。

using System.Collections.Generic;
using Game.Core.Telemetry;

namespace Game.Tests.EditMode.Telemetry
{
    /// <summary>把写出去的每一行连同级别一起记下来，测试直接读 <see cref="Lines"/> 断言。</summary>
    internal sealed class RecordingTelemetrySink : ITelemetrySink
    {
        private readonly List<string> lines = new List<string>();
        private readonly List<TelemetryLevel> levels = new List<TelemetryLevel>();

        /// <summary>按写出顺序排列的整行文本。</summary>
        public IReadOnlyList<string> Lines => lines;

        /// <summary>与 <see cref="Lines"/> 一一对应的级别。</summary>
        public IReadOnlyList<TelemetryLevel> Levels => levels;

        /// <summary>已写出的行数。</summary>
        public int Count => lines.Count;

        /// <summary>最后一行，一条都没有时返回 null。</summary>
        public string Last => lines.Count == 0 ? null : lines[lines.Count - 1];

        public void Write(TelemetryLevel level, string line)
        {
            levels.Add(level);
            lines.Add(line);
        }

        /// <summary>清空记录。会话头写完之后想只看后续事件时用。</summary>
        public void Clear()
        {
            lines.Clear();
            levels.Clear();
        }
    }
}
