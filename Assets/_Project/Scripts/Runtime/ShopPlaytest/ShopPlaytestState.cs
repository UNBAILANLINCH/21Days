// 职责：把各店独立玩法接到现有遭遇；只处理交互、身份和结果，不拥有场景或第二个移动驱动。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Battle;
using Game.Clothing;
using Game.Core.Flow;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
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
            new[] { "学徒验身", "都知调开巡逻", "乐正演奏" },
            new[] { "查看甲的线索", "查看乙的线索", "查看丙的线索", "以甲领衣", "以乙领衣", "以丙领衣" },
            new[] { "官证增加拨料", "打造金钥匙", "打造首饰", "点位1", "点位2", "点位3", "点位4", "取消工件", "提交首饰" },
            new[] { "听取客栈线索（占位对白）" },
            new[] { "自己饮酒", "给BOSS灌酒", "给客人甲灌酒", "挑战BOSS", "给客人乙灌酒" }
        };
        private readonly IUIService ui;
        private readonly ISaveService saves;
        private readonly ISubscriber<SessionStartedEvent> sessionStarted;
        private IDisposable sessionSubscription;
        private long sessionGeneration, boundGeneration;
        private readonly IGameFlow flow;
        private readonly IWorldPauseService pause;
        private readonly IInteractionRegistry interaction;
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
        private readonly List<ShopPlaytestActor> roles = new List<ShopPlaytestActor>();
        private ShopPlaytestWorld world;
        private EncounterSceneView encounterView;
        private ShopPlaytestView view;
        private string role = "";
        private bool openingActivity, awaitingBattle, tavernCompleted, awaitingOutcome;
        private int closeRhythmAfterFrame = -1;
        private long runNumber, expectedBattleGeneration;
        private CancellationTokenSource lifetime;
        private bool progressReady;
        public int ActiveShop => world == null ? -1 : world.ActiveShop;
        public string CurrentRole => identity.IsInEffect ? role : "";
        public ClothingRules Clothing => clothing.Rules;
        public ForgingRules Forging => forging.Rules;
        public TavernDrunkRules Tavern => tavern.Rules;
        public TheaterController Theater => theater;
        public TamingRules Taming => step.Taming;
        public ShopPlaytestWorld World => world;
        public Vector2 ControlledPosition => Taming.IsControllingEnemy ? Taming.GetTarget(Taming.CurrentControlId).Model.Position : player.Position;
        private Vector3 ControlledScenePosition => encounterView.ToScenePosition(ControlledPosition);
        private bool Busy => openingActivity || rhythm.IsActivityOpen || battle.IsBattleRunning || narrative.IsBusy || dialogue.IsRunning || awaitingBattle || pause.IsPaused;
        public ShopPlaytestState(IUIService ui, ISaveService saves, ISubscriber<SessionStartedEvent> sessionStarted, IGameFlow flow, IWorldPauseService pause, IInteractionRegistry interaction,
            PlayerModel player, EncounterStep step, RhythmState rhythm, BattleFlow battle, BattleSetup battleSetup,
            NarrativeService narrative, DialogueService dialogue, TheaterConfig theaterConfig, ClothingController clothing,
            ForgingController forging, TavernController tavern, TheaterController theater, IdentityRules identityRules,
            IdentityState identity, IdentityLedger ledger, ITelemetryService telemetry)
        {
            this.ui = ui; this.saves = saves; this.sessionStarted = sessionStarted; this.flow = flow; this.pause = pause; this.interaction = interaction;
            this.player = player; this.step = step; this.rhythm = rhythm; this.battle = battle;
            this.battleSetup = battleSetup; this.narrative = narrative; this.dialogue = dialogue;
            this.theaterConfig = theaterConfig; this.clothing = clothing; this.forging = forging;
            this.tavern = tavern; this.theater = theater; this.identityRules = identityRules;
            this.identity = identity; this.ledger = ledger; this.telemetry = telemetry.Scope("shopplaytest");
        }
        public void Start() => sessionSubscription = sessionStarted.Subscribe(_ => sessionGeneration++);
        public void Dispose() { sessionSubscription?.Dispose(); sessionSubscription = null; }
        public async UniTask BindAsync(ShopPlaytestWorld scene, EncounterSceneView existingView, CancellationToken ct)
        {
            world = scene; encounterView = existingView;
            boundGeneration = sessionGeneration;
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
            RestoreProgress();
            FindActor("shop_boss").gameObject.SetActive(!tavernCompleted);
            foreach (ShopPlaytestActor actor in world.Actors)
            { actor.Bind(Interact, CanInteract); interaction.Register(actor); }
            Taming.OnControlChanged += ControlChanged;
            step.OnAdvanced += AdvanceIdentity;
            Taming.BindSelection(CanSelect);
            Taming.BindModifiers(AwarenessMultiplier, ActorSpeed);
            step.BindPlayerModifiers(() => tavern.Rules.SpeedMultiplier, () => tavern.Rules.CanSneak);
            rhythm.OnActivityClosed += ActivityClosed;
            battleSetup.BindDrunkSource(BossDrunk);
            narrative.OnChanged += NarrativeChanged;
            using (telemetry.BeginSpan("view_ready")) view = await ui.OpenAsync<ShopPlaytestView>(null, ct);
            view.OnRole += BorrowRole; view.OnCommand += ExecuteCommand; view.OnBody += ReturnBody; view.OnRetry += Retry;
            ControlChanged(Taming.CurrentControlId);
            view.gameObject.SetActive(false);
        }
        private ShopPlaytestActor FindActor(string id)
        {
            if (world == null) return null;
            foreach (ShopPlaytestActor actor in world.Actors) if (actor != null && actor.StableId == id) return actor;
            return null;
        }
        private bool CanInteract(ShopPlaytestActor actor) => world != null && !Busy && actor.Shop == ActiveShop &&
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
            if (actor.Role.Length == 0) { OpenLocalPanel(); return; }
            if (!encounterView.RequestControl(actor.StableId, true)) Show("当前不能借用这个人物");
        }
        public void BorrowRole(int index)
        { if (index >= 0 && index < roles.Count) Interact(roles[index]); }
        public void ReturnBody()
        { if (world != null && !Busy) encounterView.RequestControl(Taming.PlayerId); }
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
            SetupPanel();
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
        private void SetupPanel()
        {
            if (view == null) return;
            roles.Clear();
            foreach (ShopPlaytestActor actor in world.Actors)
                if (actor.Shop == ActiveShop && actor.Role.Length > 0) roles.Add(actor);
            if (ActiveShop < 0) { view.gameObject.SetActive(false); return; }
            var names = new string[roles.Count];
            for (int i = 0; i < names.Length; i++) names[i] = roles[i].DisplayName;
            view.Setup(Titles[ActiveShop] + " · 街面操作", names, Commands[ActiveShop]);
            Refresh();
        }
        public void OpenLocalPanel()
        {
            if (ActiveShop < 0 || Busy || !world.NearStation(ActiveShop, ControlledScenePosition)) return;
            SetupPanel(); view.gameObject.SetActive(true);
            telemetry.Track("local_panel_opened", ("shop", ActiveShop));
            Show("在现场借身份，再操作；WASD移动，离开店前范围收起此面板。");
        }
        public void ExecuteCommand(int index)
        {
            if (world == null || ActiveShop < 0 || Busy || index < 0 || index >= Commands[ActiveShop].Length ||
                !world.NearStation(ActiveShop, ControlledScenePosition))
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
                    ForgingAction action = index == 0 ? ForgingAction.SetQuota : index == 1 ? ForgingAction.StartKey :
                        index == 2 ? ForgingAction.StartOrnament : index <= 6 ? ForgingAction.Strike :
                        index == 7 ? ForgingAction.Cancel : ForgingAction.Submit;
                    message = forging.Apply(action, index == 0 ? forging.MaximumQuota : index - 3, CurrentRole); break;
                case 3: InnDialogueAsync().Forget(); return;
                case 4:
                    string targetId = index == 1 || index == 3 ? "shop_boss" : index == 4 ? "shop_customer_b" : "shop_customer_a";
                    if (index != 0 && !NearDrinkTarget(targetId)) { Show("走近对应酒客或BOSS再操作"); return; }
                    if (index == 3) { StartBattleAsync().Forget(); return; }
                    message = tavern.Apply(index == 0 ? TavernAction.DrinkPlayer : index == 1 ? TavernAction.DrinkBoss :
                        TavernAction.DrinkCustomer, CurrentRole, index == 4 ? "shop_customer_b" : "shop_customer_a"); break;
            }
            Persist(); Show(message); Refresh();
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
            Persist(); Refresh(); Show("已重试本店；其它店成果保留。调试重试不回退剧情标记或战斗掉落。");
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
        private async UniTask InnDialogueAsync()
        {
            try { await dialogue.PlayAsync(1001, world.Station(3), lifetime.Token); Show("占位对白结束；客栈正式线索待文策替换。"); }
            catch (OperationCanceledException) { }
            catch (Exception error) { telemetry.TrackError("inn_dialogue_failed", error); Show("对白未打开：" + error.Message); }
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
                view.gameObject.SetActive(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { telemetry.TrackError("performance_failed", error); Show("演奏未打开：" + error.Message); }
            finally { openingActivity = false; }
        }
        private void PerformanceResult(bool success)
        {
            var intent = new TheaterIntent(success ? TheaterAction.FinishSuccess : TheaterAction.FinishFailure, CurrentRole);
            Show(theater.Apply(in intent)); Persist(); closeRhythmAfterFrame = Time.frameCount + 1; Refresh();
        }
        private void PerformanceInterrupted(string text) { Show(text); closeRhythmAfterFrame = Time.frameCount + 1; }
        private void ActivityClosed() { if (view != null && world != null) { view.gameObject.SetActive(ActiveShop >= 0); Refresh(); } }
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
            finally { awaitingBattle = false; Refresh(); }
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
            Persist(); Refresh();
        }
        public void Tick()
        {
            if (world == null || view == null || boundGeneration != sessionGeneration || !(flow.Current is MonsterEncounterState)) return;
            if (closeRhythmAfterFrame >= 0 && Time.frameCount >= closeRhythmAfterFrame)
            { closeRhythmAfterFrame = -1; rhythm.CloseActivityAsync().Forget(); }
            if (Busy) return;
            int next = world.ShopAt(ControlledScenePosition);
            if (next != ActiveShop)
            {
                if (ActiveShop == 4) { tavern.Apply(TavernAction.Leave, CurrentRole); Persist(); }
                world.Display(next, Taming.CurrentControlId);
                SetupPanel(); view.gameObject.SetActive(false);
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
                forging.Rules.Restore(saved.Forging);
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
        private void Refresh()
        {
            if (view == null || world == null || ActiveShop < 0) return;
            string text = "当前控制：" + Taming.GetDisplayName(Taming.CurrentControlId) + "\n";
            switch (ActiveShop)
            {
                case 0: text += (theater.Entered ? "已验身" : "尚未验身") + " / " +
                    (theater.PatrolRedirected ? "巡逻已调开" : "巡逻未调开") + " / " + (theater.Completed ? "演奏完成" : "未完成"); break;
                case 1: text += clothing.Rules.Received ? "蓝长衫已领取（本地成果）" : "收集证词，推断取衣人"; break;
                case 2: text += "拨料 " + forging.Rules.Quota + " / 可用 " + forging.Rules.Available + " / 已用 " + forging.Rules.Spent +
                    "\n金钥匙 " + forging.Rules.Keys + " / 首饰 " + forging.Rules.Ornaments +
                    (forging.Rules.IsForging ? " / 下一点位 " + (forging.Rules.NextPoint + 1) : "") +
                    (forging.Rules.Submitted ? " / 已提交" : ""); break;
                case 3: text += "客栈对白入口；正式线索待替换"; break;
                case 4: text += "自己 " + tavern.Rules.PlayerDrunk + " / BOSS " + tavern.Rules.BossDrunk +
                    " / 客人甲 " + tavern.Rules.CustomerDrunkFor("shop_customer_a") +
                    " / 客人乙 " + tavern.Rules.CustomerDrunkFor("shop_customer_b") +
                    "\n移动倍率 " + tavern.Rules.SpeedMultiplier + " / 静步" + (tavern.Rules.CanSneak ? "可用" : "禁止"); break;
            }
            view.Status(text); view.HighlightCommand(ActiveShop == 2 && forging.Rules.IsForging ? forging.Rules.NextPoint + 3 : -1);
        }
        private void Show(string text) { if (view != null) view.Message(text); }
        public async UniTask UnbindAsync()
        {
            if (lifetime == null) return;
            using var unloading = telemetry.BeginSpan("scene_unloading");
            try
            {
                Persist(); lifetime.Cancel();
                await rhythm.CloseActivityAsync();
            }
            finally
            {
                progressReady = false;
                rhythm.OnActivityClosed -= ActivityClosed; narrative.OnChanged -= NarrativeChanged;
                battleSetup.BindDrunkSource(null);
                Taming.OnControlChanged -= ControlChanged; Taming.BindSelection(null); Taming.BindModifiers(null, null);
                step.OnAdvanced -= AdvanceIdentity; step.BindPlayerModifiers(null, null);
                if (boundGeneration == sessionGeneration && role.Length > 0 && identity.IsBorrowing && identity.Current.Value == role)
                    identityRules.TryExit(identity);
                if (interaction.Actor != null) interaction.Actor.SetAnchor(null);
                if (world != null) foreach (ShopPlaytestActor actor in world.Actors)
                    if (actor != null) { actor.Bind(null, null); interaction.Unregister(actor); }
                ShopPlaytestView closing = view;
                if (closing != null) { closing.OnRole -= BorrowRole; closing.OnCommand -= ExecuteCommand; closing.OnBody -= ReturnBody; closing.OnRetry -= Retry; }
                view = null; roles.Clear(); world = null; encounterView = null; role = "";
                lifetime.Dispose(); lifetime = null; closeRhythmAfterFrame = -1; awaitingBattle = awaitingOutcome = false;
                if (closing != null) await ui.CloseAsync(closing);
            }
        }
    }
}
