// 职责：Performance 模块回放——代码按 id 拉起演出（黑边、字幕、世界时停、停顿确认、结束恢复）、长按跳过提前结束、
//   场景触发区进入即播且只播一次、对白节点前插播演出（PRD 验收 A2–A5）。
// 确认 / 跳过一律走 IPerformanceService.Confirm() / Skip()（等价玩家按确认 / 长按满），用例 1 的第一次确认点面板 TapArea；不读输入、不碰规则与舞台。
using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using Game.Core.Input;
using Game.Core.Save;
using Game.Core.UI;
using Game.Core.UI.Views;
using Game.Dialogue;
using Game.Performance;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests.Showcase.Performance
{
    [Category("Showcase")]
    public sealed class PerformanceShowcase : ShowcaseScenario
    {
        private const float BootTimeoutSeconds = 20f;

        /// <summary>示例演出 id（Addressables 地址），由 T25c 建。</summary>
        private const string SampleId = "perf_sample_greeting";

        /// <summary>第二句台词节点前插播示例演出的对白。</summary>
        private const int InterludeDialogueId = 1003;

        private const string SecondLineText = "老先生，我就看一眼——";

        /// <summary>根作用域类型名：Boot 场景的 GameBootstrap 带 DontDestroyOnLoad，收尾时按名字找来销毁。</summary>
        private const string ScopeTypeName = "Game.Core.Boot.GameLifetimeScope, Game.Core";

        private IUIService ui;
        private IPerformanceService performance;
        private PerformanceRules rules;
        private IInputService input;
        private ISaveService save;
        private bool hasResult;
        private PerformanceResult lastResult;

        protected override string Module => "Performance";

        protected override string ScenePath => "Assets/_Project/Scenes/Verify/Performance.unity";

        protected override bool LoadBootScene => true;

        /// <summary>启动流程走到标题界面才算就绪（同 DialogueShowcase）；另要求演出服务已注册（Boot 挂了 PerformanceInstaller）。</summary>
        protected override IEnumerator WaitForBootReady()
        {
            yield return WaitUntil(
                "启动流程到达标题界面，演出服务已注册",
                () =>
                {
                    IUIService candidate = ResolveService<IUIService>();
                    return candidate != null && candidate.Get<TitleView>() != null
                           && ResolveService<IPerformanceService>() != null;
                },
                BootTimeoutSeconds);
        }

        /// <summary>每条用例都重新加载 Boot：销毁上一条的根作用域，避免叠出多套容器（同 DialogueShowcase）。</summary>
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
        public IEnumerator PlayById_ShowsLetterboxSubtitlesAndRestoresWorld()
        {
            Connect();
            yield return CloseTitleIfOpen();

            yield return Step($"代码拉起演出 {SampleId}", () => PlayAndRecord(SampleId).Forget(), hold: 0f);
            yield return Check("演出进行中：面板打开（黑边、字幕），世界时停，Gameplay 输入图关闭",
                () => performance.IsRunning && View() != null && Time.timeScale == 0f && !GameplayEnabled(), 5f);
            yield return WaitPanelShown();
            yield return Check("底部出现字幕（说话者 + 正文）", SubtitleVisible, 3f);
            yield return Snapshot("黑边与字幕");

            yield return Check("时间轴走到停顿标记，右下角出现「▼」等待确认",
                () => rules != null && rules.Phase == PerformancePhase.Holding && HoldPromptVisible(), 10f);
            yield return Snapshot("等待确认");

            // 第一次确认走面板全屏点击区，证明「点击 = 确认」链路；后续停顿仍用 Confirm()。
            yield return Step("点击画面继续（点演出面板的 TapArea）", () => RequireButton("TapArea").onClick.Invoke());
            yield return Check("点击后停顿解除：「▼」收起，时间轴继续",
                () => rules != null && rules.Phase != PerformancePhase.Holding && !HoldPromptVisible(), 3f);
            yield return DriveUntilEnded(15f);
            yield return Check("演出正常播完：结果 Completed，已记为播过",
                () => !performance.IsRunning && hasResult && lastResult.Outcome == PerformanceOutcome.Completed
                      && performance.HasPlayed(SampleId), 2f);
            yield return Check("世界恢复：timeScale=1、Gameplay 输入图打开、演出面板已关",
                () => Mathf.Approximately(Time.timeScale, 1f) && GameplayEnabled() && View() == null, 3f);
            yield return Snapshot("结束恢复");
        }

        [UnityTest]
        public IEnumerator HoldSkip_EndsEarlyWithSkippedOutcome()
        {
            Connect();
            yield return CloseTitleIfOpen();

            yield return Step($"代码拉起演出 {SampleId}", () => PlayAndRecord(SampleId).Forget(), hold: 0f);
            yield return Check("演出面板打开，右上角显示跳过提示", () => performance.IsRunning && View() != null && SkipRootVisible(), 5f);
            yield return WaitPanelShown();
            yield return Snapshot("跳过提示");

            yield return Step("跳过（等价长按跳过键到满）", () => performance.Skip());
            yield return Check("演出提前结束，结果 Skipped，已记为播过",
                () => !performance.IsRunning && hasResult && lastResult.Outcome == PerformanceOutcome.Skipped
                      && performance.HasPlayed(SampleId), 5f);
            yield return Check("世界恢复：timeScale=1、Gameplay 输入图打开、演出面板已关",
                () => Mathf.Approximately(Time.timeScale, 1f) && GameplayEnabled() && View() == null, 3f);
            yield return Snapshot("跳过后恢复");
        }

        [UnityTest]
        public IEnumerator Trigger_PlaysOnceOnEnter()
        {
            Connect();
            yield return CloseTitleIfOpen();

            var player = FindRequired<Transform>("Player");
            var trigger = FindRequired<PerformanceTrigger>("Trigger_Intro");
            Vector3 home = player.position;
            yield return Step("清空「已播演出」存档，保证起点干净", () =>
            {
                if (save != null)
                {
                    save.Get<PerformanceSaveData>().PlayedIds.Clear();
                }
            });
            yield return Check($"{SampleId} 尚未播过", () => performance != null && !performance.HasPlayed(SampleId));

            yield return Step("玩家走进触发区 Trigger_Intro", () => Teleport(player, trigger.transform.position), hold: 0f);
            yield return WaitPhysicsFrames();
            yield return Check("进入触发区即拉起演出", () => performance.IsRunning && performance.CurrentId == SampleId, 3f);
            yield return WaitPanelShown();
            yield return Snapshot("触发区拉起演出");

            yield return Step("跳过这段演出", () => performance.Skip());
            yield return Check("演出结束，已记为播过", () => !performance.IsRunning && performance.HasPlayed(SampleId), 5f);

            yield return Step("玩家离开触发区", () => Teleport(player, home));
            yield return WaitPhysicsFrames();
            yield return Step("玩家再次走进触发区", () => Teleport(player, trigger.transform.position), hold: 0f);
            yield return WaitPhysicsFrames();

            // 「2 秒内一直没拉起」要按真实时间观察整段窗口，不能用带超时的 Check（它只等「变真」）。
            bool retriggered = false;
            float until = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < until)
            {
                retriggered |= performance.IsRunning;
                yield return null;
            }

            yield return Check("只播一次：再次进入 2 秒内没有再拉起演出", () => !retriggered && !performance.IsRunning);
            yield return Snapshot("再次进入不重播");
            Teleport(player, home);
        }

        [UnityTest]
        public IEnumerator DialogueNode_PlaysPerformanceBeforeSecondLine()
        {
            Connect();
            yield return CloseTitleIfOpen();

            var dialogue = ResolveService<DialogueService>();
            var dialogueRules = ResolveService<DialogueRules>();
            var controller = ResolveService<DialogueController>();
            yield return Step($"拉起对白 {InterludeDialogueId}（第二句前插播演出）",
                () => PlayDialogue(dialogue, InterludeDialogueId).Forget(), hold: 0f);
            // 规则先于面板就绪（面板异步打开）：要等到对白面板的点击区出现，下一步「点对白区」才点得到。
            yield return Check("对白面板打开，第一句开始打字",
                () => dialogueRules != null && dialogueRules.Current != null && dialogueRules.Current.Id == "l1"
                      && FindInDialogueView<Button>("TapArea") != null, 5f);

            yield return Step("点对白区补全第一句", () => TapDialogue());
            yield return Check("第一句整句显示，等待推进",
                () => dialogueRules.Phase == DialogueSaveData.Phase.AwaitAdvance && dialogueRules.Current.Id == "l1", 3f);

            yield return Step("点对白区推进", () => TapDialogue(), hold: 0f);
            yield return Check("对白进入「演出中」，演出服务正在播放",
                () => controller != null && controller.Performing && performance.IsRunning, 5f);
            yield return WaitPanelShown();
            yield return Snapshot("对白插播");

            yield return DriveUntilEnded(15f);
            yield return Check("演出结束后对白继续，正文显示第二句",
                () => !controller.Performing && dialogueRules.Current != null && dialogueRules.Current.Id == "l2"
                      && DialogueBodyText() == SecondLineText, 15f);
            yield return Snapshot("插播结束回到对白");

            yield return TapUntilDialogueEnds(dialogue, 15f);
            yield return Check("对白走完，世界恢复",
                () => !dialogue.IsRunning && Mathf.Approximately(Time.timeScale, 1f) && GameplayEnabled(), 5f);
        }

        /// <summary>从根容器取本回放要用的服务；取不到留 null，由后续检查点记失败。</summary>
        private void Connect()
        {
            hasResult = false;
            lastResult = default;
            ui = ResolveService<IUIService>();
            performance = ResolveService<IPerformanceService>();
            rules = ResolveService<PerformanceRules>();
            input = ResolveService<IInputService>();
            save = ResolveService<ISaveService>();
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

        /// <summary>起播不 await：回放协程要在播放期间继续检查；结果或异常记下来，失败只记 Warning（不能 LogError）。</summary>
        private async UniTaskVoid PlayAndRecord(string id)
        {
            try
            {
                lastResult = await performance.PlayAsync(id);
                hasResult = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VERIFY] 演出 {id} 播放异常：{e.Message}");
            }
        }

        private static async UniTaskVoid PlayDialogue(DialogueService dialogue, int dialogueId)
        {
            try
            {
                await dialogue.PlayAsync(dialogueId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VERIFY] 对白 {dialogueId} 播放异常：{e.Message}");
            }
        }

        /// <summary>等演出结束；途中每遇到停顿就确认一次（等价玩家按确认键），超时记失败继续。</summary>
        private IEnumerator DriveUntilEnded(float timeout)
        {
            float until = Time.realtimeSinceStartup + timeout;
            while (performance != null && performance.IsRunning && Time.realtimeSinceStartup < until)
            {
                if (rules != null && rules.Phase == PerformancePhase.Holding)
                {
                    performance.Confirm();
                }

                yield return null;
            }

            yield return Check("演出在限时内结束（途中停顿逐个确认）", () => performance != null && !performance.IsRunning);
        }

        /// <summary>连点对白区直到对白结束（每 0.2 秒真实时间一下），超时记失败继续。</summary>
        private IEnumerator TapUntilDialogueEnds(DialogueService dialogue, float timeout)
        {
            yield return Step("连点对白区，把剩余台词走完", hold: 0f);
            float until = Time.realtimeSinceStartup + timeout;
            float nextTap = 0f;
            while (dialogue != null && dialogue.IsRunning && Time.realtimeSinceStartup < until)
            {
                if (Time.realtimeSinceStartup >= nextTap)
                {
                    nextTap = Time.realtimeSinceStartup + 0.2f;
                    Button tap = FindInDialogueView<Button>("TapArea");
                    if (tap != null && tap.isActiveAndEnabled)
                    {
                        tap.onClick.Invoke();
                    }
                }

                yield return null;
            }
        }

        private static void Teleport(Transform target, Vector3 position)
        {
            target.position = position;
            Physics2D.SyncTransforms();
        }

        private static IEnumerator WaitPhysicsFrames()
        {
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
        }

        private PerformanceView View()
        {
            return ui == null ? null : ui.Get<PerformanceView>();
        }

        private bool GameplayEnabled()
        {
            return input != null && input.Actions != null && input.Actions.Gameplay.enabled;
        }

        private bool HoldPromptVisible()
        {
            // HoldPrompt 挪进了对白面板 SubtitleRoot 里（面板右下角 ▼），按名字在整棵子树里找，不依赖层级。
            PerformanceView view = View();
            if (view == null)
            {
                return false;
            }

            Transform[] all = view.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == "HoldPrompt")
                {
                    return all[i].gameObject.activeInHierarchy;
                }
            }

            return false;
        }

        /// <summary>
        /// 等演出面板真正盖上来再截图：服务进入播放后面板是异步打开的，黑边推入 / 进场黑场还要再走 FadeSeconds，
        /// 紧跟「服务在播」的检查点截图只能拍到面板打开前的那一帧（HUD、对白框都还在）。
        /// </summary>
        private IEnumerator WaitPanelShown()
        {
            yield return Check("演出面板盖上来：上下黑边出现，HUD / 对白框随层隐藏",
                () => View() != null && ChildActive(View(), "LetterboxTop"), 5f);
            yield return Wait(0.6f);
        }

        private bool SubtitleVisible()
        {
            PerformanceView view = View();
            if (view == null)
            {
                return false;
            }

            Transform[] all = view.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == "SubtitleRoot")
                {
                    return all[i].gameObject.activeInHierarchy;
                }
            }

            return false;
        }

        private bool SkipRootVisible()
        {
            return ChildActive(View(), "SkipRoot");
        }

        private static bool ChildActive(Component root, string childName)
        {
            if (root == null)
            {
                return false;
            }

            Transform child = root.transform.Find(childName);
            return child != null && child.gameObject.activeInHierarchy;
        }

        private Button RequireButton(string objectName)
        {
            PerformanceView view = View();
            Transform child = view == null ? null : view.transform.Find(objectName);
            Button button = child == null ? null : child.GetComponent<Button>();
            if (button == null)
            {
                throw new InvalidOperationException($"演出面板下找不到按钮「{objectName}」（面板没开，或预制体物体名不一致）");
            }

            return button;
        }

        private void TapDialogue()
        {
            Button tap = FindInDialogueView<Button>("TapArea");
            if (tap == null)
            {
                throw new InvalidOperationException("对白面板下找不到按钮「TapArea」（面板没开，或预制体物体名不一致）");
            }

            tap.onClick.Invoke();
        }

        private string DialogueBodyText()
        {
            TMP_Text body = FindInDialogueView<TMP_Text>("Body");
            return body == null ? null : body.text;
        }

        /// <summary>在对白面板下按物体名找组件（含未激活的）；面板没开或找不到返回 null。</summary>
        private T FindInDialogueView<T>(string objectName) where T : Component
        {
            DialogueView view = ui == null ? null : ui.Get<DialogueView>();
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
    }
}
