// 职责：IReplayStateProvider 的框架默认实现——把各模块注册进来的 IReplayState 按注册顺序串成一张表，
//   序列化时按同一顺序依次写出、反序列化时按同一顺序依次读回。这张表的顺序就是回放快照的字节布局本身。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：工程里没有这个契约的任何实现。唯一一份是 Tests/Showcase/Replay/ReplayShowcase.cs 里的
//      private 嵌套类 DemoStateProvider——它是那条用例自带的测试替身，住在 Game.Tests.Showcase 程序集里，
//      Core 既够不着也不许反向引用（依赖方向只许 Tests → Runtime → Core）。于是真实启动路径上
//      至今没有任何东西实现它：容器建完，ReplayRecorder 与 ReplayPlayer 手上的 stateProvider 都是 null，
//      状态哈希、完整快照、漂移检测、快照续跑全部静默空转——Showcase 能跑通只是因为它自带了替身，
//      把这个缺口挡住了。本文件补的就是这个缺口。
//   2. 扩展不行：不能把这张表实现在 ReplayRecorder 或 ReplayPlayer 身上。两者各持一份注册表，
//      就会出现两套注册顺序 = 两套字节布局，而回放成立的前提恰恰是「录的时候和放的时候布局逐字一致」；
//      并且这两个类的职责是「环怎么滚、文件怎么写」和「按帧推几格、哪里比哈希」，
//      再多一份注册表会让它们同时成为世界状态的持有者。也不能塞进 StateBuffer：
//      那是字节容器，只认识「字节怎么摆」，不认识「有哪些状态、谁先谁后」。
//   3. 新建，且只做一件事：维护顺序 + 按顺序转发。它刻意不认识任何玩法类型，Core 里零玩法名词。

using System;
using Game.Core.Logging;

namespace Game.Core.Replay
{
    /// <summary>
    /// 世界状态注册表的框架默认实现。接线时注册进容器（见 <c>GameLifetimeScope.RegisterReplay</c>），
    /// 开局由各玩法模块把自己的 <see cref="IReplayState"/> 注册进来，
    /// 之后 <see cref="ReplayRecorder"/> 与 <see cref="ReplayPlayer"/> 统一从这里取字节。
    ///
    /// <para>
    /// <b>玩法模块怎么用</b>：注入 <see cref="IReplayStateProvider"/>（注入接口，不要注入本类），
    /// 在**开局接线那一处**把状态注册进去，之后再也不动。注册顺序写死在接线代码里，
    /// 不要让它取决于谁先被解析、谁先 Awake、场景谁先加载——那样同一份代码在两台机器上
    /// 可能排出两种布局，而这种问题只会在别人的机器上偶发。
    /// </para>
    /// <code>
    /// // Scripts/Runtime/Battle/BattleState.cs —— 模块自己的状态
    /// public sealed class BattleState : IReplayState
    /// {
    ///     private int hp;
    ///     private Vector2 position;
    ///
    ///     // 两个方法逐行镜像：写几个字段就读几个，顺序、类型一一对应
    ///     public void Serialize(IStateWriter writer)
    ///     {
    ///         writer.WriteInt(hp);
    ///         writer.WriteVector2(position);
    ///     }
    ///
    ///     public void Deserialize(IStateReader reader)
    ///     {
    ///         hp = reader.ReadInt();
    ///         position = reader.ReadVector2();
    ///     }
    /// }
    ///
    /// // Scripts/Runtime/Battle/BattleInstaller.cs —— 继承 GameplayInstaller，挂在 GameBootstrap 物体上
    /// public override void Install(IContainerBuilder builder)
    /// {
    ///     builder.Register&lt;BattleState&gt;(Lifetime.Singleton).AsSelf();
    ///     builder.Register&lt;BattleTimers&gt;(Lifetime.Singleton).AsSelf();
    ///
    ///     // 注册顺序 = 字节布局，就写死在这两行的先后上。RegisterBuildCallback 里才 Resolve：
    ///     // Install 执行时容器还没建好（见 GameplayInstaller 的注释），回调则在建好之后跑。
    ///     builder.RegisterBuildCallback(resolver =&gt;
    ///     {
    ///         IReplayStateProvider replayStates = resolver.Resolve&lt;IReplayStateProvider&gt;();
    ///         replayStates.Register(resolver.Resolve&lt;BattleState&gt;());
    ///         replayStates.Register(resolver.Resolve&lt;BattleTimers&gt;());
    ///     });
    /// }
    /// </code>
    /// <para>
    /// <b>调换这两行、或者增删任何一行，都要同步升 <see cref="ReplayFormat.CurrentFormatVersion"/></b>——
    /// 靠版本号把老录像硬拒掉。不升的话老录像会被按新布局解读，从第一个字段起全错，还多半不抛异常。
    /// </para>
    ///
    /// <para>
    /// <b>零个状态是合法状态</b>：纯框架启动、玩法还没接进来时，<see cref="SerializeAll"/> 什么都不写，
    /// 对空内容算出的哈希恒为 <see cref="EmptyStateHash"/>（确定的常量），不抛异常。
    /// 但这种情况会记一条 Info，也在 <see cref="IsEmpty"/> 上体现——「录了一场只有输入的回放」
    /// 是有意义的信息，静默过去的话，人会以为漂移检测一直在跑，实际上它一次都没跑过。
    /// </para>
    ///
    /// <para>分配约定：注册在接线期，那时扩容一次没关系；<see cref="SerializeAll"/> /
    /// <see cref="DeserializeAll"/> 在周期性路径上（哈希每秒一次、快照每十秒一次），
    /// 走的是定长数组上的下标循环，稳态零堆分配，不用 foreach（迭代器在接口上会装箱）。</para>
    /// <para>线程约定：只在主线程 / 逻辑线程上用，内部不加锁。</para>
    /// </summary>
    public sealed class ReplayStateRegistry : IReplayStateProvider
    {
        /// <summary>
        /// 零个状态时 <see cref="SerializeAll"/> 产出空内容，对它算哈希恒为这个值
        /// （FNV-1a 的 offset basis，一个字节都没喂进去时的结果）。
        /// 「没有状态」于是有一个确定的、可断言的哈希，而不是某个碰巧的数。
        /// </summary>
        public const ulong EmptyStateHash = StateHasher.OffsetBasis;

        /// <summary>初始容量。玩法模块的状态是按模块数计的，一局几个到十几个，8 起手基本不用扩。</summary>
        private const int InitialCapacity = 8;

        /// <summary>
        /// 按注册顺序排的状态表。用定长数组而不是 List：序列化路径上要的是下标循环，
        /// 数组少一层间接，也免得有人顺手在上面写 foreach / LINQ。
        /// </summary>
        private IReplayState[] states = new IReplayState[InitialCapacity];

        private int count;

        /// <summary>已经出过字节了没有。出过就意味着布局已经被人（环里的快照、录像文件）依赖上了。</summary>
        private bool layoutPublished;

        private int lateRegisterCount;

        /// <summary>空注册表的 Info 只报一次。反复报的话它会在每秒一次的路径上刷屏，然后被人忽略。</summary>
        private bool emptyNoticeLogged;

        /// <summary>迟到注册的 Warn 只报一次，次数看 <see cref="LateRegisterCount"/>。</summary>
        private bool lateRegisterWarned;

        /// <inheritdoc />
        public int Count => count;

        /// <summary>
        /// 一个状态都没注册。诊断用：为 true 说明这一局录出来的回放只有输入流，
        /// 没有状态哈希也没有完整快照，漂移检测与快照续跑都不会生效。
        /// </summary>
        public bool IsEmpty => count == 0;

        /// <summary>
        /// 在已经产出过状态字节之后又注册了几次。诊断用：<b>不为 0 就是接线有问题</b>——
        /// 注册点没定死在开局，布局在一局中途变长了，那之前存进环里的快照会按新布局被读短一截。
        /// </summary>
        public int LateRegisterCount => lateRegisterCount;

        /// <summary>
        /// 注册一份世界状态。<b>调用顺序即序列化顺序</b>，约束见 <see cref="IReplayStateProvider"/>。
        /// <para>
        /// 同一个实例注册两次直接抛异常，<b>不静默跳过、也不真的收两遍</b>：收两遍会让这份状态的字节
        /// 被写两份，布局悄悄变长，而且两份之间还会彼此覆盖着读回去；静默跳过则会把一个接线 bug
        /// 藏起来——调用方以为注册进去了两个不同的东西，实际只有一个。抛在接线期是最便宜的发现时机。
        /// </para>
        /// </summary>
        /// <param name="state">要纳入快照与哈希的状态对象，不可为空。</param>
        /// <exception cref="ArgumentNullException">state 为 null。</exception>
        /// <exception cref="InvalidOperationException">这个实例已经注册过了。</exception>
        public void Register(IReplayState state)
        {
            // state 的静态类型是接口，这里走的是普通引用比较；即便某个模块把 IReplayState 实现在
            // MonoBehaviour 上，「已销毁的伪空对象」也不该在接线期出现——真出现了，问题在接线点太晚，
            // 不在这一行的判空写法上。
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            for (int i = 0; i < count; i++)
            {
                if (ReferenceEquals(states[i], state))
                {
                    throw new InvalidOperationException(
                        $"世界状态 {state.GetType().Name} 已经注册过了（第 {i} 位）。"
                        + "重复注册会让它的字节被写两份、布局变长，回放的快照与哈希跟着全错。"
                        + "接线点只该有一处，检查是不是两个 Installer 都注册了它，"
                        + "或者某个状态在构造里自注册的同时接线处又注册了一次。");
                }
            }

            if (layoutPublished)
            {
                lateRegisterCount++;
                if (!lateRegisterWarned)
                {
                    lateRegisterWarned = true;
                    Log.Warn(
                        $"世界状态 {state.GetType().Name} 是在回放已经产出过状态字节之后才注册的："
                        + "字节布局从这一刻起变长了，本局之前存进环里的快照会按新布局被读短一截，"
                        + "跳回那些快照时世界会被恢复成错的。注册必须集中在开局接线点上，"
                        + "不要等到场景加载、或某个状态第一次被用到时才注册。只报这一条，次数看 LateRegisterCount。");
                }
            }

            if (count == states.Length)
            {
                // 只会发生在接线期（状态数超过初始容量那一次），周期性路径上不会走到这里。
                Array.Resize(ref states, states.Length * 2);
            }

            states[count] = state;
            count++;
        }

        /// <summary>
        /// 按注册顺序把所有状态写进 <paramref name="writer"/>。状态哈希与完整快照共用这一段字节，
        /// 所以不可能出现「进了快照但没进哈希」的字段。
        /// </summary>
        /// <param name="writer">写入器，不可为空。</param>
        /// <exception cref="ArgumentNullException">writer 为 null。</exception>
        public void SerializeAll(IStateWriter writer)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            layoutPublished = true;

            if (count == 0)
            {
                ReportEmptyOnce();
                return;
            }

            for (int i = 0; i < count; i++)
            {
                states[i].Serialize(writer);
            }
        }

        /// <summary>
        /// 按注册顺序把所有状态从 <paramref name="reader"/> 读回来，顺序与 <see cref="SerializeAll"/> 严格一致。
        /// </summary>
        /// <param name="reader">读取器，不可为空。</param>
        /// <exception cref="ArgumentNullException">reader 为 null。</exception>
        public void DeserializeAll(IStateReader reader)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            if (count == 0)
            {
                ReportEmptyOnce();
                return;
            }

            for (int i = 0; i < count; i++)
            {
                states[i].Deserialize(reader);
            }
        }

        /// <summary>
        /// 一个状态都没有时报一条 Info，整个实例只报一次。
        /// <para>用 Info 不用 Warn：纯框架启动（玩法还没接进来）时这就是正常状态，
        /// 常态刷 Warn 会训练人忽略警告。消息是字符串字面量，不拼接，报完之后这条路径不再产生分配。</para>
        /// </summary>
        private void ReportEmptyOnce()
        {
            if (emptyNoticeLogged)
            {
                return;
            }

            emptyNoticeLogged = true;
            Log.Info(
                "回放状态注册表里一个世界状态都没有：本局录出来的回放只有输入流，"
                + "没有状态哈希也没有完整快照，漂移检测、起点恢复与快照续跑都不会生效。"
                + "纯框架启动（玩法模块还没接进来）时这是正常的；玩法已经在跑却看到这条，"
                + "就是模块忘了把自己的 IReplayState 注册进 IReplayStateProvider，"
                + "写法见 ReplayStateRegistry 的类注释。");
        }
    }
}
