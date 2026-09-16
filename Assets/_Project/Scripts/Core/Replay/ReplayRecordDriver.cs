// 职责：接线件——把「每个逻辑 tick 发生了什么」喂给 ReplayRecorder，并在启动时替它开一局录制。
//   它是**录制器与确定性内核之间唯一的那条线**：内核不认识回放系统（依赖方向只许 Replay → Simulation），
//   所以录制器不能去订阅推进器；反过来由本类以一个 ISimulationStep 的身份站进推进器的步骤表，
//   每 tick 把 tick 号与那一 tick 用掉的输入交给录制器。
// 为什么要单独建这个文件（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：工程内没有任何「把推进器的每 tick 事件转给别人」的件。SimulationRunner 只认
//      ISimulationStep；ReplayRecorder 自己实现的 ITickable 是**渲染帧**回调（它只用来轮询热键），
//      拿它当逻辑 tick 会漏记也会重记。
//   2. 扩展不行：不能把 ISimulationStep 实现在 ReplayRecorder 身上。那会让录制器直接长出内核的接口，
//      「谁驱动谁」当场反转——而这条依赖方向是整套设计刻意立的（见 ReplayRecorder 文件头第 2 条）。
//      也不能塞进 GameLifetimeScope：组合根只该注册，不该自己长出每 tick 跑的逻辑。
//   3. 它很小，但必须存在：没有它，容器里的录制器就是个一条记录都收不到的空壳。

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Boot;
using Game.Core.Config;
using Game.Core.Logging;
using Game.Core.Simulation;

namespace Game.Core.Replay
{
    /// <summary>
    /// 录制接线件。启动时开一局录制并把自己挂进推进器的步骤表，之后每个逻辑 tick 给录制器喂一条记录。
    ///
    /// <para>
    /// <b>必须是推进器的第一个步骤</b>，这条不是风格问题而是字节对齐问题：
    /// <see cref="ReplayRecorder.RecordTick"/> 在记输入的同时可能顺带序列化一次世界状态
    /// （状态哈希与完整快照都从那里来）。站在第一位，记下的就是「tick T 开始之前的世界」，
    /// 与文件头「起始 tick 的快照 + 从该 tick 起的输入」这个契约正好对齐：
    /// 重放时恢复快照、再喂 T 的输入，第一格就接得上。
    /// 挪到最后一位的话记下的会是「T 跑完之后的世界」，重放时会把 T 这一 tick 跑第二遍，
    /// 表现为整份回放稳定偏一格——回放类 bug 里最难用肉眼看出来的那一种。
    /// 本类在 <see cref="InitializeAsync"/> 里挂进步骤表，那发生在启动串行初始化期间，
    /// 比任何玩法模块加步骤都早。
    /// </para>
    ///
    /// <para>
    /// <b>放录像时不录</b>：重放期间输入来自录像本身，再录一遍只会把环里真正的现场覆盖掉，
    /// 所以看 <see cref="InputSourceSwitch.IsReplaying"/> 直接跳过。
    /// </para>
    ///
    /// <para>每 tick 路径零堆分配：一次布尔判断、一次结构体拷贝，没有 new、没有字符串。</para>
    /// </summary>
    public sealed class ReplayRecordDriver : IGameService, ISimulationStep
    {
        private readonly ReplayRecorder recorder;
        private readonly SimulationRunner runner;
        private readonly InputSourceSwitch inputSwitch;
        private readonly RandomService random;
        private readonly IConfigService config;

        private bool attached;
        private long skippedWhileReplaying;

        /// <param name="recorder">录制器，不可为空。</param>
        /// <param name="runner">推进器，本类要挂进它的步骤表，不可为空。</param>
        /// <param name="inputSwitch">输入源切换壳，用来判断当前是不是在放录像，不可为空。</param>
        /// <param name="random">随机源，只取它的主种子写进录像头。可为 null（那就记 0，但那样的录像重放不了）。</param>
        /// <param name="config">配置服务，只取 <see cref="IConfigService.ContentHash"/>。可为 null。</param>
        public ReplayRecordDriver(
            ReplayRecorder recorder,
            SimulationRunner runner,
            InputSourceSwitch inputSwitch,
            RandomService random,
            IConfigService config)
        {
            this.recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
            this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
            this.inputSwitch = inputSwitch ?? throw new ArgumentNullException(nameof(inputSwitch));
            this.random = random;
            this.config = config;
        }

        /// <summary>已经挂进推进器的步骤表了没有。</summary>
        public bool IsAttached => attached;

        /// <summary>放录像期间跳过了几个 tick 的录制。纯诊断用。</summary>
        public long SkippedWhileReplaying => skippedWhileReplaying;

        /// <summary>
        /// 启动时：开一局录制（记下种子与配置指纹），再把自己挂进推进器的第一个步骤位。
        /// <para>
        /// 注册顺序把本类排在 <c>ConfigService</c> 之后，所以这里读 <see cref="IConfigService.ContentHash"/>
        /// 是安全的；真读不到（裁剪过的作用域、配置初始化失败）也不让启动挂掉，记 0 并报一句——
        /// 指纹为 0 的录像放的时候会被判成「配置版本不匹配」，那正是我们希望人看到的结果。
        /// </para>
        /// </summary>
        public UniTask InitializeAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (!recorder.Enabled)
            {
                // 录制关着时连步骤都不挂：每 tick 少一次虚调用，也免得有人在步骤表里看见一个什么都不做的条目。
                Log.Info("回放录制是关着的（ReplayConfig 的 Enable Mode），本次运行不记录任何现场。");
                return UniTask.CompletedTask;
            }

            recorder.BeginSession(
                random == null ? 0UL : random.MasterSeed,
                ReadConfigHash(),
                runner.Clock.FixedDeltaTime);

            if (!attached)
            {
                runner.AddStep(this);
                attached = true;
            }

            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 每个逻辑 tick 给录制器喂一条：这一 tick 的序号，以及它用掉的那条输入命令。
        /// <para>两者都取自 <paramref name="context"/>，不去别处拿——推进器给的这一份就是本 tick 的全部确定性输入，
        /// 从别处再取一次就可能取到不是同一 tick 的值。</para>
        /// </summary>
        public void Step(in SimulationContext context)
        {
            if (inputSwitch.IsReplaying)
            {
                skippedWhileReplaying++;
                return;
            }

            recorder.RecordTick((uint)context.Tick, context.Input);
        }

        /// <summary>取配置指纹。配置服务没准备好时不让启动挂掉，记 0 并报一句。</summary>
        private ulong ReadConfigHash()
        {
            if (config == null)
            {
                return 0UL;
            }

            try
            {
                return config.ContentHash;
            }
            catch (Exception e)
            {
                Log.Warn(
                    $"取不到配置指纹，本次录制的文件头里会记 0：{e.GetType().Name}：{e.Message}。"
                    + "这份录像回放时会被判成「配置版本不匹配」——那是对的，因为确实不知道它录于哪一版配置。");
                return 0UL;
            }
        }
    }
}
