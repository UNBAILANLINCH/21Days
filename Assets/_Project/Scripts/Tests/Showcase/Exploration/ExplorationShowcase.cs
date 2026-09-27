// 职责：探索层白盒示例回放——走 Boot 真实流程（标题「开始」→ MonsterEncounterState → IsometricEncounter 场景），
//   验证探索 HUD 控件（走跑切换、摇杆 / 触屏三键按平台显隐）、万向标（屏外兴趣点贴边、沉浸隐藏）、
//   物资箱（靠近提示、开箱奖励、任务计数、重复开箱无效）、重置进度（任务与箱子回初始、场景重进）。
//   覆盖 PRP/exploration-whitebox 验收 V1–V5。
// 为什么新建（project-root.md「加能力的顺序」）：
//   复用 —— IsometricExplorationShowcase 不走 Boot、自己 new 规则对象，验不到容器接线与 HUD；QuestShowcase 用的是 Verify/Quest 场景，
//           没有物资箱与探索 HUD。
//   扩展 —— 塞进上面任一个都会让它们的职责说不通（一个是纸片场景适配，一个是任务模块）。
// 骨架照抄 QuestShowcase：Boot 就绪等待、每条用例收尾销毁根作用域、反射取服务、HUD 物体按名字查找。
using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.Core.Flow;
using Game.Core.Simulation;
using Game.Core.UI;
using Game.Core.UI.Views;
using Game.IsometricExploration;
using Game.Loot;
using Game.Monster;
using Game.Player;
using Game.Quest;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests.Showcase.Exploration
{
    [Category("Showcase")]
    public sealed class ExplorationShowcase : ShowcaseScenario
    {
        private const float BootTimeoutSeconds = 20f;
        private const float EnterTimeoutSeconds = 20f;

        /// <summary>根作用域类型名：Boot 场景的 GameBootstrap 带 DontDestroyOnLoad，收尾时按名字找来销毁。</summary>
        private const string ScopeTypeName = "Game.Core.Boot.GameLifetimeScope, Game.Core";

        private const int MainQuestId = 1001;
        private const int CrateQuestId = 2002;
        private const string PromptText = "E 打开物资箱";
        private const string RewardTitle = "获得物资";

        /// <summary>
        /// 「获得物资」通知前面最多可能排队的条数，用来估算等待上限（2026-09-26 查实：不是数据问题，
        /// 是 <see cref="INotificationService"/> 单队列时序——进场景的任务接取通知、开箱前后各一次自动保存
        /// 「已保存」通知都可能排在「获得物资」前面，同标题才合并，不同标题只会排队，见
        /// <c>QuestNotificationPresenter</c> / <c>SaveTriggerBridge</c> / <c>NotificationQueue</c>）。
        /// 队列深度不对外暴露，只能按已知触发点估个上界，宁可等久一点也不要在通知还没轮到时就判失败。
        /// </summary>
        private const int MaxNotificationsAheadOfReward = 3;

        /// <summary>摇杆推动的时长（真实时间）。步行 3 m/s、奔跑 5 m/s，0.6 秒足够拉开差距。</summary>
        private const float StickSeconds = 0.6f;

        /// <summary>Crate_A 在场景 XZ (7.5, 5.8)；玩家放到它南侧 0.9 m，在交互半径 1.5 内、离村民对白半径足够远。</summary>
        private static readonly Vector2 NearCrateA = new Vector2(7.5f, 4.9f);

        private IUIService ui;
        private IHudVisibility hudVisibility;
        private LiveInputSource liveInput;
        private PlayerModel playerModel;
        private PlayerRules playerRules;
        private LootService loot;
        private SupplyCrateFocus focus;
        private QuestService quest;
        private Gamepad testPad;

        protected override string Module => "Exploration";

        /// <summary>世界由流程加载（MonsterEncounterState → Addressables「IsometricEncounter」），这里不直接加载场景。</summary>
        protected override string ScenePath => null;

        protected override bool LoadBootScene => true;

        /// <summary>启动流程走到标题界面才算就绪：此时容器已建完、UI 服务可用。</summary>
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

        /// <summary>每条用例都重新加载 Boot：不销毁上一条的根作用域会叠出多套容器 / UIRoot / EventSystem（同 QuestShowcase）。</summary>
        [UnityTearDown]
        public IEnumerator DestroyBootScope()
        {
            if (liveInput != null)
            {
                liveInput.HeldButtons = 0u;
            }

            if (testPad != null)
            {
                InputSystem.RemoveDevice(testPad);
                testPad = null;
            }

            // 先经流程退回标题，让遭遇状态自己卸载探索场景、释放 Addressables 场景句柄。
            // 直接销毁根作用域会留下一个被强制释放的场景句柄，下一条用例再点「开始」时场景加载不出来（实测）。
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
        // 波 6（PC 优先）：桌面下触屏控件全部隐藏；走跑只断言 PlayerModel.IsRunning（按钮隐藏，不再断言标签）。
        public IEnumerator Controls_RunToggleViaKeyAndTouchControlsHiddenOnDesktop()
        {
            yield return EnterExploration();

            yield return Check("桌面平台：摇杆、触屏三键（潜行 / 伪装 / 攻击）、走跑按钮全部隐藏；初始为散步",
                () => !HudActive("Stick") && !HudActive("TouchButtons") && !HudActive("RunToggle") && !playerModel.IsRunning, 3f);
            yield return Snapshot("控件初始·散步");

            EncounterSceneView view = UnityEngine.Object.FindObjectOfType<EncounterSceneView>();
            Vector2 spawn = view == null ? Vector2.zero : view.PlayerStart;
            Vector2 start = default;
            float walked = 0f;
            yield return Step("玩家回出生点，摇杆向右推 0.6 秒（散步）", () =>
            {
                playerRules.Reset(spawn);
                start = spawn;
            }, 0f);
            yield return PushStick(Vector2.right, StickSeconds);
            walked = Vector2.Distance(start, playerModel.Position);
            yield return Check($"散步有位移（{walked:0.00} m）", () => walked > 0.3f);

            yield return Step("玩家回出生点，按一下走跑键（Gameplay/Run）", () => playerRules.Reset(spawn), 0f);
            yield return PulseRun();
            yield return Check("进入奔跑模式（PlayerModel.IsRunning = true）",
                () => playerModel.IsRunning, 3f);
            yield return Snapshot("奔跑模式");

            float ran = 0f;
            yield return Step("同样向右推摇杆 0.6 秒（奔跑）", () => start = playerModel.Position, 0f);
            yield return PushStick(Vector2.right, StickSeconds);
            ran = Vector2.Distance(start, playerModel.Position);
            yield return Check($"奔跑位移（{ran:0.00} m）明显大于散步（{walked:0.00} m）", () => ran > walked * 1.3f);

            yield return Step("再按一下走跑键", null, 0f);
            yield return PulseRun();
            yield return Check("回到散步（PlayerModel.IsRunning = false）",
                () => !playerModel.IsRunning, 3f);
            yield return Snapshot("回到散步");
        }

        [UnityTest]
        public IEnumerator Compass_ShowsOffscreenPoiAndHidesWhenImmersive()
        {
            yield return EnterExploration();

            yield return Check("默认关闭：出生点没有任何激活的万向标克隆", () => ShownCompassCount() == 0, 3f);

            yield return Step("打开万向标开关（默认关）", () => ResolveService<ExplorationCompassPresenter>().Enabled = true, 0f);

            int visiblePoi = 0;
            yield return Step("站在出生点看万向标", () => visiblePoi = VisiblePoiCount());
            yield return Check("至少 3 个屏外兴趣点有贴边万向标，且至少 1 个屏内兴趣点没有标记",
                () =>
                {
                    int shown = ShownCompassCount();
                    return shown >= 3 && shown < VisiblePoiCount();
                }, 5f);
            int shownBefore = ShownCompassCount();
            yield return Snapshot("万向标·出生点");

            yield return Step($"进入沉浸模式（当前 {shownBefore} 个万向标 / {visiblePoi} 个兴趣点）",
                () => hudVisibility.SetHudHidden(true));
            yield return Check("控件整体隐藏（alpha 0），万向标全部消失，箱子头顶标记隐藏",
                () => ControlsAlpha() < 0.01f && ShownCompassCount() == 0 && AllCrateMarkers(false), 3f);
            yield return Snapshot("沉浸·万向标隐藏");

            yield return Step("退出沉浸模式", () => hudVisibility.SetHudHidden(false));
            yield return Check("控件恢复（alpha 1），万向标重新出现，未开箱子的头顶标记恢复",
                () => ControlsAlpha() > 0.99f && ShownCompassCount() >= 3 && AllCrateMarkers(true), 3f);
            yield return Snapshot("退出沉浸·万向标恢复");
        }

        [UnityTest]
        public IEnumerator Crate_CollectGivesRewardAndQuestProgress()
        {
            yield return EnterExploration();
            SupplyCrate crateA = FindRequired<SupplyCrate>("Crate_A");

            yield return Check("Crate_A 未开、头顶标记显示；支线 2002「清点营地物资」计数 0",
                () => !crateA.IsOpened && CrateMarkerActive(crateA) && CrateQuestCount() == 0, 3f);

            yield return Step("把玩家挪到 Crate_A 旁", () => playerRules.Reset(NearCrateA));
            yield return Check("焦点落在 Crate_A，屏幕下方出现「E 打开物资箱」提示",
                () => focus.Current == crateA && HudActive("InteractPrompt") && HudText("InteractPrompt") == PromptText, 3f);
            yield return Snapshot("靠近箱子·提示");

            bool first = false;
            int itemsBefore = ItemTotal();
            yield return Step("确认开箱", () => first = loot.TryCollect(focus.Current), 0f);
            yield return Check("开箱成功：箱子变开、头顶标记消失、提示消失（这几项在 TryCollect 返回时已同步生效，不经过通知队列）",
                () => first && crateA.IsOpened && !CrateMarkerActive(crateA) && !HudActive("InteractPrompt"), 3f);
            // 期望正文数据驱动：物品名查 tbitem、拼法走 LootService.ComposeBody（同开箱路径），场景改 itemId / 表改名都不用改这里。
            string crateABody = ExpectedRewardBody(crateA);
            // 通知走 INotificationService 的共用单队列（NotificationQueue，仅同标题合并）：进场景时的任务
            // 接取通知、开箱前后的自动保存「已保存」通知都可能排在「获得物资」前面，它不一定立刻显示。
            // 用一个每帧采样的等待：只要曾经见过目标标题 + 正文就记住（NotificationView 换卡片很快，
            // 逐帧轮询防止卡在两次轮询之间错过），超时按「最多可能排队的条数 + 1」估算，时长从
            // UIConfig 读，不写死。
            bool rewardSeen = false;
            float rewardTimeout = NotificationTimeoutSeconds(MaxNotificationsAheadOfReward);
            yield return Check(
                $"顶部通知「{RewardTitle}」在最多 {rewardTimeout:0.#} 秒内曾经显示过、正文为「{crateABody}」"
                + $"（Crate_A = tbitem {crateA.ItemId} ×{crateA.Count}；通知共用队列，可能被场景通知 / 自动保存通知排在前面，不代表立即出现）",
                () =>
                {
                    if (NotificationShows(RewardTitle, crateABody)) rewardSeen = true;
                    return rewardSeen;
                },
                rewardTimeout);
            yield return Check("支线 2002 计数 +1（1/3），背包多了物品（这两项同样在开箱那一刻就同步生效，不依赖上面的通知是否已经轮到）",
                () => CrateQuestCount() == 1 && ItemTotal() > itemsBefore, 3f);
            yield return Snapshot("开箱·获得物资");

            bool second = true;
            int itemsAfter = ItemTotal();
            yield return Step("对同一只箱子再确认一次", () => second = loot.TryCollect(crateA));
            yield return Check("重复开箱无效：返回 false，计数仍 1，背包不变",
                () => !second && CrateQuestCount() == 1 && ItemTotal() == itemsAfter);
            yield return Snapshot("重复开箱无效");
        }

        [UnityTest]
        public IEnumerator Reset_RestoresQuestsAndCrates()
        {
            yield return EnterExploration();
            SupplyCrate crateA = FindRequired<SupplyCrate>("Crate_A");

            yield return Step("把玩家挪到 Crate_A 旁并开箱", () =>
            {
                playerRules.Reset(NearCrateA);
                loot.TryCollect(crateA);
            });
            yield return Check("Crate_A 已开，支线 2002 计数 1，背包非空",
                () => crateA.IsOpened && CrateQuestCount() == 1 && loot.Items.Count > 0, 3f);
            yield return Snapshot("重置前·已开箱");

            yield return Step("点左上角「重置进度」", () => RequireHudChild<Button>("ResetButton").onClick.Invoke());
            yield return Check("弹出重置确认框", () => ConfirmView() != null, 5f);
            yield return Snapshot("重置确认框");

            int oldCrateId = crateA.GetInstanceID();
            yield return Step("点确认", () => RequireConfirmButton("ConfirmButton").onClick.Invoke(), 0f);
            yield return WaitUntil("确认框关闭、场景重进（出现新的 Crate_A）",
                () => ConfirmView() == null && FreshCrate(oldCrateId) != null, EnterTimeoutSeconds);

            yield return Check("任务回初始：追踪主线 1001，支线 2002 计数 0",
                () => quest.TrackedId == MainQuestId && CrateQuestCount() == 0, 5f);
            yield return Check("三只箱子全部闭合且头顶标记显示，背包清空",
                () => AllCratesClosed() && AllCrateMarkers(true) && loot.Items.Count == 0, 5f);
            yield return Snapshot("重置后·回到初始");
        }

        // ───────────────────────── 波 9：多层地图 / 遮挡碰撞 / 遮挡半透明 ─────────────────────────

        /// <summary>围栏横杆在场景 z −1.04..−0.96；胶囊半径 0.3，被挡住时脚底中心停在 ≈ −0.64。留 0.06 容差。</summary>
        private const float FenceStopZ = -0.7f;

        /// <summary>围栏本体所在的 z；越过它就是穿模。</summary>
        private const float FenceZ = -1f;

        /// <summary>上层甲板顶面 y = 7.89（地面 4.89 + 3）；站上去贴地后应不低于 7.8。</summary>
        private const float DeckTopY = 7.8f;

        [UnityTest]
        public IEnumerator Collision_FenceBlocksPlayer()
        {
            yield return EnterExploration();
            EncounterSceneView view = FindRequired<EncounterSceneView>("Encounter");

            yield return Step("把玩家挪到围栏北侧 (2, 1.0)", () => playerRules.Reset(new Vector2(2f, 1f)));
            yield return Check("玩家站到围栏北侧（场景 z ≈ 1.0）",
                () => Mathf.Abs(view.PlayerScenePosition.z - 1f) < 0.05f, 3f);

            float minZ = float.MaxValue;
            yield return Step("摇杆向下（−z）推 2 秒，正面撞向围栏", null, 0f);
            yield return PushStickSampling(Vector2.down, 2f, null,
                () => minZ = Mathf.Min(minZ, view.PlayerScenePosition.z));
            yield return Check($"玩家被围栏挡住：全程场景 z 最小 {minZ:0.00} ≥ {FenceStopZ}，没有穿到 z < {FenceZ}",
                () => minZ >= FenceStopZ && minZ > FenceZ);
            yield return Check("逻辑位置同步回写到围栏前（PlayerModel.Position.y ≥ −0.7）",
                () => playerModel.Position.y >= FenceStopZ, 1f);
            yield return Snapshot("围栏挡住玩家");
        }

        [UnityTest]
        public IEnumerator MultiLevel_RampLeadsToDeck()
        {
            yield return EnterExploration();
            EncounterSceneView view = FindRequired<EncounterSceneView>("Encounter");

            yield return Step("把玩家挪到南坡道脚下 (23.5, −2.5)", () => playerRules.Reset(new Vector2(23.5f, -2.5f)));
            yield return Check("玩家站在地面（场景 y ≈ 4.89）",
                () => Mathf.Abs(view.PlayerScenePosition.z + 2.5f) < 0.05f && view.PlayerScenePosition.y < 5f, 3f);
            yield return Snapshot("坡道脚下");

            float lastY = view.PlayerScenePosition.y;
            float maxDrop = 0f;
            yield return Step("摇杆向上（+z）推，沿坡道走上甲板", null, 0f);
            yield return PushStickSampling(Vector2.up, 8f, () => view.PlayerScenePosition.z >= 9f, () =>
            {
                float y = view.PlayerScenePosition.y;
                maxDrop = Mathf.Max(maxDrop, lastY - y);
                lastY = y;
            });
            Vector3 top = view.PlayerScenePosition;
            yield return Check($"上坡过程中身体高度单调不降（最大回落 {maxDrop:0.000} m）", () => maxDrop <= 0.001f);
            yield return Check($"站上甲板：场景 y {top.y:0.00} ≥ {DeckTopY}，z {top.z:0.00} ≥ 7.5",
                () => top.y >= DeckTopY && top.z >= 7.5f);
            yield return Step("停在甲板上看多层结构", null, 1f);
            yield return Snapshot("站上甲板·多层");
        }

        [UnityTest]
        public IEnumerator Occluder_FadesBridgeWhenPlayerBeneath()
        {
            yield return EnterExploration();
            SceneOccluder bridge = FindRequired<SceneOccluder>("Bridge_West");
            SceneOccluder rail = FindRequired<SceneOccluder>("Rail_Bridge_S");
            Renderer bridgeRenderer = bridge.GetComponent<Renderer>();

            yield return Check("初始桥体不透明", () => !bridge.IsFaded && bridgeRenderer.sharedMaterial != bridge.FadedMaterial, 3f);

            // 相机俯角约 40°，桥底离地 2.6 m：站在桥正中 (17.25, 10.25) 时相机其实看得见人；
            // 站到桥北沿内侧 (17.25, 12.3)，相机→胸口的视线才穿过桥身（屏幕上人被桥压住）。
            yield return Step("把玩家挪到桥后侧（相机视线被桥挡住的位置）", () => playerRules.Reset(new Vector2(17.25f, 12.3f)));
            yield return Check("桥体与挡在前面的南侧栏杆变半透明（sharedMaterial = M_Graybox_Faded），能看见桥后的人",
                () => bridge.IsFaded && bridgeRenderer.sharedMaterial == bridge.FadedMaterial && rail.IsFaded, 3f);
            yield return Snapshot("桥挡视线·半透明");

            yield return Step("把玩家挪回开阔地 (17.25, 3.4)", () => playerRules.Reset(new Vector2(17.25f, 3.4f)));
            yield return Check("桥体与栏杆恢复原材质",
                () => !bridge.IsFaded && bridgeRenderer.sharedMaterial != bridge.FadedMaterial && !rail.IsFaded, 3f);
            yield return Snapshot("离开·桥体恢复");
        }

        /// <summary>
        /// 波 10：粗射线（球形扫掠）让前景高物体也淡出。Tower 在 (12, 10.4)、半径 2.5、顶高 9.39。
        /// 实测 (16, 15.5)（楼梯中段）塔东沿离视线约 1.5 m、视线从塔顶上方越过，塔只占画面左侧、不压人（截图为证），按 1 m 半径不淡；
        /// 走到楼梯下段 (13.8, 15.5)（第 8 级）时塔的北上沿压住人，这里验淡出。
        /// </summary>
        [UnityTest]
        public IEnumerator Occluder_FadesTowerWhenPlayerOnStairs()
        {
            yield return EnterExploration();
            SceneOccluder tower = FindRequired<SceneOccluder>("Tower");
            Renderer towerRenderer = tower.GetComponent<Renderer>();

            yield return Check("初始塔体不透明", () => !tower.IsFaded && towerRenderer.sharedMaterial != tower.FadedMaterial, 3f);

            yield return Step("把玩家挪到西侧楼梯下段 (13.8, 15.5)（塔压住人的位置）", () => playerRules.Reset(new Vector2(13.8f, 15.5f)));
            yield return Check("前景的 Tower 变半透明（sharedMaterial = M_Graybox_Faded），能看见楼梯上的人",
                () => tower.IsFaded && towerRenderer.sharedMaterial == tower.FadedMaterial, 3f);
            yield return Step("等淡出过渡走完", null, 0.5f);
            yield return Snapshot("塔挡视线·半透明");

            yield return Step("把玩家挪回开阔地 (2, 3.4)", () => playerRules.Reset(new Vector2(2f, 3.4f)));
            yield return Check("塔体恢复原材质（过渡结束后换回）",
                () => !tower.IsFaded && towerRenderer.sharedMaterial != tower.FadedMaterial, 3f);
            yield return Snapshot("离开·塔体恢复");
        }

        /// <summary>
        /// 同 <see cref="PushStick"/>，但每帧回调 <paramref name="onFrame"/> 采样，<paramref name="stopWhen"/> 为真时提前松手。
        /// </summary>
        private IEnumerator PushStickSampling(Vector2 direction, float maxSeconds, Func<bool> stopWhen, Action onFrame)
        {
            if (testPad == null)
            {
                testPad = InputSystem.AddDevice<Gamepad>("ShowcaseGamepad");
            }

            float deadline = Time.realtimeSinceStartup + maxSeconds;
            while (Time.realtimeSinceStartup < deadline && (stopWhen == null || !stopWhen()))
            {
                InputSystem.QueueStateEvent(testPad, new GamepadState { leftStick = direction });
                yield return null;
                onFrame?.Invoke();
            }

            InputSystem.QueueStateEvent(testPad, new GamepadState());
            for (int i = 0; i < 3; i++)
            {
                yield return null;
                onFrame?.Invoke();
            }
        }

        /// <summary>后台切回标题；异常只记 Warning（LogError 会被 UTF 当未预期错误），无论成败都回调 done。</summary>
        private static async UniTaskVoid LeaveToTitleAsync(IGameFlow flow, Action done)
        {
            try
            {
                await flow.GoToAsync<TitleState>();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{ShowcaseOptions.Prefix}[Exploration] 收尾切回标题失败：{e.GetType().Name}：{e.Message}");
            }
            finally
            {
                done();
            }
        }

        // ───────────────────────── 进入与服务 ─────────────────────────

        /// <summary>取服务 → 点标题「开始」→ 等探索场景与 HUD 就绪。</summary>
        private IEnumerator EnterExploration()
        {
            Connect();
            IGameFlow flow = ResolveService<IGameFlow>();
            yield return WaitUntil("流程进入标题状态（标题面板已接上「开始」按钮）",
                () => flow != null && flow.Current is TitleState, BootTimeoutSeconds);
            yield return Step("点标题界面「开始」", () => RequireTitleStart(ui.Get<TitleView>()).onClick.Invoke(), 0f);
            yield return WaitUntil("进入探索场景：标题关闭、物资箱登记、探索 HUD 已打开",
                () => ui != null && ui.Get<TitleView>() == null && GameObject.Find("Crates") != null
                      && Hud() != null && focus != null,
                EnterTimeoutSeconds);
            yield return Step("等相机跟到玩家", null, 1f);
        }

        /// <summary>从根容器取本回放要用的服务；取不到留 null，由后续检查点记失败。</summary>
        private void Connect()
        {
            ui = ResolveService<IUIService>();
            hudVisibility = ResolveService<IHudVisibility>();
            liveInput = ResolveService<LiveInputSource>();
            playerModel = ResolveService<PlayerModel>();
            playerRules = ResolveService<PlayerRules>();
            loot = ResolveService<LootService>();
            focus = ResolveService<SupplyCrateFocus>();
            quest = ResolveService<QuestService>();
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

        // ───────────────────────── 输入驱动 ─────────────────────────

        /// <summary>
        /// 用一只测试手柄持续推左摇杆驱动 Gameplay/Move（与屏上摇杆绑的是同一个 &lt;Gamepad&gt;/leftStick），
        /// 结束后回中。每帧重新入队一次状态，防止被其它设备的回中事件覆盖。
        /// </summary>
        private IEnumerator PushStick(Vector2 direction, float seconds)
        {
            if (testPad == null)
            {
                testPad = InputSystem.AddDevice<Gamepad>("ShowcaseGamepad");
            }

            float deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                InputSystem.QueueStateEvent(testPad, new GamepadState { leftStick = direction });
                yield return null;
            }

            InputSystem.QueueStateEvent(testPad, new GamepadState());
            yield return null;
            yield return null;
        }

        /// <summary>
        /// 走跑键按一下：软件按住位置 Run，保持到模拟采样到按下沿（IsRunning 翻转）或 0.5 秒，再清位。
        /// 这是屏上 RunToggle 与键盘 Run 共用的那一路（LiveInputSource.HeldButtons 进 InputCommand）。
        /// </summary>
        private IEnumerator PulseRun()
        {
            if (liveInput == null || playerModel == null)
            {
                yield break;
            }

            bool before = playerModel.IsRunning;
            liveInput.HeldButtons |= InputCommand.ButtonRun;
            float deadline = Time.realtimeSinceStartup + 0.5f;
            while (playerModel.IsRunning == before && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            yield return null;
            liveInput.HeldButtons &= ~InputCommand.ButtonRun;
            yield return null;
            yield return null;
        }

        // ───────────────────────── HUD 查询 ─────────────────────────

        private ExplorationHudView Hud()
        {
            return ui == null ? null : ui.Get<ExplorationHudView>();
        }

        private ExplorationConfirmView ConfirmView()
        {
            return ui == null ? null : ui.Get<ExplorationConfirmView>();
        }

        /// <summary>HUD 下按物体名（递归，含未激活）找到的物体是否在层级里显示。</summary>
        private bool HudActive(string objectName)
        {
            ExplorationHudView hud = Hud();
            Transform child = hud == null ? null : FindDeep<Transform>(hud.transform, objectName);
            return child != null && child.gameObject.activeInHierarchy;
        }

        private string HudText(string objectName)
        {
            ExplorationHudView hud = Hud();
            // 文字组件可能挂在该物体自身或其子物体上，取子树里第一个。
            Transform node = hud == null ? null : FindDeep<Transform>(hud.transform, objectName);
            TMP_Text text = node == null ? null : node.GetComponentInChildren<TMP_Text>(true);
            return text == null || text.text == null ? string.Empty : text.text;
        }

        private T RequireHudChild<T>(string objectName) where T : Component
        {
            ExplorationHudView hud = Hud();
            T component = hud == null ? null : FindDeep<T>(hud.transform, objectName);
            if (component == null)
            {
                throw new InvalidOperationException($"探索 HUD 下找不到「{objectName}」上的 {typeof(T).Name}（没开，或预制体物体名不一致）");
            }

            return component;
        }

        private Button RequireConfirmButton(string objectName)
        {
            ExplorationConfirmView view = ConfirmView();
            Button button = view == null ? null : FindDeep<Button>(view.transform, objectName);
            if (button == null)
            {
                throw new InvalidOperationException($"重置确认框下找不到按钮「{objectName}」（没开，或预制体物体名不一致）");
            }

            return button;
        }

        /// <summary>ControlsRoot 上 CanvasGroup 的 alpha；找不到按 -1。</summary>
        private float ControlsAlpha()
        {
            ExplorationHudView hud = Hud();
            CanvasGroup group = hud == null ? null : FindDeep<CanvasGroup>(hud.transform, "ControlsRoot");
            return group == null ? -1f : group.alpha;
        }

        /// <summary>CompassRoot 下当前激活的万向标克隆数（模板本身运行时隐藏，不计）。</summary>
        private int ShownCompassCount()
        {
            ExplorationHudView hud = Hud();
            RectTransform root = hud == null ? null : hud.CompassRoot;
            if (root == null)
            {
                return 0;
            }

            int shown = 0;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.gameObject.activeSelf && child != hud.CompassMarkerTemplate)
                {
                    shown++;
                }
            }

            return shown;
        }

        private static int VisiblePoiCount()
        {
            ExplorationPointOfInterest[] points = UnityEngine.Object.FindObjectsOfType<ExplorationPointOfInterest>();
            int visible = 0;
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i].IsVisible)
                {
                    visible++;
                }
            }

            return visible;
        }

        /// <summary>按箱子的 itemId / count 算通知正文：名字查 tbitem（查不到用「#id」，同 LootService），格式取 LootConfig.RewardBodyFormat。</summary>
        private string ExpectedRewardBody(SupplyCrate crate)
        {
            Game.Core.Config.IConfigService config = ResolveService<Game.Core.Config.IConfigService>();
            LootConfig lootConfig = ResolveService<LootConfig>();
            // cfg.Item 继承 Luban.Runtime 的 BeanBase，本程序集不引用 Luban.Runtime，所以取表行走反射（只动 ExplorationShowcase，不改 asmdef）。
            string name = null;
            if (config != null)
            {
                object table = config.Tables.TbItem;
                object item = table.GetType().GetMethod("GetOrDefault").Invoke(table, new object[] { crate.ItemId });
                name = item == null ? null : item.GetType().GetField("Name").GetValue(item) as string; // Luban 生成的是 readonly 字段
            }

            if (string.IsNullOrEmpty(name))
            {
                name = "#" + crate.ItemId;
            }
            return LootService.ComposeBody(lootConfig == null ? null : lootConfig.RewardBodyFormat, name, crate.Count);
        }

        /// <summary>
        /// 估算「等一条通知最多该花多久」：(可能排在前面的条数 + 自己这一条) × 单条停留秒数 + 2 秒缓冲。
        /// 秒数从 <see cref="UIConfig"/> 读（容器里拿不到时退回 NotificationService 的默认值 2.5，不写死）；
        /// 「已保存」等通知实际停留比这个短（<c>SessionConfig.SaveNoticeSeconds</c>），按 UIConfig 的值算
        /// 是往宽了估，不会因为估少了而误判。
        /// </summary>
        private float NotificationTimeoutSeconds(int maxAhead)
        {
            UIConfig config = ResolveService<UIConfig>();
            float seconds = config == null ? 2.5f : config.NotificationSeconds;
            return (maxAhead + 1) * seconds + 2f;
        }

        private bool NotificationShows(string title, string body)
        {
            NotificationView view = ui == null ? null : ui.Get<NotificationView>();
            if (view == null || !view.IsCardShown)
            {
                return false;
            }

            TMP_Text titleText = FindDeep<TMP_Text>(view.transform, "Title");
            TMP_Text bodyText = FindDeep<TMP_Text>(view.transform, "Body");
            return titleText != null && titleText.text == title
                   && bodyText != null && bodyText.text == body;
        }

        // ───────────────────────── 箱子与任务查询 ─────────────────────────

        private static SupplyCrate[] Crates()
        {
            return UnityEngine.Object.FindObjectsOfType<SupplyCrate>();
        }

        private static SupplyCrate FreshCrate(int oldInstanceId)
        {
            SupplyCrate[] crates = Crates();
            for (int i = 0; i < crates.Length; i++)
            {
                if (crates[i].name == "Crate_A" && crates[i].GetInstanceID() != oldInstanceId)
                {
                    return crates[i];
                }
            }

            return null;
        }

        private static bool CrateMarkerActive(SupplyCrate crate)
        {
            Transform marker = crate == null ? null : crate.transform.Find("Marker");
            return marker != null && marker.gameObject.activeSelf;
        }

        /// <summary>visible 为真：所有未开箱子的头顶标记都显示；为假：所有箱子的头顶标记都隐藏。场上至少要有一只箱子。</summary>
        private static bool AllCrateMarkers(bool visible)
        {
            SupplyCrate[] crates = Crates();
            if (crates.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < crates.Length; i++)
            {
                bool expected = visible && !crates[i].IsOpened;
                if (CrateMarkerActive(crates[i]) != expected)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AllCratesClosed()
        {
            SupplyCrate[] crates = Crates();
            if (crates.Length < 3)
            {
                return false;
            }

            for (int i = 0; i < crates.Length; i++)
            {
                if (crates[i].IsOpened)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>支线 2002 当前目标计数；任务不在进行中返回 -1。</summary>
        private int CrateQuestCount()
        {
            if (quest == null || !quest.TryGet(CrateQuestId, out QuestProgress progress) || progress == null)
            {
                return -1;
            }

            return progress.Count;
        }

        private int ItemTotal()
        {
            if (loot == null)
            {
                return 0;
            }

            int total = 0;
            foreach (KeyValuePair<int, int> pair in loot.Items)
            {
                total += pair.Value;
            }

            return total;
        }

        /// <summary>在 root 下按物体名递归找组件（含未激活）；找不到返回 null。</summary>
        private static T FindDeep<T>(Transform root, string objectName) where T : Component
        {
            T[] candidates = root.GetComponentsInChildren<T>(true);
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
