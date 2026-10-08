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
        /// <summary>主动交互的触发类型（NarrativeTrigger 发出的候选）。</summary>
        private const string InteractTrigger = "Interact";

        private readonly NarrativeCatalog catalog;
        private readonly DialogueService dialogue;
        private readonly DialogueSceneBinder scene;
        private readonly ISaveService saves;
        private readonly ISubscriber<SessionStartedEvent> sessions;
        private readonly ISubscriber<QuestCompletedEvent> completed;
        private readonly IPublisher<NarrativeChangedEvent> changed;
        private readonly IPublisher<BattleStageEnteredEvent> battleStages;
        private readonly ITelemetryScope telemetry;
        private NarrativeRules rules;
        private EncounterRules encounters;
        private IDisposable subscriptions;
        private CancellationTokenSource playback;
        private bool busy;
        private bool disposed;

        public NarrativeService(NarrativeCatalog catalog, DialogueService dialogue, DialogueSceneBinder scene,
            NarrativeConditionSource conditions, ISaveService saves, ISubscriber<SessionStartedEvent> sessions,
            ISubscriber<QuestCompletedEvent> completed, IPublisher<NarrativeChangedEvent> changed, ITelemetryScope telemetry,
            IPublisher<BattleStageEnteredEvent> battleStages = null)
        {
            // 可空：不装战斗侧的测试 / 场景照旧能建；为空时停到 Battle 阶段只埋点、不通知（与接线前行为一致）。
            this.battleStages = battleStages;
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

        /// <summary>
        /// 剧情分区刚写回（与 <see cref="NarrativeChangedEvent"/> 同一时刻，含读档 / 换槽后的重载）。
        /// 给不进容器的场景组件用（<see cref="NarrativeFlagVisibility"/>）；订阅方只读状态，不得在回调里推进剧情。
        /// </summary>
        public event Action OnChanged;

        public bool IsReady => rules != null && !disposed;
        public bool IsBusy => busy;
        public long Generation => RequireRules().Generation;
        public bool CanSave => IsReady && !busy && IsStable(rules);
        public NarrativeSaveData Capture() => RequireRules().Capture();

        /// <summary>当前槽位的剧情标记里有没有 <paramref name="key"/>（只看落进分区的标记，不含身份 / 遭遇的派生事实）。未就绪时为 false。</summary>
        public bool HasStoryFlag(string key)
        {
            if (!IsReady || string.IsNullOrEmpty(key)) return false;
            HashSet<string> flags = saves.Get<NarrativeSaveData>().StoryFlags;
            return flags != null && flags.Contains(key);
        }

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
            foreach (NarrativeFlagVisibility visibility in UnityEngine.Object.FindObjectsByType<NarrativeFlagVisibility>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                visibility.Bind(this);
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
            // PRP/turnbased-battle D9：读档停在 Battle 阶段 → 战斗从头开始（战斗会话状态不进存档，不升 ReplayFormat）。
            // 存档里的「战斗在途」标记必然是旧进程的残留（在途时 CanSave=false，正常写不进盘），先清掉，下面 Flush 一并写回分区。
            bool battle = rules.Stage != null && rules.Stage.Kind == NarrativeContent.StageKind.Battle;
            if (battle) rules.ClearRequestIssued(rules.Generation, rules.Current.ActivationId);
            SyncQuestFlags();
            telemetry.Track("session_restored");
            if (battle) PublishBattleStage("restored");
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
            long interactEpoch = -1;
            foreach (EncounterRules.Candidate candidate in candidates ?? throw new ArgumentNullException(nameof(candidates)))
            {
                if (candidate?.Context == null) throw new ArgumentException("遭遇候选缺少目标身份");
                long epoch = candidate.EntryEpoch;
                // 主动交互没有「进入区域」那种天然纪元（NarrativeTrigger 恒给 0），Reenter 规则的消费键会在第一次后永远撞上。
                // 用剧情的 NextActivationId 当纪元：上一段剧情跑过（任何阶段进入都会 +1）才算一次新的交互，
                // 同一段剧情里连按不会重复进入；它随 NarrativeSaveData 落盘，读档后不会与已消费键冲突。
                // Once / RisingCondition 的消费键不含纪元，不受影响（PRP/turnbased-battle D11：BOSS 打输后可再按 E 重打）。
                if (epoch == 0 && candidate.TriggerKind == InteractTrigger)
                {
                    if (interactEpoch < 0) interactEpoch = rules.Capture().NextActivationId;
                    epoch = interactEpoch;
                }
                current.Add(new EncounterRules.Candidate
                {
                    TriggerId = candidate.TriggerId, TriggerKind = candidate.TriggerKind, EntryEpoch = epoch,
                    Context = Conditions.Snapshot(candidate.Context.TargetId),
                });
            }
            // 停在 Battle 阶段而战斗没在跑（上一次开战失败 / 表现缺失被放弃）：同目标主动交互 = 重新开这一仗，
            // 与下面「未完成对白」的重试同一口径；战斗在途（RequestIssued）时不重发，免得两场叠在一起。
            if (rules.Stage != null && rules.Stage.Kind == NarrativeContent.StageKind.Battle && !rules.Current.RequestIssued)
                foreach (EncounterRules.Candidate candidate in current)
                    if (candidate.TriggerKind == InteractTrigger && candidate.Context.TargetId == rules.Current.TargetId &&
                        candidate.Context.PlayerAlive && candidate.Context.TargetAlive)
                    {
                        PublishBattleStage("retried");
                        return true;
                    }
            // Once 已在首次接受时消费；同目标主动交互恢复未完成对白，不重新进入遭遇。
            if (rules.Stage != null && rules.Stage.Kind == NarrativeContent.StageKind.Dialogue)
                foreach (EncounterRules.Candidate candidate in current)
                    if (candidate.TriggerKind == InteractTrigger && candidate.Context.TargetId == rules.Current.TargetId &&
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

        /// <summary>
        /// 战斗结果回写：战斗侧（EncounterStep / Monster）按 <c>battleResults</c> 里的结果码提交一次。
        /// 三项身份（Generation / ActivationId / TargetId）里前两项取自 <see cref="EncounterStep"/> 登记的请求身份，
        /// 对不上就是旧场景或读档前的迟到结果，返回 false 而不是抛。
        /// </summary>
        public async UniTask<bool> CompleteBattleAsync(long generation, long activationId, string targetId,
            string resultKey, CancellationToken ct = default)
        {
            if (!CanAccept()) return false;
            ct.ThrowIfCancellationRequested();
            bool accepted = rules.CompleteBattle(generation, activationId, targetId, resultKey);
            if (accepted)
            {
                telemetry.Track("battle_result_applied", ("result", resultKey), ("activation", activationId));
                Flush();
            }
            else telemetry.TrackWarn("battle_result_rejected", TelemetryProps.Of(("result", resultKey), ("activation", activationId)));
            if (accepted) await DriveAsync(ct);
            return accepted;
        }

        /// <summary>
        /// 战斗侧开打前登记「战斗在途」（PRP/turnbased-battle D9 的「战中不存档」）：身份三项对得上当前 Battle 阶段才登记，
        /// 复用阶段帧的 RequestIssued——登记后 <see cref="CanSave"/> 为 false，自动保存与离场保存都被 Session 闸住。
        /// 返回 false = 旧身份（读档 / 换主线之后的迟到通知）或已经在途，战斗侧不该开仗。
        /// </summary>
        public bool TryBeginBattle(long generation, long activationId, string targetId)
        {
            RequireRules();
            if (!rules.CanCompleteBattle(generation, activationId, targetId) || !rules.MarkRequestIssued(activationId))
            {
                telemetry.TrackWarn("battle_begin_rejected", TelemetryProps.Of(("generation", generation), ("activation", activationId)));
                return false;
            }
            Flush();
            telemetry.Track("battle_begun", ("activation", activationId));
            return true;
        }

        /// <summary>
        /// 战斗没打完就收场（异常 / 取消 / 表现缺失 / 回写被拒）：解除在途登记，阶段原地不动——Battle 阶段回到可保存、
        /// 可重试（同目标再交互或读档都会重新发布 <see cref="BattleStageEnteredEvent"/>）。身份已过期时是空操作。
        /// </summary>
        public void ReleaseBattle(long generation, long activationId)
        {
            if (!IsReady) return;
            rules.ClearRequestIssued(generation, activationId);
            Flush();
            telemetry.Track("battle_released", ("activation", activationId));
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
                    if (rules.Stage == null) return;
                    // 战斗与操作等待都停在原地：战斗关的推进权在战斗侧，Narrative 只等结果回写。
                    // 这里必须和 WaitAction 一起停，否则 while 会对着同一个 Battle 阶段空转到取消。
                    // 停到 Battle 时同步通知战斗侧（PRP/turnbased-battle D2）。订阅方不得在回调里同步跑完整场：
                    // 此刻 busy 仍为 true，CompleteBattleAsync 会被 CanAccept 拒掉（Game.Battle 的世界门闸先让出一帧）。
                    if (rules.Stage.Kind == NarrativeContent.StageKind.Battle)
                    {
                        PublishBattleStage("entered");
                        return;
                    }
                    if (rules.Stage.Kind == NarrativeContent.StageKind.WaitAction) return;
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

        /// <summary>
        /// 可恢复边界只认「无外部请求、无待回写结果的等待」。
        /// Battle 阶段（PRP/turnbased-battle D9）：战斗在途（RequestIssued，见 <see cref="TryBeginBattle"/>）时不稳定——
        /// 战斗会话只活在内存里，不能落盘；没在打时稳定，读档后 <see cref="ReloadFromSave"/> 重新发布通知、战斗从头开始，
        /// 所以不再是「永远等不到结果的阶段」。
        /// </summary>
        private static bool IsStable(NarrativeRules value) => value.Stage == null ||
            (value.Stage.Kind == NarrativeContent.StageKind.WaitAction && !value.Stage.IssueRequest && !value.Current.RequestIssued) ||
            (value.Stage.Kind == NarrativeContent.StageKind.Battle && !value.Current.RequestIssued);

        private void SyncQuestFlags()
        {
            // 读持久分区而非依赖 SessionStartedEvent 的订阅次序；完成事件可能早于本服务初始化。
            foreach (QuestProgressData quest in saves.Get<QuestSaveData>().Quests)
                if (quest.State == (int)QuestState.Completed && catalog.QuestFlags.TryGetValue(quest.Id, out string flag))
                    rules.SetFlag(flag);
            Flush();
        }

        /// <summary>按当前阶段的身份发布 <see cref="BattleStageEnteredEvent"/>；reason 只进埋点（entered / restored / retried）。</summary>
        private void PublishBattleStage(string reason)
        {
            NarrativeSaveData.Frame frame = rules.Current;
            string payload = rules.Stage.PayloadId ?? string.Empty;
            telemetry.Track("battle_stage_entered", ("stage", frame.StageId), ("payload", payload),
                ("activation", frame.ActivationId), ("reason", reason));
            battleStages?.Publish(new BattleStageEnteredEvent(rules.Generation, frame.ActivationId, frame.TargetId, frame.StageId, payload));
        }

        private void Flush()
        {
            CopyToPartition();
            changed.Publish(new NarrativeChangedEvent(rules.Current?.StageId));
            RaiseChanged();
        }

        // 场景组件的显隐回调不许把剧情推进一起炸掉：逐个调、各自兜住。
        private void RaiseChanged()
        {
            Action handlers = OnChanged;
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try { ((Action)handler)(); }
                catch (Exception e) { telemetry.TrackError("changed_handler_failed", e); }
            }
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
            {
                foreach (NarrativeTrigger target in root.GetComponentsInChildren<NarrativeTrigger>(true))
                    target.Bind(this, scene.Actor);
                foreach (NarrativeFlagVisibility visibility in root.GetComponentsInChildren<NarrativeFlagVisibility>(true))
                    visibility.Bind(this);
            }
        }

        public void Dispose()
        {
            disposed = true;
            playback?.Cancel();
            subscriptions?.Dispose();
            SceneManager.sceneLoaded -= BindScene;
            OnChanged = null;
        }
    }
}
