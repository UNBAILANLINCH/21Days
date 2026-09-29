// 职责：将叙事纯规则接到现有 Dialogue、Quest 和 Session；异步开始前固定身份，落盘仅允许稳定等待/结束。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Boot;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Dialogue;
using Game.Quest;
using Game.Session;
using MessagePipe;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Narrative
{
    public sealed class NarrativeService : IGameService, IDisposable
    {
        private readonly NarrativeCatalog catalog;
        private readonly DialogueService dialogue;
        private readonly DialogueSceneBinder scene;
        private readonly ISaveService saves;
        private readonly ISubscriber<SessionStartedEvent> sessions;
        private readonly ISubscriber<QuestCompletedEvent> completed;
        private readonly IPublisher<NarrativeChangedEvent> changed;
        private readonly ITelemetryScope telemetry;
        private NarrativeRules rules;
        private EncounterRules encounters;
        private IDisposable subscriptions;
        private CancellationTokenSource playback;
        private bool busy;
        private bool disposed;

        public NarrativeService(NarrativeCatalog catalog, DialogueService dialogue, DialogueSceneBinder scene,
            NarrativeConditionSource conditions, ISaveService saves, ISubscriber<SessionStartedEvent> sessions,
            ISubscriber<QuestCompletedEvent> completed, IPublisher<NarrativeChangedEvent> changed, ITelemetryScope telemetry)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.dialogue = dialogue ?? throw new ArgumentNullException(nameof(dialogue));
            this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
            Conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
            this.saves = saves ?? throw new ArgumentNullException(nameof(saves));
            this.sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            this.completed = completed ?? throw new ArgumentNullException(nameof(completed));
            this.changed = changed ?? throw new ArgumentNullException(nameof(changed));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        public NarrativeConditionSource Conditions { get; }
        public bool IsReady => rules != null && !disposed;
        public bool IsBusy => busy;
        public long Generation => RequireRules().Generation;
        public bool CanSave => IsReady && !busy && IsStable(rules);
        public NarrativeSaveData Capture() => RequireRules().Capture();

        public UniTask InitializeAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            rules = new NarrativeRules(catalog.Stories, telemetry);
            encounters = new EncounterRules(catalog.Encounters, rules);
            ReloadFromSave();
            var bag = DisposableBag.CreateBuilder();
            sessions.Subscribe(_ => ReloadFromSave()).AddTo(bag);
            completed.Subscribe(_ => SyncQuestFlags()).AddTo(bag);
            subscriptions = bag.Build();
            SceneManager.sceneLoaded += BindScene;
            foreach (NarrativeTrigger target in UnityEngine.Object.FindObjectsByType<NarrativeTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                target.Bind(this, scene.Actor);
            telemetry.Track("initialized", ("stories", catalog.Stories.Count));
            return UniTask.CompletedTask;
        }

        /// <summary>只读候选校验；旧档缺 Narrative 分区视为尚未开始，未知内容/对白半途快照拒绝提交。</summary>
        public void ValidateCandidate(SaveSnapshot candidate)
        {
            if (!candidate.Contains<NarrativeSaveData>()) return;
            var check = new NarrativeRules(catalog.Stories, null);
            NarrativeSaveData saved = candidate.Require<NarrativeSaveData>();
            check.Restore(saved);
            if (!IsStable(check) || (saved.Parent != null && saved.Parent.RequestIssued))
                throw new ArgumentException("该存档包含尚不支持恢复的叙事阶段");
        }

        public void ReloadFromSave()
        {
            RequireRules().Restore(saves.Get<NarrativeSaveData>());
            playback?.Cancel(); // Restore 已使旧回调的 Generation 失效。
            SyncQuestFlags();
            telemetry.Track("session_restored");
        }

        public async UniTask<bool> StartAsync(string storyId, string targetId, CancellationToken ct = default)
        {
            if (!CanAccept()) return false;
            ct.ThrowIfCancellationRequested();
            rules.Start(storyId, targetId);
            Flush();
            await DriveAsync(ct);
            return true;
        }

        /// <summary>批量仲裁；候选中的玩家/目标事实重新读取，不能用过期快照进入对白。</summary>
        public async UniTask<bool> TryEncounterAsync(IEnumerable<EncounterRules.Candidate> candidates, CancellationToken ct = default)
        {
            if (!CanAccept()) return false;
            ct.ThrowIfCancellationRequested();
            var current = new List<EncounterRules.Candidate>();
            foreach (EncounterRules.Candidate candidate in candidates ?? throw new ArgumentNullException(nameof(candidates)))
            {
                if (candidate?.Context == null) throw new ArgumentException("遭遇候选缺少目标身份");
                current.Add(new EncounterRules.Candidate
                {
                    TriggerId = candidate.TriggerId, TriggerKind = candidate.TriggerKind, EntryEpoch = candidate.EntryEpoch,
                    Context = Conditions.Snapshot(candidate.Context.TargetId),
                });
            }
            // Once 已在首次接受时消费；同目标主动交互恢复未完成对白，不重新进入遭遇。
            if (rules.Stage != null && rules.Stage.Kind == NarrativeContent.StageKind.Dialogue)
                foreach (EncounterRules.Candidate candidate in current)
                    if (candidate.TriggerKind == "Interact" && candidate.Context.TargetId == rules.Current.TargetId &&
                        candidate.Context.PlayerAlive && candidate.Context.TargetAlive)
                    {
                        telemetry.Track("dialogue_retried", ("target", rules.Current.TargetId), ("activation", rules.Current.ActivationId));
                        await DriveAsync(ct);
                        return true;
                    }
            bool activated = encounters.TryActivate(current);
            Flush(); // 条件下降沿即使没激活也须保存。
            telemetry.Track("encounter_requested", ("accepted", activated), ("candidates", current.Count));
            if (!activated) return false;
            await DriveAsync(ct);
            return true;
        }

        public async UniTask<bool> SubmitAsync(NarrativeIntent intent, CancellationToken ct = default)
        {
            if (!CanAccept()) return false;
            ct.ThrowIfCancellationRequested();
            if (!rules.Apply(in intent))
            {
                telemetry.TrackWarn("intent_rejected", TelemetryProps.Of(("generation", intent.Generation), ("activation", intent.ActivationId)));
                return false;
            }
            Flush();
            await DriveAsync(ct);
            return true;
        }

        /// <summary>取消/失败留在当前对白阶段，显式重试；不把整段重播伪装成读档恢复。</summary>
        public UniTask RetryAsync(CancellationToken ct = default)
        {
            if (!CanAccept()) throw new InvalidOperationException("叙事或对白正在运行");
            return DriveAsync(ct);
        }

        public void CaptureIntoPartition()
        {
            if (!CanSave) throw new InvalidOperationException("叙事尚未到可恢复的保存边界");
            CopyToPartition();
        }

        private async UniTask DriveAsync(CancellationToken ct)
        {
            busy = true;
            using var local = CancellationTokenSource.CreateLinkedTokenSource(ct);
            playback = local;
            long generation = rules.Generation;
            using var span = telemetry.BeginSpan("advance");
            try
            {
                while (generation == rules.Generation && rules.Current != null)
                {
                    local.Token.ThrowIfCancellationRequested();
                    rules.ResolveAutomatic(Conditions.Snapshot(rules.Current.TargetId));
                    Flush();
                    if (rules.Stage == null || rules.Stage.Kind == NarrativeContent.StageKind.WaitAction) return;
                    if (rules.Stage.Kind != NarrativeContent.StageKind.Dialogue) continue;
                    NarrativeSaveData.Frame frame = rules.Current;
                    // Frame 的四项身份固定在 await 前；恢复或换阶段后回来的结果会被 Apply 拒绝。
                    long activation = frame.ActivationId;
                    string targetId = frame.TargetId;
                    string request = frame.ActionRequestId;
                    NarrativeTrigger target = Conditions.Find(targetId);
                    if (target == null || !target.isActiveAndEnabled) throw new OperationCanceledException("叙事目标已失效");
                    if (!rules.MarkRequestIssued(activation)) throw new InvalidOperationException("对白请求已经发出");
                    Flush();
                    try
                    {
                        int id = int.Parse(rules.Stage.PayloadId, CultureInfo.InvariantCulture);
                        using var targetToken = CancellationTokenSource.CreateLinkedTokenSource(local.Token, target.LifetimeToken);
                        DialogueResult result = await dialogue.PlayAsync(id, target.transform, targetId, targetToken.Token);
                        var intent = new NarrativeIntent(generation, activation, targetId, request, result.Outcome);
                        if (!rules.Apply(in intent)) return;
                    }
                    finally { rules.ClearRequestIssued(generation, activation); }
                }
            }
            catch (OperationCanceledException)
            {
                telemetry.Track("advance_cancelled");
                throw;
            }
            catch (Exception e)
            {
                telemetry.TrackError("advance_failed", e);
                throw;
            }
            finally
            {
                if (playback == local) playback = null;
                busy = false;
                if (!disposed) Flush();
            }
        }

        private bool CanAccept()
        {
            RequireRules();
            if (!busy && !dialogue.IsRunning) return true;
            telemetry.TrackWarn("request_rejected", TelemetryProps.Of(("reason", "busy")));
            return false;
        }

        private NarrativeRules RequireRules() => IsReady ? rules : throw new InvalidOperationException("NarrativeService 尚未初始化或已释放");

        private static bool IsStable(NarrativeRules value) => value.Stage == null ||
            (value.Stage.Kind == NarrativeContent.StageKind.WaitAction && !value.Stage.IssueRequest && !value.Current.RequestIssued);

        private void SyncQuestFlags()
        {
            // 读持久分区而非依赖 SessionStartedEvent 的订阅次序；完成事件可能早于本服务初始化。
            foreach (QuestProgressData quest in saves.Get<QuestSaveData>().Quests)
                if (quest.State == (int)QuestState.Completed && catalog.QuestFlags.TryGetValue(quest.Id, out string flag))
                    rules.SetFlag(flag);
            Flush();
        }

        private void Flush()
        {
            CopyToPartition();
            changed.Publish(new NarrativeChangedEvent(rules.Current?.StageId));
        }

        private void CopyToPartition()
        {
            NarrativeSaveData copy = rules.Capture();
            NarrativeSaveData target = saves.Get<NarrativeSaveData>();
            target.NextActivationId = copy.NextActivationId;
            target.Current = copy.Current;
            target.Parent = copy.Parent;
            target.Outcome = copy.Outcome;
            target.ConsumedTriggers = copy.ConsumedTriggers;
            target.ConditionEdges = copy.ConditionEdges;
            target.EdgeCounters = copy.EdgeCounters;
            target.StoryFlags = copy.StoryFlags;
        }

        private void BindScene(Scene loaded, LoadSceneMode mode)
        {
            foreach (GameObject root in loaded.GetRootGameObjects())
                foreach (NarrativeTrigger target in root.GetComponentsInChildren<NarrativeTrigger>(true))
                    target.Bind(this, scene.Actor);
        }

        public void Dispose()
        {
            disposed = true;
            playback?.Cancel();
            subscriptions?.Dispose();
            SceneManager.sceneLoaded -= BindScene;
        }
    }
}
