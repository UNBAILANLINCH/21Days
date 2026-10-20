// 职责：正式剧情的场景接线和真实操作回写；剧情图仍只在 NarrativeService。
// 现有 NarrativeService 不应认识五店细节，ShopPlaytest 不应拥有序章，故新增章节适配器。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Battle;
using Game.Core.Flow;
using Game.Core.Events;
using Game.Core.Input;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Dialogue;
using Game.Interaction;
using Game.Monster;
using Game.Quest;
using Game.Rhythm;
using Game.Session;
using Game.ShopPlaytest;
using MessagePipe;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;
namespace Game.Narrative
{
    public sealed class SpotlightStoryFlow : IStartable, ITickable, IDisposable
    {
        private readonly NarrativeService narrative;
        private readonly ShopPlaytestState shops;
        private readonly IGameFlow flow;
        private readonly IInputService input;
        private readonly IWorldPauseService pause;
        private readonly IInteractionRegistry interaction;
        private readonly QuestService quests;
        private readonly DialogueService dialogue;
        private readonly DialogueRules dialogueRules;
        private bool archiveDialogue;
        private readonly BattleFlow battle;
        private readonly RhythmState rhythm;
        private readonly ISubscriber<SessionStartedEvent> sessions;
        private readonly ITelemetryScope telemetry;
        private readonly EncounterStep step;
        private readonly ILoadingCurtain curtain;
        private readonly ISubscriber<GameStateChangingEvent> stateChanging;
        private readonly ISubscriber<GameStateChangedEvent> stateChanged;
        private IDisposable stateChangingSubscription, stateChangedSubscription;
        private IDisposable sessionSubscription;
        private SpotlightSceneBindings scene;
        private SpotlightSceneBindings humanBindings, demonBindings;
        private CancellationTokenSource lifetime;
        private CancellationTokenSource sessionLifetime = new CancellationTokenSource();
        private bool crossingMirror;
        private Camera sceneCamera;
        private bool windowShotPlayed;
        private long sessionGeneration;
        private bool newGame, startHandled, working;
        private string visualStory = "", visualStage = "";
        private readonly Dictionary<int, SpotlightEnvironmentView> environmentViews = new Dictionary<int, SpotlightEnvironmentView>();
        private SpotlightEnvironmentView environment;
        private bool environmentDirty = true, environmentTransition;
        private long environmentSession = -1;
        private long environmentOperation;
        private bool environmentFlowChanging;
        private CancellationTokenSource environmentCancellation;
        private Action releaseEnvironmentControls;
        public SpotlightSceneBindings Bindings => scene;
        public bool IsFormalStory => narrative.CurrentStoryId == "spotlight_current";
        public bool CanSave => !crossingMirror && !working && (!newGame || startHandled);
        public event Action OnMirrorCrossingStarted;
        public event Action<bool> OnMirrorCrossingFinished;
        public SpotlightStoryFlow(NarrativeService narrative, ShopPlaytestState shops, IGameFlow flow,
            IInputService input, IWorldPauseService pause, IInteractionRegistry interaction, QuestService quests,
            DialogueService dialogue, BattleFlow battle, RhythmState rhythm,
            ISubscriber<SessionStartedEvent> sessions, ITelemetryService telemetry, EncounterStep step, ILoadingCurtain curtain, DialogueRules dialogueRules,
            ISubscriber<GameStateChangingEvent> stateChanging, ISubscriber<GameStateChangedEvent> stateChanged)
        {
            this.narrative = narrative; this.shops = shops; this.flow = flow; this.input = input;
            this.pause = pause; this.interaction = interaction; this.quests = quests;
            this.dialogue = dialogue; this.battle = battle; this.rhythm = rhythm; this.sessions = sessions;
            this.telemetry = telemetry.Scope("spotlight");
            this.step = step; this.curtain = curtain;
            this.dialogueRules = dialogueRules;
            this.stateChanging = stateChanging; this.stateChanged = stateChanged;
        }
        public void Start()
        {
            dialogue.OnStarted += DialogueStarted;
            dialogue.OnEnded += DialogueEnded;
            sessionSubscription = sessions.Subscribe(start => {
                CancelEnvironmentTransition();
                sessionLifetime.Cancel(); sessionLifetime.Dispose(); sessionLifetime = new CancellationTokenSource();
                crossingMirror = false; working = false;
                windowShotPlayed = false;
                sessionGeneration++; newGame = start.IsNewGame; startHandled = false;
                RestoreEnvironment(); environmentDirty = true; environmentTransition = false;
            });
            narrative.OnChanged += EnvironmentChanged;
            stateChangingSubscription = stateChanging.Subscribe(_ => {
                environmentFlowChanging = true;
                CancelEnvironmentTransition(); RestoreEnvironment();
            });
            stateChangedSubscription = stateChanged.Subscribe(_ => {
                // Changed 在主流程揭幕之前发布；真实队列结束前仍不能启动第二个环境黑幕。
                environmentFlowChanging = flow is IGameFlowTransitionStatus status && status.IsTransitioning;
                environmentDirty = true;
            });
            SceneManager.activeSceneChanged += ActiveEnvironmentSceneChanged;
            SceneManager.sceneLoaded += Loaded; SceneManager.sceneUnloaded += Unloaded;
            for (int i = 0; i < SceneManager.sceneCount; i++) Loaded(SceneManager.GetSceneAt(i), LoadSceneMode.Additive);
        }
        private void Loaded(Scene loaded, LoadSceneMode mode)
        {
            foreach (var root in loaded.GetRootGameObjects())
            {
                foreach (var candidate in root.GetComponentsInChildren<SpotlightEnvironmentView>(true))
                {
                    if (environmentViews.TryGetValue(loaded.handle, out var previous) && previous != candidate)
                        throw new InvalidOperationException("同一剧情场景只能接一个环境表现入口");
                    environmentViews[loaded.handle] = candidate;
                }
                foreach (var bindings in root.GetComponentsInChildren<SpotlightSceneBindings>(true))
                    if (bindings.IsHumanStory) humanBindings = bindings; else demonBindings = bindings;
            }
        }
        private void Bind(SpotlightSceneBindings bindings)
        {
            Unbind(); scene = bindings;
            if (scene == null) return;
            lifetime = new CancellationTokenSource();
            sceneCamera = Camera.main;
            environmentViews.TryGetValue(scene.gameObject.scene.handle, out environment);
            if (environment != null) environment.OnInvalidated += EnvironmentChanged;
            environmentDirty = true;
            if (scene.Config == null || scene.MainTarget == null || scene.MentorTarget == null) throw new InvalidOperationException("正式剧情场景引用未接齐");
            foreach (var point in scene.Points) { point.Bind(scene.Config, Allowed, Interact, scene.IsHumanStory); interaction.Register(point); }
        }
        private void Unloaded(Scene unloaded)
        {
            if (scene != null && scene.gameObject.scene == unloaded) Unbind();
            if (humanBindings != null && humanBindings.gameObject.scene == unloaded) humanBindings = null;
            if (demonBindings != null && demonBindings.gameObject.scene == unloaded) demonBindings = null;
            environmentViews.Remove(unloaded.handle);
        }
        private bool Blocked => crossingMirror || working || dialogue.IsRunning || narrative.IsBusy || battle.IsBattleRunning || rhythm.IsActivityOpen || pause.IsPaused;
        private bool Allowed(SpotlightSceneBindings.Point point) => scene != null && IsFormalStory && !Blocked &&
            (point.StageId != "enter_r0188" || scene.IsHumanStory || windowShotPlayed) &&
            narrative.CurrentStageId == point.StageId && !StoryGuideRules.IsAutomaticEntry(point.StageId) && point.StageId != "enter_r0293" && interaction.Actor != null;
        private void Interact(SpotlightSceneBindings.Point point)
        {
            if (!Allowed(point) || (interaction.Actor.Anchor.position - point.Position).sqrMagnitude > scene.Config.InteractionRadius * scene.Config.InteractionRadius) return;
            if (scene.IsHumanStory && point.StageId == "enter_r0171") { CrossMirrorAsync().Forget(); return; }
            InteractAsync(point).Forget();
        }
        private async UniTask InteractAsync(SpotlightSceneBindings.Point point)
        {
            working = true; CancellationToken token = lifetime.Token;
            long generation = sessionGeneration;
            try
            {
                if (point.StageId == "action_r0070")
                {
                    await narrative.TryEncounterAsync(new[] { new EncounterRules.Candidate {
                        TriggerId = scene.MentorTarget.TargetId, TriggerKind = "SpotlightMentor",
                        EntryEpoch = narrative.Capture().NextActivationId,
                        Context = narrative.Conditions.Snapshot(scene.MentorTarget.TargetId) } }, token);
                }
                else
                {
                    NarrativeIntent intent = narrative.ResultIntent("Success");
                    await narrative.SubmitAsync(intent, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { telemetry.TrackError("interaction_failed", error); }
            finally { if (generation == sessionGeneration) working = false; }
        }
        public void Tick()
        {
            if (environmentFlowChanging)
            {
                // Changed 只代表成功；失败/取消由队列的实际终态解锁，不能永久停住剧情。
                if (!(flow is IGameFlowTransitionStatus status) || status.IsTransitioning) return;
                environmentFlowChanging = false; environmentDirty = true;
            }
            if (!(flow.Current is MonsterEncounterState encounter) || !step.IsActive || !narrative.IsReady)
            { RestoreEnvironment(); return; }
            var bindings = encounter.IsHumanWorld ? humanBindings : demonBindings;
            if (scene != bindings) Bind(bindings);
            if (scene == null) return;
            var line = dialogueRules.Current;
            bool wangSpeaking = line != null && line.SpeakerId == "wang_luye";
            bool serious = false;
            if (wangSpeaking) foreach (var portrait in line.Portraits)
                if (portrait.CharacterId == "wang_luye" && portrait.ExpressionId == "serious") serious = true;
            scene.PresentWangLuye(archiveDialogue && dialogue.IsRunning, wangSpeaking && dialogueRules.Phase == DialogueSaveData.Phase.Typing,
                wangSpeaking && serious, interaction.Actor == null ? null : interaction.Actor.Anchor, sceneCamera);
            scene.PresentLunhuitangGuard(dialogue.IsRunning && line != null && line.SpeakerId == "lunhuitang_guard" &&
                dialogueRules.Phase == DialogueSaveData.Phase.Typing);
            if (!startHandled)
            {
                startHandled = true;
                if (newGame && narrative.CurrentStoryId.Length == 0) { StartStoryAsync().Forget(); return; }
            }
            string currentStory = narrative.CurrentStoryId, currentStage = narrative.CurrentStageId;
            UpdateEnvironment(true);
            bool mentorBattle = currentStory == "spotlight_mentor_duel";
            if (visualStory != currentStory || visualStage != currentStage)
            {
                bool localBoss = currentStory == "shop_boss_battle" && narrative.Capture().Parent != null;
                scene.Display(currentStage, IsFormalStory || mentorBattle || localBoss, mentorBattle);
                visualStory = currentStory; visualStage = currentStage;
                quests.SetObjectivePresentation(scene.Config.QuestId, IsFormalStory ? StoryGuideRules.Objective(currentStage, scene.IsHumanStory) : "");
            }
            if (!IsFormalStory) return;
            if (!Blocked && !scene.IsHumanStory && currentStage == "enter_r0188" && !windowShotPlayed && interaction.Actor != null)
            {
                var entrance = scene.Find(currentStage);
                if (entrance != null && (interaction.Actor.Anchor.position - entrance.Position).sqrMagnitude <=
                    scene.Config.InteractionRadius * scene.Config.InteractionRadius)
                { PlayWindowShotAsync(encounter).Forget(); return; }
            }
            if (!Blocked && scene.IsHumanStory && StoryGuideRules.IsAutomaticEntry(currentStage))
            { AdvanceNarrationAsync().Forget(); return; }
            UpdateQuest();
        }
        private void EnvironmentChanged() => environmentDirty = true;
        private void ActiveEnvironmentSceneChanged(Scene previous, Scene next) => environmentDirty = true;
        private void RestoreEnvironment()
        {
            if (environment != null && environment.HasApplied) environment.Restore();
            environmentDirty = true; environmentSession = -1;
        }
        private void UpdateEnvironment(bool allowTransition)
        {
            if (environment == null || !environment.isActiveAndEnabled || environmentTransition || environmentFlowChanging || !narrative.IsReady) return;
            if (!environmentDirty) return;
            // Capture 是深拷贝，只在剧情/会话/绑定发生变化后读取，不放进每帧路径。
            var desired = SpotlightEnvironmentRules.Resolve(narrative.Capture());
            bool switchToLibraryNight = allowTransition && environmentSession == sessionGeneration && environment.HasApplied &&
                environment.CurrentState.Lighting == SpotlightEnvironmentRules.Preset.Day &&
                desired.Lighting == SpotlightEnvironmentRules.Preset.MoonNight &&
                narrative.CurrentStoryId == "spotlight_current" && narrative.CurrentStageId == "enter_r0129";
            if (switchToLibraryNight && (working || crossingMirror || dialogue.IsRunning || narrative.IsBusy || battle.IsBattleRunning)) return;
            environmentDirty = false;
            if (switchToLibraryNight) { ChangeEnvironmentUnderCurtainAsync(environment, desired).Forget(); return; }
            ApplyEnvironment(environment, desired);
        }
        private void ApplyEnvironment(SpotlightEnvironmentView target, SpotlightEnvironmentRules.State state)
        {
            if (target == null) return;
            try { target.Apply(state); environmentSession = sessionGeneration; }
            catch (Exception error) { telemetry.TrackError("environment_apply_failed", error); }
        }
        private async UniTask ChangeEnvironmentUnderCurtainAsync(SpotlightEnvironmentView target, SpotlightEnvironmentRules.State desired)
        {
            environmentTransition = true; working = true;
            long generation = sessionGeneration;
            long operation = ++environmentOperation;
            var owner = flow.Current as MonsterEncounterState;
            bool navigation = owner != null && owner.NavigationBlocked;
            if (owner != null) owner.NavigationBlocked = true;
            bool gameplay = input.Actions.Gameplay.enabled; // lint-ok: 保存并恢复动作图状态，不采样输入。
            IDisposable hold = pause.Acquire(this);
            input.DisableMap(InputService.GameplayMap);
            bool released = false;
            Action release = () => {
                if (released) return;
                released = true;
                try
                {
                    if (gameplay) input.EnableMap(InputService.GameplayMap);
                    if (owner != null) owner.NavigationBlocked = navigation;
                }
                finally { hold.Dispose(); }
            };
            releaseEnvironmentControls = release;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(sessionLifetime.Token, lifetime.Token);
            environmentCancellation = cancellation;
            try
            {
                await curtain.CoverAsync(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (OwnsEnvironmentTransition(operation, generation, owner) && environment == target &&
                    narrative.CurrentStoryId == "spotlight_current" && narrative.CurrentStageId == "enter_r0129")
                    ApplyEnvironment(target, desired);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { telemetry.TrackError("environment_transition_failed", error); }
            finally
            {
                if (OwnsEnvironmentTransition(operation, generation, owner))
                {
                    try
                    {
                        await curtain.RevealAsync(cancellation.Token);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception error) { telemetry.TrackError("environment_reveal_failed", error); }
                    finally
                    {
                        // Reveal 也可能跨帧；切槽或主流程接管后，旧请求不能开输入或改新会话标记。
                        if (OwnsEnvironmentTransition(operation, generation, owner))
                        {
                            release();
                            working = false; environmentTransition = false;
                            environmentDirty = true; environmentSession = -1;
                        }
                    }
                }
                if (ReferenceEquals(releaseEnvironmentControls, release)) releaseEnvironmentControls = null;
                if (ReferenceEquals(environmentCancellation, cancellation)) environmentCancellation = null;
            }
        }
        private bool OwnsEnvironmentTransition(long operation, long generation, MonsterEncounterState owner)
            => owner != null && SpotlightEnvironmentRules.OwnsTransition(operation, environmentOperation,
                generation, sessionGeneration, environmentFlowChanging, ReferenceEquals(flow.Current, owner));
        private void CancelEnvironmentTransition()
        {
            environmentOperation++;
            // 在主流程开始接管之前同步归还本操作关闭的输入，旧 async finally 随后只能空收尾。
            var release = releaseEnvironmentControls; releaseEnvironmentControls = null; release?.Invoke();
            environmentCancellation?.Cancel();
            if (environmentTransition) { environmentTransition = false; working = false; }
        }
        private async UniTask StartStoryAsync()
        {
            working = true; long generation = sessionGeneration;
            try
            {
                await narrative.StartAsync("spotlight_current", scene.MainTarget.TargetId, lifetime.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { telemetry.TrackError("start_failed", error); startHandled = false; }
            finally { if (generation == sessionGeneration) working = false; }
        }
        private async UniTask PlayWindowShotAsync(MonsterEncounterState encounter)
        {
            working = true; windowShotPlayed = true;
            long generation = sessionGeneration;
            bool gameplay = input.Actions.Gameplay.enabled; // lint-ok: 运镜结束成对恢复输入图。
            bool navigationBlocked = encounter.NavigationBlocked;
            encounter.NavigationBlocked = true;
            IDisposable hold = pause.Acquire(this);
            input.DisableMap(InputService.GameplayMap);
            using (var token = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, sessionLifetime.Token))
            {
                try
                {
                    telemetry.Track("window_shot_started");
                    await scene.PlayWindowShotAsync(sceneCamera, token.Token);
                    telemetry.Track("window_shot_completed");
                }
                catch (OperationCanceledException) { if (generation == sessionGeneration) windowShotPlayed = false; }
                catch (Exception error) { telemetry.TrackError("window_shot_failed", error); }
                finally
                {
                    hold.Dispose();
                    if (generation == sessionGeneration)
                    {
                        if (gameplay) input.EnableMap(InputService.GameplayMap);
                        encounter.NavigationBlocked = navigationBlocked;
                        working = false;
                    }
                }
            }
        }
        private async UniTask AdvanceNarrationAsync()
        {
            working = true; long generation = sessionGeneration;
            var token = lifetime.Token;
            var intent = narrative.ResultIntent("Success");
            try
            {
                await narrative.SubmitAsync(intent, token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { telemetry.TrackError("narration_failed", error); }
            finally { if (generation == sessionGeneration) working = false; }
        }
        private async UniTask CrossMirrorAsync()
        {
            crossingMirror = true;
            long generation = sessionGeneration;
            CancellationToken token = sessionLifetime.Token;
            var encounter = (MonsterEncounterState)flow.Current;
            encounter.NavigationBlocked = true;
            bool gameplay = input.Actions.Gameplay.enabled; // lint-ok: 只保存输入图状态，换界后成对恢复。
            IDisposable hold = pause.Acquire(this);
            input.DisableMap(InputService.GameplayMap);
            bool crossed = false;
            try
            {
                OnMirrorCrossingStarted?.Invoke();
                await curtain.CoverAsync(token);
                await encounter.EnterDemonWorldAsync(token);
                token.ThrowIfCancellationRequested();
                Bind(demonBindings);
                if (scene == null || scene.IsHumanStory || shops.World == null) throw new InvalidOperationException("入镜目标未接入妖界五店");
                UpdateEnvironment(false); // 已有换界黑幕内重算，不额外发起第二次转场。
                telemetry.Track("mirror_crossed", ("world", "demon"));
                crossed = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                telemetry.TrackError("mirror_failed", error);
            }
            finally
            {
                OnMirrorCrossingFinished?.Invoke(crossed);
                hold.Dispose();
                if (generation == sessionGeneration)
                {
                    try { await curtain.RevealAsync(CancellationToken.None); }
                    finally
                    {
                        if (gameplay) input.EnableMap(InputService.GameplayMap);
                        encounter.NavigationBlocked = false;
                        crossingMirror = false; working = false;
                    }
                }
            }
        }
        private void UpdateQuest()
        {
            if (!quests.TryGet(scene.Config.QuestId, out var progress) || !progress.HasCurrentObjective) return;
            string key = progress.CurrentObjective.Key;
            if (key == "spotlight.three_shops" && shops.CanEnterTavern || key == "spotlight.tavern_boss" && shops.TavernCompleted)
                quests.Report(QuestObjectiveKind.Counter, key);
        }
        private void Unbind()
        {
            if (scene != null) scene.PresentWangLuye(false, false, false, null, null);
            CancelEnvironmentTransition();
            if (environment != null) environment.OnInvalidated -= EnvironmentChanged;
            RestoreEnvironment(); environment = null;
            if (scene != null) foreach (var point in scene.Points) { interaction.Unregister(point); point.Bind(scene.Config, null, null); }
            lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null; scene = null;
            quests.SetObjectivePresentation(0, "");
            working = false; visualStory = visualStage = "";
        }
        public void Dispose()
        {
            dialogue.OnStarted -= DialogueStarted; dialogue.OnEnded -= DialogueEnded;
            sessionSubscription?.Dispose(); SceneManager.sceneLoaded -= Loaded; SceneManager.sceneUnloaded -= Unloaded; Unbind();
            narrative.OnChanged -= EnvironmentChanged; environmentViews.Clear();
            stateChangingSubscription?.Dispose(); stateChangedSubscription?.Dispose();
            SceneManager.activeSceneChanged -= ActiveEnvironmentSceneChanged;
            sessionLifetime.Cancel(); sessionLifetime.Dispose();
        }
        private void DialogueStarted(DialogueStartedEvent started) => archiveDialogue = started.DialogueId == 21007 || started.DialogueId == 21014;
        private void DialogueEnded(DialogueEndedEvent ended)
        {
            archiveDialogue = false;
            if (scene != null) scene.PresentWangLuye(false, false, false, null, null);
        }
    }
}
