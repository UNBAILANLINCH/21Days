// 职责：Performance 模块世界舞台回放——走 Boot 真实流程进 SampleScene，玩家走进村口触发区拉起 perf_sample_scene_talk：
//   五个方舟小人站在 3D 灰盒里、舞台相机接管画面、底部对白面板带头像；逐句确认走完 / 长按跳过，两条路径都要把主相机、
//   HUD 层、玩家渲染器恢复原样。截图给人看构图（对标《明日方舟》活动探索截图）。
// 为什么新建（project-root.md「加能力的顺序」）：
//   复用 —— PerformanceShowcase 停在标题、把 SampleScene 叠加加载，不走「开始」进场的真实流程（玩家规则、存档、相机接管都不齐）；
//   扩展 —— 往它里面加用例要改 ScenePath，四条旧用例会跟着换场景，职责说不通。进场方式照抄 ExplorationShowcase（sealed，不能继承）。
// 确认 / 跳过走 IPerformanceService.Confirm() / Skip()（等价玩家按确认 / 长按满），不读输入；
//   LOG / 自动点演出面板上的 HistoryButton / AutoButton，台词记录（TranscriptView，Top 层）点它的 CloseButton 关。
using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.Core.Flow;
using Game.Core.UI;
using Game.Core.UI.Views;
using Game.Performance;
using Game.Player;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests.Showcase.Performance
{
    [Category("Showcase")]
    public sealed class ScenePerformanceShowcase : ShowcaseScenario
    {
        private const float BootTimeoutSeconds = 20f;
        private const float EnterTimeoutSeconds = 20f;

        /// <summary>单句字幕 4 秒 + 间隔 0.3 秒，等下一句 / 下一个停顿的上限留足余量。</summary>
        private const float LineTimeoutSeconds = 8f;

        /// <summary>进场后至少等多久再走进触发区（相机跟到玩家）。</summary>
        private const float MinSettleSeconds = 1.5f;

        /// <summary>通知卡片连续多久没出现才算进场通知放完（含淡出）。</summary>
        private const float QuietSeconds = 1.5f;

        /// <summary>等通知放完的上限；到点照走，截图上可能带一张通知。</summary>
        private const float SettleTimeoutSeconds = 20f;

        private const string SampleId = "perf_sample_scene_talk";
        private const string TriggerName = "Trigger_VillageEntrance";

        /// <summary>SampleScene 巡逻怪（与舞台上的陈同一形象），只用来记录位置排查构图重影。</summary>
        private const string PatrolMonsterName = "enerme";

        /// <summary>触发区在场景 XZ (9.5, 2.5)。</summary>
        private static readonly Vector2 TriggerPoint = new Vector2(9.5f, 2.5f);

        /// <summary>小人根在舞台相机视口内的安全范围（两轴都要落在里面）。</summary>
        private const float ViewportMin = 0.08f;
        private const float ViewportMax = 0.92f;

        /// <summary>根作用域类型名：Boot 场景的 GameBootstrap 带 DontDestroyOnLoad，收尾时按名字找来销毁。</summary>
        private const string ScopeTypeName = "Game.Core.Boot.GameLifetimeScope, Game.Core";

        private IUIService ui;
        private IPerformanceService performance;
        private PerformanceRules rules;
        private PlayerRules playerRules;

        private Camera mainCamera;
        private int mainMaskBefore;
        private List<Component> playerVisuals;
        private bool[] playerVisualsBefore;
        private List<Component> sceneVisuals;
        private bool[] sceneVisualsBefore;

        protected override string Module => "Performance";

        /// <summary>世界由流程加载（标题「开始」→ 探索场景），这里不直接加载场景。</summary>
        protected override string ScenePath => null;

        protected override bool LoadBootScene => true;

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

        /// <summary>先经流程退回标题（让流程自己卸载探索场景），再销毁根作用域（同 ExplorationShowcase）。</summary>
        [UnityTearDown]
        public IEnumerator DestroyBootScope()
        {
            IGameFlow flow = ResolveService<IGameFlow>();
            if (flow != null && !(flow.Current is TitleState))
            {
                bool left = false;
                LeaveToTitleAsync(flow, () => left = true).Forget();
                float deadline = Time.realtimeSinceStartup + EnterTimeoutSeconds;
                while (!left && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }
            }

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
        public IEnumerator VillageEntrance_WorldStageTalkAndRestore()
        {
            yield return EnterSampleScene();
            yield return WalkIntoTrigger();

            // 逐字只持续约一秒：不停顿直接逐帧轮询「正在打字」的瞬态；超时给 5 s 是等演出加载与面板打开，条件本身只在打字那几帧成立。
            yield return Check("第一句逐字显示中（0 < 已显示字数 < 总字数）", () =>
            {
                TMP_Text body = FindInView<TMP_Text>("Body");
                if (body == null || !body.gameObject.activeInHierarchy || string.IsNullOrEmpty(body.text)) return false;
                int shown = body.maxVisibleCharacters;
                return shown > 0 && shown < body.textInfo.characterCount;
            }, 5f);
            yield return Check("面板显示第一句：说话者「阿米娅」、头像在左侧",
                () => SpeakerText() == "阿米娅" && AvatarShownOn(false), 5f);
            yield return Check("舞台相机接管画面：它是深度最高的启用相机，主相机遮罩清零",
                () => StageCameraOnTop(Stage()) && mainCamera != null && mainCamera.cullingMask == 0, 3f);
            yield return Check("玩家渲染器与名牌全部隐藏（舞台上的阿米娅是替身，不出重影）",
                () => AllHidden(playerVisuals), 2f);
            yield return Check("场景 NPC / 巡逻怪 / 物资箱标记的 Renderer 与 Canvas 全部隐藏（与舞台小人同形象，不出重影）",
                () => AllHidden(sceneVisuals), 2f);
            yield return Check("五个小人都在舞台相机画面内", () => ActorsInView(Stage()), 2f);
            yield return Wait(0.6f);
            yield return Snapshot("01-第一句·阿米娅");

            // LOG：第一句停顿时点左上「LOG」，台词记录（Top 层）压在演出之上、时间轴不走；关掉后照常确认继续。
            yield return Check("时间轴走到第一句停顿", () => rules != null && rules.Phase == PerformancePhase.Holding, LineTimeoutSeconds);
            yield return Step("点左上「LOG」打开台词记录", () => RequireViewButton("HistoryButton").onClick.Invoke());
            yield return Check("台词记录（TranscriptView）已打开，内容含「阿米娅：」", () => TranscriptText().Contains("阿米娅："), 3f);
            double directorTimeAtLog = DirectorTime();
            yield return Step("LOG 开着静置 0.5 秒（真实时间）", null, 0f);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Check("LOG 开着 0.5 秒：导演时间没走、演出仍停在停顿",
                () => directorTimeAtLog >= 0d && Math.Abs(DirectorTime() - directorTimeAtLog) < 1e-4
                      && performance.IsRunning && rules.Phase == PerformancePhase.Holding);
            yield return Snapshot("LOG·演出");
            yield return Step("点台词记录的「关闭」", () => RequireTranscriptClose().onClick.Invoke());
            yield return Check("台词记录已关闭，演出仍停在停顿等确认",
                () => ui.Get<TranscriptView>() == null && rules.Phase == PerformancePhase.Holding, 3f);

            yield return ConfirmAtHold();
            yield return Check("第二句：说话者「陈」、头像在右侧", () => SpeakerText() == "陈" && AvatarShownOn(true), LineTimeoutSeconds);
            yield return Wait(0.4f);
            yield return Snapshot("02-第二句·陈");

            yield return ConfirmAtHold();
            yield return Check("第三句：说话者「德克萨斯」、头像在左侧", () => SpeakerText() == "德克萨斯" && AvatarShownOn(false), LineTimeoutSeconds);
            yield return Wait(0.4f);
            yield return Snapshot("03-第三句·德克萨斯");

            // 自动：点右上「自动」后不再确认，余下第三～六句的四个停顿都是字打完再等自动间隔（默认 1.5 秒）自己继续，直到时间轴走完。
            yield return Step("点右上「自动」", () => RequireViewButton("AutoButton").onClick.Invoke());
            yield return Check("「自动」标签变为「自动中」", () => AutoLabelText() == "自动中", 2f);
            yield return Snapshot("自动中·演出");
            // 四句 × (4 秒片段 + 0.3 秒间隔 + 字打完后 1.5 秒自动间隔) ≈ 23 秒，超时给足 60 秒。
            yield return Check("不再确认：演出自己逐句走完，结果 Completed、六个停顿全部走过",
                () => !performance.IsRunning && rules != null && rules.Outcome == PerformanceOutcome.Completed
                      && rules.HoldCount == 6, 60f);
            yield return CheckRestored();
            yield return Wait(0.5f);
            yield return Snapshot("04-结束·回到探索");
        }

        [UnityTest]
        public IEnumerator VillageEntrance_HoldSkipRestores()
        {
            yield return EnterSampleScene();
            yield return WalkIntoTrigger();
            yield return Check("第一句出现", () => SpeakerText() == "阿米娅", 5f);

            yield return Step("跳过（等价长按跳过键到满）", () => performance.Skip());
            yield return Check("演出提前结束，结果 Skipped",
                () => !performance.IsRunning && rules != null && rules.Outcome == PerformanceOutcome.Skipped, 5f);
            yield return CheckRestored();
            yield return Wait(0.5f);
            yield return Snapshot("05-跳过后恢复");
        }

        // ───────────────────────── 进入与触发 ─────────────────────────

        private IEnumerator EnterSampleScene()
        {
            Connect();
            IGameFlow flow = ResolveService<IGameFlow>();
            yield return WaitUntil("流程进入标题状态", () => flow != null && flow.Current is TitleState, BootTimeoutSeconds);
            yield return Step("点标题界面「开始」", () => RequireTitleStart(ui.Get<TitleView>()).onClick.Invoke(), 0f);
            yield return WaitUntil("进入探索场景：标题关闭、村口触发区与玩家已就位",
                () => ui != null && ui.Get<TitleView>() == null && GameObject.Find(TriggerName) != null
                      && UnityEngine.Object.FindObjectOfType<PerformanceTriggerActor>() != null,
                EnterTimeoutSeconds);
            Connect();
            yield return Step("等相机跟到玩家、进场的「接取任务」等通知放完", null, 0f);
            // 按真实时间等（不受回放倍率影响）：通知卡片在 Top 层，演出只藏 Hud / Popup，不等它放完会压在演出截图上。
            // 进场会连着弹好几张（接取任务 ×2、自动保存），要求卡片连续 QuietSeconds 不出现才算放完。
            float start = Time.realtimeSinceStartup;
            float lastShown = start;
            while (Time.realtimeSinceStartup - start < SettleTimeoutSeconds)
            {
                NotificationView notice = ui == null ? null : ui.Get<NotificationView>();
                if (notice != null && notice.IsCardShown)
                {
                    lastShown = Time.realtimeSinceStartup;
                }

                if (Time.realtimeSinceStartup - start >= MinSettleSeconds
                    && Time.realtimeSinceStartup - lastShown >= QuietSeconds)
                {
                    break;
                }

                yield return null;
            }
        }

        private IEnumerator WalkIntoTrigger()
        {
            // 环境守卫：before 快照必须拍在演出介入之前。静置期里玩家若被外部输入带进触发区（曾因卡键复现），
            // 快照会记下「已被演出隐藏」的状态，后面三条恢复检查全部误报；这里先红，报告一眼看出是环境问题。
            yield return Check("回放环境干净：演出尚未运行、玩家存活",
                () => performance != null && !performance.IsRunning
                      && playerRules != null && playerRules.Model.Snapshot.IsAlive, 1f);
            mainCamera = Camera.main;
            mainMaskBefore = mainCamera == null ? 0 : mainCamera.cullingMask;
            var actor = UnityEngine.Object.FindObjectOfType<PerformanceTriggerActor>();
            playerVisuals = CollectVisuals(actor == null ? null : actor.gameObject);
            playerVisualsBefore = ReadEnabled(playerVisuals);
            GameObject triggerGo = GameObject.Find(TriggerName);
            PerformanceTrigger trigger = triggerGo == null ? null : triggerGo.GetComponent<PerformanceTrigger>();
            sceneVisuals = new List<Component>();
            if (trigger != null)
            {
                foreach (GameObject root in trigger.HiddenDuringPlay)
                {
                    sceneVisuals.AddRange(CollectVisuals(root));
                }
            }

            sceneVisualsBefore = ReadEnabled(sceneVisuals);

            GameObject patrol = GameObject.Find(PatrolMonsterName);
            Debug.Log($"{ShowcaseOptions.Prefix}[Performance] 触发前巡逻怪位置：{(patrol == null ? "无" : patrol.transform.position.ToString("F2"))}");
            yield return Step("玩家走进村口触发区 (9.5, 2.5)",
                () => playerRules.Reset(TriggerPoint), hold: 0f);
            yield return Check($"进入触发区即拉起演出 {SampleId}",
                () => performance != null && performance.IsRunning && performance.CurrentId == SampleId, 5f);
        }

        /// <summary>等时间轴走到停顿（▼）再确认一次。</summary>
        private IEnumerator ConfirmAtHold()
        {
            yield return Check("时间轴走到停顿标记，等待确认",
                () => rules != null && rules.Phase == PerformancePhase.Holding, LineTimeoutSeconds);
            yield return Step("确认（点击 / 空格）", () => performance.Confirm(), 0.3f);
        }

        private IEnumerator CheckRestored()
        {
            yield return Check("主相机遮罩恢复、HUD 层恢复可见、演出面板已关",
                () => mainCamera != null && mainCamera.cullingMask == mainMaskBefore && HudCanvasVisible()
                      && (ui == null || ui.Get<PerformanceView>() == null), 3f);
            yield return Check("玩家渲染器与名牌恢复到进入前的开关状态", () => Restored(playerVisuals, playerVisualsBefore), 2f);
            yield return Check("场景 NPC / 巡逻怪 / 标记恢复到进入前的开关状态", () => Restored(sceneVisuals, sceneVisualsBefore), 2f);
            yield return Check("世界恢复：timeScale = 1", () => Mathf.Approximately(Time.timeScale, 1f), 2f);
        }

        private void Connect()
        {
            ui = ResolveService<IUIService>();
            performance = ResolveService<IPerformanceService>();
            rules = ResolveService<PerformanceRules>();
            playerRules = ResolveService<PlayerRules>();
        }

        private static async UniTaskVoid LeaveToTitleAsync(IGameFlow flow, Action done)
        {
            try
            {
                await flow.GoToAsync<TitleState>();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{ShowcaseOptions.Prefix}[Performance] 收尾切回标题失败：{e.GetType().Name}：{e.Message}");
            }
            finally
            {
                done();
            }
        }

        // ───────────────────────── 判定 ─────────────────────────

        /// <summary>演出实例是服务异步实例化的：每次现找，不在触发那一刻缓存（那时可能还没建出来）。</summary>
        private static PerformanceStage Stage()
        {
            return UnityEngine.Object.FindObjectOfType<PerformanceStage>();
        }

        private static bool StageCameraOnTop(PerformanceStage stage)
        {
            Camera stageCamera = stage == null ? null : stage.StageCamera;
            if (stageCamera == null || !stageCamera.isActiveAndEnabled)
            {
                return false;
            }

            Camera[] cameras = Camera.allCameras;
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != stageCamera && cameras[i].depth >= stageCamera.depth)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ActorsInView(PerformanceStage stage)
        {
            Camera stageCamera = stage == null ? null : stage.StageCamera;
            Transform actors = stage == null ? null : stage.transform.Find("Actors");
            if (stageCamera == null || actors == null || actors.childCount != 5)
            {
                return false;
            }

            for (int i = 0; i < actors.childCount; i++)
            {
                Vector3 p = stageCamera.WorldToViewportPoint(actors.GetChild(i).position);
                if (p.z <= 0f || p.x < ViewportMin || p.x > ViewportMax || p.y < ViewportMin || p.y > ViewportMax)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>物体根下全部 Renderer 与 Canvas（世界空间名牌 / 标记）；根为 null 返回空表。</summary>
        private static List<Component> CollectVisuals(GameObject root)
        {
            var result = new List<Component>();
            if (root == null)
            {
                return result;
            }

            result.AddRange(root.GetComponentsInChildren<Renderer>(true));
            result.AddRange(root.GetComponentsInChildren<Canvas>(true));
            return result;
        }

        private static bool IsEnabled(Component component)
        {
            if (component is Renderer renderer)
            {
                return renderer.enabled;
            }

            return component is Behaviour behaviour && behaviour.enabled;
        }

        private static bool[] ReadEnabled(List<Component> components)
        {
            var states = new bool[components.Count];
            for (int i = 0; i < components.Count; i++)
            {
                states[i] = components[i] != null && IsEnabled(components[i]);
            }

            return states;
        }

        private static bool AllHidden(List<Component> components)
        {
            if (components == null || components.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < components.Count; i++)
            {
                if (components[i] != null && IsEnabled(components[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Restored(List<Component> components, bool[] before)
        {
            if (components == null || before == null)
            {
                return false;
            }

            for (int i = 0; i < components.Count; i++)
            {
                if (components[i] != null && IsEnabled(components[i]) != before[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HudCanvasVisible()
        {
            GameObject hud = GameObject.Find("Canvas_Hud");
            Canvas canvas = hud == null ? null : hud.GetComponent<Canvas>();
            return canvas != null && canvas.enabled;
        }

        private string SpeakerText()
        {
            TMP_Text speaker = FindInView<TMP_Text>("Speaker");
            return speaker == null || !speaker.gameObject.activeInHierarchy ? null : speaker.text;
        }

        // 头像只显示在指定一侧：该侧可见、另一侧不可见。
        private bool AvatarShownOn(bool right)
        {
            bool leftShown = ImageShown(FindInView<Image>("Avatar"));
            bool rightShown = ImageShown(FindInView<Image>("AvatarRight"));
            return right ? rightShown && !leftShown : leftShown && !rightShown;
        }

        private static bool ImageShown(Image image)
        {
            return image != null && image.enabled && image.sprite != null && image.gameObject.activeInHierarchy;
        }

        private T FindInView<T>(string objectName) where T : Component
        {
            PerformanceView view = ui == null ? null : ui.Get<PerformanceView>();
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

        /// <summary>演出面板下按物体名取按钮；面板没开或找不到就抛异常，让 Step 记失败。</summary>
        private Button RequireViewButton(string objectName)
        {
            Button button = FindInView<Button>(objectName);
            if (button == null)
            {
                throw new InvalidOperationException($"演出面板下找不到按钮「{objectName}」（面板没开，或预制体物体名不一致）");
            }

            return button;
        }

        /// <summary>「自动」按钮主标签文字（AutoButton/Label）；找不到返回 null。Label 重名，所以先定位按钮再找子物体。</summary>
        private string AutoLabelText()
        {
            Button auto = FindInView<Button>("AutoButton");
            Transform label = auto == null ? null : auto.transform.Find("Label");
            TMP_Text text = label == null ? null : label.GetComponent<TMP_Text>();
            return text == null ? null : text.text;
        }

        /// <summary>台词记录（TranscriptView，Top 层）的正文；没开返回空串。</summary>
        private string TranscriptText()
        {
            TranscriptView transcript = ui == null ? null : ui.Get<TranscriptView>();
            TMP_Text content = transcript == null ? null : FindDeep<TMP_Text>(transcript.transform, "Content");
            return content == null ? string.Empty : content.text;
        }

        /// <summary>台词记录的「关闭」按钮；没开或找不到就抛异常，让 Step 记失败。</summary>
        private Button RequireTranscriptClose()
        {
            TranscriptView transcript = ui == null ? null : ui.Get<TranscriptView>();
            Button close = transcript == null ? null : FindDeep<Button>(transcript.transform, "CloseButton");
            if (close == null)
            {
                throw new InvalidOperationException("台词记录没开，或找不到「CloseButton」");
            }

            return close;
        }

        /// <summary>演出时间轴当前时间（秒）；没有演出实例返回 -1。</summary>
        private static double DirectorTime()
        {
            PerformanceStage stage = Stage();
            return stage == null || stage.Director == null ? -1d : stage.Director.time;
        }

        private static Button RequireTitleStart(TitleView title)
        {
            Button button = title == null ? null : FindDeep<Button>(title.transform, "StartButton");
            if (button == null)
            {
                throw new InvalidOperationException("标题界面没开，或找不到「StartButton」");
            }

            return button;
        }
    }
}
