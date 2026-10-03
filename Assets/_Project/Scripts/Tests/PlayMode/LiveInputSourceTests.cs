// 职责：在真实 Play 输入更新中验证短按锁存；EditMode 的编辑器更新不推进运行时动作图。
using Game.Core.Input;
using Game.Core.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Game.Tests.PlayMode
{
    public sealed class LiveInputSourceTests
    {
        [Test]
        public void TamePressLatch_PreservesSecondPressWithoutReleaseSample_AndClearsWhileHeld()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var input = new ReadyInput();
            var source = new LiveInputSource(input);
            InputSettings original = InputSystem.settings;
            InputSettings temporary = Object.Instantiate(original);
            temporary.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            temporary.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            try
            {
                InputSystem.settings = temporary;
                input.Actions.asset.devices = new InputDevice[] { keyboard };
                source.Initialize();
                input.Actions.Gameplay.Enable();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.T));
                InputSystem.Update();
                source.Sample(0);
                Assert.That(source.Current.HasButton(InputCommand.ButtonTamePressed), Is.True, "第一次按下锁存");
                source.Sample(1);
                Assert.That(source.Current.HasButton(InputCommand.ButtonTame), Is.True);
                Assert.That(source.Current.HasButton(InputCommand.ButtonTamePressed), Is.False, "长按只锁存一次");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.Update();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.T));
                InputSystem.Update();
                source.Sample(2);
                Assert.That(source.Current.HasButton(InputCommand.ButtonTamePressed), Is.True, "未采到松开 tick 仍保留第二次按下");
            }
            finally
            {
                source.Dispose();
                input.Actions.Dispose();
                InputSystem.RemoveDevice(keyboard);
                InputSystem.settings = original;
                Object.DestroyImmediate(temporary);
            }
        }

        private sealed class ReadyInput : IInputService
        {
            public GameInput Actions { get; } = new GameInput();
            public void EnableMap(string map) => Actions.asset.FindActionMap(map).Enable();
            public void DisableMap(string map) => Actions.asset.FindActionMap(map).Disable();
        }
    }
}
