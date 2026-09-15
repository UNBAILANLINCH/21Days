// 职责：守住日志桥的两道防递归闸，以及「只转 Error / Exception / Assert」这条筛选。
// 为什么新建：防递归失手的后果不是一条错日志，而是几毫秒内把栈撑爆、整个游戏卡死，
//   而且只有在「恰好打了一条 LogError」时才复现——必须有一份用假消息直接喂进回调的测试守着。
//   没有并进 TelemetryServiceTests：那份测的是格式与策略，这份测的是与 Unity 日志系统的耦合点。

using System.Collections.Generic;
using Game.Core.Telemetry;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Telemetry
{
    /// <summary>
    /// UnityLogTelemetryBridge 的 EditMode 测试。全程不真的调 <c>Debug.LogError</c>：
    /// 直接调 <see cref="UnityLogTelemetryBridge.HandleLog"/> 喂假消息，
    /// 既不惊动 LogAssert，也不依赖 Unity 什么时候回调。
    /// </summary>
    public sealed class UnityLogTelemetryBridgeTests
    {
        private FakeTelemetryClock clock;
        private RecordingTelemetrySink sink;
        private TelemetryService telemetry;
        private UnityLogTelemetryBridge bridge;

        [SetUp]
        public void SetUp()
        {
            clock = new FakeTelemetryClock();
            sink = new RecordingTelemetrySink();
            telemetry = new TelemetryService(TelemetryOptions.Default, clock, sink);
            bridge = new UnityLogTelemetryBridge(telemetry);
        }

        [TearDown]
        public void TearDown()
        {
            bridge.Dispose();
            telemetry.Dispose();
        }

        [Test]
        public void HandleLog_WithPlainError_TracksUnityError()
        {
            bridge.HandleLog("空引用", "at Game.Sample.Run()\nat Main()", LogType.Error);

            Assert.That(sink.Count, Is.EqualTo(1));
            Assert.That(sink.Last, Does.StartWith(TelemetryFormat.LinePrefix + "E "
                                                  + TelemetryKeys.Log + "/" + TelemetryKeys.LogEvents.UnityError));
            Assert.That(sink.Last, Does.Contain("\"err\":\"空引用\""));
            Assert.That(sink.Last, Does.Contain("\"st\":"));
        }

        [Test]
        public void HandleLog_WhenMessageIsTelemetryLineItself_ProducesNothing()
        {
            // 埋点自己的 E 级事件最终就是一次 Debug.LogError，Unity 会同步把它回调回来。
            // 挡不住这一条就会无限递归：转成 unity_error → 又是一次 LogError → 又回调……
            string ownLine = TelemetryFormat.LinePrefix + "E core.asset/load_failed | {\"t\":1,\"s\":2,\"f\":3}";
            bridge.HandleLog(ownLine, "at Game.Core.Telemetry.TelemetryService.Write()", LogType.Error);

            Assert.That(sink.Count, Is.EqualTo(0), "以 [Game][T] 开头的消息是埋点自己打的，必须忽略");
        }

        [Test]
        public void HandleLog_WithNonErrorTypes_ProducesNothing()
        {
            bridge.HandleLog("普通日志", string.Empty, LogType.Log);
            bridge.HandleLog("警告", string.Empty, LogType.Warning);

            Assert.That(sink.Count, Is.EqualTo(0));
        }

        [Test]
        public void HandleLog_WhenSinkLogsAgain_ReentrancyGuardStopsTheLoop()
        {
            // 第二道闸：消息**不带**埋点前缀（前缀过滤挡不住），模拟「处理过程中别的东西又打了一条 Error」。
            // 没有重入标志的话这里会一直递归下去，直到栈爆。
            ReentrantSink reentrant = new ReentrantSink();
            TelemetryService service = new TelemetryService(TelemetryOptions.Default, clock, reentrant);
            UnityLogTelemetryBridge loopBridge = new UnityLogTelemetryBridge(service);
            reentrant.Bridge = loopBridge;

            loopBridge.HandleLog("第一条", "at A()", LogType.Error);

            Assert.That(reentrant.Count, Is.EqualTo(1), "重入的那一条不该再产生新埋点");

            loopBridge.Dispose();
            service.Dispose();
        }

        [Test]
        public void AttachThenDetach_IsIdempotent()
        {
            bridge.Attach();
            bridge.Attach();
            bridge.Detach();
            bridge.Detach();

            // 只要求不抛异常：重复注册会让同一条日志转两次，重复反注册在 Unity 里是空操作
            Assert.Pass();
        }

        /// <summary>写出去的同时反手再喂桥一条日志，用来逼出重入。</summary>
        private sealed class ReentrantSink : ITelemetrySink
        {
            private readonly List<string> lines = new List<string>();

            /// <summary>要重入的桥。构造顺序决定了它只能造好之后再赋值。</summary>
            public UnityLogTelemetryBridge Bridge { get; set; }

            public int Count => lines.Count;

            public void Write(TelemetryLevel level, string line)
            {
                lines.Add(line);
                if (Bridge != null)
                {
                    Bridge.HandleLog("重入的那一条", "at B()", LogType.Error);
                }
            }
        }
    }
}
