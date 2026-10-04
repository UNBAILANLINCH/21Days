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

        private sealed class Lease : IDisposable
        {
            public bool Disposed { get; private set; }
            public void Dispose() => Disposed = true;
        }
    }
}
