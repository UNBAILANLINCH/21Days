// 职责：序列帧小人回放——驱动场景里 player 下现成的 Chibi_amiya：待机 → 向右走（Walk 态、朝右）→ 向左走（翻面）→ 停（Idle），
//   走 / 跑步频差别，以及世界时停（对白）期间待机动画仍在播。
// 舞台是 SampleScene，走 Boot 真实流程（标题「开始」进场），虚拟手柄推摇杆、虚拟键盘按动作驱动场景里的真实玩家，
//   小人只从玩家根的位移反推动画（ChibiPuppetMotion），回放不碰小人本身，只读它的 Animator 状态与 Speed 参数。
// 本次重写理由：原版不加载 Boot，另从预制体实例化一只独立小人、用协程逐帧推它的根——演的是「另一只」小人，
//   与 demo 场景里玩家身上那只、以及遭遇控制器驱动它的真实链路脱节。
// Run 态：chr_amiya.controller 有 Run 状态，但 motion 仍是 chr_amiya_walk（2026-09-28 按 24 fps 重渲时只出了 idle / walk 两个剪辑），
//   预制体与场景实例的 ChibiPuppet.hasRunClip 都是 false，驱动层永不置 Running——奔跑仍停在 Walk 态、只是 walk 剪辑提速到上限。
//   所以奔跑一步断言「Walk 态、倍率夹到 rateMax」；等美术交了 run 剪辑、hasRunClip 为真，改断言 Run 态、倍率 ≈ 1。
// 时停走真实路径：出生点离长者 3、交互半径 2——走到长者旁（ElderName / ElderStandOffset，见 Framework/ShowcaseScenario.DemoScene.cs）
//   按交互键拉起对白 1001（世界时停），看待机动画仍在推进，
//   再按对白「跳过」键 → 确认弹窗点确认 → 跳过停在选项处点第二项「拒绝」，三步关掉对白、世界恢复
//   （同 DialogueShowcase.Skip_StopsAtChoice_ThenFinishesSkipped 的口径）。用例中途失败没关掉时，[UnityTearDown] 按同样三步兜底，
//   兜不住再由基类收尾销毁根作用域（WorldPauseService.Dispose 恢复 timeScale）。
using System;
using System.Collections;
using System.Collections.Generic;
using Game.CharacterPuppet;
using Game.Core.Input;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Dialogue;
using Game.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests.Showcase.CharacterPuppet
{
    [Category("Showcase")]
    public sealed class CharacterPuppetShowcase : ShowcaseScenario
    {
        private const string PuppetPath = "player/Visual/Chibi_amiya";
        private const string PuppetConfigPath = "Assets/_Project/Data/CharacterPuppet/ChibiPuppetConfig.asset";

        /// <summary>左右走各推多久：步行 3，出生点 (-4,3.4) 向右 1 秒到 x≈-1、再向左回到原处，碰不到左墙（x -6）。</summary>
        private const float TurnSeconds = 1f;

        /// <summary>走跑对比各推多久：向右走 0.8 秒（2.4 米）、向左跑 0.6 秒（3 米），终点 x≈-4.6，仍在画面内、碰不到左墙。</summary>
        private const float WalkSeconds = 0.8f;
        private const float RunSeconds = 0.6f;

        /// <summary>播放倍率容差：Speed 参数 = clamp(速度 / 剪辑地速, rateMin, rateMax)，采样窗口有抖动。</summary>
        private const float RateTolerance = 0.1f;

        private static readonly int SpeedParamHash = Animator.StringToHash("Speed");

        private IInputService inputService;
        private PlayerModel player;
        private IUIService ui;
        private DialogueService dialogue;
        private DialogueRules dialogueRules;
        private ChibiPuppet puppet;

        protected override string Module => "CharacterPuppet";

        /// <summary>世界由流程加载（标题「开始」→ MonsterEncounterState → SampleScene），基类不再叠加载一份。</summary>
        protected override string ScenePath => null;

        /// <summary>用例中途失败留下没关的对白时，按「跳过 → 确认 → 选第二项」兜底关掉，免得时停带进基类收尾。</summary>
        [UnityTearDown]
        public IEnumerator CloseDialogueIfOpen()
        {
            for (int attempt = 0; attempt < 3 && dialogue != null && dialogue.IsRunning; attempt++)
            {
                TryClick(FindUnder<Button>(DialogueViewRoot(), "SkipButton"));
                yield return WaitRealtime(0.3f);
                TryClick(FindUnder<Button>(SkipConfirmRoot(), "ConfirmButton"));
                yield return WaitRealtime(0.5f);
                ClickChoiceIfAny(1);
                yield return WaitRealtime(0.5f);
            }

            if (dialogue != null && dialogue.IsRunning)
            {
                Debug.LogWarning($"{ShowcaseOptions.Prefix}[{Module}] 收尾时对白仍未关闭，交给基类销毁根作用域恢复时停");
            }
        }

        [UnityTest]
        public IEnumerator IdleWalkTurnStop_PlaysMatchingAnimation()
        {
            yield return EnterWorld();
            yield return Check("站着不动：播放待机动画", () => IsState("Idle"), 2f);
            yield return Snapshot("待机");

            bool walkRight = false;
            yield return Step($"摇杆向右推 {TurnSeconds} 秒", null, 0f);
            yield return PushWatching(Vector2.right, TurnSeconds, () => IsState("Walk") && !puppet.FaceLeft, seen => walkRight = seen);
            yield return Check("向右走时切到走路动画、面朝右", () => walkRight);
            yield return Snapshot("向右走");

            bool walkLeft = false;
            yield return Step($"摇杆向左推 {TurnSeconds} 秒", null, 0f);
            yield return PushWatching(Vector2.left, TurnSeconds, () => IsState("Walk") && puppet.FaceLeft, seen => walkLeft = seen);
            yield return Check("向左走时翻面朝左、继续走路动画", () => walkLeft);
            yield return Snapshot("向左走");

            yield return Step("松开摇杆停下", null, 0f);
            yield return Check("回到待机动画，保持朝左", () => IsState("Idle") && puppet.FaceLeft, 1.5f);
            yield return Snapshot("停下待机");
        }

        [UnityTest]
        public IEnumerator WalkVersusRun_RunPlaysFaster()
        {
            yield return EnterWorld();
            PuppetMotionLimits(out float walkClipSpeed, out float rateMax);

            float walkRate = 0f;
            float expectedWalk = Mathf.Clamp(ResolveService<PlayerConfig>().MoveSpeed / walkClipSpeed, 0f, rateMax);
            yield return Step($"步行模式下摇杆向右推 {WalkSeconds} 秒", null, 0f);
            yield return PushWatching(Vector2.right, WalkSeconds, () => IsState("Walk"), null, rate => walkRate = rate);
            yield return Check($"走路动画按剪辑地速播放（倍率 {walkRate:0.00}，期望约 {expectedWalk:0.00}）",
                () => Mathf.Abs(walkRate - expectedWalk) <= RateTolerance);
            yield return Snapshot("步行步频");

            yield return Step("按一下走跑键（Gameplay/Run）", null, 0f);
            yield return Input.PressUntil(inputService.Actions.Gameplay.Run, () => player.IsRunning);
            yield return Check("切到奔跑模式", () => player.IsRunning, 2f);

            float runRate = 0f;
            bool stayedWalkState = true;
            yield return Step($"奔跑模式下摇杆向左推 {RunSeconds} 秒（回到出生点附近）", null, 0f);
            yield return PushWatching(Vector2.left, RunSeconds, () =>
            {
                if (IsState("Run"))
                {
                    stayedWalkState = false;
                }

                return IsState("Walk");
            }, null, rate => runRate = rate);
            yield return Check($"没有 run 剪辑：仍是 Walk 态，倍率夹到上限 {rateMax:0.0}（实测 {runRate:0.00}），高于步行 {walkRate:0.00}",
                () => !puppet.HasRunClip && stayedWalkState && Mathf.Abs(runRate - rateMax) <= RateTolerance && runRate > walkRate);
            yield return Snapshot("奔跑步频");

            yield return Step("再按一下走跑键，切回步行", null, 0f);
            yield return Input.PressUntil(inputService.Actions.Gameplay.Run, () => !player.IsRunning);
            yield return Check("回到步行模式、小人回到待机", () => !player.IsRunning && IsState("Idle"), 2f);
            yield return Snapshot("切回步行待机");
        }

        [UnityTest]
        public IEnumerator WorldPauseDuringDialogue_IdleKeepsPlaying()
        {
            yield return EnterWorld();
            var elder = FindRequired<Transform>(ElderName);
            Vector2 elderLogic = new Vector2(elder.position.x, elder.position.z);

            yield return Step("走到长者身旁（交互半径 2 以内）", null, 0f);
            yield return WalkTo(elderLogic + ElderStandOffset, 0.2f, 5f);
            yield return Check("站定后播放待机动画", () => IsState("Idle"), 2f);

            yield return Step("按交互键（Gameplay/Interact）和长者说话", null, 0f);
            yield return Input.Press(inputService.Actions.Gameplay.Interact);
            yield return Check("对白面板打开，世界时停（timeScale = 0）",
                () => dialogue.IsRunning && ui.Get<DialogueView>() != null && Time.timeScale == 0f, 3f);

            float idleBefore = IdleNormalizedTime();
            yield return Check("时停期间小人仍是待机态，待机动画还在推进（未定格）",
                () => IsState("Idle") && IdleNormalizedTime() > idleBefore + 0.02f, 2f);
            yield return Snapshot("时停待机");

            yield return Step("按对白「跳过」键（Dialogue/Skip）", null, 0f);
            yield return Input.Press(inputService.Actions.Dialogue.Skip);
            yield return Check("弹出跳过确认", () => SkipConfirmRoot() != null, 3f);
            // 弹窗一登记进 UI 服务就能取到，但对白控制器要等打开流程走完才订阅「确认」事件，太早点会被吞掉（实测弹窗一直不关）；
            // 所以每 0.3 秒补点一次，直到弹窗关闭。
            yield return Step("点确认", null, 0f);
            yield return ClickWhenReady("确认弹窗关闭", () => FindUnder<Button>(SkipConfirmRoot(), "ConfirmButton"),
                () => SkipConfirmRoot() == null, 3f, 0.3f);
            yield return Check("跳过停在选项处", () => dialogueRules.Phase == DialogueSaveData.Phase.AwaitChoice, 3f);
            yield return WaitUntil("选项按钮已显示", () => ActiveChoices().Count >= 2, 3f);
            yield return Step("选第二项「拒绝」结束对白", () => ClickChoice(1), 0f);
            yield return Check("对白关闭、世界恢复（timeScale = 1、不再暂停）",
                () => !dialogue.IsRunning && Time.timeScale == 1f && !ResolveService<IWorldPauseService>().IsPaused, 5f);
            yield return Snapshot("对白结束");
        }

        // ───────────────────────── 进场与驱动 ─────────────────────────

        /// <summary>标题「开始」进世界（EnterDemoWorld），等容器里的输入 / 对白服务可用，再找到玩家身上的小人。</summary>
        private IEnumerator EnterWorld()
        {
            yield return EnterDemoWorld("遭遇逻辑在跑、输入 / 对白服务可用", () =>
            {
                inputService = ResolveService<IInputService>();
                player = ResolveService<PlayerModel>();
                ui = ResolveService<IUIService>();
                dialogue = ResolveService<DialogueService>();
                dialogueRules = ResolveService<DialogueRules>();
                return inputService != null && inputService.Actions != null
                       && player != null && ui != null && dialogue != null && dialogueRules != null;
            }, "进世界后容器里取不到 PlayerModel / IInputService / IUIService / DialogueService / DialogueRules");

            puppet = FindRequired<ChibiPuppet>(PuppetPath);
        }

        /// <summary>
        /// 取剪辑地速（ChibiPuppet 公开属性）与播放倍率上限（读 ChibiPuppetConfig.asset，跟着资产走，不写死）。
        /// 配置只挂在 ChibiPuppetMotion 的私有字段上，回放不碰私有实现，改为编辑器下按资产路径读；读不到时退回资产当前值 1.6。
        /// </summary>
        private void PuppetMotionLimits(out float walkClipSpeed, out float rateMax)
        {
            walkClipSpeed = puppet.WalkClipSpeed;
            ChibiPuppetConfig config = null;
#if UNITY_EDITOR
            config = UnityEditor.AssetDatabase.LoadAssetAtPath<ChibiPuppetConfig>(PuppetConfigPath);
#endif
            rateMax = config == null ? 1.6f : config.RateMax;
        }

        /// <summary>
        /// 朝 <paramref name="direction"/> 逐帧推摇杆 <paramref name="seconds"/> 秒，推满 0.3 秒后开始采样：
        /// <paramref name="state"/> 是否成立过（回调 <paramref name="reportSeen"/>）、最后一次采到的 Speed 参数（回调 <paramref name="reportRate"/>）。
        /// 松杆后等玩家停稳。
        /// </summary>
        private IEnumerator PushWatching(Vector2 direction, float seconds, Func<bool> state,
            Action<bool> reportSeen, Action<float> reportRate = null)
        {
            bool seen = false;
            float rate = 0f;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                Input.SetStick(direction);
                yield return null;
                if (Time.realtimeSinceStartup - start >= 0.3f && state())
                {
                    seen = true;
                    rate = puppet.Animator == null ? 0f : puppet.Animator.GetFloat(SpeedParamHash);
                }
            }

            Input.ReleaseStick();
            yield return WaitPlayerStable();
            reportSeen?.Invoke(seen);
            reportRate?.Invoke(rate);
        }

        private bool IsState(string state)
        {
            return puppet != null && puppet.Animator != null && puppet.Animator.GetCurrentAnimatorStateInfo(0).IsName(state);
        }

        private float IdleNormalizedTime()
        {
            return puppet == null || puppet.Animator == null ? 0f : puppet.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        }

        // ───────────────────────── 对白界面 ─────────────────────────

        private Transform DialogueViewRoot()
        {
            DialogueView view = ui == null ? null : ui.Get<DialogueView>();
            return view == null ? null : view.transform;
        }

        private Transform SkipConfirmRoot()
        {
            DialogueSkipConfirmView view = ui == null ? null : ui.Get<DialogueSkipConfirmView>();
            return view == null ? null : view.transform;
        }

        private static T FindUnder<T>(Transform root, string objectName) where T : Component
        {
            return FindDeep<T>(root, objectName);
        }

        /// <summary>找不到按钮就抛异常：放在 Step 的 act 里只把这一步记成失败。</summary>
        private static Button RequireButton(Transform root, string objectName)
        {
            Button button = FindUnder<Button>(root, objectName);
            if (button == null)
            {
                throw new InvalidOperationException($"找不到按钮「{objectName}」（界面没开，或预制体物体名不一致）");
            }

            return button;
        }

        /// <summary>ChoiceRoot 下当前激活的选项按钮（排除隐藏模板）。</summary>
        private List<Button> ActiveChoices()
        {
            var result = new List<Button>();
            Transform root = FindUnder<Transform>(DialogueViewRoot(), "ChoiceRoot");
            if (root == null)
            {
                return result;
            }

            Button[] buttons = root.GetComponentsInChildren<Button>(false);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i].name != "ChoiceTemplate")
                {
                    result.Add(buttons[i]);
                }
            }

            return result;
        }

        private void ClickChoice(int index)
        {
            List<Button> choices = ActiveChoices();
            if (index >= choices.Count)
            {
                throw new InvalidOperationException($"只有 {choices.Count} 个激活选项，点不到第 {index + 1} 项");
            }

            choices[index].onClick.Invoke();
        }

        private void ClickChoiceIfAny(int index)
        {
            List<Button> choices = ActiveChoices();
            if (choices.Count > 0)
            {
                choices[Mathf.Min(index, choices.Count - 1)].onClick.Invoke();
            }
        }

        private static void TryClick(Button button)
        {
            if (button != null && button.interactable)
            {
                button.onClick.Invoke();
            }
        }

        private static IEnumerator WaitRealtime(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
        }
    }
}
