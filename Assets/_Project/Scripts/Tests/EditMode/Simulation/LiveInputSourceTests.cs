// 职责：钉住 LiveInputSource 的两条契约——① 软件侧 HeldButtons 每次采样都被 OR 进命令；
//   ② Gameplay 动作图里的每个动作要么接进 InputCommand，要么被明确豁免（守卫不让「新动作静默丢」重演）。
// 为什么新建：LiveInputSource 此前没有测试文件；按「测试类 = <被测类>Tests」各占一个文件，
//   不塞进 InputCommandTests（被测类不同）。
//   两组用例各用各的替身：HeldButtons 那两条用「Actions 为 null 的假输入服务」——启动期未就绪分支静默返回、
//   不打日志，动作槽位全空，正好把软件侧长按那一路单独隔离出来；采样那几条必须用**真的 GameInput 加真的设备事件**，
//   因为要验的恰恰是「动作名 → 内核位」这段接线，用替身等于把要验的东西换成了替身自己。
// 为什么继承 InputTestFixture：EditMode 下直接 QueueStateEvent + InputSystem.Update() 只更新设备状态，
//   动作相位（IsPressed 依赖的 phase）不会推进——实测过：控制值是 1，动作仍是 Waiting。
//   InputTestFixture 把输入系统与 Unity 运行时解耦、让 Update 像在播放器里一样推进动作，是本工程里
//   唯一能确定性驱动动作的 EditMode 途径（代价是 EditMode asmdef 要引 Unity.InputSystem.TestFramework）。

using System.Collections.Generic;
using Game.Core.Input;
using Game.Core.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Tests.EditMode.Simulation
{
    /// <summary>
    /// <see cref="LiveInputSource"/> 的 EditMode 测试。不碰场景，也不改工程里的 <c>.inputactions</c>：
    /// 采样用例 new 的是生成类自己的一份内存副本，补的键位绑定也只加在这一份上。
    /// </summary>
    public sealed class LiveInputSourceTests : InputTestFixture
    {
        private GameInput gameInput;
        private Keyboard keyboard;
        private readonly List<Key> heldKeys = new List<Key>();

        /// <summary>
        /// 收尾顺序要紧：先清自己的设备与动作集，再交给基类把输入系统还原。
        /// <b><c>base.TearDown()</c> 一定要走到</b>（所以放 finally）——它负责把输入系统恢复成用例开始前的样子，
        /// 漏掉它会把「设备被清空、输入被截断」的状态留给后面所有用例。
        /// </summary>
        public override void TearDown()
        {
            try
            {
                heldKeys.Clear();

                if (keyboard != null)
                {
                    InputSystem.RemoveDevice(keyboard);
                    keyboard = null;
                }

                if (gameInput != null)
                {
                    gameInput.Disable();
                    if (gameInput.asset != null)
                    {
                        // 生成的 GameInput.Dispose() 内部固定写死 Object.Destroy，非 Play 模式调用会打 Error；
                        // 和 InputService 一样改走 DestroyImmediate 销毁同一个 asset。
                        Object.DestroyImmediate(gameInput.asset);
                    }

                    gameInput = null;
                }
            }
            finally
            {
                base.TearDown();
            }
        }

        [Test]
        public void QueuedTame_PreservesConsecutivePresses()
        {
            var source = new LiveInputSource(new UnreadyInput());
            source.QueueSelection(2, InputCommand.ButtonTame);
            source.Sample(0);
            Assert.That(source.Current.HasButton(InputCommand.ButtonTamePressed), Is.True);
            source.QueueSelection(3, InputCommand.ButtonTame);
            source.Sample(1);
            Assert.That(source.Current.HasButton(InputCommand.ButtonTamePressed), Is.True);
            Assert.That(source.Current.Axis1.x, Is.EqualTo(3f));
            source.Sample(2);
            Assert.That(source.Current.HasButton(InputCommand.ButtonTamePressed), Is.False);
        }

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

        /// <summary>
        /// 软件侧长按位与设备采样是 OR，不是二选一：两边都按下时按钮位必须同时带着。
        /// 加了新动作位之后这条尤其要守——新位若把「<c>HeldButtons</c> 当初值」那行的语义改掉，
        /// 屏上奔跑钮会在按住别的键时失灵，而那种失误在回放里看不出来。
        /// </summary>
        [Test]
        public void Sample_WithHeldButtonsAndPressedAction_OrsBothSides()
        {
            LiveInputSource source = CreateWiredSource(("Tame", Key.Numpad7));

            source.HeldButtons = InputCommand.ButtonRun;
            PressKeys(Key.Numpad7);
            source.Sample(0);

            Assert.That(
                source.Current.Buttons,
                Is.EqualTo(InputCommand.ButtonRun | InputCommand.ButtonTame | InputCommand.ButtonTamePressed),
                "软件侧长按位与设备动作位应当是 OR 关系");
        }

        /// <summary>
        /// 新接进内核的三路（Tame / Interact / Inventory）按下时各自置位。
        /// <b>这三条判据就是「新动作在采样里不再丢失」</b>：把 <see cref="LiveInputSource.Sample"/> 里
        /// 对应那三段 <c>if</c> 删掉，它们当场变红。
        /// </summary>
        [Test]
        public void Sample_WithNewActionsPressed_SetsTheirBits()
        {
            LiveInputSource source = CreateWiredSource(
                ("Tame", Key.Numpad7), ("Interact", Key.Numpad8), ("Inventory", Key.Numpad9));

            PressKeys(Key.Numpad7, Key.Numpad8, Key.Numpad9);
            source.Sample(0);

            const uint newBits =
                InputCommand.ButtonTame | InputCommand.ButtonTamePressed | InputCommand.ButtonInteract | InputCommand.ButtonInventory;

            Assert.That(source.Current.HasButton(InputCommand.ButtonTame), Is.True, "Gameplay/Tame 按下后附身位没置起来，这一路会被静默丢");
            Assert.That(source.Current.HasButton(InputCommand.ButtonInteract), Is.True, "Gameplay/Interact 按下后交互位没置起来，这一路会被静默丢");
            Assert.That(source.Current.HasButton(InputCommand.ButtonInventory), Is.True, "Gameplay/Inventory 按下后背包位没置起来，这一路会被静默丢");
            Assert.That(source.Current.Buttons, Is.EqualTo(newBits), "只按了这三个动作，按钮位应恰好是这三个位");
        }

        [Test]
        public void Sample_WithSingleNewActionPressed_SetsOnlyThatBit()
        {
            LiveInputSource source = CreateWiredSource(
                ("Tame", Key.Numpad7), ("Interact", Key.Numpad8), ("Inventory", Key.Numpad9));

            PressKeys(Key.Numpad8);
            source.Sample(0);

            Assert.That(
                source.Current.Buttons,
                Is.EqualTo(InputCommand.ButtonInteract),
                "只按了交互键，同一个新动作组里别的位不该跟着亮");
        }

        [Test]
        public void Sample_AfterNewActionsReleased_ClearsTheirBits()
        {
            LiveInputSource source = CreateWiredSource(
                ("Tame", Key.Numpad7), ("Interact", Key.Numpad8), ("Inventory", Key.Numpad9));

            PressKeys(Key.Numpad7, Key.Numpad8, Key.Numpad9);
            source.Sample(0);
            Assert.That(source.Current.Buttons, Is.Not.EqualTo(0u), "前置条件没成立：三个动作没被采到，后面的清除断言验不出东西");

            ReleaseKeys();
            source.Sample(1);

            Assert.That(source.Current.Buttons, Is.EqualTo(0u), "三个新动作松开后，下一 tick 一个位都不该残留");
        }

        /// <summary>
        /// 「以后不会再踩」那条守卫：真动作图（生成的 <see cref="GameInput"/>）里的每个 Gameplay 动作，
        /// 要么接进了内核，要么在 <see cref="LiveInputSource"/> 的豁免表里被明确点名。
        /// 动作图加了新动作而没人接时这条当场变红——以前那种情况是采样静默丢到回放分叉为止。
        /// </summary>
        [Test]
        public void GameplayMap_EveryActionIsEitherWiredOrExplicitlyExempt()
        {
            gameInput = new GameInput();

            List<string> unmapped = LiveInputSource.FindUnmappedGameplayActions(gameInput.asset);

            Assert.That(
                unmapped,
                Is.Empty,
                "Gameplay 动作图里有动作既没接进 InputCommand，也没被明确豁免；"
                + "没接的表现是采样静默丢掉这一路、回放跟着分叉，而且全程不报错。"
                + "要么照 InputCommand 现有位的样子补一个位并在 LiveInputSource.Sample 里接上，"
                + "要么把名字加进 LiveInputSource.ExemptActionNames 并写清为什么不进内核。");
        }

        /// <summary>
        /// 上一条守卫的<b>可证伪锚点</b>：喂一份「有动作没人接」的动作图，它必须点名报出来。
        /// 没有这条，上一条返回空列表也可能只是因为这个守卫什么都没查。
        /// </summary>
        [Test]
        public void FindUnmappedGameplayActions_WithActionNobodyWired_NamesEveryOneOfThem()
        {
            InputActionAsset asset = ScriptableObject.CreateInstance<InputActionAsset>();
            try
            {
                InputActionMap map = asset.AddActionMap("Gameplay");
                map.AddAction("Tame", InputActionType.Button);
                map.AddAction("Immersive", InputActionType.Button);
                map.AddAction("SomethingNew", InputActionType.Button);
                map.AddAction("AnotherNew", InputActionType.Button);

                List<string> unmapped = LiveInputSource.FindUnmappedGameplayActions(asset);

                Assert.That(
                    unmapped,
                    Is.EqualTo(new[] { "SomethingNew", "AnotherNew" }),
                    "已接的动作与被豁免的动作都不该被报，其余要一个不落地按动作图里的顺序报出来");
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        /// <summary>动作集缺席时守卫不报，也不抛。</summary>
        [Test]
        public void FindUnmappedGameplayActions_WithNullAsset_ReturnsEmpty()
        {
            Assert.That(LiveInputSource.FindUnmappedGameplayActions(null), Is.Empty);
        }

        /// <summary>
        /// 造一个连到真动作集的输入源，并给指定动作各补一条我们自己的键位绑定。
        /// <para>
        /// 为什么要自己加绑定：动作图里的绑定随时会改（改绑定是策划的日常），而这里要守的是
        /// 「动作名 → 内核位」这段接线。绑到哪个键由用例自己说了算，动作名照旧取真动作图上的名字——
        /// 名字对不上时 LiveInputSource 缓存动作那一步会记 Warn、槽位留空，用例随即变红。
        /// </para>
        /// </summary>
        private LiveInputSource CreateWiredSource(params (string ActionName, Key Key)[] bindings)
        {
            gameInput = new GameInput();
            keyboard = InputSystem.AddDevice<Keyboard>("LiveInputSourceTestsKeyboard");

            foreach ((string actionName, Key key) in bindings)
            {
                InputAction action = gameInput.asset.FindAction("Gameplay/" + actionName, true);
                action.AddBinding(keyboard[key].path);
            }

            gameInput.asset.FindActionMap("Gameplay", true).Enable();

            var source = new LiveInputSource(new ReadyInput(gameInput));
            source.Initialize();
            Assert.That(source.IsReady, Is.True, "动作引用没缓存成功，下面的采样断言什么都验不出来");

            return source;
        }

        /// <summary>按住这几个键（逐个推设备状态，每次推完输入系统处理一遍），并记下来好一起松开。</summary>
        private void PressKeys(params Key[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                Press(keyboard[keys[i]]);
                heldKeys.Add(keys[i]);
            }
        }

        /// <summary>把 <see cref="PressKeys"/> 按住的键全部松开。</summary>
        private void ReleaseKeys()
        {
            for (int i = 0; i < heldKeys.Count; i++)
            {
                Release(keyboard[heldKeys[i]]);
            }

            heldKeys.Clear();
        }

        /// <summary>尚未就绪的输入服务：Actions 为 null，走「启动期还没轮到」的静默分支。</summary>
        private sealed class UnreadyInput : IInputService
        {
            public GameInput Actions => null;
            public void EnableMap(string map) { }
            public void DisableMap(string map) { }
        }

        [Test]
        public void QueueSelection_IsRecordedForOneTickAndClearedOnUnload()
        {
            var source = new LiveInputSource(new UnreadyInput());
            source.QueueSelection(3, InputCommand.ButtonSelectControl);
            source.Sample(0);
            Assert.That(source.Current.Axis1.x, Is.EqualTo(3f));
            Assert.That(source.Current.HasButton(InputCommand.ButtonSelectControl), Is.True);
            source.Sample(1);
            Assert.That(source.Current.Axis1, Is.EqualTo(Vector2.zero));
            Assert.That(source.Current.HasButton(InputCommand.ButtonSelectControl), Is.False);
            source.QueueSelection(2, InputCommand.ButtonTame);
            source.ClearQueuedSelection();
            source.Sample(2);
            Assert.That(source.Current.Buttons, Is.Zero);
        }

        /// <summary>已经把动作集交出去的替身：只负责把 Actions 递过去，不做别的。</summary>
        private sealed class ReadyInput : IInputService
        {
            private readonly GameInput actions;

            public ReadyInput(GameInput actions)
            {
                this.actions = actions;
            }

            public GameInput Actions => actions;
            public void EnableMap(string map) { }
            public void DisableMap(string map) { }
        }
    }
}
