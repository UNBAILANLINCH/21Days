// 职责：Session（存档会话）模块回放——走 Boot 真实流程验证两件玩家看得见的事：
//   1. 存 → 退 → 读一致（PRD A3）：新游戏落第一个空槽 → 对白、开箱、切任务追踪、摇杆移动 → 手动保存（顺带量 A8 主线程耗时）
//      → 回标题（触发离场保存）→ 标题「继续」→ 玩家位置、任务状态与追踪、背包、箱子开合、对白已读全部与存前一致。
//   2. 坏档显示不可用（PRD A4）：槽 2 写非法 JSON、槽 3 写信封版本 999 → 标题「选择存档」→ 两行显示「不可用」且不可选，
//      槽 1 显示「新游戏」→ Esc 关面板回到标题。
// 为什么新建（project-root.md「加能力的顺序」）：
//   复用 —— ExplorationShowcase 覆盖的是探索 HUD / 箱子 / 碰撞，没有「退出再读回」这条链路，也不认识存档槽；
//   扩展 —— 把存读档塞进它会让 Exploration 回放认识 Session 模块，职责说不通。
// 骨架照抄 ExplorationShowcase：Boot 就绪等待、收尾先经流程退回标题再销毁根作用域、反射取服务、测试手柄推摇杆；
//   对白推进照 DialogueShowcase 点 TapArea / 选项按钮。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using Game.Core.Flow;
using Game.Core.Platform;
using Game.Core.UI;
using Game.Core.UI.Views;
using Game.Dialogue;
using Game.Loot;
using Game.Monster;
using Game.Player;
using Game.Quest;
using Game.Session;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests.Showcase.Session
{
    [Category("Showcase")]
    public sealed class SessionShowcase : ShowcaseScenario
    {
        private const float BootTimeoutSeconds = 20f;
        private const float EnterTimeoutSeconds = 20f;
        private const float SaveTimeoutSeconds = 10f;
        private const float DialogueTimeoutSeconds = 30f;

        /// <summary>读回后玩家位置允许的误差（米）。</summary>
        private const float PositionTolerance = 0.05f;

        /// <summary>选槽面板默认槽数；取不到 SessionConfig 时用它。</summary>
        private const int DefaultSlotCount = 3;

        /// <summary>根作用域类型名：Boot 场景的 GameBootstrap 带 DontDestroyOnLoad，收尾时按名字找来销毁。</summary>
        private const string ScopeTypeName = "Game.Core.Boot.GameLifetimeScope, Game.Core";

        private const string UnavailableText = "不可用";
        private const string EmptyText = "新游戏";

        /// <summary>摇杆推动时长（真实时间）。步行约 3 m/s，1 秒足够和出生点、开箱点拉开明显距离。</summary>
        private const float StickSeconds = 1f;

        /// <summary>Crate_A 在场景 XZ (7.5, 5.8)；玩家放到它南侧 0.9 m（同 ExplorationShowcase）。</summary>
        private static readonly Vector2 NearCrateA = new Vector2(7.5f, 4.9f);

        /// <summary>存档服务读坏槽时按约定打的 Error：日志正文带「存档槽 N」，埋点行带 "slot":N。</summary>
        private static readonly Regex BadSlotLog = new Regex("存档槽 [23]|\"slot\"\\s*:\\s*[23]\\b");

        private IUIService ui;
        private IGameFlow flow;
        private GameSession session;
        private ISessionStateSource sessionState;
        private PlayerModel playerModel;
        private PlayerRules playerRules;
        private LootService loot;
        private QuestService quest;
        private DialogueService dialogue;
        private DialogueRules dialogueRules;
        private DialogueReadData readData;
        private Gamepad testPad;
        private Keyboard testKeyboard;

        private string saveRoot;
        private int slotCount = DefaultSlotCount;

        protected override string Module => "Session";

        /// <summary>世界由流程加载（MonsterEncounterState → Addressables「IsometricEncounter」），这里不直接加载场景。</summary>
        protected override string ScenePath => null;

        protected override bool LoadBootScene => true;

        /// <summary>启动流程走到标题界面才算就绪：此时容器已建完、UI 服务可用、标题路由已订阅。</summary>
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
        /// 基类 SetUp（加载 Boot、等到标题）之后执行：取服务、确认槽目录存在。
        /// <c>IPlatformService.SaveRoot</c> 此时已经是 <see cref="ShowcaseScenario"/> 在加载 Boot 之前
        /// 设好的临时覆盖目录（见 <c>ShowcaseSetUp</c>），跟玩家真实存档目录完全隔离，
        /// 不用再自己备份 / 还原本机槽文件。标题「开始」与「选择存档」每次都重新读槽摘要，
        /// 所以 Boot 之后确认目录为空也不影响判定（覆盖目录本来就是这次用例专用、刚建出来的）。
        /// </summary>
        [UnitySetUp]
        public IEnumerator PrepareSlots()
        {
            Connect();
            IPlatformService platform = ResolveService<IPlatformService>();
            saveRoot = platform == null ? null : platform.SaveRoot;
            SessionConfig config = ResolveService<SessionConfig>();
            // SessionConfig 是 ScriptableObject，判空只用 !=。
            slotCount = config != null ? config.SlotCount : DefaultSlotCount;

            if (string.IsNullOrEmpty(saveRoot))
            {
                Debug.LogWarning($"{ShowcaseOptions.Prefix}[Session] 取不到 IPlatformService.SaveRoot，槽文件没法准备，后续检查点会失败");
                yield break;
            }

            Directory.CreateDirectory(saveRoot);
            yield return null;
        }

        /// <summary>
        /// 先经流程退回标题（让遭遇状态自己卸载场景、释放 Addressables 句柄，同 ExplorationShowcase），
        /// 再销毁根作用域。本方法先于基类收尾执行；槽文件本身在覆盖目录里，
        /// 基类 <c>ShowcaseTearDown</c> 随后会连同整个覆盖目录一起删掉，这里不用再动它、也不碰真实存档目录。
        /// </summary>
        [UnityTearDown]
        public IEnumerator DestroyBootScope()
        {
            if (testPad != null)
            {
                InputSystem.RemoveDevice(testPad);
                testPad = null;
            }

            if (testKeyboard != null)
            {
                InputSystem.RemoveDevice(testKeyboard);
                testKeyboard = null;
            }

            IGameFlow currentFlow = ResolveService<IGameFlow>();
            if (currentFlow != null && !(currentFlow.Current is TitleState))
            {
                bool left = false;
                LeaveToTitleAsync(currentFlow, () => left = true).Forget();
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

            // 离场保存是 Forget 出去的异步写盘：多等一会儿，免得它落在基类删除覆盖目录之后再落盘报错。
            yield return new WaitForSecondsRealtime(0.5f);
            yield return null;
        }

        // ───────────────────────── 用例一：存 → 退 → 读一致（A3，顺带量 A8） ─────────────────────────

        [UnityTest]
        public IEnumerator SaveQuitContinue_RestoresSameProgress()
        {
            yield return WaitUntil("流程进入标题状态", () => flow != null && flow.Current is TitleState, BootTimeoutSeconds);
            yield return Step("点标题「开始」（三个槽都空：新游戏应落在槽 1）",
                () => RequireTitleButton("StartButton").onClick.Invoke(), 0f);
            yield return WaitUntil("进入遭遇状态且会话用的是槽 1（标题关闭、物资箱已登记、遭遇逻辑在跑）",
                () => InGameplay() && session.CurrentSlot == 1, EnterTimeoutSeconds);
            yield return Step("等相机跟到玩家", null, 1f);

            // ① 对白：走到长者身边拉起对白，一路点过去直到结束（照 DialogueShowcase 的 TapArea / 选项推进）。
            DialogueInteractable elder = FindRequired<DialogueInteractable>("Npc_Elder");
            Vector3 elderPos = elder.transform.position;
            yield return Step("把玩家挪到长者身前", () => playerRules.Reset(new Vector2(elderPos.x, elderPos.z - 1f)), 0f);
            yield return WaitUntil("长者进入交互半径", () => elder.InRange, 3f);
            yield return Step("和长者交谈（拉起对白 1001）", () => elder.Interact(), 0f);
            yield return Check("对白面板打开", () => dialogue.IsRunning && ui.Get<DialogueView>() != null, 3f);
            yield return RunDialogueToEnd();
            yield return Check("对白已结束，世界恢复", () => !dialogue.IsRunning && Mathf.Approximately(Time.timeScale, 1f), 5f);

            // ② 开箱。
            SupplyCrate crateA = FindRequired<SupplyCrate>("Crate_A");
            bool collected = false;
            yield return Step("把玩家挪到 Crate_A 旁并开箱", () =>
            {
                playerRules.Reset(NearCrateA);
                collected = loot.TryCollect(crateA);
            });
            yield return Check("Crate_A 已开，背包里有物品", () => collected && crateA.IsOpened && ItemTotal() > 0, 3f);

            // ③ 任务：换追踪到另一条进行中的任务（对白已把主线目标往前推了一格）。
            int trackTarget = 0;
            yield return Step("在进行中的任务里换一条来追踪", () =>
            {
                trackTarget = PickOtherInProgress();
                if (trackTarget == 0)
                {
                    throw new InvalidOperationException("进行中的任务只有当前追踪的这一条，换不了追踪");
                }

                quest.Track(trackTarget);
            });
            yield return Check($"追踪切到任务 {trackTarget}", () => trackTarget != 0 && quest.TrackedId == trackTarget, 3f);

            // ④ 移动：用遭遇输入（测试手柄左摇杆 → Gameplay/Move）把玩家推离开箱点。
            Vector2 beforeMove = playerModel.Position;
            yield return Step("摇杆向右推 1 秒，把玩家走到新位置", null, 0f);
            yield return PushStick(Vector2.right, StickSeconds);
            yield return WaitStable();
            EncounterSceneView sceneView = UnityEngine.Object.FindObjectOfType<EncounterSceneView>();
            Vector2 spawn = sceneView == null ? Vector2.zero : sceneView.PlayerStart;
            yield return Check("玩家走出了明显距离（离开箱点 > 1 m、离出生点 > 1 m）",
                () => Vector2.Distance(beforeMove, playerModel.Position) > 1f && Vector2.Distance(spawn, playerModel.Position) > 1f);
            yield return Snapshot("存档前·玩家新位置");

            // ⑤ 自动保存请求（对白 / 开箱 / 追踪）先落完，再手动存一次并计时，免得和自动保存抢盘。
            yield return WaitUntil("之前的自动保存请求都已落盘", () => !session.HasPendingSave, SaveTimeoutSeconds);
            yield return Wait(0.3f);

            SaveTiming timing = new SaveTiming();
            yield return MeasureSave(timing);
            yield return Check("手动保存成功（SaveNowAsync 返回 true，槽 1 文件存在）",
                () => timing.Done && timing.Success && File.Exists(SlotPath(1)));
            yield return Step(
                "A8 计时：SaveNowAsync 同步段（调用到第一个 await 返回）"
                + $" {Ms(timing.SyncMs)} ms；整段 await {Ms(timing.TotalMs)} ms，跨 {timing.Frames} 帧；"
                + $"等待期间最长一帧 {Ms(timing.MaxFrameMs)} ms",
                null, 0f);
            Debug.Log($"{ShowcaseOptions.Prefix}[Session] A8 同步段 {Ms(timing.SyncMs)} ms，总时长 {Ms(timing.TotalMs)} ms，"
                      + $"{timing.Frames} 帧，最长帧 {Ms(timing.MaxFrameMs)} ms");

            // ⑥ 期望快照与离场：同一帧里拍快照并切回标题，离场保存捕获的现场与快照一致。
            Expected expected = null;
            bool leftToTitle = false;
            yield return Step("记下期望快照，回到标题（触发离场保存）", () =>
            {
                expected = Capture();
                LeaveToTitleAsync(flow, () => leftToTitle = true).Forget();
            }, 0f);
            yield return WaitUntil("回到标题界面", () => leftToTitle && flow.Current is TitleState && ui.Get<TitleView>() != null,
                EnterTimeoutSeconds);
            yield return Check("标题「继续」显示且可点（最近存档是槽 1；没有存档时隐藏）",
                () => session.LatestSlot == 1 && RequireTitleButton("ContinueButton").gameObject.activeInHierarchy
                    && RequireTitleButton("ContinueButton").interactable, 3f);
            yield return Snapshot("回到标题");

            // ⑦ 继续：读回槽 1。
            yield return Step("点标题「继续」", () => RequireTitleButton("ContinueButton").onClick.Invoke(), 0f);
            yield return WaitUntil("读档后重新进入遭遇状态（槽 1）", () => InGameplay() && session.CurrentSlot == 1, EnterTimeoutSeconds);
            yield return Step("等场景与相机就位", null, 1f);

            yield return Check(
                $"玩家位置与存档前一致（期望 {Fmt(expected.Position)}，误差 ≤ {PositionTolerance} m）",
                () => Vector2.Distance(expected.Position, playerModel.Position) <= PositionTolerance, 5f);
            yield return Check(
                $"任务追踪与进行中列表一致（追踪 {expected.TrackedId}；{expected.Quests}）",
                () => quest.TrackedId == expected.TrackedId && QuestSignature() == expected.Quests, 3f);
            yield return Check($"背包一致（{expected.Items}）", () => ItemSignature() == expected.Items, 3f);
            yield return Check($"箱子开合一致（{expected.Crates}）",
                () => CrateSignature() == expected.Crates && loot.IsCollected(crateA.Key), 5f);
            yield return Check($"对白已读记录没有倒退（存前 {expected.ReadKeys.Count} 条，且已写进已读档案）",
                () => readData.Keys.IsSupersetOf(expected.ReadKeys) && ReadProfileContains(expected.ReadKeys), 5f);
            yield return Snapshot("继续后·进度一致");
        }

        // ───────────────────────── 用例二：坏档显示不可用（A4） ─────────────────────────

        [UnityTest]
        public IEnumerator BadSlots_ShowUnavailableAndNotSelectable()
        {
            ExpectErrorLogs("槽 2 是非法 JSON、槽 3 的信封版本是 999，读槽摘要时存档服务按约定打 Error",
                line => BadSlotLog.IsMatch(line));

            yield return Step("往槽 2 写一个非法 JSON，槽 3 写一个信封版本 999 的文件；槽 1 留空", () =>
            {
                WriteSlot(2, "{ 这不是合法 JSON ");
                WriteSlot(3, "{\n  \"formatVersion\": 999,\n  \"partitions\": {}\n}");
            }, 0f);
            yield return WaitUntil("流程进入标题状态", () => flow != null && flow.Current is TitleState, BootTimeoutSeconds);

            yield return Step("点标题「选择存档」", () => RequireTitleButton("LoadButton").onClick.Invoke(), 0f);
            yield return WaitUntil("选槽面板打开并画出三行", () => SlotsView() != null && SlotRows().Count >= slotCount, 5f);
            yield return Check("面板标题为「选择存档」", () => SlotsTitle() == SaveSlotsView.FormatTitle(SlotsMode.Load), 2f);
            yield return Check("槽 2 显示「不可用」且不可选", () => RowIs(2, UnavailableText, false), 2f);
            yield return Check("槽 3（高版本）显示「不可用」且不可选", () => RowIs(3, UnavailableText, false), 2f);
            yield return Check("槽 1 显示「新游戏」（读档模式下空槽同样不可选）", () => RowIs(1, EmptyText, false), 2f);
            yield return Snapshot("选槽面板·坏档不可用");

            yield return Step("按 Esc 关闭面板", null, 0f);
            yield return PressEscape();
            yield return Check("面板关闭，回到标题", () => SlotsView() == null && ui.Get<TitleView>() != null && flow.Current is TitleState, 3f);
            yield return Check("坏档文件原样留在磁盘上（只读候选不挪动、不覆盖）",
                () => File.Exists(SlotPath(2)) && File.Exists(SlotPath(3)) && !File.Exists(SlotPath(1)));
            yield return Snapshot("关闭面板·回到标题");
        }

        // ───────────────────────── 服务与流程 ─────────────────────────

        /// <summary>从根容器取本回放要用的服务；取不到留 null，由后续检查点记失败。</summary>
        private void Connect()
        {
            ui = ResolveService<IUIService>();
            flow = ResolveService<IGameFlow>();
            session = ResolveService<GameSession>();
            sessionState = ResolveService<ISessionStateSource>();
            playerModel = ResolveService<PlayerModel>();
            playerRules = ResolveService<PlayerRules>();
            loot = ResolveService<LootService>();
            quest = ResolveService<QuestService>();
            dialogue = ResolveService<DialogueService>();
            dialogueRules = ResolveService<DialogueRules>();
            readData = ResolveService<DialogueReadData>();
        }

        /// <summary>遭遇状态已进入且遭遇逻辑在跑、标题关闭、探索场景的箱子已登记。</summary>
        private bool InGameplay()
        {
            return flow != null && flow.Current is MonsterEncounterState
                   && sessionState != null && sessionState.IsGameplayState
                   && ui != null && ui.Get<TitleView>() == null
                   && GameObject.Find("Crates") != null;
        }

        /// <summary>后台切回标题；异常只记 Warning（LogError 会被 UTF 当未预期错误），无论成败都回调 done。</summary>
        private static async UniTaskVoid LeaveToTitleAsync(IGameFlow target, Action done)
        {
            try
            {
                await target.GoToAsync<TitleState>();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{ShowcaseOptions.Prefix}[Session] 切回标题失败：{e.GetType().Name}：{e.Message}");
            }
            finally
            {
                done();
            }
        }

        private Button RequireTitleButton(string objectName)
        {
            TitleView title = ui == null ? null : ui.Get<TitleView>();
            Button button = title == null ? null : FindDeep<Button>(title.transform, objectName);
            if (button == null)
            {
                throw new InvalidOperationException($"标题界面没开，或找不到「{objectName}」");
            }

            return button;
        }

        // ───────────────────────── 保存计时（A8） ─────────────────────────

        private sealed class SaveTiming
        {
            public bool Done { get; set; }
            public bool Success { get; set; }
            public double SyncMs { get; set; }
            public double TotalMs { get; set; }
            public double MaxFrameMs { get; set; }
            public int Frames { get; set; }
        }

        /// <summary>
        /// 调 <see cref="GameSession.SaveNowAsync"/> 并计时：同步段 = 调用到它返回 UniTask（第一个 await 让出）为止；
        /// 总时长 = 到任务完成为止（含等帧）；另记等待期间每帧的最长真实耗时，看写盘有没有卡住某一帧。
        /// </summary>
        private IEnumerator MeasureSave(SaveTiming timing)
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            UniTask<bool> task;
            try
            {
                task = session.SaveNowAsync("showcase");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{ShowcaseOptions.Prefix}[Session] SaveNowAsync 同步段抛异常：{e}");
                timing.Done = true;
                yield break;
            }

            timing.SyncMs = watch.Elapsed.TotalMilliseconds;
            ObserveAsync(task, timing).Forget();

            System.Diagnostics.Stopwatch frameWatch = System.Diagnostics.Stopwatch.StartNew();
            float deadline = Time.realtimeSinceStartup + SaveTimeoutSeconds;
            while (!timing.Done && Time.realtimeSinceStartup < deadline)
            {
                frameWatch.Restart();
                yield return null;
                timing.Frames++;
                timing.MaxFrameMs = Math.Max(timing.MaxFrameMs, frameWatch.Elapsed.TotalMilliseconds);
            }

            if (timing.TotalMs <= 0d)
            {
                timing.TotalMs = watch.Elapsed.TotalMilliseconds;
            }
        }

        private static async UniTaskVoid ObserveAsync(UniTask<bool> task, SaveTiming timing)
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                timing.Success = await task;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{ShowcaseOptions.Prefix}[Session] SaveNowAsync 抛异常：{e.GetType().Name}：{e.Message}");
                timing.Success = false;
            }
            finally
            {
                // 总时长 = 同步段 + 从拿到任务到完成的这段。
                timing.TotalMs = timing.SyncMs + watch.Elapsed.TotalMilliseconds;
                timing.Done = true;
            }
        }

        private static string Ms(double value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        // ───────────────────────── 期望快照 ─────────────────────────

        private sealed class Expected
        {
            public Vector2 Position { get; set; }
            public int TrackedId { get; set; }
            public string Quests { get; set; }
            public string Items { get; set; }
            public string Crates { get; set; }
            public HashSet<string> ReadKeys { get; set; }
        }

        private Expected Capture()
        {
            return new Expected
            {
                Position = playerModel.Position,
                TrackedId = quest.TrackedId,
                Quests = QuestSignature(),
                Items = ItemSignature(),
                Crates = CrateSignature(),
                ReadKeys = new HashSet<string>(readData.Keys, StringComparer.Ordinal),
            };
        }

        /// <summary>进行中任务的签名：按 id 排序的「id:状态:目标序号:计数」。</summary>
        private string QuestSignature()
        {
            var parts = new List<string>();
            IReadOnlyList<QuestProgress> list = quest.InProgress;
            for (int i = 0; i < list.Count; i++)
            {
                QuestProgress p = list[i];
                parts.Add($"{p.Id}:{p.State}:{p.ObjectiveIndex}:{p.Count}");
            }

            parts.Sort(StringComparer.Ordinal);
            return string.Join(" ", parts);
        }

        private string ItemSignature()
        {
            var parts = new List<string>();
            foreach (KeyValuePair<int, int> pair in loot.Items)
            {
                parts.Add($"{pair.Key}×{pair.Value}");
            }

            parts.Sort(StringComparer.Ordinal);
            return parts.Count == 0 ? "空" : string.Join(" ", parts);
        }

        /// <summary>场上箱子的开合签名：按键排序的「键=开/合」。</summary>
        private static string CrateSignature()
        {
            SupplyCrate[] crates = UnityEngine.Object.FindObjectsOfType<SupplyCrate>();
            var parts = new List<string>();
            for (int i = 0; i < crates.Length; i++)
            {
                parts.Add($"{crates[i].Key}={(crates[i].IsOpened ? "开" : "合")}");
            }

            parts.Sort(StringComparer.Ordinal);
            return string.Join(" ", parts);
        }

        private int ItemTotal()
        {
            int total = 0;
            foreach (KeyValuePair<int, int> pair in loot.Items)
            {
                total += pair.Value;
            }

            return total;
        }

        /// <summary>进行中任务里第一条不是当前追踪的；没有返回 0。</summary>
        private int PickOtherInProgress()
        {
            IReadOnlyList<QuestProgress> list = quest.InProgress;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id != quest.TrackedId)
                {
                    return list[i].Id;
                }
            }

            return 0;
        }

        /// <summary>已读档案（存档根下 profile-dialogue-read.json）里包含全部期望键。只做文本包含判断，不解析。</summary>
        private bool ReadProfileContains(HashSet<string> keys)
        {
            string path = Path.Combine(saveRoot ?? string.Empty, "profile-" + DialogueReadStore.ProfileName + ".json");
            if (!File.Exists(path))
            {
                return keys.Count == 0;
            }

            string text = File.ReadAllText(path, Encoding.UTF8);
            foreach (string key in keys)
            {
                if (!text.Contains(key))
                {
                    return false;
                }
            }

            return true;
        }

        private static string Fmt(Vector2 v)
        {
            return $"({v.x.ToString("0.00", CultureInfo.InvariantCulture)}, {v.y.ToString("0.00", CultureInfo.InvariantCulture)})";
        }

        // ───────────────────────── 对白推进 ─────────────────────────

        /// <summary>
        /// 一路推进到对白结束：打字中 / 等待推进 → 点 TapArea（补全或下一句）；出选项 → 点第一个激活选项。
        /// 不走跳过，保证每句都整句显示过、记进已读。
        /// </summary>
        private IEnumerator RunDialogueToEnd()
        {
            yield return Step("一路点对白区推进，出选项选第一项，直到对白结束", null, 0f);
            float deadline = Time.realtimeSinceStartup + DialogueTimeoutSeconds;
            while (dialogue.IsRunning && Time.realtimeSinceStartup < deadline)
            {
                try
                {
                    DialogueSaveData.Phase phase = dialogueRules.Phase;
                    if (phase == DialogueSaveData.Phase.AwaitChoice)
                    {
                        List<Button> choices = ActiveChoices();
                        if (choices.Count > 0)
                        {
                            choices[0].onClick.Invoke();
                        }
                    }
                    else if (phase == DialogueSaveData.Phase.Typing || phase == DialogueSaveData.Phase.AwaitAdvance)
                    {
                        Button tap = FindInView<Button>("TapArea");
                        if (tap != null)
                        {
                            tap.onClick.Invoke();
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{ShowcaseOptions.Prefix}[Session] 推进对白时出错：{e.GetType().Name}：{e.Message}");
                }

                yield return new WaitForSecondsRealtime(0.15f);
            }
        }

        private T FindInView<T>(string objectName) where T : Component
        {
            DialogueView view = ui == null ? null : ui.Get<DialogueView>();
            return view == null ? null : FindDeep<T>(view.transform, objectName);
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

        // ───────────────────────── 输入驱动 ─────────────────────────

        /// <summary>测试手柄持续推左摇杆驱动 Gameplay/Move，结束后回中（同 ExplorationShowcase.PushStick）。</summary>
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

        /// <summary>松杆后等玩家停稳（连续几帧位置不变，最多 2 秒），免得快照拍在滑行途中。</summary>
        private IEnumerator WaitStable()
        {
            Vector2 last = playerModel.Position;
            int still = 0;
            float deadline = Time.realtimeSinceStartup + 2f;
            while (still < 5 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                Vector2 now = playerModel.Position;
                still = (now - last).sqrMagnitude < 1e-8f ? still + 1 : 0;
                last = now;
            }
        }

        /// <summary>测试键盘按一下 Esc（UI/Cancel 绑的是 &lt;Keyboard&gt;/escape，走 UICancelRouter → CloseTopAsync）。</summary>
        private IEnumerator PressEscape()
        {
            if (testKeyboard == null)
            {
                testKeyboard = InputSystem.AddDevice<Keyboard>("ShowcaseKeyboard");
            }

            InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.Escape));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(testKeyboard, new KeyboardState());
            yield return null;
        }

        // ───────────────────────── 选槽面板查询 ─────────────────────────

        private SaveSlotsView SlotsView()
        {
            return ui == null ? null : ui.Get<SaveSlotsView>();
        }

        private string SlotsTitle()
        {
            SaveSlotsView view = SlotsView();
            TMP_Text title = view == null ? null : FindDeep<TMP_Text>(view.transform, "Title");
            return title == null ? null : title.text;
        }

        /// <summary>当前显示的槽行（模板复制出来的、激活的），按行上的「槽 N」文字索引。</summary>
        private Dictionary<int, Transform> SlotRows()
        {
            var rows = new Dictionary<int, Transform>();
            SaveSlotsView view = SlotsView();
            Transform list = view == null ? null : FindDeep<Transform>(view.transform, "List");
            if (list == null)
            {
                return rows;
            }

            for (int i = 0; i < list.childCount; i++)
            {
                Transform row = list.GetChild(i);
                if (!row.gameObject.activeInHierarchy || row.name == "RowTemplate")
                {
                    continue;
                }

                TMP_Text label = FindDeep<TMP_Text>(row, "Slot");
                for (int slot = 1; slot <= slotCount; slot++)
                {
                    if (label != null && label.text == SaveSlotsView.FormatSlotLabel(slot))
                    {
                        rows[slot] = row;
                    }
                }
            }

            return rows;
        }

        private bool RowIs(int slot, string primary, bool interactable)
        {
            if (!SlotRows().TryGetValue(slot, out Transform row))
            {
                return false;
            }

            TMP_Text text = FindDeep<TMP_Text>(row, "Primary");
            Button button = row.GetComponent<Button>();
            return text != null && text.text == primary && button != null && button.interactable == interactable;
        }

        // ───────────────────────── 槽文件 ─────────────────────────

        private string SlotPath(int slot)
        {
            return Path.Combine(saveRoot ?? string.Empty, $"slot{slot}.json");
        }

        private void WriteSlot(int slot, string content)
        {
            File.WriteAllText(SlotPath(slot), content, new UTF8Encoding(false));
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
