// 职责：正式剧情的场景接线、真实操作回写和软提示；剧情图仍只在 NarrativeService。
// 现有 NarrativeService 不应认识五店细节，ShopPlaytest 不应拥有序章，故新增章节适配器。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Battle;
using Game.Core.Flow;
using Game.Core.Input;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Dialogue;
using Game.Interaction;
using Game.Loot;
using Game.Monster;
using Game.Quest;
using Game.Rhythm;
using Game.Session;
using Game.ShopPlaytest;
using MessagePipe;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using VContainer.Unity;
namespace Game.Narrative
{
    public sealed class SpotlightStoryFlow : IStartable, ITickable, IDisposable
    {
        private readonly NarrativeService narrative;
        private readonly ShopPlaytestState shops;
        private readonly IGameFlow flow;
        private readonly IUIService ui;
        private readonly IInputService input;
        private readonly IWorldPauseService pause;
        private readonly IInteractionRegistry interaction;
        private readonly QuestService quests;
        private readonly LootService loot;
        private readonly DialogueService dialogue;
        private readonly BattleFlow battle;
        private readonly RhythmState rhythm;
        private readonly ISubscriber<SessionStartedEvent> sessions;
        private readonly ITelemetryScope telemetry;
        private readonly EncounterStep step;
        private readonly ILoadingCurtain curtain;
        private IDisposable sessionSubscription;
        private SpotlightSceneBindings scene;
        private SpotlightSceneBindings humanBindings, demonBindings;
        private StoryGuideHudView hud;
        private CancellationTokenSource lifetime;
        private CancellationTokenSource sessionLifetime = new CancellationTokenSource();
        private bool crossingMirror;
        private long sessionGeneration;
        private bool newGame, startHandled, working, hudOpening, inGuideRange;
        private string rangeStage = "", lastStage = "", lastRaw = "";
        private string visualStory = "", visualStage = "";
        private string cachedRequirement, cachedRequirementHint;
        private StoryGuideRules.Action lastAction;
        public SpotlightSceneBindings Bindings => scene;
        public bool IsFormalStory => narrative.CurrentStoryId == "spotlight_current";
        public bool CanSave => !crossingMirror && !working && (!newGame || startHandled);
        public SpotlightStoryFlow(NarrativeService narrative, ShopPlaytestState shops, IGameFlow flow, IUIService ui,
            IInputService input, IWorldPauseService pause, IInteractionRegistry interaction, QuestService quests,
            LootService loot, DialogueService dialogue, BattleFlow battle, RhythmState rhythm,
            ISubscriber<SessionStartedEvent> sessions, ITelemetryService telemetry, EncounterStep step, ILoadingCurtain curtain)
        {
            this.narrative = narrative; this.shops = shops; this.flow = flow; this.ui = ui; this.input = input;
            this.pause = pause; this.interaction = interaction; this.quests = quests; this.loot = loot;
            this.dialogue = dialogue; this.battle = battle; this.rhythm = rhythm; this.sessions = sessions;
            this.telemetry = telemetry.Scope("spotlight");
            this.step = step; this.curtain = curtain;
        }
        public void Start()
        {
            sessionSubscription = sessions.Subscribe(start => {
                sessionLifetime.Cancel(); sessionLifetime.Dispose(); sessionLifetime = new CancellationTokenSource();
                crossingMirror = false; working = false;
                sessionGeneration++; newGame = start.IsNewGame; startHandled = false;
            });
            SceneManager.sceneLoaded += Loaded; SceneManager.sceneUnloaded += Unloaded;
            for (int i = 0; i < SceneManager.sceneCount; i++) Loaded(SceneManager.GetSceneAt(i), LoadSceneMode.Additive);
        }
        private void Loaded(Scene loaded, LoadSceneMode mode)
        {
            foreach (var root in loaded.GetRootGameObjects())
            {
                foreach (var bindings in root.GetComponentsInChildren<SpotlightSceneBindings>(true))
                    if (bindings.IsHumanStory) humanBindings = bindings; else demonBindings = bindings;
            }
        }
        private void Bind(SpotlightSceneBindings bindings)
        {
            Unbind(); scene = bindings;
            if (scene == null) return;
            lifetime = new CancellationTokenSource();
            if (scene.Config == null || scene.MainTarget == null || scene.MentorTarget == null) throw new InvalidOperationException("正式剧情场景引用未接齐");
            foreach (var point in scene.Points) { point.Bind(scene.Config, Allowed, Interact, scene.IsHumanStory); interaction.Register(point); }
        }
        private void Unloaded(Scene unloaded)
        {
            if (scene != null && scene.gameObject.scene == unloaded) Unbind();
            if (humanBindings != null && humanBindings.gameObject.scene == unloaded) humanBindings = null;
            if (demonBindings != null && demonBindings.gameObject.scene == unloaded) demonBindings = null;
        }
        private bool Blocked => crossingMirror || working || dialogue.IsRunning || narrative.IsBusy || battle.IsBattleRunning || rhythm.IsActivityOpen || pause.IsPaused;
        private bool Allowed(SpotlightSceneBindings.Point point) => scene != null && IsFormalStory && !Blocked &&
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
                NarrativeIntent intent = narrative.ResultIntent("Success");
                await narrative.SubmitAsync(intent, token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { telemetry.TrackError("interaction_failed", error); }
            finally { if (generation == sessionGeneration) working = false; }
        }
        public void Tick()
        {
            if (!(flow.Current is MonsterEncounterState encounter) || !step.IsActive || !narrative.IsReady) return;
            var bindings = encounter.IsHumanWorld ? humanBindings : demonBindings;
            if (scene != bindings) Bind(bindings);
            if (scene == null) return;
            if (!startHandled)
            {
                startHandled = true;
                if (newGame && narrative.CurrentStoryId.Length == 0) { StartStoryAsync().Forget(); return; }
            }
            string currentStory = narrative.CurrentStoryId, currentStage = narrative.CurrentStageId;
            bool mentorBattle = currentStory == "spotlight_mentor_duel";
            if (visualStory != currentStory || visualStage != currentStage)
            {
                bool localBoss = currentStory == "shop_boss_battle" && narrative.Capture().Parent != null;
                scene.Display(currentStage, IsFormalStory || mentorBattle || localBoss, mentorBattle);
                visualStory = currentStory; visualStage = currentStage;
                quests.SetObjectivePresentation(scene.Config.QuestId, IsFormalStory ? StoryGuideRules.Objective(currentStage, scene.IsHumanStory) : "");
            }
            if (!IsFormalStory) { if (hud != null) hud.Show(""); return; }
            if (!Blocked && scene.IsHumanStory && StoryGuideRules.IsAutomaticEntry(currentStage))
            { AdvanceNarrationAsync(currentStage).Forget(); return; }
            if (hud == null && !hudOpening) OpenHudAsync().Forget();
            UpdateQuest(); UpdateGuide();
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
        private async UniTask AdvanceNarrationAsync(string stage)
        {
            working = true; long generation = sessionGeneration;
            var token = lifetime.Token;
            var intent = narrative.ResultIntent("Success");
            try
            {
                // 以短暂淡出衔接旧教学段，不再要求玩家打赢额外切磋。
                if (stage == "action_r0070" && curtain != null)
                { await curtain.CoverAsync(token); await curtain.RevealAsync(CancellationToken.None); }
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
            try
            {
                await curtain.CoverAsync(token);
                await encounter.EnterDemonWorldAsync(token);
                token.ThrowIfCancellationRequested();
                Bind(demonBindings);
                if (scene == null || scene.IsHumanStory || shops.World == null) throw new InvalidOperationException("入镜目标未接入妖界五店");
                telemetry.Track("mirror_crossed", ("world", "demon"));
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                telemetry.TrackError("mirror_failed", error);
            }
            finally
            {
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
        private async UniTask OpenHudAsync()
        {
            hudOpening = true; var bindings = scene; CancellationToken token = lifetime.Token;
            try
            {
                var opened = await ui.OpenAsync<StoryGuideHudView>(null, token);
                if (scene == bindings && !token.IsCancellationRequested) { hud = opened; lastStage = ""; }
                else await ui.CloseAsync(opened);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { telemetry.TrackError("guide_failed", error); }
            finally { if (scene == bindings) hudOpening = false; }
        }
        private void UpdateQuest()
        {
            if (!quests.TryGet(scene.Config.QuestId, out var progress) || !progress.HasCurrentObjective) return;
            string key = progress.CurrentObjective.Key;
            if (key == "spotlight.three_shops" && shops.CanEnterTavern || key == "spotlight.tavern_boss" && shops.TavernCompleted)
                quests.Report(QuestObjectiveKind.Counter, key);
        }
        private void UpdateGuide()
        {
            if (hud == null) return;
            string stage = narrative.CurrentStageId;
            var point = scene.Find(stage);
            bool blocked = Blocked || !input.Actions.Gameplay.enabled || !ui.IsLayerVisible(UILayer.Hud) || shops.HasPendingReply; // lint-ok: 只读动作图启用状态控制提示显隐，不采样玩法输入。
            StoryGuideRules.Action action = StoryGuideRules.Action.None;
            string text = "";
            if (stage == "action_r0197" && shops.World != null)
            {
                int shop = shops.ActiveShop;
                bool quotaReady = shops.Forging.Available >= shops.Forging.ProductCost || shops.Forging.IsForging || shops.Forging.CanSubmit;
                string required = StoryGuideRules.RequiredRole(shop, shops.Theater.Entered, shops.Theater.PatrolRedirected, quotaReady);
                int skin = ShopSkinRules.ItemId(required);
                bool owns = skin != 0 && loot.Items.TryGetValue(skin, out int count) && count > 0;
                bool completed = shop == 0 ? shops.Theater.Completed : shop == 1 ? shops.Clothing.Received : shop == 2 ? shops.Forging.Submitted : shops.TavernCompleted;
                action = StoryGuideRules.ShopAction(shop, shops.CurrentRole, owns, shops.Theater.Entered, shops.Theater.PatrolRedirected,
                    completed, quotaReady, shops.CanChallengeTavern);
                if (shop == 4 && !shops.CanEnterTavern)
                {
                    action = StoryGuideRules.Action.None;
                    string requirement = shops.TavernEntryRequirement;
                    if (cachedRequirement != requirement)
                    { cachedRequirement = requirement; cachedRequirementHint = "还缺：" + requirement + "。三家店可任意顺序完成。"; }
                    text = cachedRequirementHint;
                }
                else text = scene.Config.HintFor(action);
            }
            else if (point != null)
            {
                if (rangeStage != stage) { rangeStage = stage; inGuideRange = false; }
                float radius = scene.Config.GuideRadius + (inGuideRange ? scene.Config.ExitPadding : 0f);
                inGuideRange = interaction.Actor != null && (interaction.Actor.Anchor.position - point.Position).sqrMagnitude <= radius * radius;
                action = StoryGuideRules.Select(true, inGuideRange, false, blocked, StoryGuideRules.Action.Interact);
                if (stage == "enter_r0293") text = "";
                else if (action != StoryGuideRules.Action.None) text = stage == "enter_r0171"
                    ? scene.IsHumanStory ? "靠近镜面，按 {Interact} 穿越。" : "靠近轮回堂值守，按 {Interact} 问询。"
                    : scene.Config.Find(stage).Hint;
            }
            if (blocked) text = "";
            if (lastStage == stage && lastAction == action && lastRaw == text) return;
            lastRaw = text;
            // 只在提示变化时生成键位文案；HUD 自己也去重，不逐帧重建文字或查询绑定。
            if (text.Length > 0) text = text.Replace("{Interact}", Key(input.Actions.Gameplay.Interact)).Replace("{Sneak}", Key(input.Actions.Gameplay.Sneak)) // lint-ok: 只格式化绑定显示名，不读输入值。
                .Replace("{Secondary}", Key(input.Actions.Gameplay.Execute)).Replace("{Inventory}", Key(input.Actions.Gameplay.Inventory)); // lint-ok: 只格式化绑定显示名，不读输入值。
            hud.Show(text); lastStage = stage; lastAction = action;
        }
        private static string Key(InputAction action)
        {
            for (int i = 0; i < action.bindings.Count; i++) if (!action.bindings[i].isComposite && !action.bindings[i].isPartOfComposite) return action.GetBindingDisplayString(i);
            return action.name;
        }
        private void Unbind()
        {
            if (scene != null) foreach (var point in scene.Points) { interaction.Unregister(point); point.Bind(scene.Config, null, null); }
            lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null; scene = null;
            quests.SetObjectivePresentation(0, "");
            if (hud != null) { hud.Show(""); ui.CloseAsync(hud).Forget(); hud = null; }
            working = false; hudOpening = false; inGuideRange = false; rangeStage = lastStage = visualStory = visualStage = "";
        }
        public void Dispose()
        {
            sessionSubscription?.Dispose(); SceneManager.sceneLoaded -= Loaded; SceneManager.sceneUnloaded -= Unloaded; Unbind();
            sessionLifetime.Cancel(); sessionLifetime.Dispose();
        }
    }
}
