// 职责：固定步长逻辑推进器。把不等长的渲染帧时间换算成整数个逻辑 tick，每个 tick 取一次输入、
//   按注册顺序串行跑完所有 ISimulationStep、推进一格逻辑时钟。整个确定性内核只有这一条推进路径。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：工程内没有任何固定步长推进设施。IClock 是渲染帧时间，ITimerService 是延时回调，
//      VContainer 的 ITickable 是渲染帧回调——三者都不是逻辑 tick，也都没法被外部单步驱动。
//   2. 扩展不行：不能把它加进 TimerService。那个类管的是「到点触发一次回调」，条目是动态增删的、
//      回调顺序不保证、触发点跟着渲染帧走；而推进器要的是「固定顺序、固定步长、可被外部单步」，
//      两套语义塞一个类里，回放时根本分不清某次回调到底算不算一个 tick。
//      也不放进 Core/Timing/：「渲染帧时间」和「逻辑 tick」是这套系统里最容易写混的两个概念，
//      必须在目录层面就分开，注入的时候先想清楚要的是哪一种。
//
// 为什么不用 FixedUpdate（刻意的设计决定，不是没想到）：
//   a. 没法手动单步。重放要求「我说推一格就推一格」，FixedUpdate 的节奏由引擎掌握，外部插不进去，
//      也就没法做逐 tick 比对、断点单步、快进重放。
//   b. 步长受 timeScale 影响。timeScale 一改，两次 FixedUpdate 之间的真实间隔就变了，
//      同一份录像在暂停过 / 加速过的那次运行里会推出不一样的 tick 数。
//   c. 和物理绑死。FixedUpdate 是物理步的钩子，逻辑挂上去就等于把「逻辑要不要推进」交给物理设置去决定，
//      改一次 Fixed Timestep 全部玩法手感跟着变。
//   所以推进由本类在渲染帧里自己累积、自己判断，逻辑与引擎的物理循环完全解耦。

using System;
using System.Collections.Generic;
using Game.Core.Telemetry;
using Game.Core.Timing;
using VContainer.Unity;

namespace Game.Core.Simulation
{
    /// <summary>
    /// 固定步长逻辑推进器。由 VContainer 的 EntryPoint 每渲染帧驱动（<see cref="ITickable"/>），
    /// EditMode 测试里手动调 <see cref="Tick"/> 或 <see cref="AdvanceOneTick"/>。
    /// <para>
    /// **两种模式，一条推进路径**：<see cref="Mode.Live"/> 下 <see cref="Tick"/> 累积渲染帧时间，
    /// 按固定步长决定这一帧推 0～N 个 tick；<see cref="Mode.Driven"/> 下 <see cref="Tick"/> 什么都不做，
    /// 只等外部显式调 <see cref="AdvanceOneTick"/>（重放播放器用这个模式自己控制节奏）。
    /// 两种模式共用同一个 <see cref="AdvanceOneTick"/>，差别只在**谁决定推几次**——
    /// 这是「录下来的东西能重放出同一份结果」的前提，绝不能写成两条推进路径。
    /// </para>
    /// <para>
    /// **追帧上限**：一帧最多补 <see cref="SimulationConfig.MaxCatchUpTicks"/> 个 tick，超出的时间整段丢弃，
    /// 并埋一条 <c>core.sim/tick_dropped</c>。不设上限的话，一次卡顿攒下的时间会逼着下一帧补更多 tick，
    /// 补帧本身又更耗时，滚成「卡顿 → 补帧 → 更卡」的死亡螺旋。
    /// </para>
    /// <para>每 tick 路径零堆分配：不 new 引用对象、不拼字符串、不打日志。</para>
    /// </summary>
    public sealed class SimulationRunner : ITickable
    {
        /// <summary>谁来决定这一帧推几个 tick。</summary>
        public enum Mode
        {
            /// <summary>实时：<see cref="Tick"/> 累积渲染帧时间，自己算该推几个 tick。</summary>
            Live = 0,

            /// <summary>外部驱动：<see cref="Tick"/> 什么都不做，只认显式的 <see cref="AdvanceOneTick"/>。</summary>
            Driven = 1,
        }

        /// <summary>追帧超上限、丢掉了若干 tick，属性 <c>n</c> 是丢弃数。</summary>
        private const string TickDroppedEvent = "tick_dropped";

        private readonly LogicClock logicClock;
        private readonly IClock frameClock;
        private readonly IInputSource inputSource;
        private readonly IRandomService random;
        private readonly ITelemetryScope telemetry;
        private readonly List<ISimulationStep> steps = new List<ISimulationStep>();

        // 配置在构造时抄成只读字段：一是每 tick 路径上不再碰 ScriptableObject，
        // 二是运行中改资产不会把这一局的步长改掉（改了前后两段 tick 就对不上了）。
        private readonly float fixedDeltaTime;
        private readonly int maxCatchUpTicks;

        // 余量用 double，不是 float——这是确定性要求，不是过度设计，别「简化」回 float。
        // float 累加下，同一段总时长按不同帧率喂进来会攒出不同的误差：64Hz 下喂满 1 秒，
        // 10FPS（10 × 0.1f，每笔略大于 1/10）和 200FPS（200 × 0.005f，每笔略小于 1/200）
        // 累出来的总和差约 3e-8 秒，一个在 tick 边界左边、一个在右边，推出的 tick 数就差一个。
        // 这套内核的卖点是「同样的输入产生同样的结果」，而「推进多少个 tick」不该隐含依赖帧率的
        // 浮点累加路径。改 double 的成本几乎为零，消掉的是一整类边界问题。
        private double accumulator;

        /// <summary>
        /// 构造推进器。步长与追帧上限在这里定死，之后不再读配置资产；
        /// 逻辑时钟由本类自己 new 并独占推进权，外面只能通过 <see cref="Clock"/> 读。
        /// </summary>
        /// <param name="config">步长与追帧上限的来源，不可为空。</param>
        /// <param name="frameClock">渲染帧时间，只在 <see cref="Mode.Live"/> 下用来算该推几个 tick。</param>
        /// <param name="inputSource">输入来源。实时玩是真实设备，重放时是录像读取器。</param>
        /// <param name="random">确定性随机源，原样塞进每个 tick 的上下文。</param>
        /// <param name="telemetry">埋点服务，可为 null（EditMode 测试里直接 new 时没有容器）。</param>
        public SimulationRunner(
            SimulationConfig config,
            IClock frameClock,
            IInputSource inputSource,
            IRandomService random,
            ITelemetryService telemetry)
        {
            // SimulationConfig 是 UnityEngine.Object，判空只能用 ==（Unity 重载了它来识别已销毁对象）
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            this.frameClock = frameClock ?? throw new ArgumentNullException(nameof(frameClock));
            this.inputSource = inputSource ?? throw new ArgumentNullException(nameof(inputSource));
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            this.telemetry = telemetry == null
                ? (ITelemetryScope)NullTelemetryScope.Instance
                : telemetry.Scope(TelemetryKeys.Sim);

            logicClock = new LogicClock(config.TickRate);
            fixedDeltaTime = logicClock.FixedDeltaTime;
            maxCatchUpTicks = config.MaxCatchUpTicks < 1 ? 1 : config.MaxCatchUpTicks;
        }

        /// <summary>当前模式，默认 <see cref="Mode.Live"/>。</summary>
        public Mode CurrentMode { get; private set; } = Mode.Live;

        /// <summary>逻辑时钟（只读视图）。注入方要 tick / 逻辑时间时拿它，别去碰 <see cref="LogicClock.Advance"/>。</summary>
        public ILogicClock Clock => logicClock;

        /// <summary>已注册的步骤数，供测试与调试台查看。</summary>
        public int StepCount => steps.Count;

        /// <summary>
        /// 还没凑满一个 tick 的余量秒数，恒在 [0, <see cref="ILogicClock.FixedDeltaTime"/>) 区间内。
        /// 渲染插值要用它算「这一帧处在两个 tick 之间的哪个位置」。
        /// <para>
        /// 内部按 double 累加（见字段注释），这里转回 float 输出：插值只要一个 0~1 的比例，
        /// 拿不到也不需要 double 的精度；而推进判定必须留在 double 里，两者不是一回事。
        /// </para>
        /// </summary>
        public float Accumulator => (float)accumulator;

        /// <summary>
        /// 注册一步逻辑。**按注册顺序执行**，所以先后有依赖的步骤要按依赖顺序注册。
        /// <para>不要在 <see cref="ISimulationStep.Step"/> 执行期间增删步骤——会打乱正在进行的遍历。
        /// 步骤表是开局接线时定下来的，不是运行期动态增删的东西。</para>
        /// </summary>
        public void AddStep(ISimulationStep step)
        {
            if (step == null)
            {
                throw new ArgumentNullException(nameof(step));
            }

            steps.Add(step);
        }

        /// <summary>注销一步逻辑。返回是否真的移除了。约束同 <see cref="AddStep"/>。</summary>
        public bool RemoveStep(ISimulationStep step)
        {
            return step != null && steps.Remove(step);
        }

        /// <summary>切换模式。切换时清掉未满一个 tick 的余量，免得把上一个模式攒下的零头带进下一个模式。</summary>
        public void SetMode(Mode mode)
        {
            CurrentMode = mode;
            accumulator = 0d;
        }

        /// <summary>
        /// 回到第 0 个 tick，清空余量。开始一次新录制或新重放时调；已注册的步骤不动
        /// （步骤自己的状态由各自负责重置）。
        /// </summary>
        public void Reset()
        {
            logicClock.Reset();
            accumulator = 0d;
        }

        /// <summary>
        /// 把逻辑时钟的 tick 计数挪到 <paramref name="tick"/>，并清掉未满一个 tick 的余量。
        /// <para>
        /// <b>它只挪计数——不跑任何逻辑、不取输入、不驱动任何 <see cref="ISimulationStep"/>。</b>
        /// 唯一的正当用法是：<b>世界状态已经由快照恢复好了，现在要让时钟对上那个快照的 tick</b>。
        /// 重放一份中途起点的录像就是这个场景——崩溃现场的环形缓冲只留最近几分钟，
        /// 起始 tick 动辄上万，而世界是从录像头部的完整快照恢复的，时钟必须跟着挪过去才对得上。
        /// </para>
        /// <para>
        /// <b>它不是「快进」。</b>谁要是拿它当快进用（以为能跳过中间那段逻辑），得到的是一个
        /// <b>世界状态停在原地、时钟却跳了</b>的错乱状态：中间那些 tick 的步骤一次都没跑过，
        /// 之后每一步逻辑读到的 tick 都对不上世界的实际进度，表现成一堆查不出来由的漂移。
        /// 真要往前跑逻辑，只有 <see cref="AdvanceOneTick"/> 一条路，一格都不能省。
        /// </para>
        /// <para>
        /// <b>为什么不放进 <see cref="ILogicClock"/></b>：那是玩法也会注入的**只读**时间接口，
        /// 把「能改时钟」放进去等于给玩法开了一个后门——推进权本来就只在推进器手里，
        /// 定位权跟着它走，能调到这个方法的只有拿得到 <see cref="SimulationRunner"/> 的接线层与回放系统。
        /// </para>
        /// </summary>
        /// <param name="tick">目标 tick 序号，不能为负。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="tick"/> 为负数。</exception>
        public void SeekClockTo(long tick)
        {
            logicClock.SeekTo(tick);
            accumulator = 0d;
        }

        /// <summary>
        /// 渲染帧回调。<see cref="Mode.Live"/> 下累积帧时间并推进 0～N 个 tick；
        /// <see cref="Mode.Driven"/> 下**什么都不做**——重放的节奏由播放器说了算，
        /// 这里再偷偷推一格就会和录像错位。
        /// </summary>
        public void Tick()
        {
            if (CurrentMode != Mode.Live)
            {
                return;
            }

            Accumulate(frameClock.DeltaTime);
        }

        /// <summary>
        /// 推进**恰好一个** tick：取一条输入 → 按注册顺序串行跑完所有步骤 → 逻辑时钟走一格。
        /// <para>
        /// 两种模式共用这一个方法，<see cref="Mode.Live"/> 只是由 <see cref="Accumulate"/> 决定调几次。
        /// 重放要对得上，靠的就是「无论谁来驱动，一个 tick 里发生的事完全一样」。
        /// </para>
        /// </summary>
        public void AdvanceOneTick()
        {
            // 每个 tick 各取一次输入。一帧补多个 tick 时会连着取到多条相同的命令，这是**预期行为**，
            // 不要「优化」成一帧只取一条：录制是按 tick 逐条记、重放是按 tick 逐条喂，
            // 少记一条，后面所有 tick 的输入就整体错位一格，重放从那里开始分叉。
            //
            // 先 Sample 再读 Current，两步都走 IInputSource，不认具体实现：实时源在这里读一次设备，
            // 录像源在这里取出该 tick 录下的那条命令——推进器两边跑的是同一段代码，重放才对得上。
            // 传的是**正要推进**的这个 tick（时钟在本方法末尾才 Advance），录像源可据此断言对齐。
            inputSource.Sample(logicClock.Tick);
            InputCommand input = inputSource.Current;

            // SimulationContext 是 readonly struct，这里的 new 在栈上，不产生堆分配。
            SimulationContext context = new SimulationContext(logicClock.Tick, fixedDeltaTime, in input, random);

            for (int i = 0; i < steps.Count; i++)
            {
                steps[i].Step(in context);
            }

            // 先跑完这一 tick 的所有步骤再推进时钟：Step 里读到的 Clock.Tick 与 context.Tick 因此永远相等。
            logicClock.Advance();
        }

        /// <summary>
        /// 把一段渲染帧时长换算成整数个 tick 并推进。从 <see cref="Tick"/> 里拆出来，
        /// 是为了让「判断模式」和「换算步数」各占一个方法——后者是这个类唯一有分支的地方。
        /// </summary>
        /// <param name="frameSeconds">这一帧的时长秒数。非正数直接忽略（首帧的 deltaTime 可能是 0）。</param>
        private void Accumulate(float frameSeconds)
        {
            // 下面所有比较与加减都走 double：fixedDeltaTime 存的是 float，参与运算时显式提升，
            // 免得编译器在某一步把结果截回 float，把上面那一整类边界误差又放回来。
            double step = fixedDeltaTime;

            if (frameSeconds > 0f)
            {
                accumulator += frameSeconds;
            }

            int advanced = 0;
            while (accumulator >= step && advanced < maxCatchUpTicks)
            {
                accumulator -= step;
                AdvanceOneTick();
                advanced++;
            }

            if (accumulator < step)
            {
                return;
            }

            // 到这里说明补满了上限还剩得下至少一个 tick：这一帧掉得太狠。把超出的部分整段丢掉，
            // 逻辑时间就此落后于墙上时间——这是刻意的取舍，总比让补帧把下一帧也拖垮强。
            int dropped = (int)(accumulator / step);
            accumulator -= dropped * step;

            // W 级：丢 tick 意味着这一段逻辑时间凭空消失了，排查手感问题时必须一眼看见。
            telemetry.TrackWarn(
                TickDroppedEvent,
                TelemetryProps.Of((TelemetryKeys.Props.N, dropped)));
        }
    }
}
