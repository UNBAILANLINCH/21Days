// 职责：Performance 模块回放——代码按 id 拉起世界舞台演出 perf_sample_scene_talk（摆到村口锚点；对白面板、字幕、世界时停、
//   停顿确认、结束恢复）、长按跳过提前结束、场景触发区（SampleScene 村口）进入即播、按触发器 Once 开关判定是否重播、
//   对白节点前插播演出并摆到说话 NPC 脚下，插播期间场景里的玩家 / NPC 藏起、回到对白后恢复（PRD 验收 A2–A5）。
// 确认 / 跳过一律走 IPerformanceService.Confirm() / Skip()（等价玩家按确认 / 长按满），用例 1 的第一次确认点面板 TapArea；不读输入、不碰规则与舞台。
using System;
using System.Collections;
using System.Collections.Generic;
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

        /// <summary>示例演出 id（Addressables 地址）：世界舞台示例「村口·场景对白」（SceneTalkSampleBuilder 建）。</summary>
        private const string SampleId = "perf_sample_scene_talk";

        /// <summary>SampleScene 村口触发区：它的 Anchor（StageAnchor）是代码拉起示例演出时的摆放锚点。</summary>
        private const string TriggerName = "Trigger_VillageEntrance";

        // 对白 1003 的说话者、插播演出的摆放锚点：复用基类 ShowcaseScenario.ElderName（"Npc_Elder"）。

        /// <summary>SampleScene 的玩家根物体名。</summary>
        private const string PlayerName = "player";

        /// <summary>SampleScene 的旅人 NPC（德克萨斯小人，舞台上也有一个德克萨斯）。</summary>
        private const string TravelerName = "Npc_Traveler";

        /// <summary>插播舞台与锚点的容差：水平距离、高度差都要小于它。</summary>
        private const float PlacementTolerance = 0.5f;

        /// <summary>示例演出 6 句 ×（4 秒片段 + 0.3 秒间隔）≈ 26 秒；逐个确认走完的上限留足余量。</summary>
        private const float SampleRunTimeoutSeconds = 45f;

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
        private float lastHorizontalGap = -1f;
        private float lastVerticalGap = -1f;

        protected override string Module => "Performance";

        protected override string ScenePath => ShowcaseOptions.DemoScenePath;

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
        public IEnumerator PlayById_ShowsSubtitlesAndRestoresWorld()
        {
            Connect();
            yield return CloseTitleIfOpen();
            Renderer[] characterRenderers = SceneCharacterRenderers();
            bool[] rendererStatesBefore = EnabledStates(characterRenderers);

            Transform anchor = VillageStageAnchor();
            yield return Step($"代码拉起演出 {SampleId}（摆到村口锚点 {anchor.name}）",
                () => PlayAndRecord(SampleId, PerformancePlacement.FromTransform(anchor)).Forget(), hold: 0f);
            yield return Check("演出进行中：对白面板打开，世界时停，Gameplay 输入图关闭",
                () => performance.IsRunning && View() != null && Time.timeScale == 0f && !GameplayEnabled(), 5f);
            yield return WaitPanelShown();
            yield return Check("底部对白面板出现字幕（说话者 + 正文）", SubtitleVisible, 3f);
            yield return Check("代码试播同样隐藏全部场景角色（含新增 NPC）", () => AllDisabled(characterRenderers), 3f);
            yield return Check($"舞台摆到村口锚点：演出实例根与 {anchor.name} 水平距离、高度差都 < {PlacementTolerance}",
                () => StageNear(anchor), 3f);
            yield return Snapshot("世界舞台与字幕");

            yield return Check("时间轴走到停顿标记，右下角出现「▼」等待确认",
                () => rules != null && rules.Phase == PerformancePhase.Holding && HoldPromptVisible(), 10f);
            yield return Snapshot("等待确认");

            // 第一次确认走面板全屏点击区，证明「点击 = 确认」链路；后续停顿仍用 Confirm()。
            yield return Step("点击画面继续（点演出面板的 TapArea）", () => RequireButton("TapArea").onClick.Invoke());
            yield return Check("点击后停顿解除：「▼」收起，时间轴继续",
                () => rules != null && rules.Phase != PerformancePhase.Holding && !HoldPromptVisible(), 3f);
            yield return DriveUntilEnded(SampleRunTimeoutSeconds);
            yield return Check("演出正常播完：结果 Completed，已记为播过",
                () => !performance.IsRunning && hasResult && lastResult.Outcome == PerformanceOutcome.Completed
                      && performance.HasPlayed(SampleId), 2f);
            yield return Check("世界恢复：timeScale=1、Gameplay 输入图打开、演出面板已关",
                () => Mathf.Approximately(Time.timeScale, 1f) && GameplayEnabled() && View() == null, 3f);
            yield return Check("代码试播结束，场景角色恢复原显隐", () => StatesMatch(characterRenderers, rendererStatesBefore), 3f);
            yield return Snapshot("结束恢复");
        }

        [UnityTest]
        public IEnumerator HoldSkip_EndsEarlyWithSkippedOutcome()
        {
            Connect();
            yield return CloseTitleIfOpen();

            Transform anchor = VillageStageAnchor();
            yield return Step($"代码拉起演出 {SampleId}（摆到村口锚点 {anchor.name}）",
                () => PlayAndRecord(SampleId, PerformancePlacement.FromTransform(anchor)).Forget(), hold: 0f);
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

        /// <summary>
        /// 回放舞台 SampleScene 的村口触发区：播世界舞台对白（id 从触发器读，不写死），配置为「每次进入都播」（once = false）。
        /// 用例按触发器的 Once 开关断言：一次性的再次进入不重播；非一次性的再次进入会再拉起一次。
        /// </summary>
        [UnityTest]
        public IEnumerator Trigger_PlaysOnEnter_FollowsOnceFlag()
        {
            Connect();
            yield return CloseTitleIfOpen();

            var player = FindRequired<Transform>("player");
            var trigger = FindRequired<PerformanceTrigger>(TriggerName);
            string id = trigger.PerformanceId;
            Vector3 home = player.position;
            yield return Step("清空「已播演出」存档，保证起点干净", () =>
            {
                if (save != null)
                {
                    save.Get<PerformanceSaveData>().PlayedIds.Clear();
                }
            });
            yield return Check($"{id} 尚未播过", () => performance != null && !performance.HasPlayed(id));

            yield return Step("玩家走进村口触发区 Trigger_VillageEntrance", () => Teleport(player, trigger.transform.position), hold: 0f);
            yield return WaitPhysicsFrames();
            yield return Check($"进入触发区即拉起演出 {id}", () => performance.IsRunning && performance.CurrentId == id, 3f);
            // 村口是世界舞台演出（舞台相机 + 底部对白面板）：只等面板打开再截图，构图细节归 ScenePerformanceShowcase。
            yield return Check("演出面板打开", () => View() != null, 5f);
            yield return Wait(0.6f);
            yield return Snapshot("触发区拉起演出");

            yield return Step("跳过这段演出", () => performance.Skip());
            yield return Check("演出结束，已记为播过", () => !performance.IsRunning && performance.HasPlayed(id), 5f);

            yield return Step("玩家离开触发区", () => Teleport(player, home));
            yield return WaitPhysicsFrames();
            yield return Step("玩家再次走进触发区", () => Teleport(player, trigger.transform.position), hold: 0f);
            yield return WaitPhysicsFrames();

            if (trigger.Once)
            {
                // 「2 秒内一直没拉起」要按真实时间观察整段窗口，不能用带超时的 Check（它只等「变真」）。
                bool retriggered = false;
                float until = Time.realtimeSinceStartup + 2f;
                while (Time.realtimeSinceStartup < until)
                {
                    retriggered |= performance.IsRunning;
                    yield return null;
                }

                yield return Check("一次性触发区：再次进入 2 秒内没有再拉起演出", () => !retriggered && !performance.IsRunning);
                yield return Snapshot("再次进入不重播");
            }
            else
            {
                yield return Check($"非一次性触发区：再次进入又拉起演出 {id}",
                    () => performance.IsRunning && performance.CurrentId == id, 3f);
                yield return Check("演出面板再次打开", () => View() != null, 5f);
                yield return Wait(0.6f);
                yield return Snapshot("再次进入重播");
                yield return Step("跳过重播的演出", () => performance.Skip());
                yield return Check("演出结束，世界恢复", () => !performance.IsRunning && View() == null, 5f);
            }

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
            var elder = FindRequired<Transform>(ElderName);
            Renderer[] characterRenderers = SceneCharacterRenderers();
            bool[] rendererStatesBefore = null;
            yield return Step($"拉起对白 {InterludeDialogueId}（第二句前插播演出，锚点 {ElderName}）",
                () => PlayDialogue(dialogue, InterludeDialogueId, elder).Forget(), hold: 0f);
            // 规则先于面板就绪（面板异步打开）：要等到对白面板的点击区出现，下一步「点对白区」才点得到。
            yield return Check("对白面板打开，第一句开始打字",
                () => dialogueRules != null && dialogueRules.Current != null && dialogueRules.Current.Id == "l1"
                      && FindInDialogueView<Button>("TapArea") != null, 5f);

            // 打字中补全要连点三下（对白与演出同一规则），同一帧连点三下对白区。
            yield return Step("连点三下对白区补全第一句", () =>
            {
                TapDialogue();
                TapDialogue();
                TapDialogue();
            });
            yield return Check("第一句整句显示，等待推进",
                () => dialogueRules.Phase == DialogueSaveData.Phase.AwaitAdvance && dialogueRules.Current.Id == "l1", 3f);

            yield return Step("点对白区推进（先记下场景角色渲染器进入插播前的显隐）", () =>
            {
                rendererStatesBefore = EnabledStates(characterRenderers);
                TapDialogue();
            }, hold: 0f);
            yield return Check("对白进入「演出中」，演出服务正在播放",
                () => controller != null && controller.Performing && performance.IsRunning, 5f);
            yield return WaitPanelShown();
            yield return Check($"插播舞台摆到长者脚下：演出实例根与 {ElderName} 水平距离、高度差都 < {PlacementTolerance}",
                () => StageNear(elder), 3f);
            Debug.Log($"{ShowcaseOptions.Prefix}[Performance] 插播舞台位置实测：水平距离 {lastHorizontalGap:F3}，高度差 {lastVerticalGap:F3}");
            yield return Step($"记录实测：水平距离 {lastHorizontalGap:F3}、高度差 {lastVerticalGap:F3}", null, 0f);
            yield return Check($"插播期间场景角色已隐藏：{PlayerName}、{ElderName}、{TravelerName} 根下渲染器"
                               + $"（共 {characterRenderers.Length} 个）全部 enabled == false，画面里只剩舞台小人",
                () => AllDisabled(characterRenderers), 3f);
            yield return Snapshot("插播·场景角色已隐藏");

            yield return DriveUntilEnded(SampleRunTimeoutSeconds);
            yield return Check("演出结束后对白继续，正文显示第二句",
                () => !controller.Performing && dialogueRules.Current != null && dialogueRules.Current.Id == "l2"
                      && DialogueBodyText() == SecondLineText, 15f);
            yield return Check("插播结束回到对白：场景角色渲染器全部恢复为进入插播前的显隐",
                () => StatesMatch(characterRenderers, rendererStatesBefore), 3f);
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
        private async UniTaskVoid PlayAndRecord(string id, PerformancePlacement placement)
        {
            try
            {
                lastResult = await performance.PlayAsync(id, placement);
                hasResult = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VERIFY] 演出 {id} 播放异常：{e.Message}");
            }
        }

        /// <summary>走带锚点的重载：节点前插播的世界舞台演出摆到 <paramref name="anchor"/>（同 NPC 交互拉起时传自身）。</summary>
        private static async UniTaskVoid PlayDialogue(DialogueService dialogue, int dialogueId, Transform anchor)
        {
            try
            {
                await dialogue.PlayAsync(dialogueId, anchor);
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
            // 回放舞台 SampleScene 是 3D 场景（触发区是 BoxCollider）；2D 同步保留给代码搭的 2D 场景。
            Physics.SyncTransforms();
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
        /// 等演出面板真正盖上来再截图：服务进入播放后面板是异步打开的，进场黑场还要再走 FadeSeconds，
        /// 紧跟「服务在播」的检查点截图只能拍到面板打开前的那一帧（HUD、对白框都还在）。
        /// 以底部对白面板（SubtitleRoot）出现为准。
        /// </summary>
        private IEnumerator WaitPanelShown()
        {
            yield return Check("演出面板盖上来：底部对白面板出现，HUD / 对白框随层隐藏",
                () => View() != null && SubtitleVisible(), 5f);
            yield return Wait(0.6f);
        }

        /// <summary>村口触发区的世界舞台锚点（StageAnchor）；触发区没接锚点时退回触发区自身并记一条 Warning。</summary>
        private Transform VillageStageAnchor()
        {
            var trigger = FindRequired<PerformanceTrigger>(TriggerName);
            Transform anchor = trigger.Anchor;
            if (anchor != null)
            {
                return anchor;
            }

            Debug.LogWarning($"{ShowcaseOptions.Prefix}[Performance] {TriggerName} 没接 anchor，退回用触发区自身作摆放锚点");
            return trigger.transform;
        }

        /// <summary>
        /// 演出实例根（带 PerformanceStage 的预制体根，服务摆放的就是它）与锚点：水平（XZ）距离、高度差都小于容差。
        /// 实测值记进 lastHorizontalGap / lastVerticalGap 供报告。
        /// </summary>
        private bool StageNear(Transform anchor)
        {
            PerformanceStage stage = UnityEngine.Object.FindObjectOfType<PerformanceStage>();
            if (stage == null || anchor == null)
            {
                return false;
            }

            Vector3 delta = stage.transform.position - anchor.position;
            lastHorizontalGap = new Vector2(delta.x, delta.z).magnitude;
            lastVerticalGap = Mathf.Abs(delta.y);
            return lastHorizontalGap < PlacementTolerance && lastVerticalGap < PlacementTolerance;
        }

        /// <summary>
        /// 演出前收集场景角色根下全部渲染器（含未激活），独立于触发器的手工名单。
        /// </summary>
        private Renderer[] SceneCharacterRenderers()
        {
            var renderers = new List<Renderer>();
            string[] names = { PlayerName, ElderName, TravelerName, "Npc_Villager", "enerme", "Yao_WellWoman" };
            for (int i = 0; i < names.Length; i++)
            {
                renderers.AddRange(FindRequired<Transform>(names[i]).GetComponentsInChildren<Renderer>(true));
            }

            return renderers.ToArray();
        }

        private static bool[] EnabledStates(Renderer[] renderers)
        {
            var states = new bool[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                states[i] = renderers[i] != null && renderers[i].enabled;
            }

            return states;
        }

        private static bool AllDisabled(Renderer[] renderers)
        {
            if (renderers.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].enabled)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool StatesMatch(Renderer[] renderers, bool[] states)
        {
            if (states == null || states.Length != renderers.Length)
            {
                return false;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].enabled != states[i])
                {
                    return false;
                }
            }

            return true;
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
