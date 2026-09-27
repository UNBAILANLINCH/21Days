// 职责：已读记录的独立档案读写——启动时把 "dialogue-read" 档案灌进 DialogueReadData 单例，对白结束后合并写出。
// 为什么新建：DialogueRules 只管推进语义、DialogueService 只管会话资源，二者都不该认识存档服务；
//   DialogueReadData 是纯数据且故意不实现 ISaveData（见其文件头）。落盘时机与节流是独立职责，没有现成文件能承担。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Boot;
using Game.Core.Logging;
using Game.Core.Save;

namespace Game.Dialogue
{
    /// <summary>
    /// 已读档案存取。注册在 <see cref="DialogueService"/> 之后（IGameService 按注册顺序串行初始化，
    /// 存档服务在 Core 里更早注册，初始化时已就绪）。
    /// <para>
    /// 读入是**并集**：只往单例里加键、不清空，已读记录永不倒退；单例始终是容器里的同一个实例，
    /// <see cref="DialogueRules"/> 手里的引用不失效。
    /// </para>
    /// <para>
    /// 写出节流：<see cref="DialogueService.OnEnded"/> 只标记脏，下一帧合并成一次 <c>WriteProfileAsync</c>；
    /// 同一时刻最多一条写循环，写的途中又有对白结束则写完再补写一次。写失败记 Error 不抛，脏标记保留到下次结束重试。
    /// </para>
    /// </summary>
    public sealed class DialogueReadStore : IGameService, IDisposable
    {
        /// <summary>独立档案名，落盘为存档根目录下的 <c>profile-dialogue-read.json</c>。</summary>
        public const string ProfileName = "dialogue-read";

        private readonly DialogueReadData read;
        private readonly ISaveService saves;
        private readonly Action<Action<DialogueEndedEvent>> subscribeEnded;
        private readonly Action<Action<DialogueEndedEvent>> unsubscribeEnded;
        private readonly Action<DialogueEndedEvent> endedHandler;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();

        private bool subscribed;
        private bool dirty;
        private bool flushing;
        private bool disposed;

        /// <summary>容器用的构造：订阅对白服务的结束事件。</summary>
        public DialogueReadStore(DialogueReadData read, ISaveService saves, DialogueService dialogue)
            : this(read, saves, SubscribeOf(dialogue), UnsubscribeOf(dialogue))
        {
        }

        /// <summary>
        /// 测试用构造：结束事件的订阅 / 退订由调用方给。<see cref="DialogueService"/> 依赖一整套 UI / 输入 / 暂停服务，
        /// 测试只关心「结束 → 合并写」，用这条缝换成一个普通 C# 事件即可触发。
        /// </summary>
        public DialogueReadStore(DialogueReadData read, ISaveService saves,
            Action<Action<DialogueEndedEvent>> subscribeEnded, Action<Action<DialogueEndedEvent>> unsubscribeEnded)
        {
            this.read = read ?? throw new ArgumentNullException(nameof(read));
            this.saves = saves ?? throw new ArgumentNullException(nameof(saves));
            this.subscribeEnded = subscribeEnded ?? throw new ArgumentNullException(nameof(subscribeEnded));
            this.unsubscribeEnded = unsubscribeEnded ?? throw new ArgumentNullException(nameof(unsubscribeEnded));
            endedHandler = HandleEnded;
        }

        public async UniTask InitializeAsync(CancellationToken ct)
        {
            // 档案不存在 → new T()（空）；损坏 → 存档服务挪走现场并返回空；高版本 → 拒读返回空、文件原样保留。
            DialogueReadProfile profile = await saves.ReadProfileAsync<DialogueReadProfile>(ProfileName, Sanitize, ct);
            int before = read.Keys.Count;
            foreach (string key in profile.Keys)
            {
                // 空键视为手改或旧版本残留，跳过而不是把整份档案当损坏挪走。
                if (!string.IsNullOrEmpty(key)) read.Keys.Add(key);
            }

            // 读完再订阅：读入前若有写出，会用不完整的集合覆盖磁盘上的档案。
            if (!disposed && !subscribed)
            {
                subscribeEnded(endedHandler);
                subscribed = true;
            }

            Log.Info($"DialogueReadStore 就绪：已读记录 {read.Keys.Count} 条（档案读入 {read.Keys.Count - before} 条）");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (subscribed)
            {
                unsubscribeEnded(endedHandler);
                subscribed = false;
            }

            // 有待写的数据：写循环在等帧时取消等待、立即写；没有写循环就起一条。
            // 这里不用 GetAwaiter().GetResult() 同步等：写盘在线程池上跑、完成要回主线程，主线程阻塞等它会死锁；
            // 所以改为 Forget()，代价是退出时进程若先结束，最后这次写可能来不及落盘（已读记录丢最近一段，不影响槽位存档）。
            lifetime.Cancel();
            if (dirty && !flushing)
            {
                flushing = true;
                FlushLoopAsync().Forget();
            }
        }

        private void HandleEnded(DialogueEndedEvent _)
        {
            dirty = true;
            if (flushing || disposed) return;
            flushing = true;
            FlushLoopAsync().Forget();
        }

        private async UniTaskVoid FlushLoopAsync()
        {
            try
            {
                while (dirty)
                {
                    if (!disposed)
                    {
                        try
                        {
                            // 等到下一帧：同一帧里的多次对白结束（含事件链上连环拉起的对白）合并成一次写。
                            await UniTask.Yield(PlayerLoopTiming.Update, lifetime.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            // Dispose 取消了等帧：不再等，直接把剩下的写掉。
                        }
                    }

                    dirty = false;
                    var profile = new DialogueReadProfile { Keys = new List<string>(read.Keys) };
                    try
                    {
                        // 写盘本身不传 lifetime：Dispose 时正是要把最后一次写完。
                        await saves.WriteProfileAsync(ProfileName, profile);
                    }
                    catch (Exception e)
                    {
                        // 写失败不抛、不在这里重试（避免磁盘坏时死循环），留脏标记，下次对白结束或 Dispose 再写。
                        dirty = true;
                        Log.Error($"DialogueReadStore：写已读档案失败（{read.Keys.Count} 条），下次对白结束时重试。原因：{e.Message}");
                        break;
                    }
                }
            }
            finally
            {
                flushing = false;
            }
        }

        private static void Sanitize(DialogueReadProfile profile)
        {
            if (profile.Keys == null) profile.Keys = new List<string>();
        }

        private static Action<Action<DialogueEndedEvent>> SubscribeOf(DialogueService dialogue)
        {
            if (dialogue == null) throw new ArgumentNullException(nameof(dialogue));
            return handler => dialogue.OnEnded += handler;
        }

        private static Action<Action<DialogueEndedEvent>> UnsubscribeOf(DialogueService dialogue)
        {
            if (dialogue == null) throw new ArgumentNullException(nameof(dialogue));
            return handler => dialogue.OnEnded -= handler;
        }
    }
}
