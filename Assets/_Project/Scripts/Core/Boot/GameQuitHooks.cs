// 职责：退出前钩子的执行器——登记 / 注销钩子，退出时按登记顺序逐个 await，单个钩子失败不阻断，总超时兜底。
// 为什么新建（复用 → 扩展 → 新增）：
//   1. 复用不行：工程里没有「退出前跑一串异步回调」的现成件；Application.quitting 是同步事件，等不了异步写盘。
//   2. 扩展不行：GameQuit 是静态门面，逻辑直接写进去就只能靠真的退出来测；抽成纯类（不碰 Unity 生命周期，
//      计时器可注入）才能在 EditMode 里同步测顺序、异常与超时。GameQuit 只持有它的一个实例并转发。

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Logging;

namespace Game.Core.Boot
{
    /// <summary>
    /// 退出前钩子表。<see cref="Register"/> 登记一个异步钩子，返回的句柄 Dispose 即注销；
    /// <see cref="RunAllAsync"/> 按登记顺序串行 await 全部钩子。
    /// <para>
    /// 失败策略：单个钩子抛异常（同步抛或异步抛都算）记一条 Error 后继续下一个——
    /// 一个模块的收尾出错不能让别的模块连收尾的机会都没有。
    /// 总超时到了就不再等：正在跑的那个钩子不会被掐断（没法安全掐断），但之后的钩子不再启动，<see cref="RunAllAsync"/> 立即返回。
    /// </para>
    /// </summary>
    public sealed class GameQuitHooks
    {
        private readonly List<Func<UniTask>> hooks = new List<Func<UniTask>>();
        private readonly Func<TimeSpan, CancellationToken, UniTask> delay;

        /// <summary>正式路径用的构造：超时按真实时间计（暂停菜单里退出时 timeScale 可能是 0）。</summary>
        public GameQuitHooks()
            : this((timeout, ct) => UniTask.Delay(timeout, DelayType.Realtime, PlayerLoopTiming.Update, ct))
        {
        }

        /// <summary>
        /// 可注入计时器的构造（测试用）：<paramref name="delay"/> 返回的任务完成即视为超时到了；
        /// 钩子全部跑完时会取消传给它的令牌。
        /// </summary>
        public GameQuitHooks(Func<TimeSpan, CancellationToken, UniTask> delay)
        {
            this.delay = delay ?? throw new ArgumentNullException(nameof(delay));
        }

        /// <summary>当前登记的钩子数。</summary>
        public int Count => hooks.Count;

        /// <summary>
        /// 登记一个退出前钩子。同一个委托登记两次就会跑两次（各自一个句柄）。
        /// 返回的句柄 Dispose 后该钩子不再参与之后的 <see cref="RunAllAsync"/>；重复 Dispose 是空操作。
        /// </summary>
        public IDisposable Register(Func<UniTask> hook)
        {
            if (hook == null) throw new ArgumentNullException(nameof(hook));
            hooks.Add(hook);
            return new Registration(this, hook);
        }

        /// <summary>
        /// 按登记顺序逐个 await 全部钩子，最多等 <paramref name="timeout"/>。
        /// 本方法自己不抛异常（钩子的异常都已记 Error 吞掉）。执行期间登记 / 注销的钩子不影响这一轮（开跑时取快照）。
        /// </summary>
        public async UniTask RunAllAsync(TimeSpan timeout)
        {
            if (hooks.Count == 0)
            {
                return;
            }

            Func<UniTask>[] snapshot = hooks.ToArray();
            using (var stop = new CancellationTokenSource())
            {
                UniTask run = RunSequentialAsync(snapshot, stop.Token);
                UniTask timer = SafeDelayAsync(timeout, stop.Token);

                int winner = await UniTask.WhenAny(run, timer);

                // 两边谁先完成都要掐掉另一边：钩子跑完了就撤计时器；超时了就让串行循环别再启动下一个钩子。
                stop.Cancel();

                if (winner == 1)
                {
                    Log.Warn($"退出前钩子超过 {timeout.TotalSeconds:0.##} 秒仍未全部完成，不再等待，直接退出");
                }
            }
        }

        private static async UniTask RunSequentialAsync(Func<UniTask>[] snapshot, CancellationToken stop)
        {
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (stop.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    await snapshot[i]();
                }
                catch (Exception e)
                {
                    // 记下是第几个、属于哪个类的哪个方法：退出时出的错玩家看不到，只能靠日志追。
                    string owner = snapshot[i].Method.DeclaringType == null
                        ? snapshot[i].Method.Name
                        : snapshot[i].Method.DeclaringType.Name + "." + snapshot[i].Method.Name;
                    Log.Error($"退出前钩子 #{i + 1}（{owner}）执行失败，继续执行后面的钩子：{e}");
                }
            }
        }

        /// <summary>计时器被取消（钩子先跑完）是正常路径，不让取消异常冒出去。</summary>
        private async UniTask SafeDelayAsync(TimeSpan timeout, CancellationToken stop)
        {
            try
            {
                await delay(timeout, stop);
            }
            catch (OperationCanceledException)
            {
                // 钩子已经跑完，WhenAny 已有结果，这里的完成不会再被当成超时。
            }
        }

        private void Unregister(Func<UniTask> hook) => hooks.Remove(hook);

        /// <summary>登记句柄：Dispose 注销对应钩子，只生效一次。</summary>
        private sealed class Registration : IDisposable
        {
            private GameQuitHooks owner;
            private readonly Func<UniTask> hook;

            public Registration(GameQuitHooks owner, Func<UniTask> hook)
            {
                this.owner = owner;
                this.hook = hook;
            }

            public void Dispose()
            {
                if (owner == null)
                {
                    return;
                }

                owner.Unregister(hook);
                owner = null;
            }
        }
    }
}
