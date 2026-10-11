// 职责：Battle 测试组共用的假实现——剧情口、门闸、表现、黑幕、资源、UI 层、暂停、背包、消息总线。
// 为什么新建：BattleFlowTests / BattleArenaTests / BattleItemInventoryTests 都要用；各写一份会漂移。
//   一个顶层静态类装嵌套类型，守「一个文件一个类」。每个假实现都只记录调用、按测试脚本回应，不含业务判断。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Battle;
using Game.Core.Assets;
using Game.Core.Flow;
using Game.Core.Timing;
using Game.Core.UI;
using Game.TurnBased;
using MessagePipe;
using UnityEngine;
using UnityEngine.SceneManagement;
using StageEvent = Game.Narrative.BattleStageEnteredEvent;

namespace Game.Tests.EditMode.Battle
{
    public static class BattleFakes
    {
        /// <summary>
        /// 记录三件事的剧情口；Holding = 当前是否登记着「战斗在途」（即自动存档被压住）。
        /// 回写可注入：OnComplete 在回写那一刻跑（测时序）；ThrowOnComplete 让回写抛；HoldComplete 让回写挂起直到 FinishComplete()。
        /// </summary>
        public sealed class Narrative : IBattleNarrative
        {
            private UniTaskCompletionSource<bool> pendingComplete;

            public bool AcceptBegin { get; set; } = true;
            public bool AcceptComplete { get; set; } = true;
            public Action OnComplete { get; set; }
            public Exception ThrowOnComplete { get; set; }
            public bool HoldComplete { get; set; }
            public bool Holding { get; private set; }
            public int BeginCalls { get; private set; }
            public int ReleaseCalls { get; private set; }
            public List<string> Completed { get; } = new List<string>();
            /// <summary>与 <see cref="Completed"/> 一一对应：每次回写带的阶段身份（测「回写的是哪一场」）。</summary>
            public List<StageEvent> CompletedStages { get; } = new List<StageEvent>();
            /// <summary>每次放弃在途登记带的阶段身份（测「哪一场让位 / 放弃了」）。</summary>
            public List<StageEvent> ReleasedStages { get; } = new List<StageEvent>();
            public StageEvent? LastStage { get; private set; }

            public bool TryBeginBattle(StageEvent stage)
            {
                BeginCalls++;
                LastStage = stage;
                if (!AcceptBegin) return false;
                Holding = true;
                return true;
            }

            public void ReleaseBattle(StageEvent stage)
            {
                ReleaseCalls++;
                ReleasedStages.Add(stage);
                Holding = false;
            }

            public UniTask<bool> CompleteBattleAsync(StageEvent stage, string exitKey, CancellationToken ct)
            {
                Completed.Add(exitKey);
                CompletedStages.Add(stage);
                OnComplete?.Invoke();
                if (ThrowOnComplete != null) return UniTask.FromException<bool>(ThrowOnComplete);
                if (HoldComplete)
                {
                    pendingComplete = new UniTaskCompletionSource<bool>();
                    return pendingComplete.Task;
                }

                if (AcceptComplete) Holding = false; // 真实剧情：结果被接受即换阶段，在途标记随旧阶段一起消失
                return UniTask.FromResult(AcceptComplete);
            }

            /// <summary>放行被 HoldComplete 挂起的回写（按 AcceptComplete 给结果）。</summary>
            public void FinishComplete()
            {
                if (AcceptComplete) Holding = false;
                pendingComplete?.TrySetResult(AcceptComplete);
            }
        }

        /// <summary>门闸：默认立刻放行；Hold = true 时挂起直到 Open()。</summary>
        public sealed class Gate : IBattleWorldGate
        {
            private UniTaskCompletionSource pending;
            public bool Hold { get; set; }
            public int Waits { get; private set; }

            public UniTask WaitUntilReadyAsync(CancellationToken ct)
            {
                Waits++;
                if (!Hold) return UniTask.CompletedTask;
                pending = new UniTaskCompletionSource();
                ct.Register(() => pending.TrySetCanceled(ct));
                return pending.Task;
            }

            public void Open()
            {
                Hold = false;
                pending?.TrySetResult();
            }
        }

        /// <summary>按剧本出指令的表现层；每个钩子都可以注入断言或异常。</summary>
        public sealed class Presenter : IBattlePresenter
        {
            private readonly Queue<BattleCommand> script = new Queue<BattleCommand>();
            private UniTaskCompletionSource<BattleCommand> pendingCommand;

            public int Opens { get; private set; }
            public int Closes { get; private set; }
            public int Plays { get; private set; }
            public BattleOpening? LastOpening { get; private set; }
            public List<BattleCommandMenu> Menus { get; } = new List<BattleCommandMenu>();
            public List<BattleEvent> PlayedEvents { get; } = new List<BattleEvent>();
            public Action OnOpen { get; set; }
            public Exception ThrowOnPlay { get; set; }
            public bool BlockOnCommand { get; set; }

            public void Enqueue(params BattleCommand[] commands)
            {
                foreach (BattleCommand command in commands) script.Enqueue(command);
            }

            public UniTask OpenAsync(BattleOpening opening, CancellationToken ct)
            {
                Opens++;
                LastOpening = opening;
                OnOpen?.Invoke();
                return UniTask.CompletedTask;
            }

            public UniTask PlayAsync(BattleSession session, IReadOnlyList<BattleEvent> events, CancellationToken ct)
            {
                Plays++;
                if (ThrowOnPlay != null) return UniTask.FromException(ThrowOnPlay);
                PlayedEvents.AddRange(events);
                return UniTask.CompletedTask;
            }

            public UniTask<BattleCommand> WaitCommandAsync(BattleCommandMenu menu, CancellationToken ct)
            {
                Menus.Add(new BattleCommandMenu(menu.Session, new List<BattleItemSlot>(menu.Items), menu.Rejection));
                if (BlockOnCommand)
                {
                    pendingCommand = new UniTaskCompletionSource<BattleCommand>();
                    ct.Register(() => pendingCommand.TrySetCanceled(ct));
                    return pendingCommand.Task;
                }

                if (script.Count == 0) return UniTask.FromException<BattleCommand>(new InvalidOperationException("测试剧本里的指令用完了"));
                return UniTask.FromResult(script.Dequeue());
            }

            public UniTask CloseAsync(CancellationToken ct)
            {
                Closes++;
                return UniTask.CompletedTask;
            }
        }

        /// <summary>黑幕：记落 / 揭次数，同步完成。OnCover / OnReveal 在落幕 / 揭幕那一刻跑（测「揭幕时世界是什么状态」这类时序）。</summary>
        public sealed class Curtain : ILoadingCurtain
        {
            public bool IsCovered { get; private set; }
            public int Covers { get; private set; }
            public int Reveals { get; private set; }
            public Action OnCover { get; set; }
            public Action OnReveal { get; set; }

            public UniTask CoverAsync(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                Covers++;
                IsCovered = true;
                OnCover?.Invoke();
                return UniTask.CompletedTask;
            }

            public UniTask RevealAsync(CancellationToken ct)
            {
                Reveals++;
                OnReveal?.Invoke();
                IsCovered = false;
                return UniTask.CompletedTask;
            }
        }

        /// <summary>资源服务：只支持场景加载；Missing = true 时模拟「Addressables 没有这个地址」。</summary>
        public sealed class Assets : IAssetService
        {
            public bool Missing { get; set; }
            public List<string> LoadedKeys { get; } = new List<string>();
            public List<LoadSceneMode> Modes { get; } = new List<LoadSceneMode>();
            public SceneHandle LastHandle { get; private set; }

            public UniTask<SceneHandle> LoadSceneAsync(string key, LoadSceneMode mode, CancellationToken ct = default)
            {
                LoadedKeys.Add(key);
                Modes.Add(mode);
                if (Missing) return UniTask.FromException<SceneHandle>(new InvalidOperationException("InvalidKeyException: " + key));
                LastHandle = new SceneHandle(key, default);
                return UniTask.FromResult(LastHandle);
            }

            public UniTask<AssetHandle<T>> LoadAsync<T>(string key, CancellationToken ct = default) where T : UnityEngine.Object => throw new NotSupportedException();
            public UniTask<IReadOnlyList<AssetHandle<T>>> LoadAllAsync<T>(string label, CancellationToken ct = default) where T : UnityEngine.Object => throw new NotSupportedException();
            public UniTask<GameObject> InstantiateAsync(string key, Transform parent = null, CancellationToken ct = default) => throw new NotSupportedException();
            public void ReleaseInstance(GameObject instance) => throw new NotSupportedException();
        }

        /// <summary>UI 服务：只管层显隐（初始全可见）。</summary>
        public sealed class UI : IUIService
        {
            private readonly HashSet<UILayer> hidden = new HashSet<UILayer>();
            public UniTask<T> OpenAsync<T>(object arg = null, CancellationToken ct = default) where T : UIView => throw new NotSupportedException();
            public UniTask CloseAsync(UIView view, CancellationToken ct = default) => UniTask.CompletedTask;
            public UniTask CloseTopAsync(CancellationToken ct = default) => UniTask.CompletedTask;
            public T Get<T>() where T : UIView => null;

            public void SetLayerVisible(UILayer layer, bool visible)
            {
                if (visible) hidden.Remove(layer);
                else hidden.Add(layer);
            }

            public bool IsLayerVisible(UILayer layer) => !hidden.Contains(layer);
        }

        /// <summary>暂停服务：按持有者计数。</summary>
        public sealed class Pause : IWorldPauseService
        {
            private readonly HashSet<object> holders = new HashSet<object>();
            public bool IsPaused => holders.Count > 0;

            public IDisposable Acquire(object owner)
            {
                if (owner == null) throw new ArgumentNullException(nameof(owner));
                holders.Add(owner);
                return new Release(() => holders.Remove(owner));
            }
        }

        /// <summary>内存背包：int id → 数量 + 消耗品集合 + 名字。</summary>
        public sealed class Backpack : IBattleBackpack
        {
            private readonly Dictionary<int, int> items = new Dictionary<int, int>();
            private readonly HashSet<int> consumables = new HashSet<int>();

            public IReadOnlyDictionary<int, int> Items => items;

            public Backpack Add(int id, int count, bool consumable)
            {
                items[id] = count;
                if (consumable) consumables.Add(id);
                return this;
            }

            public int CountOf(int id) => items.TryGetValue(id, out int count) ? count : 0;
            public bool IsConsumable(int itemId) => consumables.Contains(itemId);
            public string NameOf(int itemId) => "道具" + itemId;

            public bool TryConsumeOne(int itemId)
            {
                if (!items.TryGetValue(itemId, out int count) || count <= 0) return false;
                if (count == 1) items.Remove(itemId);
                else items[itemId] = count - 1;
                return true;
            }
        }

        /// <summary>同步消息总线（发布即在当前调用栈里通知订阅者，同 MessagePipe 的行为）。</summary>
        public sealed class Bus<T> : ISubscriber<T>, IPublisher<T>
        {
            private readonly List<IMessageHandler<T>> handlers = new List<IMessageHandler<T>>();
            public int Count => handlers.Count;

            public IDisposable Subscribe(IMessageHandler<T> handler, params MessageHandlerFilter<T>[] filters)
            {
                handlers.Add(handler);
                return new Release(() => handlers.Remove(handler));
            }

            public void Publish(T message)
            {
                foreach (IMessageHandler<T> handler in handlers.ToArray()) handler.Handle(message);
            }
        }

        public sealed class Release : IDisposable
        {
            private Action action;
            public Release(Action action) => this.action = action;

            public void Dispose()
            {
                action?.Invoke();
                action = null;
            }
        }
    }
}
