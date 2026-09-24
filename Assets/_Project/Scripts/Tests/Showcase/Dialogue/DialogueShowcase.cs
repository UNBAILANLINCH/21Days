// 职责：Dialogue 模块回放——范围焦点与右下角对话按钮、无对话树 NPC 的头顶台词气泡、交互拉起对白后的打字、三连点补全、倍速、自动推进、条件选项隐藏与图标、选择与跳过（含确认弹窗），
//   以及对白期间世界时停、结束后恢复（PRD 验收 A4–A6）。
using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.Core.Input;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Core.UI.Views;
using Game.Dialogue;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests.Showcase.Dialogue
{
    [Category("Showcase")]
    public sealed class DialogueShowcase : ShowcaseScenario
    {
        private const float BootTimeoutSeconds = 20f;

        /// <summary>根作用域类型名：Boot 场景的 GameBootstrap 带 DontDestroyOnLoad，收尾时按名字找来销毁。</summary>
        private const string ScopeTypeName = "Game.Core.Boot.GameLifetimeScope, Game.Core";

        private IUIService ui;
        private DialogueService service;
        private DialogueRules rules;
        private IWorldPauseService worldPause;
        private IInputService input;
        private bool hasResult;
        private DialogueResult lastResult;

        protected override string Module => "Dialogue";

        protected override string ScenePath => "Assets/_Project/Scenes/Verify/Dialogue.unity";

        protected override bool LoadBootScene => true;

        /// <summary>启动流程走到标题界面才算就绪：此时容器已建完、UI 服务可用、Dialogue 安装器已注册。</summary>
        protected override IEnumerator WaitForBootReady()
        {
            yield return WaitUntil(
                "启动流程到达标题界面（容器建完、UI 服务可用、标题界面已打开）",
                () =>
                {
                    IUIService candidate = ResolveService<IUIService>();
                    return candidate != null && candidate.Get<TitleView>() != null;
                },
                BootTimeoutSeconds);
        }

        /// <summary>
        /// 每条用例都会重新 Single 加载 Boot，而 GameBootstrap 带 DontDestroyOnLoad：不销毁上一条的根作用域，
        /// 场上就会有两套容器 / UI 根 / EventSystem，场景绑定器与服务解析都会串台。
        /// 销毁作用域物体即释放容器（UIService.Dispose 会一并销毁 UIRoot）。本方法先于基类收尾执行。
        /// </summary>
        [UnityTearDown]
        public IEnumerator DestroyBootScope()
        {
            Type scopeType = Type.GetType(ScopeTypeName);
            if (scopeType != null)
            {
                UnityEngine.Object[] scopes = UnityEngine.Object.FindObjectsOfType(scopeType);
                for (int i = 0; i < scopes.Length; i++)
                {
                    if (scopes[i] is Component component && component != null)
                    {
                        UnityEngine.Object.Destroy(component.gameObject);
                    }
                }
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Interact_TypesThenChoicesThenEnds_WhileWorldPaused()
        {
            Connect();
            yield return CloseTitleIfOpen();

            var elder = FindRequired<DialogueInteractable>("Elder");
            yield return Step("点长者：拉起对白 1001", () =>
            {
                elder.OnCompleted -= RecordResult;
                elder.OnCompleted += RecordResult;
                elder.Interact();
            }, hold: 0f);
            // 不停顿直接验打字：l1 很短，默认 1.5 s 停顿里就会打完，三连点补全就验不到了。
            yield return Check("对白面板打开，第一句正在逐字打出",
                () => View() != null && rules.Phase == DialogueSaveData.Phase.Typing && rules.VisibleCharacters > 0, 3f);

            yield return Step("三连点补全：同一帧连点三下对白区", () =>
            {
                Button tap = RequireButton("TapArea");
                tap.onClick.Invoke();
                tap.onClick.Invoke();
                tap.onClick.Invoke();
            });
            yield return Check("第一句整句显示，停在 l1 等待推进",
                () => rules.Phase == DialogueSaveData.Phase.AwaitAdvance && CurrentIs("l1"), 1f);
            yield return Snapshot("三连点补全");
            yield return Check("世界时停（timeScale=0、逻辑暂停、Gameplay 输入图关闭）",
                () => Time.timeScale == 0f && worldPause.IsPaused && !input.Actions.Gameplay.enabled);
            yield return Snapshot("对话拉起·世界时停");

            yield return Step("单点一下对白区：推进到下一句", () => RequireButton("TapArea").onClick.Invoke());
            yield return Check("推进到第二句 l2", () => CurrentIs("l2"), 2f);

            string[] expectedSpeeds = { "x2", "x4", "x1" };
            for (int i = 0; i < expectedSpeeds.Length; i++)
            {
                string expected = expectedSpeeds[i];
                yield return Step($"倍速循环：第 {i + 1} 次点倍速按钮", () => RequireButton("SpeedButton").onClick.Invoke());
                yield return Check($"倍速标签显示 {expected}", () => LabelText("SpeedLabel") == expected, 2f);
            }

            yield return Step("开自动：点自动按钮", () => RequireButton("AutoButton").onClick.Invoke());
            yield return Check("自动推进到选项", () => rules.Phase == DialogueSaveData.Phase.AwaitChoice, 15f);
            yield return Check("只显示 2 个选项（缺剧情标记的「记得」被隐藏）", () => ActiveChoices().Count == 2, 2f);
            yield return Check("两个选项的图标都已加载并显示（Icon 激活且有 Sprite）",
                () => ChoiceHasIcon(0) && ChoiceHasIcon(1), 3f);
            yield return Snapshot("选项");

            yield return Step("选择第一项「接受」", () => ClickChoice(0));
            yield return Check("进入接受分支 r_accept", () => CurrentIs("r_accept"), 2f);

            yield return Check("自动推进到结束，结果 Outcome=Accepted、未跳过",
                () => !service.IsRunning && hasResult && lastResult.Outcome == "Accepted" && !lastResult.Skipped, 15f);
            yield return Check("世界恢复（timeScale=1、逻辑恢复、Gameplay 输入图打开）", WorldRestored, 2f);
            yield return Snapshot("结束·世界恢复");
            elder.OnCompleted -= RecordResult;
        }

        [UnityTest]
        public IEnumerator Skip_StopsAtChoice_ThenFinishesSkipped()
        {
            Connect();
            yield return CloseTitleIfOpen();

            var elder = FindRequired<DialogueInteractable>("Elder");
            yield return Step("点长者：拉起对白 1001", () =>
            {
                elder.OnCompleted -= RecordResult;
                elder.OnCompleted += RecordResult;
                elder.Interact();
            });
            yield return Check("对白面板打开", () => View() != null && service.IsRunning, 3f);

            yield return Step("对白进行中再点一次长者", () => elder.Interact());
            yield return Check("重复交互被忽略，仍是同一段对白", () => service.IsRunning && !hasResult && View() != null);

            yield return Step("点跳过", () => RequireButton("SkipButton").onClick.Invoke());
            yield return Check("弹出跳过确认，尚未进入跳过", () => SkipConfirm() != null && NotSkipping(), 3f);
            yield return Snapshot("跳过确认弹窗");
            yield return Step("点确认", () => RequireConfirmButton("ConfirmButton").onClick.Invoke());
            yield return Check("确认弹窗关闭", () => SkipConfirm() == null, 3f);
            yield return Check("跳过停在选项处", () => rules.Phase == DialogueSaveData.Phase.AwaitChoice, 3f);
            yield return Snapshot("跳过停在选项");

            yield return Step("选择第二项「拒绝」", () => ClickChoice(1));
            yield return Check("对白结束，结果 Outcome=Refused、已跳过",
                () => !service.IsRunning && hasResult && lastResult.Outcome == "Refused" && lastResult.Skipped, 5f);
            yield return Check("世界恢复（timeScale=1、逻辑恢复、Gameplay 输入图打开）", WorldRestored, 2f);
            yield return Snapshot("跳过结束·世界恢复");
            elder.OnCompleted -= RecordResult;
        }

        [UnityTest]
        public IEnumerator PointerClick_OnTraveler_PlaysSecondTree()
        {
            Connect();
            yield return CloseTitleIfOpen();

            var traveler = FindRequired<DialogueInteractable>("Traveler");
            yield return Step("用指针点击旅人（走 EventSystem 点击路径）", () =>
            {
                traveler.OnCompleted -= RecordResult;
                traveler.OnCompleted += RecordResult;
                ExecuteEvents.Execute(traveler.gameObject, new PointerEventData(EventSystem.current),
                    ExecuteEvents.pointerClickHandler);
            });
            yield return Check("对白面板打开，播的是对白 1002 的第一句 m1", () => View() != null && CurrentIs("m1"), 3f);
            yield return Snapshot("旅人对白");

            yield return Step("点跳过", () => RequireButton("SkipButton").onClick.Invoke());
            yield return Check("弹出跳过确认", () => SkipConfirm() != null, 3f);
            yield return Step("点确认", () => RequireConfirmButton("ConfirmButton").onClick.Invoke());
            yield return Check("对白结束，结果 Outcome=Done",
                () => !service.IsRunning && hasResult && lastResult.Outcome == "Done", 5f);
            yield return Check("世界恢复（timeScale=1、逻辑恢复、Gameplay 输入图打开）", WorldRestored, 2f);
            yield return Snapshot("旅人对白结束");
            traveler.OnCompleted -= RecordResult;
        }

        [UnityTest]
        public IEnumerator SkipCancelled_DialogueContinues()
        {
            Connect();
            yield return CloseTitleIfOpen();

            var elder = FindRequired<DialogueInteractable>("Elder");
            yield return Step("点长者：拉起对白 1001", () =>
            {
                elder.OnCompleted -= RecordResult;
                elder.OnCompleted += RecordResult;
                elder.Interact();
            });
            yield return Check("对白面板打开", () => View() != null && service.IsRunning, 3f);

            yield return Step("点跳过", () => RequireButton("SkipButton").onClick.Invoke());
            yield return Check("弹出跳过确认", () => SkipConfirm() != null, 3f);
            yield return Step("点取消", () => RequireConfirmButton("CancelButton").onClick.Invoke());
            yield return Check("确认弹窗关闭，未进入跳过", () => SkipConfirm() == null && NotSkipping(), 3f);

            // 取消后「覆盖中」解除：打字继续，第一句自然打完停在等待推进（不靠点击补全，与停顿倍率无关）。
            yield return Check("对白继续打字，第一句整句显示",
                () => rules.Phase == DialogueSaveData.Phase.AwaitAdvance && CurrentIs("l1"), 5f);
            yield return Step("单点一下对白区：推进到下一句", () => RequireButton("TapArea").onClick.Invoke());
            yield return Check("对白继续：推进到第二句 l2，仍在进行、未跳过",
                () => CurrentIs("l2") && service.IsRunning && NotSkipping(), 2f);
            yield return Snapshot("取消跳过·对白继续");

            yield return Step("点跳过并确认，收尾", () => RequireButton("SkipButton").onClick.Invoke());
            yield return Check("弹出跳过确认", () => SkipConfirm() != null, 3f);
            yield return Step("点确认", () => RequireConfirmButton("ConfirmButton").onClick.Invoke());
            yield return Check("跳过停在选项处", () => rules.Phase == DialogueSaveData.Phase.AwaitChoice, 3f);
            yield return Step("选择第二项「拒绝」", () => ClickChoice(1));
            yield return Check("对白结束，世界恢复", () => !service.IsRunning && hasResult && WorldRestored(), 5f);
            elder.OnCompleted -= RecordResult;
        }

        [UnityTest]
        public IEnumerator Focus_ShowsHudButton_AndHudClickStartsDialogue()
        {
            Connect();
            yield return CloseTitleIfOpen();

            var elder = FindRequired<DialogueInteractable>("Elder");
            DialogueInteractionFocus focus = ResolveService<DialogueInteractionFocus>();
            yield return WaitUntil("右下角交互 HUD 已打开", () => ui != null && ui.Get<DialogueInteractHudView>() != null, 5f);
            yield return Check("玩家在长者附近：焦点是长者，右下角「对话」按钮显示，长者头顶亮起「!」",
                () => focus != null && focus.Current == elder && HudRootActive() && ChildActive(elder, "MarkerFocus")
                      && ChildActive(elder, "NameLabel"), 3f);
            yield return Snapshot("焦点·对话按钮");

            yield return Step("点右下角「对话」按钮", () =>
            {
                elder.OnCompleted -= RecordResult;
                elder.OnCompleted += RecordResult;
                RequireHudButton().onClick.Invoke();
            });
            yield return Check("对白 1001 拉起，焦点清空、对话按钮隐藏",
                () => View() != null && service.IsRunning && focus.Current == null && !HudRootActive(), 3f);

            yield return Step("点跳过", () => RequireButton("SkipButton").onClick.Invoke());
            // 跳过确认弹窗由另一任务接入：存在就点确认，不存在就直接往下走。
            yield return Check("跳过已受理（弹出确认或已进入跳过）",
                () => SkipConfirm() != null || rules.Phase == DialogueSaveData.Phase.AwaitChoice, 3f);
            if (SkipConfirm() != null)
            {
                yield return Step("点确认", () => RequireConfirmButton("ConfirmButton").onClick.Invoke());
            }

            yield return Check("跳过停在选项处", () => rules.Phase == DialogueSaveData.Phase.AwaitChoice, 3f);
            yield return Step("选择第二项「拒绝」", () => ClickChoice(1));
            yield return Check("对白结束，世界恢复，焦点回到长者、对话按钮重新显示",
                () => !service.IsRunning && hasResult && WorldRestored() && focus.Current == elder && HudRootActive(), 5f);
            yield return Snapshot("对白结束·焦点恢复");
            elder.OnCompleted -= RecordResult;
        }

        [UnityTest]
        public IEnumerator Bubble_ShowsAboveHead_WithoutPausing()
        {
            Connect();
            yield return CloseTitleIfOpen();

            var villager = FindRequired<DialogueInteractable>("Villager");
            var bubble = FindRequired<DialogueSpeechBubble>("SpeechBubble");
            yield return Step("和村民交互（无对话树，只说常驻台词）", () => villager.Interact(), hold: 0f);
            yield return Check("头顶气泡出现第一句，世界没有暂停、没有拉起对白面板",
                () => bubble.IsShowing && bubble.CurrentText == "这个移动平台比我年轻时见到的老不少。"
                      && Mathf.Approximately(Time.timeScale, 1f) && !service.IsRunning && View() == null, 2f);
            yield return Wait(1.5f);
            yield return Snapshot("村民气泡·第一句");

            yield return Step("再次和村民交互", () => villager.Interact(), hold: 0f);
            yield return Check("气泡换成第二句", () => bubble.IsShowing && bubble.CurrentText == "今天的风有点大，小心站稳。", 1f);
            yield return Wait(1f);
            yield return Snapshot("村民气泡·第二句");
            yield return Check("停留后气泡淡出消失", () => !bubble.IsShowing, bubble.HoldSeconds + 2f);
            yield return Snapshot("气泡淡出");
        }

        /// <summary>从根容器取本回放要用的服务；取不到留 null，由后续检查点记失败。</summary>
        private void Connect()
        {
            hasResult = false;
            lastResult = default;
            ui = ResolveService<IUIService>();
            service = ResolveService<DialogueService>();
            rules = ResolveService<DialogueRules>();
            worldPause = ResolveService<IWorldPauseService>();
            input = ResolveService<IInputService>();
        }

        private IEnumerator CloseTitleIfOpen()
        {
            TitleView title = ui == null ? null : ui.Get<TitleView>();
            if (title == null)
            {
                yield break;
            }

            IEnumerator closing = null;
            yield return Step("关闭标题界面", () => closing = ui.CloseAsync(title).ToCoroutine());
            if (closing != null)
            {
                yield return closing;
            }

            yield return Check("标题界面已关闭", () => ui.Get<TitleView>() == null, 3f);
        }

        /// <summary>交互 HUD 的卡片（物体名 Root）当前是否显示。</summary>
        private bool HudRootActive()
        {
            DialogueInteractHudView hud = ui == null ? null : ui.Get<DialogueInteractHudView>();
            if (hud == null)
            {
                return false;
            }

            Transform root = hud.transform.Find("Root");
            return root != null && root.gameObject.activeInHierarchy;
        }

        /// <summary>交互 HUD 上的整卡按钮；HUD 没开或找不到就抛异常，让 Step 记失败。</summary>
        private Button RequireHudButton()
        {
            DialogueInteractHudView hud = ui == null ? null : ui.Get<DialogueInteractHudView>();
            Button button = hud == null ? null : hud.GetComponentInChildren<Button>(true);
            if (button == null)
            {
                throw new InvalidOperationException("交互 HUD 没开，或预制体下没有 Button");
            }

            return button;
        }

        /// <summary>NPC 头顶的某个子物体（MarkerFocus / NameLabel）当前是否显示。</summary>
        private static bool ChildActive(Component npc, string childName)
        {
            Transform child = npc.transform.Find(childName);
            return child != null && child.gameObject.activeInHierarchy;
        }

        private void RecordResult(DialogueResult result)
        {
            hasResult = true;
            lastResult = result;
        }

        private DialogueView View()
        {
            return ui == null ? null : ui.Get<DialogueView>();
        }

        private bool CurrentIs(string nodeId)
        {
            return rules != null && rules.Current != null && rules.Current.Id == nodeId;
        }

        private bool WorldRestored()
        {
            return Mathf.Approximately(Time.timeScale, 1f) && !worldPause.IsPaused && input.Actions.Gameplay.enabled;
        }

        /// <summary>在对白面板下按物体名找组件（含未激活的）；面板没开或找不到返回 null。</summary>
        private T FindInView<T>(string objectName) where T : Component
        {
            DialogueView view = View();
            if (view == null)
            {
                return null;
            }

            T[] candidates = view.GetComponentsInChildren<T>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i].name == objectName)
                {
                    return candidates[i];
                }
            }

            return null;
        }

        /// <summary>找不到按钮就抛异常：Step 会把这一步记成失败并写明缺了哪个物体，方便对照预制体。</summary>
        private Button RequireButton(string objectName)
        {
            Button button = FindInView<Button>(objectName);
            if (button == null)
            {
                throw new InvalidOperationException($"对白面板下找不到按钮「{objectName}」（面板没开，或预制体物体名不一致）");
            }

            return button;
        }

        /// <summary>跳过按钮仍可点 = 策略未进入跳过（View.SetControls 在跳过中禁用它）。</summary>
        private bool NotSkipping()
        {
            Button skip = FindInView<Button>("SkipButton");
            return skip != null && skip.interactable;
        }

        private DialogueSkipConfirmView SkipConfirm()
        {
            return ui == null ? null : ui.Get<DialogueSkipConfirmView>();
        }

        /// <summary>在跳过确认弹窗下按物体名找按钮；弹窗没开或找不到就抛异常，让 Step 记失败。</summary>
        private Button RequireConfirmButton(string objectName)
        {
            DialogueSkipConfirmView confirm = SkipConfirm();
            if (confirm != null)
            {
                Button[] buttons = confirm.GetComponentsInChildren<Button>(true);
                for (int i = 0; i < buttons.Length; i++)
                {
                    if (buttons[i].name == objectName)
                    {
                        return buttons[i];
                    }
                }
            }

            throw new InvalidOperationException($"跳过确认弹窗下找不到按钮「{objectName}」（弹窗没开，或预制体物体名不一致）");
        }

        /// <summary>第 index 个激活选项的 Icon 子物体已激活且有 Sprite。</summary>
        private bool ChoiceHasIcon(int index)
        {
            List<Button> choices = ActiveChoices();
            if (index >= choices.Count)
            {
                return false;
            }

            Transform icon = choices[index].transform.Find("Icon");
            if (icon == null || !icon.gameObject.activeSelf)
            {
                return false;
            }

            Image image = icon.GetComponent<Image>();
            return image != null && image.sprite != null;
        }

        private string LabelText(string objectName)
        {
            TMP_Text label = FindInView<TMP_Text>(objectName);
            return label == null ? null : label.text;
        }

        /// <summary>ChoiceRoot 下当前激活的选项按钮（排除隐藏模板本身）。</summary>
        private List<Button> ActiveChoices()
        {
            var result = new List<Button>();
            Transform root = FindInView<Transform>("ChoiceRoot");
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
    }
}
