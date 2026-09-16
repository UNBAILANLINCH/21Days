// 职责：输入源的切换闸。容器里注册的是它，玩法注入到的引用永远不变；
//   回放开始 / 结束只换它内部指向的那个源。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：工程内没有任何「可在运行时换实现的转发壳」。VContainer 支持按条件注册，
//      但那是**建容器时**决定的，回放是运行中随时开关，两回事。
//   2. 扩展不行：不能把切换塞进 LiveInputSource（它的职责是采样设备，不该知道有重放这回事），
//      也不能塞进 IInputSource 接口本身（那会让每个实现都得实现一遍切换，包括根本不需要切换的录像源）。

using Game.Core.Logging;

namespace Game.Core.Simulation
{
    /// <summary>
    /// 转发 <see cref="IInputSource"/>，按当前模式把 <see cref="Current"/> 转给实时源或录像源。
    /// <para>
    /// <b>为什么不靠重建容器来换输入源：</b>重建会连带重置所有单例服务（资源、配置、存档），
    /// 而回放时这些恰恰要保持不变——它们一重置，重放出来的就不是原来那局了。
    /// 而且玩法可能已经把 <see cref="IInputSource"/> 缓存进自己的字段，换实例会让这些字段
    /// 继续指向旧对象：一半换了、一半没换，在回放里表现为「某些系统在放录像，另一些还在收实时输入」
    /// 的缓慢漂移，比崩溃难查得多。所以这里换的是**指向**，不是**实例**。
    /// </para>
    /// <para>
    /// <see cref="Current"/> 是每 tick 都要读的，所以内部用一个 <c>active</c> 指针直接转发，
    /// 不在读路径上判断模式；<c>active</c> 任何时候都不为 null。
    /// </para>
    /// </summary>
    public sealed class InputSourceSwitch : IInputSource
    {
        private readonly IInputSource liveSource;

        private IInputSource replaySource;
        private IInputSource active;

        /// <param name="liveSource">实时输入源，通常是 <see cref="LiveInputSource"/>。整个生命周期不变。</param>
        public InputSourceSwitch(IInputSource liveSource)
        {
            if (liveSource == null)
            {
                Log.Warn("InputSourceSwitch 没拿到实时输入源，切回 live 时将只产出空命令");
                this.liveSource = EmptyInputSource.Instance;
            }
            else
            {
                this.liveSource = liveSource;
            }

            active = this.liveSource;
        }

        /// <summary>
        /// 把采样转给当前生效的那个源。和 <see cref="Current"/> 一样走 <c>active</c> 指针直接转发，
        /// 读路径上不判断模式——采样与取值必须落在同一个源上，中间多一个分支就多一处让两者
        /// 错开到不同源的机会。
        /// </summary>
        /// <param name="tick">推进器正要推进的那个 tick 号，原样透传。</param>
        public void Sample(long tick) => active.Sample(tick);

        /// <summary>当前生效的那个源给出的命令。</summary>
        public InputCommand Current => active.Current;

        /// <summary>当前是否在放录像。</summary>
        public bool IsReplaying => replaySource != null;

        /// <summary>当前生效的源。诊断用，玩法不该拿它绕过本类直接读。</summary>
        public IInputSource ActiveSource => active;

        /// <summary>
        /// 切到实时输入。回放结束时调；会松开对录像源的引用，让它能被回收。
        /// </summary>
        public void SwitchToLive()
        {
            replaySource = null;
            active = liveSource;
            Log.Info("输入源切回实时");
        }

        /// <summary>
        /// 切到指定的录像源。回放开始时调。
        /// </summary>
        /// <param name="source">录像输入源。为 null 时记 Warn 并留在当前源，不会让读路径拿到 null。</param>
        public void SwitchToReplay(IInputSource source)
        {
            if (source == null)
            {
                Log.Warn("SwitchToReplay 收到 null 录像源，已忽略，输入源保持不变");
                return;
            }

            replaySource = source;
            active = source;
            Log.Info("输入源切到录像回放");
        }

        /// <summary>
        /// 兜底的空输入源。只在构造时没拿到实时源的异常情形下顶上，
        /// 让 <see cref="Current"/> 的读路径永远不必判空。
        /// </summary>
        private sealed class EmptyInputSource : IInputSource
        {
            internal static readonly EmptyInputSource Instance = new EmptyInputSource();

            private EmptyInputSource()
            {
            }

            /// <summary>空实现：没有设备也没有录像可采，<see cref="Current"/> 恒为空命令。</summary>
            public void Sample(long tick)
            {
            }

            public InputCommand Current => InputCommand.Empty;
        }
    }
}
