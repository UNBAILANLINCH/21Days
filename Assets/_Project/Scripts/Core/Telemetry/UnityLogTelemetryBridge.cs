// 职责：把 Unity 侧的 Error / Exception / Assert 转成 core.log/unity_error 埋点。
// 为什么新建：契约 2.1 要求「任何 Debug.LogError 和未捕获异常都变成一条带序号的埋点」，
//   这需要挂 Application.logMessageReceived 并做防递归，是一件独立的事；
//   塞进 TelemetryService.cs 会让那个已经管着过滤、限流、会话的类再多一条与 Unity 事件耦合的生命周期。

using System;
using UnityEngine;

namespace Game.Core.Telemetry
{
    /// <summary>
    /// Unity 日志桥。生命周期跟着 <see cref="TelemetryService"/> 走：它 InitializeAsync 时 Attach，
    /// Dispose 时 Detach（注册与反注册成对，漏一个就会有一个死对象继续收日志）。
    /// <para>
    /// ★ 这个文件最容易踩的坑是**无限递归**：埋点自己的 E 级事件最终是一次
    /// <c>Debug.LogError</c>，而 <c>Application.logMessageReceived</c> 会在同一个调用栈上
    /// **同步**把它再回调进来 → 又转成一条 unity_error → 又是一次 LogError……几毫秒内栈就炸了。
    /// 这里用两道闸挡住，缺一不可：
    /// </para>
    /// <list type="number">
    /// <item>前缀过滤：消息以 <see cref="TelemetryFormat.LinePrefix"/> 开头的，是埋点自己打的，直接忽略。
    /// 这一道管的是「我的行」，即便将来重入标志被改坏也还有它兜着。</item>
    /// <item>重入标志：处理过程中再收到任何日志都不处理。这一道管的是「不是我的行，但是在我处理的过程中
    /// 被别人打出来的」——比如 TrackError 内部哪天多了一句 Debug.LogError。</item>
    /// </list>
    /// <para>只在主线程用：用的是非线程版的 logMessageReceived（Unity 会把别的线程的日志排到主线程），
    /// 所以重入标志用普通 bool 就够，不需要加锁。</para>
    /// </summary>
    public sealed class UnityLogTelemetryBridge : IDisposable
    {
        private readonly ITelemetryService telemetry;
        private bool attached;
        private bool handling;

        public UnityLogTelemetryBridge(ITelemetryService telemetry)
        {
            this.telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>接上 Unity 日志回调。重复调用无副作用。</summary>
        public void Attach()
        {
            if (attached)
            {
                return;
            }

            Application.logMessageReceived += HandleLog;
            attached = true;
        }

        /// <summary>摘掉回调。重复调用无副作用。</summary>
        public void Detach()
        {
            if (!attached)
            {
                return;
            }

            Application.logMessageReceived -= HandleLog;
            attached = false;
        }

        public void Dispose()
        {
            Detach();
        }

        /// <summary>
        /// 日志回调。做成 public 是为了 EditMode 测试能直接喂一条消息进来验证防递归，
        /// 不用真的去打一条 Unity 日志（那会连带惊动 LogAssert）。
        /// </summary>
        public void HandleLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
            {
                return;
            }

            // 第一道闸：这是埋点自己打出去的行
            if (condition != null && condition.StartsWith(TelemetryFormat.LinePrefix, StringComparison.Ordinal))
            {
                return;
            }

            // 第二道闸：正在处理上一条，期间冒出来的任何日志都不再转
            if (handling)
            {
                return;
            }

            handling = true;
            try
            {
                telemetry.TrackError(TelemetryKeys.Log, TelemetryKeys.LogEvents.UnityError, condition, stackTrace);
            }
            finally
            {
                handling = false;
            }
        }
    }
}
