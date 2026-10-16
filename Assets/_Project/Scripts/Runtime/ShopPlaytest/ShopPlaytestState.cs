// 职责：把各店独立玩法接到现有遭遇；只处理交互、身份和结果，不拥有场景或第二个移动驱动。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Battle;
using Game.Clothing;
using Game.Core.Flow;
using Game.Core.Input;
using Game.Core.UI;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Dialogue;
using Game.Forging;
using Game.Identity;
using Game.Interaction;
using Game.Monster;
using Game.Narrative;
using Game.Player;
using Game.Rhythm;
using Game.Taming;
using Game.Tavern;
using Game.Theater;
using Game.Session;
using MessagePipe;
using UnityEngine;
using VContainer.Unity;
namespace Game.ShopPlaytest
{
    public sealed class ShopPlaytestState : ITickable, IStartable, IDisposable
    {
        private static readonly string[] Titles = { "戏班", "衣肆", "金银行", "客栈", "酒肆" };
        private static readonly string[][] Commands = {
            new[] { "劳烦核对一下名册。", "这边由我照看，你到别处看看。", "我来奏上一曲。" },
            new[] { "", "", "", "那就劳烦您拿给我。", "那就劳烦您拿给我。", "那就劳烦您拿给我。" },
            new[] { "请按文书把金属补足。" },
            new[] { "想向您打听点事。" },
            new[] { "给我来一杯。", "请你喝一杯。", "请你喝一杯。", "我们来较量一下。", "请你喝一杯。" }
        };
        private readonly ISaveService saves;
        private readonly ISubscriber<SessionStartedEvent> sessionStarted;
        private IDisposable sessionSubscription;
        private long sessionGeneration, boundGeneration;
        private readonly IGameFlow flow;
        private readonly IWorldPauseService pause;
        private readonly IUIService ui;
        private readonly IInputService input;
        private readonly IInteractionRegistry interaction;
        private readonly InteractionFocus interactionFocus;
        private readonly PlayerModel player;
        private readonly EncounterStep step;
        private readonly RhythmState rhythm;
        private readonly BattleFlow battle;
        private readonly BattleSetup battleSetup;
        private readonly NarrativeService narrative;
        private readonly DialogueService dialogue;
        private readonly TheaterConfig theaterConfig;
        private readonly ClothingController clothing;
        private readonly ForgingController forging;
        private readonly TavernController tavern;
        private readonly TheaterController theater;
        private readonly IdentityRules identityRules;
        private readonly IdentityState identity;
        private readonly IdentityLedger ledger;
        private readonly ITelemetryScope telemetry;
        private ShopPlaytestWorld world;
        private EncounterSceneView encounterView;
        private readonly Queue<string> replies = new Queue<string>();
        private ShopPlaytestActor conversationActor;
        private ForgingView forgingView;
        // 卸载等结束信号；活动本身由Forget观察，不能再并发await同一个UniTask。
        private UniTaskCompletionSource forgingClosed;
        private string role = "";
        private bool openingActivity, awaitingBattle, tavernCompleted, awaitingOutcome;
        private int closeRhythmAfterFrame = -1;
        private long runNumber, expectedBattleGeneration;
        private CancellationTokenSource lifetime;
        private bool progressReady;
        public int ActiveShop => world == null ? -1 : world.ActiveShop;
        public string CurrentRole => identity.IsInEffect && identity.Current.Value == role ? role : "";
        public ClothingRules Clothing => clothing.Rules;
        public ForgingRules Forging => forging.Rules;
        public TavernDrunkRules Tavern => tavern.Rules;
        public TheaterController Theater => theater;
        public TamingRules Taming => step.Taming;
        public ShopPlaytestWorld World => world;
        public bool HasPendingReply => replies.Count > 0;
        public Vector2 ControlledPosition => Taming.IsControllingEnemy ? Taming.GetTarget(Taming.CurrentControlId).Model.Position : player.Position;
        private Vector3 ControlledScenePosition => encounterView.ToScenePosition(ControlledPosition);
        private bool Busy => openingActivity || rhythm.IsActivityOpen || battle.IsBattleRunning || narrative.IsBusy || dialogue.IsRunning || awaitingBattle || pause.IsPaused;
        public ShopPlaytestState(ISaveService saves, ISubscriber<SessionStartedEvent> sessionStarted, IGameFlow flow, IWorldPauseService pause, IUIService ui, IInputService input, IInteractionRegistry interaction, InteractionFocus interactionFocus,
            PlayerModel player, EncounterStep step, RhythmState rhythm, BattleFlow battle, BattleSetup battleSetup,
            NarrativeService narrative, DialogueService dialogue, TheaterConfig theaterConfig, ClothingController clothing,
            ForgingController forging, TavernController tavern, TheaterController theater, IdentityRules identityRules,
            IdentityState identity, IdentityLedger ledger, ITelemetryService telemetry)
        {
            this.saves = saves; this.sessionStarted = sessionStarted; this.flow = flow; this.pause = pause; this.interaction = interaction;
            this.ui = ui; this.input = input;
            this.interactionFocus = interactionFocus;
            this.player = player; this.step = step; this.rhythm = rhythm; this.battle = battle;
            this.battleSetup = battleSetup; this.narrative = narrative; this.dialogue = dialogue;
            this.theaterConfig = theaterConfig; this.clothing = clothing; this.forging = forging;
            this.tavern = tavern; this.theater = theater; this.identityRules = identityRules;
            this.identity = identity; this.ledger = ledger; this.telemetry = telemetry.Scope("shopplaytest");
        }
        public void Start() => sessionSubscription = sessionStarted.Subscribe(_ => sessionGeneration++);
        public void Dispose() { sessionSubscription?.Dispose(); sessionSubscription = null; }
        public UniTask BindAsync(ShopPlaytestWorld scene, EncounterSceneView existingView, CancellationToken ct)
        {
            world = scene; encounterView = existingView;
            boundGeneration = sessionGeneration;
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
            RestoreProgress();
            FindActor("shop_boss").gameObject.SetActive(!tavernCompleted);
            foreach (ShopPlaytestActor actor in world.Actors)
            { actor.Bind(Interact, CanInteract, BorrowIdentity, selected => interactionFocus.RequestFocus(selected)); interaction.Register(actor); }
            Taming.OnControlChanged += ControlChanged;
            step.OnAdvanced += AdvanceIdentity;
            Taming.BindSelection(CanSelect);
            Taming.BindModifiers(AwarenessMultiplier, ActorSpeed);
            step.BindPlayerModifiers(() => tavern.Rules.SpeedMultiplier, () => tavern.Rules.CanSneak);
            battleSetup.BindDrunkSource(BossDrunk);
            narrative.OnChanged += NarrativeChanged;
            ControlChanged(Taming.CurrentControlId);
            return UniTask.CompletedTask;
        }
        private ShopPlaytestActor FindActor(string id)
        {
            if (world == null) return null;
            foreach (ShopPlaytestActor actor in world.Actors) if (actor != null && actor.StableId == id) return actor;
            return null;
        }
        // 旧场景仍引用衣肆柜台锚点，交谈统一由现场裁衣匠承接。
        private bool CanInteract(ShopPlaytestActor actor) => world != null && actor.StableId != "shop_station_1" && !Busy && !HasPendingReply && actor.Shop == ActiveShop &&
            actor.IsVisible && (actor.Role.Length == 0 || (actor.StableId != Taming.CurrentControlId && Taming.IsAvailable(actor.StableId)));
        private bool CanSelect(string id)
        {
            ShopPlaytestActor actor = FindActor(id);
            return actor == null || (actor.Role.Length > 0 && actor.IsVisible && actor.Shop == ActiveShop &&
                Vector2.Distance(ControlledPosition, Taming.GetTarget(id).Model.Position) <= actor.InteractionRadius);
        }
        private void Interact(ShopPlaytestActor actor)
        {
            if (!CanInteract(actor) || Vector3.Distance(ControlledScenePosition, actor.Position) > actor.InteractionRadius) return;
            ConverseAsync(actor).Forget();
        }
        public void ReturnBody()
        { if (world != null && !Busy) encounterView.RequestControl(Taming.PlayerId); }
        private void BorrowIdentity(ShopPlaytestActor actor)
        {
            if (!actor.CanInteractSecondary || !CanInteract(actor) ||
                Vector3.Distance(ControlledScenePosition, actor.Position) > actor.InteractionRadius) return;
            if (!encounterView.RequestControl(actor.StableId, true)) Show("当前不能借用这个人物");
        }
        private void ControlChanged(string id)
        {
            ShopPlaytestActor actor = FindActor(id);
            string nextRole = actor == null ? "" : actor.Role;
            if (nextRole.Length > 0)
            {
                IdentityEnterResult result = identityRules.TryEnter(identity, ledger, IdentityId.From(nextRole));
                if (result != IdentityEnterResult.Entered && result != IdentityEnterResult.AlreadyBorrowing)
                { role = ""; Taming.TryControl(Taming.PlayerId); Show("身份借用失败：" + result); return; }
            }
            else if (identity.IsBorrowing && IsShopRole(identity.Current.Value)) identityRules.TryExit(identity);
            role = nextRole;
            if (interaction.Actor != null) interaction.Actor.SetAnchor(encounterView.CurrentControlObject);
            int next = world.ShopAt(ControlledScenePosition);
            if (ActiveShop == 4 && next != 4) { tavern.Apply(TavernAction.Leave, CurrentRole); Persist(); }
            world.Display(next, id);
        }
        private int? BossDrunk(string id)
        {
            if (world == null || boundGeneration != sessionGeneration || id != "sample_boss" || !narrative.IsReady) return null;
            var current = narrative.Capture().Current;
            return current != null && current.TargetId == "shop_boss" ? (int?)tavern.Rules.BossDrunk : null;
        }
        private bool IsShopRole(string id)
        {
            foreach (ShopPlaytestActor actor in world.Actors) if (actor.Role.Length > 0 && actor.Role == id) return true;
            return false;
        }
        private void AdvanceIdentity(float deltaTime)
        {
            if (world == null || boundGeneration != sessionGeneration) return;
            bool owned = role.Length > 0 && identity.IsBorrowing && identity.Current.Value == role;
            IdentityExitReason reason = identityRules.AdvanceIdentity(identity, deltaTime);
            if (owned && reason != IdentityExitReason.None) Taming.TryControl(Taming.PlayerId);
        }
        private float AwarenessMultiplier(string id) => id == "shop_boss" ? tavern.Rules.BossAwarenessMultiplier
            : id == "shop_customer_a" || id == "shop_customer_b" ? tavern.Rules.CustomerAwareness(id) : 1f;
        private float ActorSpeed(string id) => id == Taming.CurrentControlId ? tavern.Rules.SpeedMultiplier :
            id == "shop_boss" ? tavern.Rules.BossSpeedMultiplier :
            id == "shop_customer_a" || id == "shop_customer_b" ? tavern.Rules.CustomerSpeed(id) : 1f;
        public static int[] CommandsFor(string id, string currentRole, bool isForging, int nextPoint)
        {
            switch (id)
            {
                case "shop_customer0": case "shop_customer1": case "shop_customer2": return Array.Empty<int>();
                case "shop_tailor":
                    if (currentRole == "customer0") return new[] { 3 };
                    if (currentRole == "customer1") return new[] { 4 };
                    if (currentRole == "customer2") return new[] { 5 };
                    return Array.Empty<int>();
                case "shop_apprentice": case "shop_musician": return Array.Empty<int>();
                case "shop_director": return currentRole == "musician" ? new[] { 2 } : Array.Empty<int>();
                case "shop_patrol": return currentRole == "apprentice" ? new[] { 0 } : currentRole == "director" ? new[] { 1 } : Array.Empty<int>();
                case "shop_station_0":
                    return currentRole == "apprentice" ? new[] { 0 } : currentRole == "director" ? new[] { 1 }
                        : currentRole == "musician" ? new[] { 2 } : Array.Empty<int>();
                case "shop_officer": return currentRole == "officer" ? new[] { 0 } : Array.Empty<int>();
                case "shop_smith": case "shop_station_2":
                    if (currentRole == "officer") return new[] { 0 };
                    return Array.Empty<int>();
                case "shop_innkeeper": case "shop_station_3": return new[] { 0 };
                case "shop_boss": return currentRole == "guest" ? new[] { 1, 3 } : Array.Empty<int>();
                case "shop_customer_a": return currentRole == "guest" ? new[] { 2 } : Array.Empty<int>();
                case "shop_customer_b": return currentRole == "guest" ? new[] { 4 } : Array.Empty<int>();
                case "shop_guest": case "shop_station_4": return currentRole == "guest" ? new[] { 0 } : Array.Empty<int>();
                default: return Array.Empty<int>();
            }
        }

        private static DialogueContent Conversation(string id, string speaker, string text, DialogueContent.Choice[] choices)
        {
            // shortcut: 五店占位台词仍在接线代码中，正式文案定稿后迁到已有对白内容表。
            return new DialogueContent("shop:" + id, "line", new[] {
                new DialogueContent.Node { Id = "line", SpeakerName = speaker, Text = text, Next = "choices" },
                new DialogueContent.Node { Id = "choices", Kind = DialogueContent.NodeKind.Choice,
                    SpeakerName = speaker, Text = text, Choices = choices }
            });
        }

        private async UniTask ConverseAsync(ShopPlaytestActor actor)
        {
            var scene = world;
            CancellationToken token = lifetime.Token;
            string recognizedRole = CurrentRole;
            var choices = new List<DialogueContent.Choice>();
            foreach (int command in CommandsFor(actor.StableId, CurrentRole, forging.Rules.IsForging, forging.Rules.NextPoint))
            {
                if (actor.Shop == 1 && !clothing.OffersClothes(CurrentRole)) continue;
                choices.Add(new DialogueContent.Choice { Id = "command" + command,
                    Text = Commands[actor.Shop][command], Outcome = "command" + command });
            }
            if (CurrentRole == "smith" && (actor.StableId == "shop_smith" || actor.StableId == "shop_station_2"))
                choices.Add(new DialogueContent.Choice { Id = "forge", Text = "开始打造。", Outcome = "forge" });
            if (Taming.IsControllingEnemy)
                choices.Add(new DialogueContent.Choice { Id = "body", Text = "回到本体", Outcome = "body" });
            choices.Add(new DialogueContent.Choice { Id = "leave", Text = "告辞", Outcome = "leave" });
            string greeting = Greeting(actor);
            try
            {
                DialogueResult result = await dialogue.PlayAsync(Conversation(actor.StableId, actor.DisplayName, greeting, choices.ToArray()), actor.transform, actor.StableId, token);
                if (token.IsCancellationRequested || string.IsNullOrEmpty(result.Outcome) || world != scene || CurrentRole != recognizedRole || !CanInteract(actor) ||
                    Vector3.Distance(ControlledScenePosition, actor.Position) > actor.InteractionRadius) return;
                conversationActor = actor;
                if (actor.StableId == "shop_tailor")
                {
                    int customer = clothing.CustomerFor(recognizedRole);
                    if (customer >= 0) { clothing.Apply(customer, false, recognizedRole); Persist(); }
                }
                if (result.Outcome == "body") ReturnBody();
                else if (result.Outcome == "forge")
                    OpenForgeAsync(actor).Forget();
                else if (result.Outcome.StartsWith("command", StringComparison.Ordinal) && int.TryParse(result.Outcome.Substring(7), out int command))
                    ExecuteCommand(command, actor);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { telemetry.TrackError("conversation_failed", error); }
        }
        private string Greeting(ShopPlaytestActor actor)
        {
            if (actor.StableId == "shop_tailor") return clothing.TailorReply(CurrentRole);
            string line = actor.StableId == "shop_customer0" ? clothing.CustomerGreeting(0)
                : actor.StableId == "shop_customer1" ? clothing.CustomerGreeting(1)
                : actor.StableId == "shop_customer2" ? clothing.CustomerGreeting(2) : GreetingFor(actor.StableId, CurrentRole);
            return CurrentRole.Length == 0 || actor.StableId.StartsWith("shop_station_", StringComparison.Ordinal)
                ? line : Taming.GetDisplayName(Taming.CurrentControlId) + "，" + line;
        }
        public static string GreetingFor(string id, string currentRole)
        {
            switch (id)
            {
                case "shop_apprentice": case "shop_patrol": case "shop_director": case "shop_musician": case "shop_station_0":
                    return currentRole == "apprentice" ? "来核对入场名册吧？" : currentRole == "director" ? "巡逻的事，听您安排。"
                        : currentRole == "musician" ? "戏台备好了，请奏一曲吧。"
                        : id == "shop_apprentice" ? "我还得去验身，您有什么事？" : id == "shop_patrol" ? "我正在巡逻，您有什么事？"
                        : id == "shop_director" ? "戏班的事由我安排，您有什么事？" : id == "shop_musician" ? "我还在练曲，您有什么事？"
                        : "戏班今日还在排练，您有什么事？";
                case "shop_officer": return currentRole == "smith" ? "金属若不足，需按文书核准后再领。" : "拨料的文书都在这里。";
                case "shop_smith": case "shop_station_2": return currentRole == "officer" ? "文书上的金属份额，需要补足吗？"
                    : currentRole == "smith" ? "打造台已经备好，可以开工了。" : "来看看金银行的工件吗？";
                case "shop_innkeeper": case "shop_station_3": return "客官，请进，有什么想问的？";
                case "shop_guest": return "来喝一杯？";
                case "shop_boss": return currentRole == "guest" ? "又来了？一起喝一杯，还是较量一下？" : "找我有什么事？";
                case "shop_customer_a": case "shop_customer_b": return currentRole == "guest" ? "你也来喝酒了？一起坐吧。" : "我在这里歇歇脚。";
                case "shop_station_4": return currentRole == "guest" ? "酒已备好，来一杯吧。" : "客官，想喝点什么？";
                default: return "找我有什么事？";
            }
        }
        private void ExecuteCommand(int index, ShopPlaytestActor actor)
        {
            if (world == null || ActiveShop < 0 || Busy || index < 0 || index >= Commands[ActiveShop].Length ||
                actor == null || actor.Shop != ActiveShop || !actor.IsVisible ||
                Vector3.Distance(ControlledScenePosition, actor.Position) > actor.InteractionRadius)
            { telemetry.TrackWarn("command_rejected", TelemetryProps.Of(("reason", "unavailable_or_distance"), ("command", index))); return; }
            string message = "";
            switch (ActiveShop)
            {
                case 0:
                    if (index == 2) { PerformAsync().Forget(); return; }
                    var intent = new TheaterIntent(index == 0 ? TheaterAction.Verify : TheaterAction.Redirect, CurrentRole);
                    bool redirected = theater.PatrolRedirected;
                    message = theater.Apply(in intent);
                    if (!redirected && theater.PatrolRedirected)
                        Taming.GetTarget("shop_patrol").ChangePatrolRoute(world.Route(encounterView, true));
                    break;
                case 1: message = clothing.Apply(index % 3, index >= 3, CurrentRole); break;
                case 2:
                    if (index != 0) return;
                    message = forging.Apply(ForgingAction.SetQuota, forging.MaximumQuota, CurrentRole); break;
                case 3: message = "客栈的正式线索还未配置。"; break;
                case 4:
                    string targetId = index == 1 || index == 3 ? "shop_boss" : index == 4 ? "shop_customer_b" : "shop_customer_a";
                    if (index != 0 && !NearDrinkTarget(targetId)) { Show("走近对应酒客或BOSS再操作"); return; }
                    if (index == 3) { StartBattleAsync().Forget(); return; }
                    message = tavern.Apply(index == 0 ? TavernAction.DrinkPlayer : index == 1 ? TavernAction.DrinkBoss :
                        TavernAction.DrinkCustomer, CurrentRole, index == 4 ? "shop_customer_b" : "shop_customer_a"); break;
            }
            Persist(); Show(message);
        }
        public void Retry()
        {
            if (!Debug.isDebugBuild || world == null || boundGeneration != sessionGeneration || ActiveShop < 0 || Busy ||
                !world.NearStation(ActiveShop, ControlledScenePosition)) return;
            switch (ActiveShop)
            {
                case 0:
                    theater.Reset();
                    Taming.GetTarget("shop_patrol").ChangePatrolRoute(world.Route(encounterView, false));
                    break;
                case 1: clothing.Reset(); break;
                case 2: forging.Reset(); break;
                case 4:
                    tavern.Reset(); tavernCompleted = false;
                    foreach (string id in new[] { "shop_boss", "shop_customer_a", "shop_customer_b" })
                    {
                        ShopPlaytestActor actor = FindActor(id);
                        Taming.GetTarget(id).Reset(actor.GetComponent<TamingActor>().PatrolPositions(encounterView));
                        actor.gameObject.SetActive(true);
                    }
                    break;
            }
            world.Display(ActiveShop, Taming.CurrentControlId);
            Persist(); Show("已重试本店；其它店成果保留。调试重试不回退剧情标记或战斗掉落。");
            telemetry.Track("local_retry", ("shop", ActiveShop));
        }
        private bool NearDrinkTarget(string id)
        {
            ShopPlaytestActor actor = FindActor(id);
            bool allowed = actor != null && actor.isActiveAndEnabled && actor.IsVisible && Taming.IsAvailable(id) &&
                Taming.GetTarget(id).Model.Health > 0 && Vector2.Distance(ControlledPosition, Taming.GetTarget(id).Model.Position) <= actor.InteractionRadius;
            if (!allowed) telemetry.TrackWarn("drink_target_rejected", TelemetryProps.Of(("id", id), ("reason", "unavailable_or_distance")));
            return allowed;
        }
        private async UniTask OpenForgeAsync(ShopPlaytestActor actor)
        {
            if (CurrentRole != "smith")
            { telemetry.TrackWarn("forging_entry_rejected", TelemetryProps.Of(("role", CurrentRole), ("reason", "not_smith"))); Show("先借用金银匠身份，再使用打造台"); return; }
            openingActivity = true;
            var closed = new UniTaskCompletionSource();
            forgingClosed = closed;
            var scene = world;
            CancellationToken token = lifetime.Token;
            bool gameplayEnabled = input.Actions.Gameplay.enabled; // lint-ok: 只保存动作图启用状态，退出时恢复，不采样玩法按键。
            IDisposable paused = pause.Acquire(this);
            input.DisableMap("Gameplay");
            try
            {
                string Apply(ForgingAction action, int value)
                {
                    if (token.IsCancellationRequested || world != scene || boundGeneration != sessionGeneration ||
                        ActiveShop != 2 || actor == null || !actor.IsVisible || CurrentRole != "smith" ||
                        Vector3.Distance(ControlledScenePosition, actor.Position) > actor.InteractionRadius)
                    {
                        telemetry.TrackWarn("forging_command_rejected", TelemetryProps.Of(("action", (int)action), ("role", CurrentRole), ("reason", "session_or_actor_unavailable")));
                        return "当前不能使用打造台，请返回后重新交互";
                    }
                    string message = forging.Apply(action, value, CurrentRole);
                    Persist();
                    return message;
                }
                forgingView = await ui.OpenAsync<ForgingView>((forging, (Func<ForgingAction, int, string>)Apply), token);
                await forgingView.WaitAsync(token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { telemetry.TrackError("forging_view_failed", error); Show("打造界面未打开，请重试"); }
            finally
            {
                // 外部Esc关闭会在OnClose内唤醒等待者，先让UIService完成出栈，避免重入关闭。
                await UniTask.Yield();
                try { if (forgingView != null && ui.Get<ForgingView>() == forgingView) await ui.CloseAsync(forgingView, CancellationToken.None); }
                finally
                {
                    try
                    {
                        forgingView = null; paused.Dispose();
                        if (gameplayEnabled) input.EnableMap("Gameplay");
                    }
                    finally { openingActivity = false; forgingClosed = null; closed.TrySetResult(); }
                }
            }
        }
        private async UniTask PerformAsync()
        {
            if (!theater.CanPerform(CurrentRole)) { Show("先借学徒验身、都知调巡逻，再控制乐正演奏"); return; }
            openingActivity = true;
            string context = "main-theater-" + ++runNumber;
            var policy = new RhythmFixedChartCombatPolicy(theaterConfig.Chart.ChartId, theaterConfig.Chart.ChartId,
                "standalone", "four-lane-tap-hold", "1", theaterConfig.PassScorePercent);
            var permission = new TheaterEntryPermission(Taming, Taming.CurrentControlId, context,
                () => world != null && ActiveShop == 0 && theater.CanPerform(CurrentRole));
            var consumer = new RhythmExternalSession(permission, policy,
                _ => PerformanceResult(true), _ => PerformanceResult(false),
                _ => PerformanceInterrupted("演奏已取消，可以重试"), _ => PerformanceInterrupted("音频或输入中断，可以重试"));
            var request = new RhythmPlayRequest(context + "-run", context, theaterConfig.Chart.ChartId, RhythmPlayMode.Combat);
            try
            {
                using (telemetry.BeginSpan("performance_open")) await rhythm.OpenActivityAsync(request, consumer, lifetime.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { telemetry.TrackError("performance_failed", error); Show("演奏未打开：" + error.Message); }
            finally { openingActivity = false; }
        }
        private void PerformanceResult(bool success)
        {
            var intent = new TheaterIntent(success ? TheaterAction.FinishSuccess : TheaterAction.FinishFailure, CurrentRole);
            Show(theater.Apply(in intent)); Persist(); closeRhythmAfterFrame = Time.frameCount + 1;
        }
        private void PerformanceInterrupted(string text) { Show(text); closeRhythmAfterFrame = Time.frameCount + 1; }
        private async UniTask StartBattleAsync()
        {
            if (tavernCompleted) { Show("BOSS已击败"); return; }
            if (CurrentRole != "guest") { Show("借酒客身份才能挑战"); return; }
            awaitingBattle = true; expectedBattleGeneration = narrative.Generation + 1; awaitingOutcome = true;
            try
            {
                telemetry.Track("battle_requested", ("boss_drunk", tavern.Rules.BossDrunk));
                bool started;
                using (telemetry.BeginSpan("battle_entry_ready"))
                    started = await narrative.StartAsync("shop_boss_battle", "shop_boss", lifetime.Token);
                if (!started) { awaitingOutcome = false; Show("战斗入口正忙"); }
            }
            catch (OperationCanceledException) { awaitingOutcome = false; }
            catch (Exception error) { awaitingOutcome = false; telemetry.TrackError("battle_failed", error); Show(error.Message); }
            finally { awaitingBattle = false; }
        }
        private void NarrativeChanged()
        {
            if (world == null || !awaitingOutcome || narrative.Generation != expectedBattleGeneration) return;
            var snapshot = narrative.Capture();
            if (snapshot.Current != null) return;
            awaitingOutcome = false;
            if (snapshot.Outcome == "Success")
            { tavernCompleted = true; FindActor("shop_boss").gameObject.SetActive(false); Show("BOSS已击败，挑战完成"); }
            else if (snapshot.Outcome == "Downed") Show("挑战失败，饮酒进度保留，可再挑战");
            Persist();
        }
        public void Tick()
        {
            if (world == null || boundGeneration != sessionGeneration || !(flow.Current is MonsterEncounterState)) return;
            if (closeRhythmAfterFrame >= 0 && Time.frameCount >= closeRhythmAfterFrame)
            { closeRhythmAfterFrame = -1; rhythm.CloseActivityAsync().Forget(); }
            if (Busy) return;
            if (replies.Count > 0) { ReplyAsync(replies.Dequeue()).Forget(); return; }
            int next = world.ShopAt(ControlledScenePosition);
            if (next != ActiveShop)
            {
                if (ActiveShop == 4) { tavern.Apply(TavernAction.Leave, CurrentRole); Persist(); }
                world.Display(next, Taming.CurrentControlId);
                telemetry.Track("shop_area_changed", ("shop", next));
            }
        }
        private void RestoreProgress()
        {
            progressReady = false;
            clothing.Reset(); forging.Reset(); tavern.Reset(); theater.Reset(); tavernCompleted = false;
            ShopProgressSaveData saved = saves.Get<ShopProgressSaveData>();
            if (saved.Initialized)
            {
                clothing.Rules.Restore(saved.Investigated, saved.ClothesReceived);
                if (saved.Forging != null) forging.Rules.Restore(saved.Forging);
                tavern.Rules.Restore(saved.PlayerDrunk, saved.BossDrunk, saved.Customers);
                theater.Restore(saved.TheaterEntered, saved.PatrolRedirected, saved.TheaterCompleted);
                tavernCompleted = saved.TavernCompleted;
                Taming.GetTarget("shop_patrol").ChangePatrolRoute(world.Route(encounterView, theater.PatrolRedirected));
            }
            progressReady = true;
        }
        private void Persist()
        {
            if (!progressReady || boundGeneration != sessionGeneration) return;
            // 分区每次现取；新游戏与读档会整体替换，不能缓存旧槽的数据对象。
            ShopProgressSaveData saved = saves.Get<ShopProgressSaveData>();
            saved.Initialized = true;
            saved.TheaterEntered = theater.Entered; saved.PatrolRedirected = theater.PatrolRedirected; saved.TheaterCompleted = theater.Completed;
            saved.Investigated = clothing.Rules.CaptureInvestigations(); saved.ClothesReceived = clothing.Rules.Received;
            saved.Forging = forging.Rules.Capture(); saved.PlayerDrunk = tavern.Rules.PlayerDrunk; saved.BossDrunk = tavern.Rules.BossDrunk;
            saved.Customers = tavern.Rules.CaptureCustomers(); saved.TavernCompleted = tavernCompleted;
        }
        private void Show(string text)
        { if (lifetime != null && !lifetime.IsCancellationRequested && !string.IsNullOrEmpty(text)) replies.Enqueue(text); }
        private async UniTask ReplyAsync(string text)
        {
            var actor = conversationActor;
            string speaker = actor != null ? actor.DisplayName : ActiveShop >= 0 ? Titles[ActiveShop] : "";
            string id = actor != null ? actor.StableId : "result";
            try
            {
                await dialogue.PlayAsync(Conversation(id + ":reply", speaker, text,
                    new[] { new DialogueContent.Choice { Id = "close", Text = "知道了", Outcome = "close" } }),
                    actor != null ? actor.transform : null, id, lifetime.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { telemetry.TrackError("reply_failed", error); }
        }
        public async UniTask UnbindAsync()
        {
            if (lifetime == null) return;
            using var unloading = telemetry.BeginSpan("scene_unloading");
            try
            {
                var closed = forgingClosed;
                Persist(); lifetime.Cancel();
                if (closed != null) await closed.Task;
                await rhythm.CloseActivityAsync();
            }
            finally
            {
                progressReady = false;
                narrative.OnChanged -= NarrativeChanged;
                battleSetup.BindDrunkSource(null);
                Taming.OnControlChanged -= ControlChanged; Taming.BindSelection(null); Taming.BindModifiers(null, null);
                step.OnAdvanced -= AdvanceIdentity; step.BindPlayerModifiers(null, null);
                if (boundGeneration == sessionGeneration && role.Length > 0 && identity.IsBorrowing && identity.Current.Value == role)
                    identityRules.TryExit(identity);
                if (interaction.Actor != null) interaction.Actor.SetAnchor(null);
                if (world != null) foreach (ShopPlaytestActor actor in world.Actors)
                    if (actor != null) { actor.Bind(null, null); interaction.Unregister(actor); }
                replies.Clear(); conversationActor = null; world = null; encounterView = null; role = "";
                lifetime.Dispose(); lifetime = null; closeRhythmAfterFrame = -1; awaitingBattle = awaitingOutcome = false;
            }
        }
    }
}
