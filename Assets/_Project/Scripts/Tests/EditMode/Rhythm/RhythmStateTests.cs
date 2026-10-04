// 职责：验证输入配置不完整时仍释放会话资源；规则测试不覆盖 State 的 Unity 生命周期。
using System;
using System.Reflection;
using Game.Core.Telemetry;
using Game.Rhythm;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Tests.EditMode.Rhythm
{
    public sealed class RhythmStateTests
    {
        [TestCase(0.0)]
        [TestCase(0.2)]
        [TestCase(2.0)]
        public void ClockGuard_RenderStallDoesNotBreakBridge(double stallSeconds)
        {
            var guard = new RhythmClockGuard(100, 400, 0.1);
            Assert.That(guard.Check(401, 101, 0, out _), Is.True);
            Assert.That(guard.Check(401.02 + stallSeconds, 101.02 + stallSeconds, 0, out _), Is.True);
        }

        [TestCase(401.3, 101, "clock_discontinuity")]
        [TestCase(401.02, 100.99, "clock_reversed")]
        [TestCase(400.99, 101.02, "clock_reversed")]
        [TestCase(double.NaN, 101.02, "invalid_clock")]
        public void ClockGuard_InterruptedOrInvalidClockRejectsRound(double realtime, double dsp, string expected)
        {
            var guard = new RhythmClockGuard(100, 400, 0.1);
            Assert.That(guard.Check(401, 101, 0, out _), Is.True);
            Assert.That(guard.Check(realtime, dsp, 0, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo(expected));
        }

        [Test]
        public void ClockGuard_PoorReadAccountsForSampleSpanWithoutChangingBridge()
        {
            var guard = new RhythmClockGuard(100, 400, 0.1);
            Assert.That(guard.Check(401.2, 101, 0.3, out _), Is.True);
            Assert.That(guard.Check(401.4, 101.1, 0, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo("clock_discontinuity"));
        }

        [Test]
        public void ClockGuard_RetryUsesNewClockOrigin()
        {
            var old = new RhythmClockGuard(100, 400, 0.1);
            Assert.That(old.Check(405, 101, 0, out _), Is.False);
            var retry = new RhythmClockGuard(101, 405, 0.1);
            Assert.That(retry.Check(405.02, 101.02, 0, out _), Is.True);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void Dispose_MissingLane_ReleasesInputAndPause(int missing)
        {
            using var telemetry = new TelemetryService(TelemetryOptions.Default,
                new UnityTelemetryClock(), new UnityDebugTelemetrySink());
            var state = new RhythmState(null, null, null, null, null, null, null, telemetry, null);
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = new InputActionMap("Rhythm");
            for (int i = 0; i < 4; i++) if (i != missing) map.AddAction("Lane" + i, InputActionType.Button);
            actions.AddActionMap(map);
            var lease = new Lease();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var actionsField = typeof(RhythmState).GetField("actions", flags);
            actionsField.SetValue(state, actions);
            typeof(RhythmState).GetField("pauseToken", flags).SetValue(state, lease);
            try
            {
                // 被测是运行时清理路径；EditMode 禁止延迟 Destroy，测试末尾立即销毁临时输入。
                UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                    new System.Text.RegularExpressions.Regex("Destroy may not be called from edit mode!"));
                Assert.DoesNotThrow(state.Dispose);
                Assert.That(actionsField.GetValue(state), Is.Null);
                Assert.That(lease.Disposed, Is.True);
                Assert.DoesNotThrow(state.Dispose);
            }
            finally { UnityEngine.Object.DestroyImmediate(actions); }
        }

        [TestCase("audio_paused", RhythmDiagnosticData.Reason.AudioPaused)]
        [TestCase("application_paused", RhythmDiagnosticData.Reason.ApplicationPaused)]
        [TestCase("input_mode_changed", RhythmDiagnosticData.Reason.InputModeChanged)]
        [TestCase("clock_discontinuity", RhythmDiagnosticData.Reason.ClockDiscontinuity)]
        [TestCase("invalid_clock", RhythmDiagnosticData.Reason.InvalidClock)]
        [TestCase("clock_reversed", RhythmDiagnosticData.Reason.ClockRollback)]
        public void DiagnosticReason_RuntimeInterruptRetainsSpecificCause(string reason, RhythmDiagnosticData.Reason expected)
        {
            var method = typeof(RhythmState).GetMethod("DiagnosticReason", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method.Invoke(null, new object[] { reason }), Is.EqualTo(expected));
        }

        private sealed class Lease : IDisposable
        {
            public bool Disposed { get; private set; }
            public void Dispose() => Disposed = true;
        }
    }
}
