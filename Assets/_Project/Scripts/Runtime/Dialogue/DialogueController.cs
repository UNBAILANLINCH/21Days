// 职责：连接规则、表现策略、TMP 与立绘资源生命周期；复用 UI/Assets 服务。
//   世界暂停与输入图切换由 DialogueService 统一持有，本类不碰。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Narrative;
using UnityEngine;

namespace Game.Dialogue
{
    /// <summary>
    /// 对白表现控制器：逐帧驱动打字、自动播放、跳过（先经确认弹窗）与历史面板，把 View 的点击翻译成规则意图。
    /// <para>
    /// 「覆盖中」：历史面板或跳过确认弹窗开着时不打字、不自动、不跳过，主面板输入关闭。
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

        private readonly DialogueRules rules;
        private readonly DialogueCatalog catalog;
        private readonly IUIService ui;
        private readonly IAssetService assets;
        private readonly IClock clock;
        private readonly ITelemetryScope telemetry;
        private readonly AssetHandle<Sprite>[] handles = new AssetHandle<Sprite>[DialogueContent.SlotCount];
        // 选项图标：地址 → 句柄。键存在而值为 null 表示加载中或加载失败（不重复请求）；换节点 / 收尾时整体释放。
        private readonly Dictionary<string, AssetHandle<Sprite>> choiceIcons =
            new Dictionary<string, AssetHandle<Sprite>>(StringComparer.Ordinal);
        private Dictionary<string, DialogueCharacter> characters;
        private DialogueView view;
        private DialogueHistoryView history;
        private DialogueSkipConfirmView skipConfirm;
        private IDialogueConditionSource conditions;
        private DialoguePlaybackPolicy policy;
        private string targetId;
        private bool running;
        private bool historyOpen;
        private bool historyRequested;
        private bool dismissHistory;
        private bool skipConfirmOpen;
        private bool skipConfirmRequested;
        private bool skipConfirmed;
        private bool skipCancelled;
        // 每次释放选项图标自增；异步加载完成时对不上说明节点已换或对白已收尾，句柄直接释放。
        private int choiceIconEpoch;
        private CancellationToken presentToken;
        private bool inputConsumed;
        private bool ready;
        private float characterProgress;
        private float lastChoiceRefresh;
        private bool[] availability;

        public DialogueController(DialogueRules rules, DialogueCatalog catalog, IUIService ui, IAssetService assets,
            IClock clock, ITelemetryScope telemetry)
        {
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            this.assets = assets ?? throw new ArgumentNullException(nameof(assets));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>外部挂起（如存档中）：挂起期间不推进、不收输入。</summary>
        public bool Suspended { get; set; }
        public bool IsRunning => running;
        /// <summary>未展示，或当前节点已准备完毕（可存档的稳定点）。</summary>
        public bool IsStable => !running || ready;

        /// <summary>
        /// 展示当前对白直到完成，返回出口。调用方须先 <c>rules.Start(content)</c>（或 Restore），本方法只负责表现。
        /// </summary>
        /// <exception cref="OperationCanceledException">ct 取消，或对白被外部中断（Generation 变化 / 未到 Completed）。</exception>
        public async UniTask<string> PresentAsync(IDialogueConditionSource conditionSource, string target,
            DialoguePlaybackPolicy playback, CancellationToken ct)
        {
            if (running) throw new InvalidOperationException("已有对白正在展示");
            if (conditionSource == null) throw new ArgumentNullException(nameof(conditionSource));
            if (playback == null) throw new ArgumentNullException(nameof(playback));
            EnsureCharacters();
            running = true;
            conditions = conditionSource;
            targetId = target ?? string.Empty;
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
                        await PrepareAsync(generation, visit, ct);
                        if (generation != rules.Generation || visit != rules.Visit) continue;
                        ready = true;
                    }
                    if (historyRequested && !Suspended && !skipConfirmOpen)
                    {
                        historyRequested = false;
                        historyOpen = true;
                        history = await ui.OpenAsync<DialogueHistoryView>(ct: ct);
                        history.Show(rules.History, rules.HistoryTruncated);
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
                    }
                    if (!Suspended && !Overlaid)
                    {
                        RefreshChoices(false);
                        float delta = clock.UnscaledDeltaTime;
                        if (rules.Phase == DialogueSaveData.Phase.Typing)
                        {
                            characterProgress += policy.CharactersPerSecond * delta;
                            rules.RevealTo((int)characterProgress);
                        }
                        if (policy.TickAuto(delta, rules.Phase))
                            Submit(new DialogueIntent(DialogueIntent.Action.Advance, generation, visit));
                    }
                    view.SetVisible(rules.VisibleCharacters);
                    view.SetControls(policy.AutoPlay, policy.Speed, policy.Skipping);
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
                    foreach (AssetHandle<Sprite> handle in handles) handle?.Dispose();
                    Array.Clear(handles, 0, handles.Length);
                    ReleaseChoiceIcons();
                    view = null;
                    history = null;
                    skipConfirm = null;
                    historyOpen = historyRequested = dismissHistory = running = inputConsumed = false;
                    skipConfirmOpen = skipConfirmRequested = skipConfirmed = skipCancelled = false;
                    presentToken = CancellationToken.None;
                    conditions = null;
                    policy = null;
                    targetId = null;
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

        private bool Overlaid => historyOpen || skipConfirmOpen;

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
            characterProgress = 0;
            availability = null;
            ReleaseChoiceIcons();
            for (int slot = 0; slot < handles.Length; slot++)
            {
                view.SetPortrait(slot, null, false);
                handles[slot]?.Dispose();
                handles[slot] = null;
            }
            foreach (DialogueContent.Portrait portrait in rules.Portraits)
            {
                AssetHandle<Sprite> handle = await LoadPortraitAsync(portrait, ct);
                if (generation != rules.Generation || visit != rules.Visit || ct.IsCancellationRequested)
                { handle?.Dispose(); ct.ThrowIfCancellationRequested(); return; }
                handles[portrait.Slot] = handle;
                view.SetPortrait(portrait.Slot, handle?.Asset, portrait.CharacterId == node.SpeakerId || string.IsNullOrEmpty(node.SpeakerId));
            }
            if (preparing) rules.Ready(generation, visit, count, rules.Text, speaker);
            RefreshChoices(true);
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
            for (int i = 0; i < choices.Length; i++)
            {
                DialogueContent.Choice choice = choices[i];
                bool shown = availability[i] || !choice.HideWhenUnavailable;
                view.AddChoice(choice, availability[i], shown ? ResolveChoiceIcon(choice.IconKey) : null);
            }
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

        private void ToggleAuto() { if (running) policy.ToggleAuto(); }
        private void CycleSpeed() { if (running) policy.CycleSpeed(); }
        // 点跳过只请求确认弹窗，确认后才 BeginSkip；已在跳过中或覆盖中则忽略。
        private void RequestSkip() { if (running && !policy.Skipping && !Overlaid) skipConfirmRequested = true; }
        private void ConfirmSkip() => skipConfirmed = true;
        private void CancelSkip() => skipCancelled = true;
        private void RequestHistory() { if (!Overlaid && ready) historyRequested = true; }
        private void DismissHistory() => dismissHistory = true;
    }
}
