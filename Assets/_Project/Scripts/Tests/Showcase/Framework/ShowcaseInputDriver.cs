// 职责：回放用的虚拟输入设备——一只虚拟 Gamepad（推摇杆）+ 一只虚拟 Keyboard（按动作），
//   事件走 Input System 的真实路径（设备状态 → 动作图 → LiveInputSource → InputCommand → SimulationRunner → 玩家规则 / UI 路由），
//   让回放像玩家一样操作，而不是瞬移 + 直接调接口。
//
// 用法（模块作者一律经 ShowcaseScenario.Input 拿实例，不要自己 new；基类 TearDown 会 Dispose）：
//   yield return Input.HoldStick(Vector2.right, 1f);                    // 推左摇杆 1 秒后回中
//   yield return Input.Press(inputService.Actions.Gameplay.Interact);   // 按一下「交互」，键位从动作绑定里查（至少跨一个逻辑 tick）
//   yield return Input.PressUntil(actions.Gameplay.Run, () => player.IsRunning); // 按住直到生效（最多 0.5 秒）再松——Gameplay 开关键首选
//   yield return Input.Hold(inputService.Actions.Gameplay.Sneak);       // 按住潜行……
//   yield return Input.Release(inputService.Actions.Gameplay.Sneak);    // ……松开
//   逐帧自己控制方向（边走边采样）：循环里每帧 Input.SetStick(dir); yield return null;，结束 Input.ReleaseStick()。
//
// 编辑器注意：基类 SetUp 临时放开输入焦点限制，TearDown / 退出 Play 恢复原设置；切走窗口不会吞虚拟键盘事件。
//
// 两个实测过的坑（2026-09-28 Player / Monster / Disguise 等五份回放改走虚拟输入时踩到）：
//   ① 设备要一起建：新设备加入会让 Input System 重新解析所有动作的绑定并复位动作状态，
//      「先按住潜行（这时才建键盘）再推摇杆（这时才建手柄）」时潜行在建手柄那一刻被复位掉。
//      所以第一次用任一设备就同时建出手柄和键盘（EnsureDevices），EnterWorldFromTitle 点「开始」前也会 Prime 一次。
//   ② 按键要跨逻辑 tick：LiveInputSource 只在 60 Hz 逻辑 tick 上读 IsPressed、没有按下沿锁存，
//      编辑器高帧率时「按住两帧」（几毫秒）可能整段落在两个 tick 之间被吞。所以 Press 至少按住 MinPressFrames 帧
//      且 MinPressSeconds 真实秒；要「按到生效为止」用 PressUntil。
//
// 为什么新建（project-root.md「加能力的顺序」）：
//   复用 —— Exploration / Session 各自复制了一份 PushStick / PressEscape（按键位写死 Key.Escape），
//           别的 Showcase 没有可复用的公共件，只能瞬移或直接调接口；没有现成的类可以直接拿来用。
//   扩展 —— 塞进 ShowcaseScenario 会让基类同时管报告节奏和设备生命周期（AddDevice / RemoveDevice / 已按住的控件），
//           职责说不通；单独一个类，由基类懒建、TearDown 统一 Dispose。
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Game.Tests.Showcase
{
    /// <summary>
    /// 回放用虚拟输入设备。设备懒建（手柄与键盘一起建），<see cref="Dispose"/> 时 <c>InputSystem.RemoveDevice</c>。
    /// <para>所有写入都用 <c>StateEvent.From</c> 拷一份设备当前状态、只改目标控件再入队，
    /// 所以按住的键（Sneak 的 leftShift）不会被另一次按键（Interact 的 e）抹掉，推摇杆也不会抹掉手柄按键。</para>
    /// <para>找不到绑定、设备建不出来一律 <c>Debug.LogWarning</c>（前缀 <see cref="ShowcaseOptions.Prefix"/>），
    /// 不 LogError——Test Framework 会把 LogError 当失败当场打断，报告就写不出来了。</para>
    /// </summary>
    public sealed class ShowcaseInputDriver : IDisposable
    {
        private const string KeyboardPrefix = "<Keyboard>";
        private const string GamepadPrefix = "<Gamepad>";

        /// <summary>Press 至少按住的帧数：让按下事件与松开事件落在不同的输入更新里。</summary>
        private const int MinPressFrames = 2;

        /// <summary>
        /// Press 至少按住的真实秒数：比 60 Hz 逻辑步长（约 16.7 ms）多半步，保证按住期间至少跨过一次逻辑 tick 采样
        /// （LiveInputSource 只在 tick 上读 IsPressed，没有按下沿锁存；理由见文件头坑②）。
        /// </summary>
        private const float MinPressSeconds = 0.025f;

        private readonly string module;
        private readonly List<InputControl> held = new List<InputControl>();

        private Gamepad pad;
        private Keyboard keyboard;
        private bool disposed;

        /// <param name="module">模块名，只用于日志前缀。</param>
        public ShowcaseInputDriver(string module)
        {
            this.module = module;
        }

        /// <summary>
        /// 推左摇杆 <paramref name="seconds"/> 秒（真实时间），到时回中并等两帧。
        /// 每帧重新入队一次（防止被其它设备的回中事件覆盖）。驱动的是 &lt;Gamepad&gt;/leftStick，即 Gameplay/Move。
        /// </summary>
        /// <param name="direction">摇杆方向，长度 ≤ 1。x = 场景 +X，y = 场景 +Z（玩家逻辑坐标的 y）。</param>
        public IEnumerator HoldStick(Vector2 direction, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                SetStick(direction);
                yield return null;
            }

            ReleaseStick();
            yield return null;
            yield return null;
        }

        /// <summary>
        /// 单帧写入左摇杆方向。只对下一次输入更新生效：要保持推着，<b>每帧都要调一次</b>（给逐帧改方向的 WalkTo 这类用）；
        /// 结束时调 <see cref="ReleaseStick"/>。
        /// </summary>
        public void SetStick(Vector2 direction)
        {
            Gamepad device = EnsurePad();
            if (device == null)
            {
                return;
            }

            WriteControl(device, device.leftStick, direction);
        }

        /// <summary>左摇杆回中（写一次即可，回中状态会保持）。</summary>
        public void ReleaseStick()
        {
            if (pad == null)
            {
                return;
            }

            WriteControl(pad, pad.leftStick, Vector2.zero);
        }

        /// <summary>
        /// 把手柄和键盘一起建出来（已建过则什么都不做）。任一设备第一次使用时也会自动调用，
        /// 这里单独公开给「在按住任何键之前先把设备建齐」的时机用（EnterWorldFromTitle 点「开始」前调一次）。理由见文件头坑①。
        /// </summary>
        public void Prime()
        {
            EnsureDevices();
        }

        /// <summary>
        /// 按一下某个动作：按下 → 按住至少 <see cref="MinPressFrames"/> 帧且至少 <see cref="MinPressSeconds"/> 真实秒 → 松开 → 等一帧。
        /// 键位从 <paramref name="action"/> 的绑定里查（先 &lt;Keyboard&gt;，再 &lt;Gamepad&gt;；复合绑定跳过），
        /// 所以回放里写 <c>Input.Press(actions.Gameplay.Interact)</c>，改键位不用改回放。
        /// 找不到可用绑定只 LogWarning 并直接返回。
        /// </summary>
        public IEnumerator Press(InputAction action)
        {
            InputControl control = ResolveControl(action, "按下");
            if (control == null)
            {
                yield break;
            }

            WriteControl(control.device, control, 1f);
            int frames = 0;
            float pressedAt = Time.realtimeSinceStartup;
            while (frames < MinPressFrames || Time.realtimeSinceStartup - pressedAt < MinPressSeconds)
            {
                yield return null;
                frames++;
            }

            WriteControl(control.device, control, 0f);
            yield return null;
        }

        /// <summary>
        /// 按一下虚拟键盘上的某个键（不经动作表），节奏同 <see cref="Press"/>。只给「任意键继续」这类设备级闸门用
        /// （开局操作说明 TutorialView 读 <c>Keyboard.anyKey</c>，没有对应动作）；玩法与界面操作一律走 <see cref="Press"/>。
        /// </summary>
        public IEnumerator PressKey(Key key)
        {
            Keyboard device = EnsureKeyboard();
            InputControl control = device == null ? null : device[key];
            if (control == null)
            {
                Warn($"按键 {key} 失败：虚拟键盘不可用");
                yield break;
            }

            WriteControl(device, control, 1f);
            int frames = 0;
            float pressedAt = Time.realtimeSinceStartup;
            while (frames < MinPressFrames || Time.realtimeSinceStartup - pressedAt < MinPressSeconds)
            {
                yield return null;
                frames++;
            }

            WriteControl(device, control, 0f);
            yield return null;
        }

        /// <summary>
        /// 按住某个动作直到 <paramref name="changed"/> 成立（最多 <paramref name="maxSeconds"/> 真实秒）再松开、等一帧。
        /// Gameplay 开关键（走跑、伪装、攻击）首选它：按住时长跟着逻辑 tick 走，不会被漏采，也不会按太久连触发。
        /// 按住期间 <paramref name="changed"/> 每帧求值一次；超时不记失败（调用方随后自己 Check 结果）。
        /// </summary>
        /// <param name="changed">「按下已生效」的判定，例如 <c>() =&gt; player.IsRunning</c>。</param>
        /// <param name="maxSeconds">最多按住多久（真实秒，不乘节奏倍率）。</param>
        public IEnumerator PressUntil(InputAction action, Func<bool> changed, float maxSeconds = 0.5f)
        {
            yield return Hold(action);
            float deadline = Time.realtimeSinceStartup + maxSeconds;
            while (!changed() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            yield return Release(action);
            yield return null;
        }

        /// <summary>
        /// 按住某个动作（潜行这类要持续按着的），等两帧让它生效后返回；按住状态跨帧保持，直到 <see cref="Release"/> 或 <see cref="Dispose"/>。
        /// 按住期间再 <see cref="Press"/> 别的动作不会把它抹掉。
        /// </summary>
        public IEnumerator Hold(InputAction action)
        {
            InputControl control = ResolveControl(action, "按住");
            if (control == null)
            {
                yield break;
            }

            WriteControl(control.device, control, 1f);
            if (!held.Contains(control))
            {
                held.Add(control);
            }

            yield return null;
            yield return null;
        }

        /// <summary>松开之前 <see cref="Hold"/> 住的动作，等一帧。没按住过也照样写一次 0（无副作用）。</summary>
        public IEnumerator Release(InputAction action)
        {
            InputControl control = ResolveControl(action, "松开");
            if (control == null)
            {
                yield break;
            }

            WriteControl(control.device, control, 0f);
            held.Remove(control);
            yield return null;
        }

        /// <summary>移除两只虚拟设备（按住的键随设备一起消失）。可重复调用。</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            held.Clear();
            if (pad != null)
            {
                if (pad.added)
                {
                    InputSystem.RemoveDevice(pad);
                }

                pad = null;
            }

            if (keyboard != null)
            {
                if (keyboard.added)
                {
                    InputSystem.RemoveDevice(keyboard);
                }

                keyboard = null;
            }
        }

        // ───────────────────────── 内部 ─────────────────────────

        private Gamepad EnsurePad()
        {
            if (disposed)
            {
                Warn("虚拟输入设备已释放，忽略这次输入（TearDown 之后还在推摇杆？）");
                return null;
            }

            EnsureDevices();
            return pad;
        }

        private Keyboard EnsureKeyboard()
        {
            if (disposed)
            {
                Warn("虚拟输入设备已释放，忽略这次输入（TearDown 之后还在按键？）");
                return null;
            }

            EnsureDevices();
            return keyboard;
        }

        /// <summary>手柄和键盘一起建（理由见文件头坑①）；已释放时什么都不做。</summary>
        private void EnsureDevices()
        {
            if (disposed)
            {
                return;
            }

            if (pad == null)
            {
                pad = InputSystem.AddDevice<Gamepad>("ShowcaseGamepad");
            }

            if (keyboard == null)
            {
                keyboard = InputSystem.AddDevice<Keyboard>("ShowcaseKeyboard");
            }
        }

        /// <summary>
        /// 从动作绑定里挑出虚拟设备上对应的控件：先找第一条 &lt;Keyboard&gt;/… 绑定，找不到再找 &lt;Gamepad&gt;/…；
        /// 复合绑定（2DVector 本体与它的 part）跳过。用 effectivePath，玩家改过键也跟着走。
        /// </summary>
        private InputControl ResolveControl(InputAction action, string verb)
        {
            if (action == null)
            {
                Warn($"{verb}动作失败：动作为 null（输入服务还没初始化？）");
                return null;
            }

            string path = FindBindingPath(action, KeyboardPrefix);
            InputDevice device = null;
            if (path != null)
            {
                device = EnsureKeyboard();
            }
            else
            {
                path = FindBindingPath(action, GamepadPrefix);
                if (path != null)
                {
                    device = EnsurePad();
                }
            }

            if (path == null)
            {
                Warn($"{verb}动作「{action.actionMap?.name}/{action.name}」失败：没有可用的 <Keyboard>/ 或 <Gamepad>/ 单键绑定");
                return null;
            }

            if (device == null)
            {
                return null;
            }

            InputControl control = InputControlPath.TryFindControl(device, path);
            if (control == null)
            {
                Warn($"{verb}动作「{action.actionMap?.name}/{action.name}」失败：虚拟 {device.layout} 上找不到控件 {path}");
            }

            return control;
        }

        private static string FindBindingPath(InputAction action, string devicePrefix)
        {
            IReadOnlyList<InputBinding> bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                InputBinding binding = bindings[i];
                if (binding.isComposite || binding.isPartOfComposite)
                {
                    continue;
                }

                string path = binding.effectivePath;
                if (!string.IsNullOrEmpty(path) && path.StartsWith(devicePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }

            return null;
        }

        /// <summary>
        /// 拷一份设备当前状态做成事件，只改 <paramref name="control"/> 的值再入队——其它已按住的控件原样保留。
        /// </summary>
        private void WriteControl<TValue>(InputDevice device, InputControl control, TValue value) where TValue : struct
        {
            if (device == null || control == null || !device.added)
            {
                return;
            }

            try
            {
                using (StateEvent.From(device, out InputEventPtr eventPtr))
                {
                    control.WriteValueIntoEvent(value, eventPtr);
                    InputSystem.QueueEvent(eventPtr);
                }
            }
            catch (Exception e)
            {
                Warn($"往虚拟 {device.layout} 写 {control.path} 失败：{e.GetType().Name}：{e.Message}");
            }
        }

        private void Warn(string message)
        {
            Debug.LogWarning($"{ShowcaseOptions.Prefix}[{module}] 虚拟输入：{message}");
        }
    }
}
