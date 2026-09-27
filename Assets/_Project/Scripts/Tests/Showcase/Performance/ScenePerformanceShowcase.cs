// 职责：Performance 模块世界舞台回放——走 Boot 真实流程进 SampleScene，玩家走进村口触发区拉起 perf_sample_scene_talk：
//   五个方舟小人站在 3D 灰盒里、舞台相机接管画面、底部对白面板带头像；逐句确认走完 / 长按跳过，两条路径都要把主相机、
//   HUD 层、玩家渲染器恢复原样。截图给人看构图（对标《明日方舟》活动探索截图）。
// 为什么新建（project-root.md「加能力的顺序」）：
//   复用 —— PerformanceShowcase 加载的是 Verify/Performance（2D 验证场景、叠加模式），拍不到 3D 灰盒与世界舞台；
//   扩展 —— 往它里面加用例要改 ScenePath，四条旧用例会跟着换场景，职责说不通。进场方式照抄 ExplorationShowcase（sealed，不能继承）。
// 确认 / 跳过走 IPerformanceService.Confirm() / Skip()（等价玩家按确认 / 长按满），不读输入。
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

            yield return Check("面板显示第一句：说话者「阿米娅」、左侧头像已显示",
                () => SpeakerText() == "阿米娅" && AvatarShown(), 5f);
            yield return Check("舞台相机接管画面：它是深度最高的启用相机，主相机遮罩清零",
                () => StageCameraOnTop(Stage()) && mainCamera != null && mainCamera.cullingMask == 0, 3f);
            yield return Check("玩家渲染器与名牌全部隐藏（舞台上的阿米娅是替身，不出重影）",
                () => AllHidden(playerVisuals), 2f);
            yield return Check("场景 NPC / 巡逻怪 / 物资箱标记的 Renderer 与 Canvas 全部隐藏（与舞台小人同形象，不出重影）",
                () => AllHidden(sceneVisuals), 2f);
            yield return Check("五个小人都在舞台相机画面内", () => ActorsInView(Stage()), 2f);
            yield return Wait(0.6f);
            yield return Snapshot("01-第一句·阿米娅");

            yield return ConfirmAtHold();
            yield return Check("第二句：说话者「陈」", () => SpeakerText() == "陈" && AvatarShown(), LineTimeoutSeconds);
            yield return Wait(0.4f);
            yield return Snapshot("02-第二句·陈");

            yield return ConfirmAtHold();
            yield return Check("第三句：说话者「德克萨斯」", () => SpeakerText() == "德克萨斯" && AvatarShown(), LineTimeoutSeconds);
            yield return Wait(0.4f);
            yield return Snapshot("03-第三句·德克萨斯");

            // 余下第三～六句的四个停顿逐个确认，最后一个确认后时间轴走完收尾。
            yield return ConfirmAtHold();
            yield return ConfirmAtHold();
            yield return ConfirmAtHold();
            yield return ConfirmAtHold();

            yield return Check("演出正常播完：结果 Completed",
                () => !performance.IsRunning && rules != null && rules.Outcome == PerformanceOutcome.Completed, 5f);
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

        private bool AvatarShown()
        {
            Image avatar = FindInView<Image>("Avatar");
            return avatar != null && avatar.gameObject.activeInHierarchy && avatar.sprite != null;
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

        private static Button RequireTitleStart(TitleView title)
        {
            Button button = title == null ? null : FindDeep<Button>(title.transform, "StartButton");
            if (button == null)
            {
                throw new InvalidOperationException("标题界面没开，或找不到「StartButton」");
            }

            return button;
        }

        private static T FindDeep<T>(Transform root, string objectName) where T : Component
        {
            T[] all = root.GetComponentsInChildren<T>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == objectName)
                {
                    return all[i];
                }
            }

            return null;
        }
    }
}
