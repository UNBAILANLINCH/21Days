// 职责：Quest 模块回放——任务 HUD 显示追踪任务与目标指引（屏内世界空间头顶标记 / 屏外 HUD 贴边箭头 + 距离 / 未追踪隐藏）、
//   对白与抵达地点推进目标、主线完成后解锁后续主线、无人匹配的上报不生效、任务面板打开时世界暂停与追踪切换。
//   覆盖 PRD 验收：A3（面板开关与世界暂停 / 输入图）、A6（屏内 / 屏外指引，肉眼）、A7（对白与地点推进目标）、
//   A8（HUD 与面板显示追踪任务、切换追踪）、A9（面板列表主线在前、主线 / 支线标签）。
// 骨架照抄 DialogueShowcase：Boot 就绪等待、每条用例收尾销毁根作用域、反射取服务、对白跳过驱动路径。
using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.Core.Input;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Core.UI.Views;
using Game.Dialogue;
using Game.Quest;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests.Showcase.Quest
{
    [Category("Showcase")]
    public sealed class QuestShowcase : ShowcaseScenario
    {
        private const float BootTimeoutSeconds = 20f;

        /// <summary>根作用域类型名：Boot 场景的 GameBootstrap 带 DontDestroyOnLoad，收尾时按名字找来销毁。</summary>
        private const string ScopeTypeName = "Game.Core.Boot.GameLifetimeScope, Game.Core";

        private const int MainQuestId = 1001;
        private const int SideQuestId = 2001;
        private const string UntrackedLabel = "未追踪任务";

        /// <summary>支线 2001 目标瞭望点的 x 坐标（Verify/Quest.unity 的 QuestLocation_Lookout）。</summary>
        private const float LookoutX = 24f;

        private IUIService ui;
        private QuestService service;
        private DialogueService dialogue;
        private DialogueRules dialogueRules;
        private IWorldPauseService pause;
        private IInputService input;
        private string dialogueError;

        protected override string Module => "Quest";

        protected override string ScenePath => "Assets/_Project/Scenes/Verify/Quest.unity";

        protected override bool LoadBootScene => true;

        /// <summary>启动流程走到标题界面才算就绪：此时容器已建完、UI 服务可用、Quest 安装器已注册。</summary>
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

        /// <summary>每条用例都重新加载 Boot：不销毁上一条的根作用域会叠出多套容器 / UIRoot / EventSystem（同 DialogueShowcase）。</summary>
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
        public IEnumerator Tracking_ShowsMainQuestAndGuidance()
        {
            Connect();
            yield return CloseTitleIfOpen();
            yield return Check("任务 HUD 已打开", () => Hud() != null, 5f);

            yield return Check("HUD 显示追踪中的主线「找到落脚处」，目标「与长者谈谈」",
                () => service.TrackedId == MainQuestId && HudText("Root/Title").Contains("找到落脚处")
                      && HudText("Root/Objective").Contains("与长者谈谈"), 3f);
            var elder = FindRequired<DialogueInteractable>("Elder");
            yield return Check("长者在画面内：HUD 不画指引，长者头顶出现世界空间任务标记",
                () => !HudActive("Guidance") && MarkerActive()
                      && Marker().transform.position.y > elder.transform.position.y, 5f);
            yield return Snapshot("主线追踪·屏内头顶标记");

            yield return Step("追踪支线 2001（目标瞭望点在画面外右侧）", () => service.Track(SideQuestId));
            yield return Check("HUD 换成「观察神秘生物」，指引贴屏幕边缘并显示箭头与距离「N m」，标记移到瞭望点",
                () => HudText("Root/Title").Contains("观察神秘生物") && HudActive("Guidance") && HudActive("Guidance/Arrow")
                      && HudText("Guidance/Distance").EndsWith("m")
                      && Marker() != null && Math.Abs(Marker().transform.position.x - LookoutX) < 0.5f, 3f);
            yield return Snapshot("支线追踪·屏外箭头");

            yield return Step("取消追踪", () => service.Untrack());
            yield return Check("HUD 标题显示「未追踪任务」，指引与头顶标记都消失",
                () => HudText("Root/Title") == UntrackedLabel && !HudActive("Guidance") && !MarkerActive(), 3f);
            yield return Snapshot("未追踪");

            yield return Step("追踪回主线", () => service.Track(MainQuestId));
            yield return Check("HUD 标题回到「找到落脚处」，长者头顶标记重新出现",
                () => HudText("Root/Title").Contains("找到落脚处") && MarkerActive(), 3f);
            yield return Snapshot("追踪回主线");
        }

        [UnityTest]
        public IEnumerator Progress_DialogueAndLocationAdvanceObjectives()
        {
            Connect();
            yield return CloseTitleIfOpen();
            yield return Check("任务 HUD 已打开", () => Hud() != null, 5f);
            var player = FindRequired<DialogueInteractionActor>("player");

            yield return Step("拉起长者对白 1001", () => PlayDialogueAsync(1001).Forget());
            yield return Check("对白进行中，任务栏隐藏",
                () => dialogue.IsRunning && View() != null && !HudActive("Root"), 3f);

            yield return Step("点跳过", () => RequireButton("SkipButton").onClick.Invoke());
            yield return Check("弹出跳过确认", () => SkipConfirm() != null, 3f);
            yield return Step("点确认", () => RequireConfirmButton("ConfirmButton").onClick.Invoke());
            yield return Check("跳过停在选项处", () => dialogueRules.Phase == DialogueSaveData.Phase.AwaitChoice, 3f);
            yield return Step("选择第一个可用选项", () => ClickChoice(0));
            yield return Check("对白结束（无异常）", () => !dialogue.IsRunning && dialogueError == null, 15f);

            yield return Check("目标①完成：任务栏重新显示，目标换成「前往营地」",
                () => service.TryGet(MainQuestId, out QuestProgress p) && p.ObjectiveIndex == 1
                      && HudActive("Root") && HudText("Root/Objective").Contains("前往营地"), 3f);
            yield return Snapshot("目标推进·前往营地");

            yield return Step("把玩家挪进营地", () => player.transform.position = new Vector3(0f, 0f, 6f));
            yield return Check("主线 1001 完成，后续主线「与旅人叙旧」接取并被追踪",
                () => service.TryGet(MainQuestId, out QuestProgress p) && p.State == QuestState.Completed
                      && service.TrackedId == 1002 && HudText("Root/Title").Contains("与旅人叙旧"), 3f);
            yield return Snapshot("主线完成·解锁后续");

            int advanced = -1;
            yield return Step("上报一个无人匹配的计数", () => advanced = service.Report(QuestObjectiveKind.Counter, "nothing", 2));
            yield return Check("没有任何目标被推进（返回 0）", () => advanced == 0);

            yield return Step("把玩家挪到瞭望点", () => player.transform.position = new Vector3(24f, 0f, 0f));
            yield return Check("支线 2001 完成，HUD 仍追踪「与旅人叙旧」",
                () => service.TryGet(SideQuestId, out QuestProgress s) && s.State == QuestState.Completed
                      && HudText("Root/Title").Contains("与旅人叙旧"), 3f);
            yield return Snapshot("支线完成·追踪不变");
        }

        [UnityTest]
        public IEnumerator Panel_OpensPausesAndTogglesTracking()
        {
            Connect();
            yield return CloseTitleIfOpen();
            yield return Check("任务 HUD 已打开", () => Hud() != null, 5f);

            yield return Step("点任务栏", () => RequireHudChild<Button>("Root").onClick.Invoke());
            yield return Check("任务面板打开，世界暂停、Gameplay 输入图关闭",
                () => Panel() != null && pause.IsPaused && input.Actions != null && !input.Actions.Gameplay.enabled, 5f);
            yield return Check("列表主线在前：「找到落脚处·主线」，其次「观察神秘生物·支线」",
                () => ItemIs(0, "找到落脚处", "主线") && ItemIs(1, "观察神秘生物", "支线"), 3f);
            yield return Snapshot("任务面板·列表");

            yield return Step("选中支线", () => ClickItem(1));
            yield return Check("详情显示「观察神秘生物」，按钮为「追踪」",
                () => PanelText("Detail/Title") == "观察神秘生物" && PanelText("Detail/TrackButton/TrackLabel") == "追踪", 3f);

            yield return Step("点追踪", () => RequirePanelChild<Button>("Detail/TrackButton").onClick.Invoke());
            yield return Check("追踪切到支线：按钮变「取消追踪」，HUD 标题换成「观察神秘生物」",
                () => service.TrackedId == SideQuestId && PanelText("Detail/TrackButton/TrackLabel") == "取消追踪"
                      && HudText("Root/Title").Contains("观察神秘生物"), 3f);
            yield return Snapshot("面板·已追踪支线");

            yield return Step("再点一次取消追踪", () => RequirePanelChild<Button>("Detail/TrackButton").onClick.Invoke());
            yield return Check("不再追踪：按钮回到「追踪」，HUD 标题「未追踪任务」",
                () => service.TrackedId == 0 && PanelText("Detail/TrackButton/TrackLabel") == "追踪"
                      && HudText("Root/Title") == UntrackedLabel, 3f);

            yield return Step("关面板", () => RequirePanelChild<Button>("CloseButton").onClick.Invoke());
            yield return Check("面板关闭，世界恢复、Gameplay 输入图打开",
                () => Panel() == null && !pause.IsPaused && input.Actions != null && input.Actions.Gameplay.enabled, 5f);
            yield return Snapshot("面板关闭·世界恢复");
        }

        /// <summary>从根容器取本回放要用的服务；取不到留 null，由后续检查点记失败。</summary>
        private void Connect()
        {
            dialogueError = null;
            ui = ResolveService<IUIService>();
            service = ResolveService<QuestService>();
            dialogue = ResolveService<DialogueService>();
            dialogueRules = ResolveService<DialogueRules>();
            pause = ResolveService<IWorldPauseService>();
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

        /// <summary>后台播放对白；异常只记下来由检查点判失败，不 LogError（会被 UTF 当未预期错误打断报告）。</summary>
        private async UniTaskVoid PlayDialogueAsync(int dialogueId)
        {
            try
            {
                await dialogue.PlayAsync(dialogueId);
            }
            catch (OperationCanceledException)
            {
                dialogueError = "对白被取消";
            }
            catch (Exception e)
            {
                dialogueError = e.GetType().Name + "：" + e.Message;
                Debug.LogWarning($"{ShowcaseOptions.Prefix}[Quest] 对白 {dialogueId} 播放失败：{dialogueError}");
            }
        }

        private QuestHudView Hud()
        {
            return ui == null ? null : ui.Get<QuestHudView>();
        }

        /// <summary>场上的任务目标标记实例（含未激活的）；标记还没实例化时为 null。</summary>
        private static QuestTargetMarker Marker()
        {
            return UnityEngine.Object.FindObjectOfType<QuestTargetMarker>(true);
        }

        private static bool MarkerActive()
        {
            QuestTargetMarker marker = Marker();
            return marker != null && marker.gameObject.activeInHierarchy;
        }

        private QuestPanelView Panel()
        {
            return ui == null ? null : ui.Get<QuestPanelView>();
        }

        /// <summary>HUD 下某路径的 TMP 文本；HUD 没开或找不到返回空串。</summary>
        private string HudText(string path)
        {
            return TextAt(Hud(), path);
        }

        private string PanelText(string path)
        {
            return TextAt(Panel(), path);
        }

        private static string TextAt(Component rootView, string path)
        {
            if (rootView == null)
            {
                return string.Empty;
            }

            Transform child = rootView.transform.Find(path);
            TMP_Text label = child == null ? null : child.GetComponent<TMP_Text>();
            return label == null || label.text == null ? string.Empty : label.text;
        }

        /// <summary>HUD 下某路径的物体在层级里是否显示。</summary>
        private bool HudActive(string path)
        {
            QuestHudView hud = Hud();
            Transform child = hud == null ? null : hud.transform.Find(path);
            return child != null && child.gameObject.activeInHierarchy;
        }

        /// <summary>HUD 下按路径取组件；找不到就抛异常，让 Step 记失败并写明缺了哪个物体。</summary>
        private T RequireHudChild<T>(string path) where T : Component
        {
            return RequireChild<T>(Hud(), path, "任务 HUD");
        }

        private T RequirePanelChild<T>(string path) where T : Component
        {
            return RequireChild<T>(Panel(), path, "任务面板");
        }

        private static T RequireChild<T>(Component rootView, string path, string what) where T : Component
        {
            Transform child = rootView == null ? null : rootView.transform.Find(path);
            T component = child == null ? null : child.GetComponent<T>();
            if (component == null)
            {
                throw new InvalidOperationException($"{what}下找不到「{path}」上的 {typeof(T).Name}（没开，或预制体物体名不一致）");
            }

            return component;
        }

        /// <summary>任务面板 List 下当前激活的列表项，按兄弟顺序；排除隐藏模板 ItemTemplate。</summary>
        private List<Transform> ActiveItems()
        {
            var result = new List<Transform>();
            QuestPanelView panel = Panel();
            Transform list = panel == null ? null : panel.transform.Find("List");
            if (list == null)
            {
                return result;
            }

            for (int i = 0; i < list.childCount; i++)
            {
                Transform item = list.GetChild(i);
                if (item.gameObject.activeSelf && item.name != "ItemTemplate")
                {
                    result.Add(item);
                }
            }

            return result;
        }

        private bool ItemIs(int index, string title, string kind)
        {
            List<Transform> items = ActiveItems();
            if (index >= items.Count)
            {
                return false;
            }

            return TextAt(items[index], "Title") == title && TextAt(items[index], "Kind") == kind;
        }

        private void ClickItem(int index)
        {
            List<Transform> items = ActiveItems();
            if (index >= items.Count)
            {
                throw new InvalidOperationException($"任务列表只有 {items.Count} 个激活项，点不到第 {index + 1} 项");
            }

            Button button = items[index].GetComponent<Button>();
            if (button == null)
            {
                throw new InvalidOperationException($"任务列表第 {index + 1} 项根上没有 Button");
            }

            button.onClick.Invoke();
        }

        private DialogueView View()
        {
            return ui == null ? null : ui.Get<DialogueView>();
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

        /// <summary>找不到按钮就抛异常：Step 会把这一步记成失败并写明缺了哪个物体。</summary>
        private Button RequireButton(string objectName)
        {
            Button button = FindInView<Button>(objectName);
            if (button == null)
            {
                throw new InvalidOperationException($"对白面板下找不到按钮「{objectName}」（面板没开，或预制体物体名不一致）");
            }

            return button;
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
