// 职责：连接规则、TMP 与资源生命周期；复用 UI/Assets 服务，世界暂停由会话层统一计算。
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
    public sealed class DialogueController
    {
        private readonly DialogueRules rules;
        private readonly DialogueConfig config;
        private readonly IUIService ui;
        private readonly IAssetService assets;
        private readonly IClock clock;
        private readonly ITelemetryScope telemetry;
        private readonly Dictionary<string, DialogueCharacter> characters;
        private readonly AssetHandle<Sprite>[] handles = new AssetHandle<Sprite>[3];
        private DialogueView view;
        private DialogueHistoryView history;
        private Func<EncounterContext> context;
        private bool running;
        private bool historyOpen;
        private bool historyRequested;
        private bool dismissHistory;
        private bool inputConsumed;
        private bool skipping;
        private bool ready;
        private float characterProgress;
        private float skipElapsed;
        private bool[] availability;

        public DialogueController(DialogueRules rules, DialogueConfig config, IUIService ui, IAssetService assets,
            IClock clock, IEnumerable<DialogueCharacter> characters, ITelemetryScope telemetry)
        {
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
            this.ui = ui;
            this.assets = assets;
            this.clock = clock;
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
            this.characters = new Dictionary<string, DialogueCharacter>(StringComparer.Ordinal);
            foreach (DialogueCharacter character in characters) this.characters.Add(character.Id, character);
        }
        public bool Suspended { get; set; }
        public bool IsRunning => running;
        public bool BlocksWorld => running && (historyOpen || rules.Blocking);
        public bool IsStable => !running || ready;
        public void StopSkip() { skipping = false; skipElapsed = 0f; }

        // Start/Restore 由调用方先完成；本方法只恢复表现，不重发剧情请求。
        public async UniTask<string> PresentAsync(Func<EncounterContext> currentContext, CancellationToken ct)
        {
            if (running) throw new InvalidOperationException("已有对白正在展示");
            running = true;
            context = currentContext ?? throw new ArgumentNullException(nameof(currentContext));
            StopSkip();
            ready = false;
            long generation = rules.Generation;
            try
            {
                view = await ui.OpenAsync<DialogueView>(ct: ct);
                view.OnIntent += Submit;
                view.OnHistory += RequestHistory;
                view.OnSkip += SetSkip;
                long visit = -1;
                while (generation == rules.Generation && rules.Phase != DialogueSaveData.Phase.Completed &&
                    rules.Phase != DialogueSaveData.Phase.Closed)
                {
                    ct.ThrowIfCancellationRequested();
                    if (visit != rules.Visit)
                    {
                        ready = false;
                        visit = rules.Visit;
                        await PrepareAsync(generation, visit, ct);
                        if (generation != rules.Generation || visit != rules.Visit) continue;
                        ready = true;
                    }
                    if (historyRequested && !Suspended)
                    {
                        historyRequested = false;
                        historyOpen = true;
                        StopSkip();
                        history = await ui.OpenAsync<DialogueHistoryView>(ct: ct);
                        history.Show(rules.History, rules.HistoryTruncated);
                        history.OnDismiss += DismissHistory;
                    }
                    if (dismissHistory)
                    {
                        dismissHistory = false;
                        await ui.CloseAsync(history, ct);
                        history = null;
                        historyOpen = false;
                    }
                    if (!Suspended && !historyOpen)
                    {
                        RefreshChoices(false);
                        if (rules.Phase == DialogueSaveData.Phase.Typing)
                        {
                            characterProgress += config.CharactersPerSecond * clock.UnscaledDeltaTime;
                            rules.RevealTo((int)characterProgress);
                        }
                        if (skipping && !rules.CanSkip) StopSkip();
                        if (skipping)
                        {
                            skipElapsed += clock.UnscaledDeltaTime;
                            if (skipElapsed >= config.SkipInterval)
                            {
                                skipElapsed = 0;
                                Submit(new DialogueIntent(DialogueIntent.Action.Advance, generation, visit));
                            }
                        }
                    }
                    view.SetVisible(rules.VisibleCharacters);
                    view.SetInput(!Suspended && !historyOpen && ready,
                        rules.Phase == DialogueSaveData.Phase.Typing || rules.Phase == DialogueSaveData.Phase.AwaitAdvance, skipping);
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
                if (view != null) { view.OnIntent -= Submit; view.OnHistory -= RequestHistory; view.OnSkip -= SetSkip; }
                try
                {
                    await ui.CloseAsync(history);
                    await ui.CloseAsync(view);
                }
                finally
                {
                    foreach (AssetHandle<Sprite> handle in handles) handle?.Dispose();
                    Array.Clear(handles, 0, handles.Length);
                    view = null;
                    history = null;
                    historyOpen = historyRequested = dismissHistory = running = inputConsumed = false;
                    context = null;
                    StopSkip();
                }
            }
        }

        public void Submit(DialogueIntent intent)
        {
            if (!running || !ready || Suspended || historyOpen || inputConsumed) return;
            inputConsumed = true;
            long visit = rules.Visit;
            rules.Apply(in intent, context());
            if (rules.Visit != visit || rules.Phase == DialogueSaveData.Phase.Completed) ready = false;
            else RefreshChoices(true);
        }
        private async UniTask PrepareAsync(long generation, long visit, CancellationToken ct)
        {
            DialogueContent.Node node = rules.Current;
            string speaker = node.SpeakerName;
            if (string.IsNullOrEmpty(speaker) && characters.TryGetValue(node.SpeakerId, out DialogueCharacter character))
                speaker = character.DisplayName;
            // 恢复时使用已解析文本及姓名；内容更新不能改写旧记录。
            bool preparing = rules.Phase == DialogueSaveData.Phase.Preparing;
            int count = view.SetLine(generation, visit, preparing ? speaker : rules.Speaker, rules.Text);
            characterProgress = 0;
            availability = null;
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
            EncounterContext snapshot = context();
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
            for (int i = 0; i < choices.Length; i++) view.AddChoice(choices[i], availability[i]);
        }
        private void RequestHistory() { if (!historyOpen && ready) historyRequested = true; }
        private void DismissHistory() => dismissHistory = true;
        private void SetSkip(bool enabled) { skipping = enabled && rules.CanSkip; skipElapsed = 0f; }
    }
}
