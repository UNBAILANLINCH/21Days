// 职责：ShowcaseScenario 的「像玩家一样操作」部分（partial）——虚拟输入设备入口 Input、读玩家位置、
//   按摇杆走到某点 / 朝某方向走一段并等停稳、点刚打开的界面上的按钮（ClickWhenReady）。
//   移动全部经 ShowcaseInputDriver 走 Input System 真实路径，不瞬移、不直接调规则。
//
// 用法（先 EnterWorldFromTitle 进世界）：
//   yield return WalkTo(new Vector2(7.5f, 4.9f));                  // 逐帧朝目标推摇杆，到 1 m 内松杆、等停稳；超时记失败继续
//   yield return Walk(Vector2.right, 1f);                            // 向 +X 推 1 秒，松杆、等停稳
//   yield return Input.Press(inputService.Actions.Gameplay.Interact); // 按一下「交互」（按动作，不按键位）
//   Vector2 p = PlayerPosition;                                      // 玩家逻辑坐标 = 场景 (x, z)
//   yield return ClickWhenReady("确认弹窗关闭", () => confirmButton, () => popupClosed); // 界面刚打开时补点到生效
//
// 坐标约定（已按源码核实）：玩家逻辑坐标 PlayerModel.Position 是场景 XZ 平面的 Vector2（y = 场景 z，
//   见 EncounterSceneView.ToScenePosition）；摇杆 Gameplay/Move 的值原样进 PlayerIntent.Movement 加到 Position 上
//   （EncounterStep.Step → PlayerRules.Step），所以摇杆 +y 就是场景 +z，方向直接用 (target - position)。
//
// 为什么新建（project-root.md「加能力的顺序」）：
//   复用 —— 此前只有 Exploration / Session 各自的 PushStick / WaitStable，其它 Showcase 只能 playerRules.Reset 瞬移。
//   扩展 —— 要用基类的 WaitUntil 记失败、要在 TearDown 里释放设备，只能落在基类；但与报告引擎、Boot 流程职责不同，
//           单独成一个 partial 文件。
using System;
using System.Collections;
using System.Globalization;
using Game.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests.Showcase
{
    public abstract partial class ShowcaseScenario
    {
        /// <summary>松杆后判定「停稳」：连续这么多帧位置不变。</summary>
        private const int StableFrames = 5;

        /// <summary>等停稳的上限（真实秒）。</summary>
        private const float StableTimeoutSeconds = 2f;

        private ShowcaseInputDriver input;

        /// <summary>
        /// 虚拟输入设备（Gamepad + Keyboard），懒建；基类 TearDown 统一 Dispose，模块作者不用管设备生命周期。
        /// 注意它在派生类里遮住了 <c>UnityEngine.Input</c>——回放本来就不许读 <c>Input.*</c>。
        /// </summary>
        protected ShowcaseInputDriver Input
        {
            get
            {
                if (input == null)
                {
                    input = new ShowcaseInputDriver(Module);
                }

                return input;
            }
        }

        /// <summary>
        /// 玩家逻辑坐标（场景 XZ）：<c>ResolveService&lt;PlayerModel&gt;().Position</c>。
        /// 取不到（没 Boot、还没进世界）返回 <see cref="Vector2.zero"/> 并 LogWarning。
        /// </summary>
        protected Vector2 PlayerPosition
        {
            get
            {
                PlayerModel model = ResolveService<PlayerModel>();
                if (model == null)
                {
                    Debug.LogWarning($"{ShowcaseOptions.Prefix}[{Module}] 取不到 PlayerModel（没走 Boot，或还没进世界），PlayerPosition 按 (0, 0)");
                    return Vector2.zero;
                }

                return model.Position;
            }
        }

        /// <summary>
        /// 用摇杆走到 <paramref name="target"/>：逐帧朝目标推（方向 = 目标 − 当前位置，归一化），
        /// 进入 <paramref name="stopDistance"/> 内就松杆并等停稳。
        /// 超时（被挡住、Gameplay 动作图被关）按 <c>WaitUntil</c> 的口径记一条失败并继续，同样松杆等停稳。
        /// 这是「走」不是「瞬移」：路上会被碰撞挡、会触发沿途的交互半径，路线要自己保证是通的。
        /// </summary>
        /// <param name="target">目标点（玩家逻辑坐标，= 场景 (x, z)）。</param>
        /// <param name="stopDistance">离目标多近算到（米）。交互类目标用交互半径以内的值。</param>
        /// <param name="timeout">最多走多久（真实秒，不乘节奏倍率）。</param>
        protected IEnumerator WalkTo(Vector2 target, float stopDistance = 1f, float timeout = 10f)
        {
            string what = $"走到 {FormatPoint(target)} 附近（≤ {stopDistance.ToString("0.##", CultureInfo.InvariantCulture)} m）";
            PlayerModel model = ResolveService<PlayerModel>();
            if (model == null)
            {
                Debug.LogWarning($"{ShowcaseOptions.Prefix}[{Module}] WalkTo 取不到 PlayerModel，没法走");
                yield return WaitUntil(what, () => false, 0f);
                yield break;
            }

            yield return WaitUntil(
                what,
                () =>
                {
                    Vector2 delta = target - model.Position;
                    if (delta.magnitude <= stopDistance)
                    {
                        return true;
                    }

                    Input.SetStick(delta.normalized);
                    return false;
                },
                timeout);

            Input.ReleaseStick();
            yield return WaitPlayerStable(model);
        }

        /// <summary>
        /// 朝 <paramref name="direction"/> 推摇杆 <paramref name="seconds"/> 秒（转调 <see cref="ShowcaseInputDriver.HoldStick"/>），
        /// 松杆后等玩家停稳（连续 5 帧不动，最多 2 秒），免得检查点 / 截图落在滑行途中。
        /// </summary>
        /// <param name="direction">摇杆方向：x = 场景 +X，y = 场景 +Z。</param>
        protected IEnumerator Walk(Vector2 direction, float seconds)
        {
            yield return Input.HoldStick(direction, seconds);
            yield return WaitPlayerStable(ResolveService<PlayerModel>());
        }

        /// <summary>等玩家停稳：连续 5 帧位置不变，最多 2 秒（真实时间）。取不到玩家就只等一帧。</summary>
        protected IEnumerator WaitPlayerStable()
        {
            yield return WaitPlayerStable(ResolveService<PlayerModel>());
        }

        private static IEnumerator WaitPlayerStable(PlayerModel model)
        {
            if (model == null)
            {
                yield return null;
                yield break;
            }

            Vector2 last = model.Position;
            int still = 0;
            float deadline = Time.realtimeSinceStartup + StableTimeoutSeconds;
            while (still < StableFrames && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                Vector2 now = model.Position;
                still = (now - last).sqrMagnitude < 1e-8f ? still + 1 : 0;
                last = now;
            }
        }

        /// <summary>
        /// 点一个刚打开的界面上的按钮，直到点击生效：界面一登记进 UI 服务就能取到按钮，但控制器要等打开流程走完才订阅点击，
        /// 太早 <c>onClick.Invoke()</c> 会被吞掉（2026-09-28 CharacterPuppet 时停用例实测：跳过确认弹窗一直不关）。
        /// 所以每隔 <paramref name="retryInterval"/> 真实秒点一次（按钮为 null 或不可交互时跳过这次），
        /// 直到 <paramref name="effective"/> 为真；超时按 <c>WaitUntil</c> 口径记一条失败并继续。
        /// 报告里记成一条等待：「等到「<paramref name="what"/>」」。
        /// </summary>
        /// <param name="what">生效条件的中文描述，写「应该看到什么」，如「确认弹窗关闭」。</param>
        /// <param name="button">每次点之前重新取按钮（界面可能重建）。</param>
        /// <param name="effective">点击已生效的判定；先于点击求值，已生效就不再点。</param>
        /// <param name="timeout">最多补点多久（真实秒，不乘节奏倍率）。</param>
        /// <param name="retryInterval">两次点击的间隔（真实秒）。</param>
        protected IEnumerator ClickWhenReady(string what, Func<Button> button, Func<bool> effective,
            float timeout = 3f, float retryInterval = 0.3f)
        {
            float nextClick = 0f;
            yield return WaitUntil(
                what,
                () =>
                {
                    if (effective())
                    {
                        return true;
                    }

                    if (Time.realtimeSinceStartup >= nextClick)
                    {
                        nextClick = Time.realtimeSinceStartup + retryInterval;
                        Button target = button();
                        if (target != null && target.interactable)
                        {
                            target.onClick.Invoke();
                        }
                    }

                    return false;
                },
                timeout);
        }

        /// <summary>TearDown 用：释放虚拟设备，可重复调用。</summary>
        private void DisposeInput()
        {
            if (input != null)
            {
                input.Dispose();
                input = null;
            }
        }

        private static string FormatPoint(Vector2 point)
        {
            return $"({point.x.ToString("0.##", CultureInfo.InvariantCulture)}, {point.y.ToString("0.##", CultureInfo.InvariantCulture)})";
        }
    }
}
