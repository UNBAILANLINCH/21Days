// 职责：ITelemetrySink 的正式实现——把埋点行交给 UnityEngine.Debug，由 Unity 落进 Editor.log / Player.log / logcat。
// 为什么新建：接口要有默认实现才能注册进容器；单独一个文件是为了「输出去哪」将来能整体替换（比如接一个
//   真机上传的 sink），而不用去动服务里的策略代码。

using UnityEngine;

namespace Game.Core.Telemetry
{
    /// <summary>
    /// 走 Unity 日志的 sink。
    /// <para>
    /// **故意不经过 <see cref="Logging.Log"/> 门面**：那个门面会再加一层 <c>[Game] </c> 前缀，
    /// 行首就变成 <c>[Game] [Game][T] ...</c>，契约正则 <c>^\[Game\]\[T\] </c> 直接匹配不上，
    /// 分析脚本会把整份日志都当成没有埋点。埋点行的行首格式属于契约，只能由 TelemetryFormat 说了算。
    /// </para>
    /// </summary>
    public sealed class UnityDebugTelemetrySink : ITelemetrySink
    {
        public void Write(TelemetryLevel level, string line)
        {
            switch (level)
            {
                case TelemetryLevel.Warn:
                    Debug.LogWarning(line);
                    break;
                case TelemetryLevel.Error:
                    Debug.LogError(line);
                    break;
                default:
                    Debug.Log(line);
                    break;
            }
        }
    }
}
