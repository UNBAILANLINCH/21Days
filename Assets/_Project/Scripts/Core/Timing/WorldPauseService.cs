// 职责：IWorldPauseService 的唯一实现——按持有者引用计数，0→1 时冻结 Time.timeScale 与逻辑 tick，1→0 时恢复。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：SimulationRunner.SetPaused 只管逻辑 tick，不碰 Time.timeScale；而对话 / 菜单暂停要的是
//      「逻辑 tick 停 + 引擎动画、物理、scaled 定时器也停」，只调 SetPaused 表现层照样在跑。
//   2. 扩展不行：不能把 timeScale 塞进 SimulationRunner——它是确定性内核，刻意与 timeScale 解耦
//      （见其文件头「步长受 timeScale 影响」一条），让它去改 timeScale 等于把渲染帧时间重新搅进逻辑层。
//      更关键的是 timeScale 是全局单值：两处各自「记旧值 → 置 0 → 恢复旧值」会互相覆盖
//      （A 暂停、B 暂停、A 恢复 → 世界在 B 还没关时就动了），必须有唯一持有者做引用计数，这就是本类。

using System;
using System.Collections.Generic;
using Game.Core.Simulation;
using UnityEngine;

namespace Game.Core.Timing
{
    /// <summary>
    /// 世界暂停服务。契约见 <see cref="IWorldPauseService"/>。
    /// <para>本类是全工程**唯一**允许写 <c>Time.timeScale</c> 的地方。</para>
    /// <para>服务 Dispose（作用域销毁）时释放全部持有者并恢复 timeScale，免得退出后留下一个冻住的世界。</para>
    /// </summary>
    public sealed class WorldPauseService : IWorldPauseService, IDisposable
    {
        private readonly SimulationRunner runner;
        private readonly Dictionary<object, Token> holders = new Dictionary<object, Token>();

        /// <summary>第一个持有者进来前的 timeScale，最后一个持有者离开时原样还回去。</summary>
        private float savedTimeScale = 1f;

        /// <summary>
        /// 构造暂停服务。
        /// </summary>
        /// <param name="runner">逻辑推进器；暂停时以本服务为 owner 调 <see cref="SimulationRunner.SetPaused"/>。</param>
        public WorldPauseService(SimulationRunner runner)
        {
            this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        /// <inheritdoc />
        public bool IsPaused => holders.Count > 0;

        /// <inheritdoc />
        public IDisposable Acquire(object owner)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (holders.TryGetValue(owner, out Token existing))
            {
                return existing;
            }

            Token token = new Token(this, owner);
            holders.Add(owner, token);
            if (holders.Count == 1)
            {
                Freeze();
            }

            return token;
        }

        /// <summary>释放全部持有者并恢复世界。可重复调用。</summary>
        public void Dispose()
        {
            if (holders.Count == 0)
            {
                return;
            }

            foreach (Token token in holders.Values)
            {
                token.MarkReleased();
            }

            holders.Clear();
            Unfreeze();
        }

        private void Release(Token token)
        {
            // 只认仍登记在册的那一枚：服务 Dispose 后旧令牌再 Dispose 不应有任何副作用
            if (!holders.TryGetValue(token.Owner, out Token registered) || !ReferenceEquals(registered, token))
            {
                return;
            }

            holders.Remove(token.Owner);
            if (holders.Count == 0)
            {
                Unfreeze();
            }
        }

        private void Freeze()
        {
            savedTimeScale = Time.timeScale;
            Time.timeScale = 0f; // lint-ok: 世界暂停服务是 timeScale 的唯一持有者
            runner.SetPaused(this, true);
        }

        private void Unfreeze()
        {
            Time.timeScale = savedTimeScale; // lint-ok: 世界暂停服务是 timeScale 的唯一持有者
            runner.SetPaused(this, false);
        }

        /// <summary>单个持有者的暂停令牌。Dispose 幂等。</summary>
        private sealed class Token : IDisposable
        {
            private WorldPauseService service;

            public Token(WorldPauseService service, object owner)
            {
                this.service = service;
                Owner = owner;
            }

            public object Owner { get; }

            public void Dispose()
            {
                WorldPauseService current = service;
                if (current == null)
                {
                    return;
                }

                service = null;
                current.Release(this);
            }

            /// <summary>服务整体释放时调：之后本令牌的 Dispose 什么都不做。</summary>
            public void MarkReleased()
            {
                service = null;
            }
        }
    }
}
