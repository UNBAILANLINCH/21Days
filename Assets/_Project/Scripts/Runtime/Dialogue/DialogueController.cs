// 职责：连接规则、表现策略、TMP 与立绘资源生命周期；复用 UI/Assets 服务；节点前插播演出（按调用方给的锚点摆放，
//   插播期间场景角色的显隐由演出服务统一处理，见 PerformanceTriggerRules.HideSceneCharacters）。
//   世界暂停与输入图切换由 DialogueService 统一持有，本类不碰。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Logging;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Core.UI.Views;
using Game.Narrative;
using Game.Performance;
using UnityEngine;

namespace Game.Dialogue
{
    /// <summary>
    /// 对白表现控制器：逐帧驱动打字、自动播放、跳过（先经确认弹窗）与历史面板，把 View 的点击翻译成规则意图。
    /// <para>
    /// 「覆盖中」：历史面板或跳过确认弹窗开着时不打字、不自动、不跳过，主面板输入关闭。
    /// </para>
    /// <para>
    /// 「演出中」（<see cref="Performing"/>）：节点带 <c>PerformanceId</c> 时，摆台词之前先 await 演出服务播完；
    /// 期间语义同「覆盖中」，且点击 / 按键 / 自动 / 倍速 / 跳过全部忽略。时停与输入图由两边服务各自持令牌，本类不碰。
    /// 调用方传了演出锚点（通常是说话的 NPC）时，插播按锚点的世界位姿摆放演出（世界舞台模式据此落位）；不传则不摆放。
    /// 插播期间场景里全部角色的显隐由演出服务统一隐藏 / 恢复（<see cref="PerformanceTriggerRules.HideSceneCharacters"/>），
    /// 本类不再重复处理。
    /// </para>
    /// <para>
    /// 角色表不在构造时取：<see cref="DialogueCatalog.Characters"/> 惰性依赖 <c>IConfigService</c> 初始化完成，
    /// 而本类会随入口点 <see cref="DialogueSceneBinder"/> → <see cref="DialogueService"/> 在容器构建期就被解析，
    /// 那时配置服务还没初始化。所以持有 Catalog 引用，首次 <see cref="PresentAsync"/> 时才建角色索引。
    /// </para>
    /// </summary>
    public sealed class DialogueController
    {
        // 选项资格的周期重算间隔（unscaled 秒）。条件源每次 Snapshot 可能分配（默认实现就 new EncounterContext），
        // 不宜每帧取；资格变化以 0.25 s 粒度刷新到界面足够，提交时规则层仍会用最新快照校验，不会放过失效选项。
        private const float ChoiceRefreshInterval = 0.25f;

        // 历史里选择项的「说话者」：记录面板显示为「选择：选项文字」（与改用通用记录面板之前的格式一致）。
        private const string ChoiceSpeaker = "选择";

        private readonly DialogueRules rules;
        private readonly DialogueCatalog catalog;
        private readonly IUIService ui;
        private readonly IAssetService assets;
        private readonly IClock clock;
        private readonly ITelemetryScope telemetry;
        // 可为 null：Boot 没挂 PerformanceInstaller 时节点插播跳过（记 Warn + 埋点），不阻塞对白。
        private readonly IPerformanceService performance;
        private readonly AssetHandle<Sprite>[] handles = new AssetHandle<Sprite>[DialogueContent.SlotCount];
        // 上一节点的立绘句柄：换节点时不立刻释放，留到再下一次换节点 / 收尾——退场滑出与表情交叉淡化的残影还在显示旧图。
        private readonly AssetHandle<Sprite>[] retiring = new AssetHandle<Sprite>[DialogueContent.SlotCount];
        // 新节点立绘先全部加载到这里，再一次性交给 View，免得「先清槽再逐个加载」把每句都演成退场 + 入场。
        private readonly AssetHandle<Sprite>[] loading = new AssetHandle<Sprite>[DialogueContent.SlotCount];
        // 选项图标：地址 → 句柄。键存在而值为 null 表示加载中或加载失败（不重复请求）；换节点 / 收尾时整体释放。
        private readonly Dictionary<string, AssetHandle<Sprite>> choiceIcons =
            new Dictionary<string, AssetHandle<Sprite>>(StringComparer.Ordinal);
        private Dictionary<string, DialogueCharacter> characters;
        private DialogueView view;
        // 历史面板复用 Core 的通用记录面板（Top 层）：打开时把规则里的历史转成 TranscriptLine 交给它。
        private TranscriptView history;
        private DialogueSkipConfirmView skipConfirm;
        private IDialogueConditionSource conditions;
        private DialoguePlaybackPolicy policy;
        private string targetId;
        // 本段对白的插播演出锚点（可为 null = 不摆放）；PresentAsync 开始时记下、收尾清空，插播时才读它的世界位姿。
        private Transform performanceAnchor;
        private bool running;
        private bool historyOpen;
        private bool historyRequested;
        private bool dismissHistory;
        private bool skipConfirmOpen;
        private bool skipConfirmRequested;
        private bool skipConfirmed;
        private bool skipCancelled;
        private bool performing;
        // 演出服务缺席的 Log.Warn 每个控制器只打一次（埋点每次都埋），免得每句插播刷屏。
        private bool performanceWarned;
        // 每次释放选项图标自增；异步加载完成时对不上说明节点已换或对白已收尾，句柄直接释放。
        private int choiceIconEpoch;
        private CancellationToken presentToken;
        private bool inputConsumed;
        private bool ready;
        // 打字节奏（标点停顿）：每段对白按播放设置建一个，每句 Reset。
        private TypingCadence cadence;
        private float lastChoiceRefresh;
        private bool[] availability;
        // 当前显示的选项行（顺序同界面，隐藏的不可用选项不在其中）：可用性与选项 id，供数字键按行号选择。
        private readonly List<bool> choiceRowAvailable = new List<bool>(4);
        private readonly List<string> choiceRowIds = new List<string>(4);
        // 按钮上的键位提示（由 DialogueKeyboardInput 首次拿到动作集时传入），打开面板时交给 View。
        private string autoKeyHint = string.Empty;
        private string speedKeyHint = string.Empty;
        private string skipKeyHint = string.Empty;
        private string historyKeyHint = string.Empty;

        public DialogueController(DialogueRules rules, DialogueCatalog catalog, IUIService ui, IAssetService assets,
            IClock clock, ITelemetryScope telemetry, IPerformanceService performance = null)
        {
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            this.assets = assets ?? throw new ArgumentNullException(nameof(assets));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
            this.performance = performance;
        }

        /// <summary>外部挂起（如存档中）：挂起期间不推进、不收输入。</summary>
        public bool Suspended { get; set; }
        public bool IsRunning => running;
        /// <summary>未展示，或当前节点已准备完毕（可存档的稳定点）。</summary>
        public bool IsStable => !running || ready;
        /// <summary>正在播放节点前插播的演出：不打字、不自动、不推进，点击与按键全部忽略（语义同覆盖中）。</summary>
        public bool Performing => performing;

        /// <summary>
        /// 展示当前对白直到完成，返回出口。调用方须先 <c>rules.Start(content)</c>（或 Restore），本方法只负责表现。
        /// 节点插播演出不摆放（等价于锚点传 null）。
        /// </summary>
        /// <exception cref="OperationCanceledException">ct 取消，或对白被外部中断（Generation 变化 / 未到 Completed）。</exception>
        public UniTask<string> PresentAsync(IDialogueConditionSource conditionSource, string target,
            DialoguePlaybackPolicy playback, CancellationToken ct)
        {
            return PresentAsync(conditionSource, target, playback, null, ct);
        }

        /// <summary>
        /// 同上，并指定插播演出的锚点：节点前插播时按 <paramref name="anchor"/> 此刻的世界位姿摆放演出
        /// （<c>PerformancePlacement.FromTransform</c>，世界舞台模式据此落位）。传 null 不摆放，行为与不带锚点的重载完全一致。
        /// </summary>
        /// <exception cref="OperationCanceledException">ct 取消，或对白被外部中断（Generation 变化 / 未到 Completed）。</exception>
        public async UniTask<string> PresentAsync(IDialogueConditionSource conditionSource, string target,
            DialoguePlaybackPolicy playback, Transform anchor, CancellationToken ct)
        {
            if (running) throw new InvalidOperationException("已有对白正在展示");
            if (conditionSource == null) throw new ArgumentNullException(nameof(conditionSource));
            if (playback == null) throw new ArgumentNullException(nameof(playback));
            EnsureCharacters();
            running = true;
            conditions = conditionSource;
            targetId = target ?? string.Empty;
            performanceAnchor = anchor;
            policy = playback;
            presentToken = ct;
            ready = false;
            long generation = rules.Generation;
            try
            {
                view = await ui.OpenAsync<DialogueView>(ct: ct);
                view.OnIntent += Submit;
                view.OnTap += Tap;
                view.OnHistory += RequestHistory;
                view.OnAuto += ToggleAuto;
                view.OnSpeed += CycleSpeed;
                view.OnSkip += RequestSkip;
                view.SetKeyHints(autoKeyHint, speedKeyHint, skipKeyHint, historyKeyHint);
                view.SetMotion(policy.Settings.Motion);
                cadence = new TypingCadence(policy.Settings.CharactersPerSecond, policy.Settings.PunctuationPauseSeconds,
                    policy.Settings.PunctuationChars);
                long visit = -1;
                while (generation == rules.Generation && rules.Phase != DialogueSaveData.Phase.Completed &&
                    rules.Phase != DialogueSaveData.Phase.Closed)
                {
                    ct.ThrowIfCancellationRequested();
                    // 跳过先于准备：被跳过的句子不必等 TMP 排版与立绘加载，规则同步快进到选择 / 结束。
                    if (policy.Skipping && !Suspended && !Overlaid && IsLinePhase(rules.Phase))
                    {
                        rules.Skip(generation, ResolveSpeaker);
                        continue;
                    }
                    if (visit != rules.Visit)
                    {
                        ready = false;
                        visit = rules.Visit;
                        policy.OnNodeChanged();
                        // 插播点：节点前演出（跳过快进中不插播）；演出期间对白可能被外部中断，回来先比对身份。
                        await PerformBeforeNodeAsync(ct);
                        if (generation != rules.Generation || visit != rules.Visit) continue;
                        await PrepareAsync(generation, visit, ct);
                        if (generation != rules.Generation || visit != rules.Visit) continue;
                        ready = true;
                    }
                    if (historyRequested && !Suspended && !skipConfirmOpen)
                    {
                        historyRequested = false;
                        historyOpen = true;
                        history = await ui.OpenAsync<TranscriptView>(ct: ct);
                        history.Show(BuildTranscript(rules.History), rules.HistoryTruncated);
                        history.OnDismiss += DismissHistory;
                    }
                    if (dismissHistory)
                    {
                        dismissHistory = false;
                        // 与下面 skipConfirm 分支对称：关闭前先退订，避免面板销毁后事件仍持有委托引用。
                        if (history != null) history.OnDismiss -= DismissHistory;
                        await ui.CloseAsync(history, ct);
                        history = null;
                        historyOpen = false;
                        view.SelectChoice(); // 面板关掉后把选中还给选项（没有选项时 View 忽略）
                    }
                    if (skipConfirmRequested && !Suspended)
                    {
                        skipConfirmRequested = false;
                        if (!policy.Skipping && !Overlaid)
                        {
                            // 先置「覆盖中」再 await：弹窗加载期间也不打字、不自动。
                            skipConfirmOpen = true;
                            skipConfirmed = skipCancelled = false;
                            skipConfirm = await ui.OpenAsync<DialogueSkipConfirmView>(ct: ct);
                            skipConfirm.OnConfirm += ConfirmSkip;
                            skipConfirm.OnCancel += CancelSkip;
                        }
                    }
                    if (skipConfirmed || skipCancelled)
                    {
                        bool confirmed = skipConfirmed;
                        skipConfirmed = skipCancelled = false;
                        skipConfirm.OnConfirm -= ConfirmSkip;
                        skipConfirm.OnCancel -= CancelSkip;
                        await ui.CloseAsync(skipConfirm, ct);
                        skipConfirm = null;
                        skipConfirmOpen = false;
                        if (confirmed) policy.BeginSkip();
                        view.SelectChoice(); // 弹窗关掉后把选中还给选项（没有选项时 View 忽略）
                    }
                    if (!Suspended && !Overlaid)
                    {
                        RefreshChoices(false);
                        float delta = clock.UnscaledDeltaTime;
                        if (rules.Phase == DialogueSaveData.Phase.Typing)
                        {
                            // 预算已乘倍速；标点停顿按字符预算扣，倍速下同比缩短（见 TypingCadence）。
                            rules.RevealTo(cadence.Advance(view.VisibleText, rules.VisibleCharacters,
                                policy.CharactersPerSecond * delta));
                        }
                        if (policy.TickAuto(delta, rules.Phase))
                            Submit(new DialogueIntent(DialogueIntent.Action.Advance, generation, visit));
                    }
                    view.SetVisible(rules.VisibleCharacters);
                    view.SetControls(policy.AutoPlay, policy.Speed, policy.Skipping);
                    // 历史面板 / 跳过确认弹窗盖着时主面板输入已关，不显示「▼ 点击或按空格继续」，免得误导。
                    view.SetAdvancePrompt(rules.Phase == DialogueSaveData.Phase.AwaitAdvance && !Overlaid);
                    view.SetInput(!Suspended && !Overlaid && ready);
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    inputConsumed = false;
                }
                ct.ThrowIfCancellationRequested();
                if (generation != rules.Generation || rules.Phase != DialogueSaveData.Phase.Completed)
                    throw new OperationCanceledException("对白已中断", ct);
                return rules.Outcome;
            }
            finally
            {
                ready = false;
                performing = false;
                if (view != null)
                {
                    view.OnIntent -= Submit;
                    view.OnTap -= Tap;
                    view.OnHistory -= RequestHistory;
                    view.OnAuto -= ToggleAuto;
                    view.OnSpeed -= CycleSpeed;
                    view.OnSkip -= RequestSkip;
                }
                if (history != null) history.OnDismiss -= DismissHistory;
                if (skipConfirm != null)
                {
                    skipConfirm.OnConfirm -= ConfirmSkip;
                    skipConfirm.OnCancel -= CancelSkip;
                }
                try
                {
                    if (skipConfirm != null) await ui.CloseAsync(skipConfirm);
                    if (history != null) await ui.CloseAsync(history);
                    if (view != null) await ui.CloseAsync(view);
                }
                finally
                {
                    ReleaseAll(handles);
                    ReleaseAll(retiring);
                    ReleaseAll(loading);
                    ReleaseChoiceIcons();
                    choiceRowAvailable.Clear();
                    choiceRowIds.Clear();
                    view = null;
                    history = null;
                    skipConfirm = null;
                    historyOpen = historyRequested = dismissHistory = running = inputConsumed = false;
                    skipConfirmOpen = skipConfirmRequested = skipConfirmed = skipCancelled = false;
                    presentToken = CancellationToken.None;
                    conditions = null;
                    policy = null;
                    cadence = null;
                    targetId = null;
                    performanceAnchor = null;
                }
            }
        }

        /// <summary>提交一次意图；同帧只收第一条（防连点重复推进），未就绪 / 挂起 / 覆盖中（历史面板、跳过确认）时忽略。</summary>
        public void Submit(DialogueIntent intent)
        {
            if (!running || !ready || Suspended || Overlaid || inputConsumed) return;
            inputConsumed = true;
            long visit = rules.Visit;
            rules.Apply(in intent, conditions.Snapshot(targetId));
            if (rules.Visit != visit || rules.Phase == DialogueSaveData.Phase.Completed) ready = false;
            else RefreshChoices(true);
        }

        /// <summary>
        /// 键位提示（按钮文字后缀，如「A」「S」「Ctrl」「H」）；空串表示不显示。展示中调用会立即刷新当前面板。
        /// </summary>
        public void SetKeyHints(string autoKey, string speedKey, string skipKey, string historyKey)
        {
            autoKeyHint = autoKey ?? string.Empty;
            speedKeyHint = speedKey ?? string.Empty;
            skipKeyHint = skipKey ?? string.Empty;
            historyKeyHint = historyKey ?? string.Empty;
            if (view != null) view.SetKeyHints(autoKeyHint, speedKeyHint, skipKeyHint, historyKeyHint);
        }

        /// <summary>键位映射用的状态快照；未展示时 <c>Active</c> 为 false。</summary>
        internal DialogueKeyboardInput.State KeyState => new DialogueKeyboardInput.State(
            running && !Suspended, ready, running && rules.Phase == DialogueSaveData.Phase.AwaitChoice,
            historyOpen, skipConfirmOpen, choiceRowAvailable);

        /// <summary>
        /// 处理一次按键：经 <see cref="DialogueKeyboardInput.Map"/> 判定后，调用与点击完全相同的处理函数
        /// （推进 = 点对话框 <see cref="Tap"/>，自动 / 倍速 / 跳过 / 历史 = 点对应按钮，数字键 = 点第 N 行选项）。
        /// </summary>
        internal void HandleKey(DialogueKeyboardInput.Key key)
        {
            if (performing) return; // 演出中按键归演出服务（确认 / 长按跳过），对白一律不响应
            DialogueKeyboardInput.Command command = DialogueKeyboardInput.Map(key, KeyState);
            switch (command.Kind)
            {
                case DialogueKeyboardInput.CommandKind.Tap: Tap(); break;
                case DialogueKeyboardInput.CommandKind.ToggleAuto: ToggleAuto(); break;
                case DialogueKeyboardInput.CommandKind.CycleSpeed: CycleSpeed(); break;
                case DialogueKeyboardInput.CommandKind.RequestSkip: RequestSkip(); break;
                case DialogueKeyboardInput.CommandKind.OpenHistory: RequestHistory(); break;
                case DialogueKeyboardInput.CommandKind.CloseHistory: DismissHistory(); break;
                case DialogueKeyboardInput.CommandKind.CancelSkip: CancelSkip(); break;
                case DialogueKeyboardInput.CommandKind.Choose:
                    Submit(new DialogueIntent(DialogueIntent.Action.Choose, rules.Generation, rules.Visit,
                        choiceRowIds[command.Row]));
                    break;
            }
        }

        // 覆盖中：历史 / 跳过确认弹窗开着，或正在插播演出——三者都不打字、不自动、不跳过、不收主面板输入。
        private bool Overlaid => historyOpen || skipConfirmOpen || performing;

        // 节点前插播演出。只在新进节点（Preparing）时播：跳过快进中略过，存档恢复到已就绪的句子也不重播。
        // 摆放取锚点此刻的世界位姿（锚点为 null 或已销毁 → PerformancePlacement.None，等价于不带摆放的重载）：
        // 世界舞台演出不摆放会生成在原点（落到地面以下），所以对白要把说话的 NPC 作为锚点带进来。
        // 场景角色的隐藏 / 恢复由演出服务统一处理（PerformanceTriggerRules.HideSceneCharacters，见 PerformanceService.PlayAsync），
        // 对全部入口（直接播放、对白插播、场景触发）一视同仁，本类不再重复藏。
        // 服务缺席记 Warn 后照常摆台词；演出失败记 Error 后照常摆台词；取消原样抛出。
        private async UniTask PerformBeforeNodeAsync(CancellationToken ct)
        {
            DialogueContent.Node node = rules.Current;
            if (string.IsNullOrEmpty(node.PerformanceId) || policy.Skipping ||
                rules.Phase != DialogueSaveData.Phase.Preparing) return;
            if (performance == null)
            {
                if (!performanceWarned)
                {
                    performanceWarned = true;
                    Log.Warn("对白节点配置了插播演出，但演出服务未注册（Boot 没挂 PerformanceInstaller？），已跳过演出：" +
                             node.PerformanceId);
                }
                telemetry.TrackWarn("performance_unavailable",
                    TelemetryProps.Of(("node", node.Id), ("performance", node.PerformanceId)));
                return;
            }
            performing = true;
            view.SetInput(false);
            try
            {
                await performance.PlayAsync(node.PerformanceId, PerformancePlacement.FromTransform(performanceAnchor), ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Log.Error($"对白节点 {node.Id} 的插播演出 {node.PerformanceId} 播放失败，跳过演出继续对白：{e.Message}");
                telemetry.TrackError("performance_failed", e,
                    TelemetryProps.Of(("node", node.Id), ("performance", node.PerformanceId)));
            }
            finally
            {
                // 完成 / 跳过 / 取消 / 异常都走这里，解除「演出中」——主循环下一步就摆台词、重开输入。
                performing = false;
            }
            // 演出期间对白可能已被外部 Cancel / Restore（generation / visit 变了）：由调用方比对后 continue，这里只处理取消。
            ct.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// 把规则里的对白历史转成通用记录面板的输入：选择项记为说话者「选择」，其余原样取说话者与正文（旁白说话者为空）。
        /// 只在打开历史面板时调一次。公开是为了测试程序集可调（Game.Runtime 未对测试开 InternalsVisibleTo）。
        /// </summary>
        public static List<TranscriptLine> BuildTranscript(IReadOnlyList<DialogueSaveData.HistoryEntry> entries)
        {
            var lines = new List<TranscriptLine>(entries == null ? 0 : entries.Count);
            if (entries == null) return lines;
            for (int i = 0; i < entries.Count; i++)
            {
                DialogueSaveData.HistoryEntry entry = entries[i];
                if (entry == null) continue;
                lines.Add(entry.IsChoice
                    ? new TranscriptLine(ChoiceSpeaker, entry.Text)
                    : new TranscriptLine(entry.Speaker, entry.Text));
            }
            return lines;
        }

        private void EnsureCharacters()
        {
            if (characters != null) return;
            var map = new Dictionary<string, DialogueCharacter>(StringComparer.Ordinal);
            foreach (DialogueCharacter character in catalog.Characters) map.Add(character.Id, character);
            characters = map;
        }

        private static bool IsLinePhase(DialogueSaveData.Phase phase) =>
            phase == DialogueSaveData.Phase.Preparing || phase == DialogueSaveData.Phase.Typing ||
            phase == DialogueSaveData.Phase.AwaitAdvance;

        private string ResolveSpeaker(DialogueContent.Node node)
        {
            if (!string.IsNullOrEmpty(node.SpeakerName)) return node.SpeakerName;
            return characters.TryGetValue(node.SpeakerId ?? string.Empty, out DialogueCharacter character)
                ? character.DisplayName
                : string.Empty;
        }

        private async UniTask PrepareAsync(long generation, long visit, CancellationToken ct)
        {
            DialogueContent.Node node = rules.Current;
            string speaker = ResolveSpeaker(node);
            // 恢复时使用已解析文本及姓名；内容更新不能改写旧记录。
            bool preparing = rules.Phase == DialogueSaveData.Phase.Preparing;
            int count = view.SetLine(generation, visit, preparing ? speaker : rules.Speaker, rules.Text);
            cadence.Reset();
            availability = null;
            choiceRowAvailable.Clear(); // SetLine 已清掉界面上的选项行，这里同步清
            choiceRowIds.Clear();
            ReleaseChoiceIcons();
            // 先把本节点的立绘全部加载完（旧图在此期间照常显示），再逐槽交给 View：
            // View 只显示说话者那一槽，按「空 → 有 / 有 → 空 / 换图」自己决定播入场、退场还是交叉淡化；非说话者收起。
            ReleaseAll(loading);
            foreach (DialogueContent.Portrait portrait in rules.Portraits)
            {
                AssetHandle<Sprite> handle = await LoadPortraitAsync(portrait, ct);
                if (generation != rules.Generation || visit != rules.Visit || ct.IsCancellationRequested)
                { handle?.Dispose(); ReleaseAll(loading); ct.ThrowIfCancellationRequested(); return; }
                loading[portrait.Slot]?.Dispose();
                loading[portrait.Slot] = handle;
            }
            // 存档恢复进来的立绘直接置终态，不播入场。台词节点正常进入必经 Preparing，此刻已是 Typing / AwaitAdvance
            // 只可能是 Restore；选项节点进入即 AwaitChoice，不能拿「非 Preparing」判恢复，否则选项节点的换表情会被吞掉。
            bool instant = rules.Phase == DialogueSaveData.Phase.Typing || rules.Phase == DialogueSaveData.Phase.AwaitAdvance;
            for (int slot = 0; slot < handles.Length; slot++)
            {
                AssetHandle<Sprite> handle = loading[slot];
                // 旁白（节点无 speakerId）时两侧都不是说话者，一并收起。
                bool speaking = handle != null && !string.IsNullOrEmpty(node.SpeakerId) &&
                    SlotCharacter(slot) == node.SpeakerId;
                view.SetPortrait(slot, handle?.Asset, speaking, instant);
                retiring[slot]?.Dispose();
                retiring[slot] = handles[slot];
                handles[slot] = handle;
                loading[slot] = null;
            }
            if (preparing) rules.Ready(generation, visit, count, rules.Text, speaker);
            RefreshChoices(true);
        }

        private string SlotCharacter(int slot)
        {
            foreach (DialogueContent.Portrait portrait in rules.Portraits)
                if (portrait.Slot == slot) return portrait.CharacterId;
            return null;
        }

        private static void ReleaseAll(AssetHandle<Sprite>[] slots)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i]?.Dispose();
                slots[i] = null;
            }
        }

        private async UniTask<AssetHandle<Sprite>> LoadPortraitAsync(DialogueContent.Portrait portrait, CancellationToken ct)
        {
            if (!characters.TryGetValue(portrait.CharacterId, out DialogueCharacter character))
            { telemetry.TrackError("portrait_missing", "未知角色：" + portrait.CharacterId); return null; }
            if (!character.TrySprite(portrait.ExpressionId, out string key))
            {
                telemetry.TrackWarn("expression_fallback", TelemetryProps.Of(("character", character.Id)));
                key = character.DefaultSprite;
            }
            try { return await assets.LoadAsync<Sprite>(key, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { telemetry.TrackError("portrait_load_failed", e); }
            if (key == character.DefaultSprite) return null;
            try { return await assets.LoadAsync<Sprite>(character.DefaultSprite, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { telemetry.TrackError("portrait_fallback_failed", e); return null; }
        }

        private void RefreshChoices(bool force)
        {
            if (rules.Phase != DialogueSaveData.Phase.AwaitChoice) return;
            DialogueContent.Choice[] choices = rules.Current.Choices;
            if (availability == null) { availability = new bool[choices.Length]; force = true; }
            // 进入节点 / 提交后强制重算；其余按 unscaled 时间节流，避免 AwaitChoice 期间每帧取快照。
            float now = clock.UnscaledTime;
            if (!force && now - lastChoiceRefresh < ChoiceRefreshInterval) return;
            lastChoiceRefresh = now;
            EncounterContext snapshot = conditions.Snapshot(targetId);
            bool any = false;
            bool changed = force;
            for (int i = 0; i < choices.Length; i++)
            {
                bool enabled = NarrativeCondition.Matches(choices[i].Conditions, snapshot);
                changed |= availability[i] != enabled;
                availability[i] = enabled;
                any |= enabled;
            }
            if (!any) throw new InvalidOperationException("当前选择没有可用出口：" + rules.Current.Id);
            if (!changed) return;
            view.ClearChoices();
            choiceRowAvailable.Clear();
            choiceRowIds.Clear();
            for (int i = 0; i < choices.Length; i++)
            {
                DialogueContent.Choice choice = choices[i];
                bool shown = availability[i] || !choice.HideWhenUnavailable;
                view.AddChoice(choice, availability[i], shown ? ResolveChoiceIcon(choice.IconKey) : null);
                if (!shown) continue; // 与 View.AddChoice 的隐藏判定一致：隐藏的不占行号
                choiceRowAvailable.Add(availability[i]);
                choiceRowIds.Add(choice.Id);
            }
            view.SelectChoice();
        }

        // 已加载返回图标；未请求过就发起异步加载并先返回 null（无图标显示，加载完回填），不阻塞选项出现。
        private Sprite ResolveChoiceIcon(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (choiceIcons.TryGetValue(key, out AssetHandle<Sprite> handle)) return handle == null ? null : handle.Asset;
            choiceIcons.Add(key, null);
            LoadChoiceIconAsync(key, choiceIconEpoch, presentToken).Forget();
            return null;
        }

        private async UniTaskVoid LoadChoiceIconAsync(string key, int epoch, CancellationToken ct)
        {
            AssetHandle<Sprite> handle;
            try { handle = await assets.LoadAsync<Sprite>(key, ct); }
            catch (OperationCanceledException) { return; }
            catch (Exception e)
            {
                telemetry.TrackWarn("choice_icon_failed", TelemetryProps.Of(("key", key), ("error", e.Message)));
                return;
            }
            // 节点已换 / 对白已收尾（epoch 变了），或键已被移除：句柄不再有人持有，立即释放。
            if (!running || epoch != choiceIconEpoch || !choiceIcons.TryGetValue(key, out AssetHandle<Sprite> slot) ||
                slot != null)
            {
                handle?.Dispose();
                return;
            }
            choiceIcons[key] = handle;
            if (handle == null || view == null || rules.Phase != DialogueSaveData.Phase.AwaitChoice) return;
            foreach (DialogueContent.Choice choice in rules.Current.Choices)
                if (string.Equals(choice.IconKey, key, StringComparison.Ordinal)) view.SetChoiceIcon(choice.Id, handle.Asset);
        }

        private void ReleaseChoiceIcons()
        {
            choiceIconEpoch++;
            foreach (AssetHandle<Sprite> handle in choiceIcons.Values) handle?.Dispose();
            choiceIcons.Clear();
        }

        // 点击对话框：策略判定补全 / 推进后统一提交 Advance，规则自己区分 Typing 补全与 AwaitAdvance 推进。
        private void Tap()
        {
            if (!running || !ready || Suspended || Overlaid) return;
            DialoguePlaybackPolicy.TapOutcome outcome = policy.RegisterTap(clock.UnscaledTime, rules.Phase);
            if (outcome == DialoguePlaybackPolicy.TapOutcome.None) return;
            Submit(new DialogueIntent(DialogueIntent.Action.Advance, rules.Generation, rules.Visit));
        }

        private void ToggleAuto() { if (running && !performing) policy.ToggleAuto(); }
        private void CycleSpeed() { if (running && !performing) policy.CycleSpeed(); }
        // 点跳过只请求确认弹窗，确认后才 BeginSkip；已在跳过中或覆盖中则忽略。
        private void RequestSkip() { if (running && !policy.Skipping && !Overlaid) skipConfirmRequested = true; }
        private void ConfirmSkip() => skipConfirmed = true;
        private void CancelSkip() => skipCancelled = true;
        private void RequestHistory() { if (!Overlaid && ready) historyRequested = true; }
        private void DismissHistory() => dismissHistory = true;
    }
}
