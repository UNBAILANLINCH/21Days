// 职责：把各模块的「关键节点」接成保存请求——任务三事件、开箱、对白结束、进入玩法场景走合并式 RequestSave；
//   离开玩法状态与退出游戏直接 SaveNowAsync。
// 为什么新建：GameSession 要在 EditMode 里不起 MessagePipe / 对白服务就能测，订阅接线放在它里面会把这些具体依赖带进构造；
//   单拎一个入口点只做「事件 → 调 GameSession」，GameSession 只认 RequestSave / SaveNowAsync 两个入口。

using System;
using Cysharp.Threading.Tasks;
using Game.Core.Boot;
using Game.Core.Events;
using Game.Dialogue;
using Game.Loot;
using Game.Monster;
using Game.Quest;
using MessagePipe;
using VContainer.Unity;

namespace Game.Session
{
    /// <summary>
    /// 自动保存触发点（根作用域入口点）。所有触发只在 <see cref="GameSession.CurrentSlot"/> 非 0 时生效
    /// （<see cref="GameSession.RequestSave"/> / <see cref="GameSession.SaveNowAsync"/> 内部判断）。
    /// </summary>
    public sealed class SaveTriggerBridge : IStartable, IDisposable
    {
        private readonly GameSession session;
        private readonly SessionConfig config;
        private readonly DialogueService dialogue;
        private readonly ISubscriber<QuestActivatedEvent> questActivated;
        private readonly ISubscriber<QuestCompletedEvent> questCompleted;
        private readonly ISubscriber<QuestTrackingChangedEvent> questTracking;
        private readonly ISubscriber<CrateCollectedEvent> crateCollected;
        private readonly ISubscriber<GameStateChangedEvent> stateChanged;
        private readonly ISubscriber<GameStateChangingEvent> stateChanging;

        private IDisposable subscriptions;
        private IDisposable quitHook;
        private bool dialogueHooked;

        public SaveTriggerBridge(
            GameSession session,
            SessionConfig config,
            DialogueService dialogue,
            ISubscriber<QuestActivatedEvent> questActivated,
            ISubscriber<QuestCompletedEvent> questCompleted,
            ISubscriber<QuestTrackingChangedEvent> questTracking,
            ISubscriber<CrateCollectedEvent> crateCollected,
            ISubscriber<GameStateChangedEvent> stateChanged,
            ISubscriber<GameStateChangingEvent> stateChanging)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            // SessionConfig 是 ScriptableObject，判空只用 ==。
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
            this.dialogue = dialogue ?? throw new ArgumentNullException(nameof(dialogue));
            this.questActivated = questActivated ?? throw new ArgumentNullException(nameof(questActivated));
            this.questCompleted = questCompleted ?? throw new ArgumentNullException(nameof(questCompleted));
            this.questTracking = questTracking ?? throw new ArgumentNullException(nameof(questTracking));
            this.crateCollected = crateCollected ?? throw new ArgumentNullException(nameof(crateCollected));
            this.stateChanged = stateChanged ?? throw new ArgumentNullException(nameof(stateChanged));
            this.stateChanging = stateChanging ?? throw new ArgumentNullException(nameof(stateChanging));
        }

        public void Start()
        {
            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            questActivated.Subscribe(_ => session.RequestSave("quest_activated")).AddTo(bag);
            questCompleted.Subscribe(_ => session.RequestSave("quest_completed")).AddTo(bag);
            questTracking.Subscribe(_ => session.RequestSave("quest_tracking")).AddTo(bag);
            crateCollected.Subscribe(_ => session.RequestSave("crate")).AddTo(bag);
            stateChanged.Subscribe(HandleStateChanged).AddTo(bag);
            stateChanging.Subscribe(HandleStateChanging).AddTo(bag);
            subscriptions = bag.Build();

            // DialogueService 用 C# event（其文件头第 6 行的取舍），与 MessagePipe 订阅分开成对退订。
            dialogue.OnEnded += HandleDialogueEnded;
            dialogueHooked = true;

            quitHook = GameQuit.RegisterBeforeQuit(SaveBeforeQuitAsync);
        }

        public void Dispose()
        {
            subscriptions?.Dispose();
            subscriptions = null;

            if (dialogueHooked)
            {
                dialogue.OnEnded -= HandleDialogueEnded;
                dialogueHooked = false;
            }

            quitHook?.Dispose();
            quitHook = null;
        }

        private void HandleDialogueEnded(DialogueEndedEvent _) => session.RequestSave("dialogue");

        /// <summary>进入遭遇状态完成（新游戏第一次落盘也在这里）→ 请求保存，等闸门打开再写。</summary>
        private void HandleStateChanged(GameStateChangedEvent e)
        {
            if (e.To == typeof(MonsterEncounterState)) session.RequestSave("scene");
        }

        /// <summary>
        /// 即将离开遭遇状态 → 直接保存。回调在前一状态 ExitAsync 之前同步执行，
        /// <see cref="GameSession.SaveNowAsync"/> 的捕获段在第一个 await 之前完成，所以捕获到的是离场前的现场。
        /// </summary>
        private void HandleStateChanging(GameStateChangingEvent e)
        {
            if (e.From != typeof(MonsterEncounterState) || session.CurrentSlot == 0) return;
            session.SaveNowAsync("leave").Forget();
        }

        /// <summary>退出前保存；按配置单独限时（真实时间，暂停菜单里 timeScale 为 0 也照常计时）。</summary>
        private async UniTask SaveBeforeQuitAsync()
        {
            UniTask save = session.SaveNowAsync("quit").AsUniTask();
            float seconds = config.QuitHookTimeoutSeconds;
            if (seconds <= 0f)
            {
                await save;
                return;
            }

            await UniTask.WhenAny(save, UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.Realtime));
        }
    }
}
