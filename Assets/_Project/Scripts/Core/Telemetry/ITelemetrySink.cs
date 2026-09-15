// 职责：埋点行的输出终点抽象——拿到已经拼好的一整行，负责把它送出去。
// 为什么新建：服务里除了「拼行」还有一堆策略（级别过滤、模块过滤、限流、序号、会话生命周期），
//   这些策略只有看到输出才能验证。有了这个缝，EditMode 测试塞一个把行收进 List 的假 sink 就能断言，
//   不用去钩 Application.logMessageReceived，也不会因为测试里打了 Error 就让 LogAssert 判测试失败。
//   工程内没有同类抽象可复用；不放进 ITelemetryService.cs 是因为那是给调用方看的契约，这是给实现看的缝。

namespace Game.Core.Telemetry
{
    /// <summary>
    /// 埋点输出终点。正式运行时的主实现是 <see cref="UnityDebugTelemetrySink"/>（写进 Unity 日志，
    /// 由 Unity 自己落盘）——**真机上不另建文件通道**，理由见 docs/telemetry.md 开头。
    /// <para>
    /// 编辑器下多一个 <c>EditorMirrorTelemetrySink</c>：<c>Editor.log</c> 的路径本机全局、不是本工程独占，
    /// 所以额外镜像一份到 <c>Logs/telemetry/&lt;sid&gt;.log</c>。两个终点拿到的是**同一次格式化的同一个字符串**
    /// （见 <see cref="TelemetryService"/> 的分发循环），不是两套格式。
    /// </para>
    /// <para>实现自己吞掉异常：它在埋点热路径上，往外抛会把调用方的业务流程带崩。</para>
    /// </summary>
    public interface ITelemetrySink
    {
        /// <summary>写出一整行（不含换行符）。级别一并给出，实现按它决定走哪一档日志。</summary>
        void Write(TelemetryLevel level, string line);
    }
}
