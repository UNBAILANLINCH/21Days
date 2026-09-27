// 职责：演出校验结果的一条问题——严重度、机器可读代码、给动画师看的中文说明、可定位的对象。
// 为什么新建（复用 → 扩展 → 新建）：工程里没有通用的「校验问题」数据类可复用（AssetAuditWindow 的问题行是窗口私有结构）；
//   PerformanceValidator 的输出要同时给窗口、自定义 Inspector 与测试用，放进校验器里当嵌套类型会让调用处冗长，故单独成文件。
using UnityEngine;

namespace Game.Editor.Performance
{
    /// <summary>校验问题的严重度。Error = 播出来一定错；Warning = 可能不对，需要人看一眼；Info = 提示，不影响状态灯。</summary>
    public enum PerformanceIssueSeverity
    {
        Error,
        Warning,
        Info,
    }

    /// <summary>一条演出校验问题。不可变。</summary>
    public sealed class PerformanceIssue
    {
        public PerformanceIssue(PerformanceIssueSeverity severity, string code, string message, Object context = null)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            Context = context;
        }

        public PerformanceIssueSeverity Severity { get; }

        /// <summary>机器可读代码（小写下划线，如 <c>address_missing</c>），测试与窗口按它归类。</summary>
        public string Code { get; }

        /// <summary>中文说明，能直接念给动画师听。</summary>
        public string Message { get; }

        /// <summary>出问题的对象（点击可定位）；可空。</summary>
        public Object Context { get; }

        public bool IsError => Severity == PerformanceIssueSeverity.Error;
        public bool IsInfo => Severity == PerformanceIssueSeverity.Info;

        public override string ToString() => $"[{Severity}] {Code}: {Message}";
    }
}
