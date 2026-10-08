// 职责：开局「操作说明」的显示面板——全屏一张教程图（盖住点击），展示满最短时长后才淡入「按任意键继续」提示，
//   并等到玩家按下任意键 / 手柄键 / 点击屏幕后返回。只显示与等待，不记「该不该播」、不读配置；时长由调用方传进来。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：LoadingView 是切场景黑幕（自己管盖 / 揭两相，不等待输入）；ConfirmView 要两个按钮且只认鼠标点击，
//      「任意按键」表达不出来；NotificationView 是排队卡片、明确不挡点击。
//   2. 扩展不行：把等待逻辑塞进 NewGameTutorial（闸门）会让「读输入设备」散在业务服务里，
//      塞进任何现有视图都会让它背上与自身无关的「定时 + 等任意键」职责。
// 为什么在这里直接读 Input System 设备（Keyboard / Gamepad / Mouse / Touchscreen）而不是走动作表：
//   "任意按键"是设备级语义，动作表里没有也不需要这样一个动作；本类在 Core/UI（框架表现层），
//   不参与确定性模拟，不违反 architecture.md「玩法逻辑不直接读设备」那条（那条约束的是 Scripts/Runtime 的玩法推进）。
// 提示语用图片而不是 TMP 文字：运行时首次显示会把字形烘进动态字体资产（ai-docs/pitfalls.md「TMP Dynamic 字体资产…」），
//   而且这句提示将来会跟教程图一起换美术。

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using LitMotion.Extensions;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace Game.Core.UI.Views
{
    /// <summary>
    /// 开局操作说明视图。预制体 <c>Assets/_Project/Prefabs/UI/TutorialView.prefab</c>，
    /// Addressables 地址 <c>TutorialView</c>（UI 组）。Top 层、Esc 关不掉（顶层不进栈，关不关都由调用方决定）。
    /// <code>
    /// TutorialView view = await ui.OpenAsync&lt;TutorialView&gt;(null, ct);
    /// try { await view.WaitForDismissAsync(3f, ct); }
    /// finally { await ui.CloseAsync(view); }   // 关闭由调用方负责
    /// </code>
    /// <para>
    /// 最短展示时长内按键一律不响应（<see cref="CanDismiss"/>），过了才淡入提示并接受任意键：
    /// 玩家点「开始」的那一下鼠标 / 回车不会把教程顺手关掉。等待期间 <c>ct</c> 取消会抛
    /// <see cref="OperationCanceledException"/>，由调用方决定怎么收尾。
    /// </para>
    /// </summary>
    public sealed class TutorialView : UIView
    {
        [Tooltip("黑底（Image，raycastTarget 开着：挡掉底下的标题按钮）。可空，空则只有教程图。")]
        [SerializeField] private Image background;

        [Tooltip("教程图（Image，Preserve Aspect 勾上）。必填——没有它这个面板什么也不显示。")]
        [SerializeField] private Image content;

        [Tooltip("「按任意键继续」提示的 CanvasGroup（在教程图之上，最短时长到了才淡入）。可空。")]
        [SerializeField] private CanvasGroup hintGroup;

        [Tooltip("提示淡入的秒数（真实时间）。")]
        [Min(0.01f)]
        [SerializeField] private float hintFadeSeconds = 0.25f;

        private MotionHandle hintMotion;

        /// <summary>Top 层：压在标题（Panel 层）之上，且不进栈——不会有别的面板把它顶掉或跟着它一起消失。</summary>
        public override UILayer Layer => UILayer.Top;

        /// <summary>Top 层本来就不进栈，这里只是别让将来改成 Panel 层的人顺手把它压成半透明。</summary>
        public override bool IsFullScreen => false;

        /// <summary>Esc 不该关掉它（关了流程就卡在标题页不知道下一步），要退出只能走 <see cref="WaitForDismissAsync"/> 的任意键。</summary>
        public override bool CloseOnCancel => false;

        /// <summary>本次等待已经过去的秒数（真实时间），只给埋点看，不参与判定。</summary>
        public float ElapsedSeconds { get; private set; }

        /// <summary>
        /// 最短展示时长是否已满。未满时任何按键都不算数——点「开始」的那一下输入不该把教程一起关掉。
        /// 纯函数，EditMode 可测。
        /// </summary>
        public static bool CanDismiss(float elapsedSeconds, float minSeconds) => elapsedSeconds >= minSeconds;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            if (content == null)
            {
                throw new InvalidOperationException(
                    "TutorialView 预制体缺少 content（教程图）引用，检查 Prefabs/UI/TutorialView.prefab 的接线。");
            }

            ElapsedSeconds = 0f;
            StopHint();
            if (background != null)
            {
                background.raycastTarget = true;
            }

            HideHint();
            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            StopHint();
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 等满 <paramref name="minSeconds"/> 秒，再等玩家按任意键 / 手柄键 / 点一下屏幕，然后返回（不关面板，关闭由调用方负责）。
        /// 最短时长内不装任何输入监听：过了才出提示，提示出现即代表现在按下去有效。
        /// </summary>
        public async UniTask WaitForDismissAsync(float minSeconds, CancellationToken ct)
        {
            float elapsed = 0f;
            while (!CanDismiss(elapsed, minSeconds))
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                elapsed += Time.unscaledDeltaTime;
            }

            ElapsedSeconds = elapsed;
            ShowHint();
            await UniTask.WaitUntil(AnyDismissPressed, PlayerLoopTiming.Update, ct);
        }

        /// <summary>
        /// 这一帧有没有「任意键」按下：键盘任一键、手柄任一面键 / 肩键 / Start 这类按钮、鼠标任一键、触屏按下。
        /// <para>
        /// 手柄只认真正的按钮（<see cref="ButtonControl"/>）：摇杆推到底、扳机按一半都被建模成 AxisControl，
        /// 拿它们当「按键」会让手上还压着摇杆的玩家一进教程就被关掉。
        /// </para>
        /// 只读 <c>wasPressedThisFrame</c>：进面板之前一直按住的键不算数，玩家得重新按一下。
        /// </summary>
        private static bool AnyDismissPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                var controls = gamepad.allControls;
                for (int i = 0; i < controls.Count; i++)
                {
                    if (controls[i] is ButtonControl button && button.wasPressedThisFrame)
                    {
                        return true;
                    }
                }
            }

            Mouse mouse = Mouse.current;
            if (mouse != null
                && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame
                    || mouse.middleButton.wasPressedThisFrame))
            {
                return true;
            }

            Touchscreen touch = Touchscreen.current;
            return touch != null && touch.primaryTouch.press.wasPressedThisFrame;
        }

        private void ShowHint()
        {
            // CanvasGroup 是 UnityEngine.Object，判空只用 == null。
            if (hintGroup == null)
            {
                return;
            }

            StopHint();
            hintGroup.gameObject.SetActive(true);
            hintGroup.alpha = 0f;
            hintGroup.blocksRaycasts = false;
            hintGroup.interactable = false;

            // AddTo(gameObject)：面板被直接销毁（退出播放模式，不走 OnCloseAsync）时随之掐断。
            // UpdateIgnoreTimeScale：暂停把 timeScale 置 0 的场景下也照常淡入。
            hintMotion = LMotion.Create(0f, 1f, hintFadeSeconds)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .BindToAlpha(hintGroup)
                .AddTo(gameObject);
        }

        private void HideHint()
        {
            if (hintGroup == null)
            {
                return;
            }

            hintGroup.alpha = 0f;
            hintGroup.blocksRaycasts = false;
            hintGroup.interactable = false;
            hintGroup.gameObject.SetActive(false);
        }

        private void StopHint()
        {
            if (hintMotion.IsActive())
            {
                hintMotion.Cancel();
            }
        }
    }
}
