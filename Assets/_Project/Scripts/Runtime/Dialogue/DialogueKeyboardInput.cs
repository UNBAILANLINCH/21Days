// 职责：对白的键盘 / 手柄路径——对白进行中每帧读 Dialogue 动作图（外加 UI/Cancel），把按键翻成与点击完全相同的处理
//   （推进 = 点对话框、自动 / 倍速 / 跳过 / 历史 = 点对应按钮、数字键 = 点第 N 个选项），弹窗打开时只放行弹窗键位。
// 为什么新建：DialogueView 由 Addressables 实例化、不注入服务，按规则不能在 View 里读输入；DialogueController 是纯表现驱动、
//   不依赖输入服务（EditMode 测试直接 new 它），把读动作塞进去会让它多一个 IInputService 依赖且没法单测映射；
//   DialogueInteractionFocus 只管「对白开始前」的焦点与交互键（E / F），职责不同。所以单列一个 ITickable 协作者，
//   「哪个状态下哪个键有效」抽成静态纯函数 Map，EditMode 直接测。
using System;
using System.Collections.Generic;
using Game.Core.Input;
using UnityEngine.InputSystem;
using VContainer.Unity;

namespace Game.Dialogue
{
    /// <summary>
    /// 对白键位入口点。只在 <see cref="DialogueController.IsRunning"/> 时读动作；Dialogue 图由 <see cref="DialogueService"/>
    /// 在对白期间开关，本类不开关任何动作图。
    /// </summary>
    public sealed class DialogueKeyboardInput : ITickable
    {
        /// <summary>对白里会用到的键位（与 Dialogue 动作图一一对应，另加 UI/Cancel）。</summary>
        public enum Key { Advance, Auto, Speed, Skip, History, Cancel, Choice1, Choice2, Choice3, Choice4 }

        /// <summary>按键翻译出的处理动作。</summary>
        public enum CommandKind { None, Tap, ToggleAuto, CycleSpeed, RequestSkip, OpenHistory, CloseHistory, CancelSkip, Choose }

        /// <summary>映射需要的对白状态快照（由 Controller 提供）。</summary>
        public readonly struct State
        {
            public State(bool active, bool ready, bool awaitingChoice, bool historyOpen, bool skipConfirmOpen,
                IReadOnlyList<bool> choiceRows)
            {
                Active = active;
                Ready = ready;
                AwaitingChoice = awaitingChoice;
                HistoryOpen = historyOpen;
                SkipConfirmOpen = skipConfirmOpen;
                ChoiceRows = choiceRows;
            }

            /// <summary>对白在展示中且未被外部挂起。</summary>
            public bool Active { get; }
            /// <summary>当前节点已准备完毕（可收输入）。</summary>
            public bool Ready { get; }
            /// <summary>规则处于等待选择阶段（选项已显示）。</summary>
            public bool AwaitingChoice { get; }
            public bool HistoryOpen { get; }
            public bool SkipConfirmOpen { get; }
            /// <summary>当前显示的选项行（顺序同界面），值为该行是否可用；隐藏的不可用选项不在其中。可为 null。</summary>
            public IReadOnlyList<bool> ChoiceRows { get; }
        }

        /// <summary>处理动作；<see cref="Row"/> 只对 <see cref="CommandKind.Choose"/> 有意义（显示行下标，从 0 起）。</summary>
        public readonly struct Command
        {
            public static readonly Command None = new Command(CommandKind.None, -1);

            public Command(CommandKind kind, int row)
            {
                Kind = kind;
                Row = row;
            }

            public CommandKind Kind { get; }
            public int Row { get; }
        }

        private readonly DialogueController controller;
        private readonly IInputService input;
        private bool hintsApplied;

        public DialogueKeyboardInput(DialogueController controller, IInputService input)
        {
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
        }

        /// <summary>
        /// 纯映射：给定对白状态与按下的键，返回应执行的处理。规则：
        /// 未激活不响应；跳过确认弹窗开着只认 Cancel（= 点取消，确认走 UI Submit）；历史面板开着只认 History / Cancel（关闭）；
        /// 选项显示期间 Advance 不推进（Enter 由 UI Submit 点中选中的选项）；Choice N 只对显示中的第 N 行且可用时生效；
        /// 主面板上 Cancel 不做任何事（Esc 不能关对白）。
        /// </summary>
        public static Command Map(Key key, in State state)
        {
            if (!state.Active) return Command.None;
            if (state.SkipConfirmOpen)
                return key == Key.Cancel ? new Command(CommandKind.CancelSkip, -1) : Command.None;
            if (state.HistoryOpen)
                return key == Key.History || key == Key.Cancel ? new Command(CommandKind.CloseHistory, -1) : Command.None;
            switch (key)
            {
                case Key.Advance:
                    return state.Ready && !state.AwaitingChoice ? new Command(CommandKind.Tap, -1) : Command.None;
                case Key.Auto: return new Command(CommandKind.ToggleAuto, -1);
                case Key.Speed: return new Command(CommandKind.CycleSpeed, -1);
                case Key.Skip: return new Command(CommandKind.RequestSkip, -1);
                case Key.History: return new Command(CommandKind.OpenHistory, -1);
                case Key.Choice1: return MapChoice(0, in state);
                case Key.Choice2: return MapChoice(1, in state);
                case Key.Choice3: return MapChoice(2, in state);
                case Key.Choice4: return MapChoice(3, in state);
                default: return Command.None;
            }
        }

        /// <summary>
        /// 取动作的第一条键盘绑定的显示文字（跟随系统键盘布局，如「Ctrl」「A」）；没有键盘绑定返回空串。
        /// 动作图里绑定未分控制方案组，所以按绑定路径的设备前缀筛，不用 bindingGroup 掩码。
        /// </summary>
        public static string KeyboardHint(InputAction action)
        {
            if (action == null) return string.Empty;
            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding binding = action.bindings[i];
                if (binding.isComposite || binding.isPartOfComposite) continue;
                string path = binding.effectivePath;
                if (path == null || !path.StartsWith("<Keyboard>", StringComparison.Ordinal)) continue;
                return action.GetBindingDisplayString(i, InputBinding.DisplayStringOptions.DontIncludeInteractions);
            }
            return string.Empty;
        }

        public void Tick()
        {
            GameInput actions = input.Actions; // lint-ok: 对白表现层读动作，不进确定性模拟
            if (actions == null) return;
            if (!hintsApplied)
            {
                // 动作集在输入服务初始化后才有，所以键位提示在第一次拿到动作集时取一次交给 Controller，之后不再每帧取。
                hintsApplied = true;
                GameInput.DialogueActions dialogue = actions.Dialogue;
                controller.SetKeyHints(KeyboardHint(dialogue.Auto), KeyboardHint(dialogue.Speed),
                    KeyboardHint(dialogue.Skip), KeyboardHint(dialogue.History));
            }
            if (!controller.IsRunning) return;

            GameInput.DialogueActions map = actions.Dialogue;
            // Dialogue 图只在对白期间启用，关着时 WasPressedThisFrame 恒为 false；UI/Cancel 由 UIService 常开。
            if (map.Advance.WasPressedThisFrame()) controller.HandleKey(Key.Advance); // lint-ok: 对白表现层读动作，不进确定性模拟
            if (map.Auto.WasPressedThisFrame()) controller.HandleKey(Key.Auto); // lint-ok: 对白表现层读动作，不进确定性模拟
            if (map.Speed.WasPressedThisFrame()) controller.HandleKey(Key.Speed); // lint-ok: 对白表现层读动作，不进确定性模拟
            if (map.Skip.WasPressedThisFrame()) controller.HandleKey(Key.Skip); // lint-ok: 对白表现层读动作，不进确定性模拟
            if (map.History.WasPressedThisFrame()) controller.HandleKey(Key.History); // lint-ok: 对白表现层读动作，不进确定性模拟
            if (map.Choice1.WasPressedThisFrame()) controller.HandleKey(Key.Choice1); // lint-ok: 对白表现层读动作，不进确定性模拟
            if (map.Choice2.WasPressedThisFrame()) controller.HandleKey(Key.Choice2); // lint-ok: 对白表现层读动作，不进确定性模拟
            if (map.Choice3.WasPressedThisFrame()) controller.HandleKey(Key.Choice3); // lint-ok: 对白表现层读动作，不进确定性模拟
            if (map.Choice4.WasPressedThisFrame()) controller.HandleKey(Key.Choice4); // lint-ok: 对白表现层读动作，不进确定性模拟
            if (actions.UI.Cancel.WasPressedThisFrame()) controller.HandleKey(Key.Cancel); // lint-ok: 对白表现层读动作，不进确定性模拟
        }

        private static Command MapChoice(int row, in State state)
        {
            if (!state.Ready || !state.AwaitingChoice || state.ChoiceRows == null) return Command.None;
            if (row >= state.ChoiceRows.Count || !state.ChoiceRows[row]) return Command.None;
            return new Command(CommandKind.Choose, row);
        }
    }
}
