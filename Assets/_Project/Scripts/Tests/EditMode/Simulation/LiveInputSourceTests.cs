// 职责：钉住 LiveInputSource.HeldButtons 的契约——软件侧长按位（如走跑切换的 ButtonRun）每次采样都被 OR 进命令。
// 为什么新建：LiveInputSource 此前没有测试文件；按「测试类 = <被测类>Tests」各占一个文件，
//   不塞进 InputCommandTests（被测类不同）。
//   用「Actions 为 null 的假输入服务」而不是真 GameInput：启动期未就绪分支静默返回、不打日志，
//   动作槽位全空，正好把 HeldButtons 这一路单独隔离出来测。

using Game.Core.Input;
using Game.Core.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Simulation
{
    /// <summary><see cref="LiveInputSource"/> 的 EditMode 测试。不碰设备、不碰场景。</summary>
    public sealed class LiveInputSourceTests
    {
        [Test]
        public void Sample_WithHeldButtons_OrsThemIntoTheCommand()
        {
            var source = new LiveInputSource(new UnreadyInput());

            source.HeldButtons = InputCommand.ButtonRun;
            source.Sample(0);

            Assert.That(source.Current.HasButton(InputCommand.ButtonRun), Is.True, "HeldButtons 置了 Run 位，采样结果该带上");
            Assert.That(source.Current.Buttons, Is.EqualTo(InputCommand.ButtonRun), "动作全空时，按钮位应恰好等于 HeldButtons");
            Assert.That(source.Current.Axis0, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void Sample_AfterHeldButtonsCleared_DropsTheBitOnTheNextTick()
        {
            var source = new LiveInputSource(new UnreadyInput());

            source.HeldButtons = InputCommand.ButtonRun;
            source.Sample(0);
            source.HeldButtons = 0u;
            source.Sample(1);

            Assert.That(source.Current.HasButton(InputCommand.ButtonRun), Is.False, "清掉长按位后下一 tick 不该残留");
        }

        /// <summary>尚未初始化的输入服务：Actions 为 null，走「启动期还没轮到」的静默分支。</summary>
        private sealed class UnreadyInput : IInputService
        {
            public GameInput Actions => null;
            public void EnableMap(string map) { }
            public void DisableMap(string map) { }
        }
    }
}
