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
using Game.Loot;
using Game.Stealth;
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
            new[] { "请教这里的酒令。", "五魁首、六六顺，划两拳？", "划两拳，输的喝酒。", "我们来较量一下。", "划两拳，输的喝酒。" }
        };
        private readonly ISaveService saves;
        private readonly LootService loot;
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
        private readonly DialogueCatalog dialogues;
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
        private UniTaskCompletionSource tavernClosed;
        private TavernFingerGuessView tavernView;
        private string role = "";
        private bool openingActivity, awaitingBattle, tavernCompleted, awaitingOutcome;
        private int closeRhythmAfterFrame = -1;
        private long runNumber, expectedBattleGeneration;
        private CancellationTokenSource lifetime;
        private bool progressReady;
        private readonly AssassinationRules assassination;
        private ExecutionResolver execution;
        private ShopSkinAppearance skinAppearance;
        private ShopPlaytestActor equippedSkin;
        public string CurrentSkinActorId => equippedSkin == null ? "" : equippedSkin.StableId;
        public int ActiveShop => world == null ? -1 : world.ActiveShop;
        public string CurrentRole => identity.IsInEffect && identity.Current.Value == role ? role : "";
        public ClothingRules Clothing => clothing.Rules;
        public ForgingRules Forging => forging.Rules;
        public TavernDrunkRules Tavern => tavern.Rules;
        public TheaterController Theater => theater;
        public TamingRules Taming => step.Taming;
        public ShopPlaytestWorld World => world;
        public bool HasPendingReply => replies.Count > 0;
        public string TavernEntryRequirement => ShopRewardRules.MissingDisguise(loot.Items);
        public bool CanEnterTavern => ShopRewardRules.HasDisguise(loot.Items);
        public bool TavernCompleted => tavernCompleted;
        public bool CanChallengeTavern => CanEnterTavern && tavern.CanChallenge;
        public Vector2 ControlledPosition => Taming.IsControllingEnemy ? Taming.GetTarget(Taming.CurrentControlId).Model.Position : player.Position;
        private Vector3 ControlledScenePosition => encounterView.ToScenePosition(ControlledPosition);
        private bool Busy => openingActivity || rhythm.IsActivityOpen || battle.IsBattleRunning || narrative.IsBusy || dialogue.IsRunning || awaitingBattle || pause.IsPaused;
        public ShopPlaytestState(ISaveService saves, ISubscriber<SessionStartedEvent> sessionStarted, IGameFlow flow, IWorldPauseService pause, IUIService ui, IInputService input, IInteractionRegistry interaction, InteractionFocus interactionFocus,
            PlayerModel player, EncounterStep step, RhythmState rhythm, BattleFlow battle, BattleSetup battleSetup,
            NarrativeService narrative, DialogueService dialogue, DialogueCatalog dialogues, TheaterConfig theaterConfig, ClothingController clothing,
            ForgingController forging, TavernController tavern, TheaterController theater, IdentityRules identityRules,
            IdentityState identity, IdentityLedger ledger, LootService loot, StealthKernel stealth, ITelemetryService telemetry)
        {
            this.loot = loot;
            this.saves = saves; this.sessionStarted = sessionStarted; this.flow = flow; this.pause = pause; this.interaction = interaction;
            this.ui = ui; this.input = input;
            this.interactionFocus = interactionFocus;
            this.player = player; this.step = step; this.rhythm = rhythm; this.battle = battle;
            this.battleSetup = battleSetup; this.narrative = narrative; this.dialogue = dialogue; this.dialogues = dialogues;
            this.theaterConfig = theaterConfig; this.clothing = clothing; this.forging = forging;
            this.tavern = tavern; this.theater = theater; this.identityRules = identityRules;
            this.identity = identity; this.ledger = ledger; this.telemetry = telemetry.Scope("shopplaytest");
            AssassinationSettings settings = stealth.Assassination.Settings;
            assassination = new AssassinationRules(new AssassinationSettings(settings.RearAngle, settings.MaxDistance, true));
        }
        public void Start() => sessionSubscription = sessionStarted.Subscribe(_ => sessionGeneration++);
        public void Dispose() { sessionSubscription?.Dispose(); sessionSubscription = null; }
        public UniTask BindAsync(ShopPlaytestWorld scene, EncounterSceneView existingView, CancellationToken ct)
        {
            world = scene; encounterView = existingView;
            boundGeneration = sessionGeneration;
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
            string savedSkin = saves.Get<ShopProgressSaveData>().EquippedSkinId;
            skinAppearance = new ShopSkinAppearance(existingView, world.Actors);
            execution = new ExecutionResolver(assassination, step.FactSink, null, telemetry);
            RestoreProgress();
            FindActor("shop_boss").gameObject.SetActive(!tavernCompleted);
            foreach (ShopPlaytestActor actor in world.Actors)
            { actor.Bind(Interact, CanInteract, TakeOrWearSkin, selected => interactionFocus.RequestFocus(selected),
                target => OwnsSkin(target.Role) ? "换皮" : "潜行刺杀取皮"); interaction.Register(actor); }
            Taming.OnControlChanged += ControlChanged;
            step.OnAdvanced += AdvanceIdentity;
            Taming.BindSelection(CanSelect);
            Taming.BindModifiers(AwarenessMultiplier, ActorSpeed);
            step.BindPlayerModifiers(() => tavern.Rules.SpeedMultiplier, () => tavern.Rules.CanSneak);
            battleSetup.BindDrunkSource(BossDrunk);
            narrative.OnChanged += NarrativeChanged;
            ControlChanged(Taming.CurrentControlId);
            ShopPlaytestActor restored = FindActor(savedSkin);
            if (restored != null) TryWearSkin(ShopSkinRules.ItemId(restored.Role), out _);
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
            (narrative.CurrentStoryId != "spotlight_current" || narrative.CurrentStageId == "action_r0197") &&
            actor.IsVisible && (actor.Role.Length == 0 || (actor.StableId != Taming.CurrentControlId && Taming.IsAvailable(actor.StableId)));
        private bool CanSelect(string id)
        {
            return FindActor(id) == null;
        }
        private void Interact(ShopPlaytestActor actor)
        {
            if (!CanInteract(actor) || Vector3.Distance(ControlledScenePosition, actor.Position) > actor.InteractionRadius) return;
            if (actor.Shop == 4 && !CheckTavernEntry()) return;
            if (actor.Role.Length > 0 && Taming.GetTarget(actor.StableId).Model.Health <= 0)
            { Show("这人已被刺杀；取得的皮可以在背包中使用。"); return; }
            ConverseAsync(actor).Forget();
        }
        public void ReturnBody()
        {
            if (world == null || Busy) return;
            ClearSkin();
            if (Taming.IsControllingEnemy) encounterView.RequestControl(Taming.PlayerId);
            Persist();
        }
        private bool OwnsSkin(string skinRole) => ShopSkinRules.ItemId(skinRole) != 0 &&
            loot.Items.TryGetValue(ShopSkinRules.ItemId(skinRole), out int count) && count > 0;
        private void TakeOrWearSkin(ShopPlaytestActor actor)
        {
            if (!actor.CanInteractSecondary || !CanInteract(actor) ||
                Vector3.Distance(ControlledScenePosition, actor.Position) > actor.InteractionRadius) return;
            if (actor.Shop == 4 && !CheckTavernEntry()) return;
            int itemId = ShopSkinRules.ItemId(actor.Role);
            if (OwnsSkin(actor.Role))
            { Show(TryWearSkin(itemId, out string reason) ? "已换上" + actor.DisplayName + "的皮。" : reason); return; }
            if (Taming.IsControllingEnemy || player.Health <= 0) { Show("请用本体潜行绕背刺杀。"); return; }
            MonsterRules target = Taming.GetTarget(actor.StableId);
            MonsterModel model = target.Model;
            var snapshot = new AssassinationInput(player.Position, player.Facing, model.Position, model.Facing,
                model.Health > 0, model.Mode != MonsterMode.PatrolWalk && model.Mode != MonsterMode.PatrolPause, player.IsSneaking);
            var attack = new ExecutionInput(in snapshot, itemId != 0);
            ExecutionVerdict result = execution.TryExecute(in attack, target);
            if (!result.Allowed) { Show("刺杀失败：" + result.Describe() + "。需潜行、绕背并靠近未察觉的目标。"); return; }
            if (!loot.GrantItems(itemId, 1)) { Show("取皮入包失败，请重新进场恢复。"); return; }
            Persist();
            Show(actor.DisplayName + "的皮已存入背包；使用皮后才能获得身份。");
        }
        public bool TryWearSkin(int itemId, out string reason)
        {
            reason = "当前不能换皮";
            string nextRole = ShopSkinRules.Role(itemId);
            if (world == null || boundGeneration != sessionGeneration || nextRole.Length == 0 || player.Health <= 0 ||
                openingActivity || rhythm.IsActivityOpen || battle.IsBattleRunning || narrative.IsBusy || dialogue.IsRunning || awaitingBattle)
                return false;
            if (!OwnsSkin(nextRole)) { reason = "先潜行刺杀取得对应的皮"; return false; }
            ShopPlaytestActor actor = FindActor("shop_" + nextRole);
            if (actor == null || !skinAppearance.CanWear(actor.StableId)) { reason = "这个身份的外观未接好"; return false; }
            if (actor.Shop == 4 && !CanEnterTavern) { reason = "还缺酒肆三件套：" + TavernEntryRequirement; return false; }
            IdentityEnterResult result = identityRules.TryEnter(identity, ledger, IdentityId.From(nextRole));
            if (result != IdentityEnterResult.Entered && result != IdentityEnterResult.AlreadyBorrowing)
            { reason = "换皮失败：" + result; return false; }
            if (Taming.IsControllingEnemy) Taming.TryControl(Taming.PlayerId);
            role = nextRole; equippedSkin = actor;
            skinAppearance.Wear(actor.StableId);
            Persist(); reason = ""; return true;
        }
        private void ClearSkin()
        {
            if (role.Length > 0 && identity.IsBorrowing && identity.Current.Value == role) identityRules.TryExit(identity);
            skinAppearance?.Clear(); equippedSkin = null; role = "";
            if (world != null) world.Display(ActiveShop, Taming.CurrentControlId);
        }
        private void ControlChanged(string id)
        {
            if (FindActor(id) != null) { Taming.TryControl(Taming.PlayerId); return; }
            if (id != Taming.PlayerId) ClearSkin();
            else if (equippedSkin == null && identity.IsBorrowing && IsShopRole(identity.Current.Value)) identityRules.TryExit(identity);
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
            if (owned && reason != IdentityExitReason.None) { ClearSkin(); Persist(); }
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
            // 正文取正式内容表，现场选项仍由角色资格生成，不另建对白系统。
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
            if (Taming.IsControllingEnemy || equippedSkin != null)
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
            string key = GreetingKey(actor.StableId, CurrentRole);
            if (actor.StableId == "shop_tailor" && CurrentRole == "customer1" && clothing.Rules.Received) key = "tailor_done";
            if (actor.StableId == "shop_customer_a" && tavern.UnlockedClue(actor.StableId).Length > 0) key = "customer_a_drunk";
            if (actor.StableId == "shop_customer_b" && tavern.UnlockedClue(actor.StableId).Length > 0) key = "customer_b_drunk";
            string line = ShopLine(key);
            return CurrentRole.Length == 0 || actor.StableId.StartsWith("shop_station_", StringComparison.Ordinal)
                ? line : (equippedSkin == null ? Taming.GetDisplayName(Taming.CurrentControlId) : equippedSkin.DisplayName) + "，" + line;
        }
        private string ShopLine(string key)
        {
            if (!dialogues.TryGet(21101, out var content)) throw new InvalidOperationException("缺少五店正式对白21101");
            return content.Get(key).Text;
        }
        public static string GreetingKey(string id, string currentRole)
        {
            switch (id)
            {
                case "shop_customer0": return "customer0";
                case "shop_customer1": return "customer1";
                case "shop_customer2": return "customer2";
                case "shop_tailor": return currentRole == "customer1" ? "tailor_ready" : currentRole == "customer0" || currentRole == "customer2" ? "tailor_wrong" : "tailor_visitor";
                case "shop_apprentice": return "apprentice";
                case "shop_musician": return "musician";
                case "shop_patrol": return currentRole == "apprentice" ? "theater_verify" : currentRole == "director" ? "theater_command" : "patrol";
                case "shop_director": case "shop_station_0": return currentRole == "apprentice" ? "theater_verify" : currentRole == "director" ? "theater_command" : currentRole == "musician" ? "theater_perform" : "theater_visitor";
                case "shop_officer": return "quota";
                case "shop_smith": case "shop_station_2": return currentRole == "officer" ? "forge_officer" : currentRole == "smith" ? "forge_smith" : "forge_visitor";
                case "shop_innkeeper": case "shop_station_3": return "inn";
                case "shop_guest": case "shop_station_4": return "guest";
                case "shop_boss": return currentRole == "guest" ? "boss_guest" : "boss_visitor";
                case "shop_customer_a": return "customer_a";
                case "shop_customer_b": return "customer_b";
                default: return "generic";
            }
        }
        private void ExecuteCommand(int index, ShopPlaytestActor actor)
        {
            if (world == null || ActiveShop < 0 || Busy || index < 0 || index >= Commands[ActiveShop].Length ||
                actor == null || actor.Shop != ActiveShop || !actor.IsVisible ||
                Vector3.Distance(ControlledScenePosition, actor.Position) > actor.InteractionRadius)
            { telemetry.TrackWarn("command_rejected", TelemetryProps.Of(("reason", "unavailable_or_distance"), ("command", index))); return; }
            if (ActiveShop == 4 && !CheckTavernEntry()) return;
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
                case 3: message = "戏班借行头，衣肆取黑色长衫，金银行打金饰。三样凑齐，才能进酒肆。三店不分先后。"; break;
                case 4:
                    string targetId = index == 1 || index == 3 ? "shop_boss" : index == 4 ? "shop_customer_b" : "shop_customer_a";
                    if (index != 0 && !NearDrinkTarget(targetId)) { Show("走近对应酒客或BOSS再操作"); return; }
                    if (index == 3) { StartBattleAsync().Forget(); return; }
                    if (index == 0) { Show("先各出零至五指，再喊双方总数。只有一人猜中才分胜负，输者饮一杯；同中或都不中不罚。走近客人或主人即可划拳。"); return; }
                    OpenGuessAsync(actor, targetId).Forget(); return;
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
        private async UniTask OpenGuessAsync(ShopPlaytestActor actor, string targetId)
        {
            if (CurrentRole != "guest" || !CanEnterTavern || !NearDrinkTarget(targetId)) return;
            openingActivity = true;
            var closed = new UniTaskCompletionSource(); tavernClosed = closed;
            var scene = world; long generation = sessionGeneration;
            CancellationToken token = lifetime.Token;
            bool gameplayEnabled = input.Actions.Gameplay.enabled; // lint-ok: 记录动作图状态，收尾恢复，不读原始玩法输入。
            IDisposable paused = pause.Acquire(this); input.DisableMap("Gameplay");
            long round = 0;
            bool Allowed() => !token.IsCancellationRequested && world == scene && generation == sessionGeneration && boundGeneration == generation &&
                CurrentRole == "guest" && CanEnterTavern && actor != null && actor.IsVisible && ActiveShop == 4 && NearDrinkTarget(targetId);
            string DrunkStatus() => "你：醉意 " + tavern.Rules.PlayerDrunk + "　" + actor.DisplayName + "：醉意 " +
                (targetId == "shop_boss" ? tavern.Rules.BossDrunk : tavern.Rules.CustomerDrunkFor(targetId));
            void Next()
            {
                if (!Allowed()) { tavern.CancelGuess(); tavernView.Reject("已离席，请返回街面重新交互。"); return; }
                round = tavern.BeginGuess(targetId); tavernView.BeginRound(actor.DisplayName, DrunkStatus());
            }
            void Confirm(int fingers, int call)
            {
                if (!Allowed()) { tavern.CancelGuess(); tavernView.Reject("当前不能划拳，请重新入席。"); return; }
                var intent = new TavernIntent(round, targetId, true, fingers, call);
                if (!tavern.ResolveGuess(in intent, out var result, out string reason)) { tavernView.Reject(reason); return; }
                string clue = tavern.UnlockedClue(targetId);
                var saved = saves.Get<ShopProgressSaveData>();
                if (clue.Length > 0 && !saved.TavernClues.Add(targetId)) clue = "";
                if (targetId == "shop_boss" && tavern.CanChallenge) clue = "主人已有醉意，回席后可以向他发起挑战。";
                Persist(); tavernView.ShowResult(in result, actor.DisplayName, DrunkStatus(), clue);
            }
            try
            {
                using (telemetry.BeginSpan("guess_open")) tavernView = await ui.OpenAsync<TavernFingerGuessView>(null, token);
                tavernView.OnGuess += Confirm; tavernView.OnNext += Next; Next();
                await tavernView.WaitAsync(token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { telemetry.TrackError("guess_failed", error); Show("划拳未能打开，请重新交互。"); }
            finally
            {
                tavern.CancelGuess(); await UniTask.Yield();
                try { if (tavernView != null && ui.Get<TavernFingerGuessView>() == tavernView) await ui.CloseAsync(tavernView, CancellationToken.None); }
                finally
                {
                    tavernView = null; paused.Dispose();
                    if (gameplayEnabled && generation == sessionGeneration) input.EnableMap("Gameplay");
                    openingActivity = false; tavernClosed = null; closed.TrySetResult();
                }
            }
        }
        private async UniTask OpenForgeAsync(ShopPlaytestActor actor)
        {
            if (CurrentRole != "smith")
            { telemetry.TrackWarn("forging_entry_rejected", TelemetryProps.Of(("role", CurrentRole), ("reason", "not_smith"))); Show("先取得并换上金银匠皮，再使用打造台"); return; }
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
            if (!theater.CanPerform(CurrentRole)) { Show("先换学徒皮验身、都知皮调巡逻，再换乐正皮演奏"); return; }
            openingActivity = true;
            string context = "main-theater-" + ++runNumber;
            var policy = new RhythmFixedChartCombatPolicy(theaterConfig.Chart.ChartId, theaterConfig.Chart.ChartId,
                "standalone", "four-lane-tap-hold", "1", theaterConfig.PassScorePercent);
            var permission = new TheaterEntryPermission(Taming, Taming.CurrentControlId, context,
                () => world != null && ActiveShop == 0 && OwnsSkin(CurrentRole) && theater.CanPerform(CurrentRole));
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
            string message = theater.Apply(in intent);
            Persist();
            Show(success && theater.Completed ? "演奏达标，行头三样已存入背包。" : message);
            closeRhythmAfterFrame = Time.frameCount + 1;
        }
        private void PerformanceInterrupted(string text) { Show(text); closeRhythmAfterFrame = Time.frameCount + 1; }
        private async UniTask StartBattleAsync()
        {
            if (!CheckTavernEntry()) return;
            if (tavernCompleted) { Show("BOSS已击败"); return; }
            if (CurrentRole != "guest") { Show("换上酒客皮才能挑战"); return; }
            if (!tavern.CanChallenge) { Show("先和主人划拳，让他喝过一杯再挑战。"); return; }
            bool local = narrative.CurrentStoryId == "spotlight_current" && narrative.CurrentStageId == "action_r0197";
            if (!local && narrative.CurrentStoryId.Length > 0) { Show("请先完成当前剧情。"); return; }
            awaitingBattle = true; expectedBattleGeneration = narrative.Generation + (local ? 0 : 1); awaitingOutcome = true;
            try
            {
                telemetry.Track("battle_requested", ("boss_drunk", tavern.Rules.BossDrunk));
                bool started;
                using (telemetry.BeginSpan("battle_entry_ready"))
                    started = local ? await narrative.TryEncounterAsync(new[] { new EncounterRules.Candidate {
                        TriggerId = "shop_boss", TriggerKind = "SpotlightShopBoss", EntryEpoch = narrative.Capture().NextActivationId,
                        Context = narrative.Conditions.Snapshot("shop_boss") } }, lifetime.Token)
                        : await narrative.StartAsync("shop_boss_battle", "shop_boss", lifetime.Token);
                if (!started) { awaitingOutcome = false; Show("战斗入口正忙"); }
            }
            catch (OperationCanceledException) { awaitingOutcome = false; }
            catch (Exception error) { awaitingOutcome = false; telemetry.TrackError("battle_failed", error); Show(error.Message); }
            finally { awaitingBattle = false; }
        }
        private bool CheckTavernEntry()
        {
            string missing = TavernEntryRequirement;
            if (missing.Length == 0) return true;
            Show("进酒肆要凑齐行头、衣、金。还缺：" + missing + "。");
            return false;
        }
        private void NarrativeChanged()
        {
            if (world == null || boundGeneration != sessionGeneration) return;
            if (narrative.HasStoryFlag("world.shopboss.defeated"))
            {
                if (!tavernCompleted) { tavernCompleted = true; FindActor("shop_boss").gameObject.SetActive(false); Persist(); }
                awaitingOutcome = false; return;
            }
            if (!awaitingOutcome || narrative.Generation != expectedBattleGeneration) return;
            var snapshot = narrative.Capture();
            if (snapshot.Current != null)
            {
                if (snapshot.Parent == null && snapshot.Current.StoryId == "spotlight_current" && snapshot.Current.StageId == "action_r0197")
                { awaitingOutcome = false; Show("挑战失败，划拳醉意保留，可以再挑战。"); }
                return;
            }
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
            tavern.RestoreLearning(saved.TavernLearning);
            progressReady = true;
            Persist();
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
            saved.TavernLearning = tavern.CaptureLearning();
            saved.EquippedSkinId = CurrentSkinActorId;
            ShopRewardRules.GrantCompleted(saved, loot.GrantItems);
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
                var closed = forgingClosed; var guessClosed = tavernClosed;
                Persist(); lifetime.Cancel();
                if (closed != null) await closed.Task;
                if (guessClosed != null) await guessClosed.Task;
                await rhythm.CloseActivityAsync();
            }
            finally
            {
                progressReady = false;
                skinAppearance?.Dispose(); skinAppearance = null; equippedSkin = null; execution = null;
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
