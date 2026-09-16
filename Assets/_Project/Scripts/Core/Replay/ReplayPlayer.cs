// 职责：把一份录下来的回放文件重新跑成一次真实的游戏过程——打开文件、校验版本与配置指纹、
//   把世界恢复到起始快照那一刻、把推进器切成外部驱动、每渲染帧按播放状态决定推几个 tick，
//   并在每个 tick 之后拿录像里的状态哈希校验有没有漂移。这是整套回放系统的收口件。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：工程内没有任何「播放器」。ReplayReader 只把文件拆成一条条记录，它不认识推进器、
//      不认识世界状态，更不管播放/暂停/变速；SimulationRunner 只会「推恰好一格」，它不知道该推几格，
//      也不许认识回放系统（依赖方向只许 Replay → Simulation）。
//   2. 扩展不行：不能把播放塞进 ReplayRecorder。录制器的生命周期是整局游戏、状态是三个环形缓冲；
//      播放器的生命周期是一份文件、状态是播放位置与漂移统计。合一之后每个字段都要先问一句
//      「现在是在录还是在放」，而这类分支正是「一边放一边把录像覆盖掉」的来源。
//      也不能塞进 ReplayReader：那个类是顺序读字节的，播放要的是随机访问（按 tick 查哈希、
//      按 tick 取快照恢复），两种访问模式共用一个游标，错位一格就从这种地方来。

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Audio;
using Game.Core.Config;
using Game.Core.Logging;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Core.Timing;
using UnityEngine;
using VContainer.Unity;

namespace Game.Core.Replay
{
    /// <summary>
    /// 回放播放器。自己实现 VContainer 的 <see cref="ITickable"/>，由 <c>RegisterEntryPoint</c>
    /// 每渲染帧驱动一次（和 <see cref="SimulationRunner"/> 同例）。
    ///
    /// <para>
    /// <b>只有播放器驱动推进，界面只改播放状态。</b>编辑器窗口、Showcase 回放场景、EditMode 测试，
    /// 三条路径都只调 <see cref="Play"/> / <see cref="Pause"/> / <see cref="StepOnce"/> /
    /// <see cref="Speed"/>，**谁都不直接调 <c>runner.AdvanceOneTick()</c>**。
    /// 所以编辑器窗口不是必需品：没有窗口，Showcase 与测试照样能把整份回放跑完，而且跑的是同一份代码。
    /// 反过来说，一旦有人在窗口里自己推一格，窗口路径和自动化路径就成了两条实现，
    /// 「窗口里能复现、跑测试复现不了」这类问题就会开始出现。
    /// </para>
    ///
    /// <para><b>一次完整的播放</b>：</para>
    /// <code>
    /// player.AttachStateProvider(provider);          // 想要漂移检测与起点恢复就得挂
    /// string error;
    /// if (!player.Load(path, out error)) { Log.Error(error); return; }
    /// player.Play();                                 // 之后每渲染帧由 Tick() 自己推
    /// </code>
    ///
    /// <para>
    /// <b>漂移检测</b>：每推进一个 tick 后，如果录像里有这一 tick 的状态哈希，就把当前世界重新
    /// 序列化一遍算哈希比对。不一致时记下**第一次**漂移的 tick（<see cref="FirstDriftTick"/>，
    /// 不是最后一次、也不逐次覆盖），报一次，然后在遇到的下一个完整快照处把世界拉回正轨继续跑。
    /// <b>不中断</b>：漂移点之后的内容往往仍有诊断价值，而且人需要看到「后面变成了什么样」。
    /// </para>
    ///
    /// <para>
    /// <b>全量载入</b>：<see cref="Load"/> 把整份文件读进内存（输入表 + 哈希表 + 快照表）再关掉文件。
    /// 回放文件的量级是几 MB 到几十 MB，换来的是按 tick 随机访问（查哈希、取快照恢复）与播放期零 IO 抖动——
    /// 一边放一边读文件的话，磁盘一卡就会让「这一帧推了几个 tick」跟着抖，而那正是回放最不该有的东西。
    /// </para>
    ///
    /// <para>线程约定：只在主线程 / 逻辑线程上用，内部不加锁。</para>
    /// </summary>
    public sealed class ReplayPlayer : ITickable, IDisposable
    {
        /// <summary>最慢播放速度。再慢就该用单步了。</summary>
        public const float MinSpeed = 0.25f;

        /// <summary>最快播放速度。</summary>
        public const float MaxSpeed = 8f;

        /// <summary>埋点模块名，和录制器同一个模块（同一套设施的两半）。事件名用 play_ 前缀区分。</summary>
        private const string TelemetryModule = "core.replay";

        /// <summary>载入成功，属性 <c>n</c>(输入条数) <c>tick</c>(起始) <c>ms</c>。</summary>
        private const string LoadedEvent = "play_loaded";

        /// <summary>载入失败，带 <c>err</c>。</summary>
        private const string LoadFailedEvent = "play_load_failed";

        /// <summary>配置指纹对不上，带 <c>expected</c> / <c>actual</c>。</summary>
        private const string ConfigMismatchEvent = "play_config_mismatch";

        /// <summary>首次漂移，带 <c>tick</c> / <c>expected</c> / <c>actual</c>。**只埋第一次**。</summary>
        private const string DriftEvent = "play_drift";

        /// <summary>从快照把世界拉回正轨，带 <c>tick</c> / <c>n</c>(第几次)。</summary>
        private const string ResyncEvent = "play_resync";

        /// <summary>放完或停止，带 <c>tick</c> / <c>n</c>(漂移次数) / <c>ok</c>。</summary>
        private const string EndedEvent = "play_ended";

        /// <summary>属性键：逻辑 tick 号。同 <see cref="ReplayRecorder"/>，TelemetryKeys.Props 里还没有这个语义。</summary>
        private const string TickProp = "tick";

        /// <summary>属性键：期望值（录像里记着的）。</summary>
        private const string ExpectedProp = "expected";

        /// <summary>属性键：实际值（这次重放算出来的）。</summary>
        private const string ActualProp = "actual";

        /// <summary>
        /// 一帧最多推多少个 tick。8 倍速 60Hz 也只要 8 个/帧，这个上限只在编辑器卡住一大段时间之后
        /// 才会碰到；碰到就把超出的时间整段丢掉，理由同 <see cref="SimulationRunner"/> 的追帧上限
        /// （补帧本身更耗时，不设上限会滚成「卡顿 → 补帧 → 更卡」）。
        /// </summary>
        private const int MaxTicksPerFrame = 64;

        /// <summary>比较两个固定步长时的容差。步长是 1/tickRate 存成 float，逐位相等靠不住。</summary>
        private const float StepEpsilon = 1e-6f;

        /// <summary>哈希 chunk 的 payload 字节数（一个小端 u64）。</summary>
        private const int StateHashPayloadSize = 8;

        private readonly SimulationRunner runner;
        private readonly InputSourceSwitch inputSwitch;
        private readonly IClock frameClock;
        private readonly RandomService random;
        private readonly IConfigService config;
        private readonly ReplayConfig replayConfig;
        private readonly IAudioService audioService;
        private readonly ITelemetryScope telemetry;

        private readonly ReplayInputSource inputs = new ReplayInputSource();

        /// <summary>序列化世界状态用的复用缓冲。算哈希与从快照恢复都走它，两件事不同时发生。</summary>
        private readonly StateBuffer scratch = new StateBuffer();

        // 状态哈希表：按 tick 升序，和播放一样只向前走，所以用一个游标而不是字典。
        private uint[] hashTicks = new uint[64];
        private ulong[] hashValues = new ulong[64];
        private int hashCount;
        private int hashCursor;

        // 快照表：同上。payload 各自拷一份留着——ReplayChunk 给的是会被下一条覆盖的复用缓冲。
        private uint[] snapshotTicks = new uint[8];
        private byte[][] snapshotPayloads = new byte[8][];
        private int[] snapshotLengths = new int[8];
        private int snapshotCount;
        private int snapshotCursor;

        private IReplayStateProvider stateProvider;

        private ReplayHeader header;
        private string sourcePath;
        private long startTick;
        private long endTick;
        private long currentTick;

        private double accumulator;
        private float speed = 1f;
        private int pendingSteps;
        private bool playing;
        private bool active;

        private bool pendingResync;
        private bool clockDesyncLogged;
        private bool resyncLogged;

        private float savedMasterVolume;
        private bool muted;

        /// <param name="runner">推进器。播放期间它被切成 <see cref="SimulationRunner.Mode.Driven"/>，只认本类的显式推进。</param>
        /// <param name="inputSwitch">输入源切换壳。播放开始时切到录像源，结束时切回实时。</param>
        /// <param name="frameClock">渲染帧时间，用来把一帧的时长换算成该推几个 tick。</param>
        /// <param name="random">
        /// 随机源。载入时把它的主种子换成录像头部记的那个值（见 <see cref="ApplyHeaderSeed"/>）。
        /// 可为 null，那就跳过这一步——代价是重放途中新建的流会按当前主种子派生，和录制时不是同一串数。
        /// </param>
        /// <param name="config">配置服务，用来取当前配置指纹做版本校验。可为 null（那就跳过校验）。</param>
        /// <param name="replayConfig">回放配置资产，本类只读它的「重放时静音」。可为空。</param>
        /// <param name="audioService">音频服务，静音用。可为 null。</param>
        /// <param name="telemetry">埋点服务，可为 null（EditMode 测试里直接 new 时没有容器）。</param>
        public ReplayPlayer(
            SimulationRunner runner,
            InputSourceSwitch inputSwitch,
            IClock frameClock,
            RandomService random,
            IConfigService config,
            ReplayConfig replayConfig,
            IAudioService audioService,
            ITelemetryService telemetry)
        {
            this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
            this.inputSwitch = inputSwitch ?? throw new ArgumentNullException(nameof(inputSwitch));
            this.frameClock = frameClock ?? throw new ArgumentNullException(nameof(frameClock));
            this.random = random;
            this.config = config;

            // ReplayConfig 是 ScriptableObject（UnityEngine.Object）：用它的地方判空只能用 ==，
            // Unity 重载了它来识别「已销毁但引用还在」的伪空对象（见 ApplyMute）。
            this.replayConfig = replayConfig;
            this.audioService = audioService;
            this.telemetry = telemetry == null
                ? (ITelemetryScope)NullTelemetryScope.Instance
                : telemetry.Scope(TelemetryModule);
        }

        /// <summary>有没有一份回放已经载入（数据还在内存里，统计可查）。</summary>
        public bool IsLoaded { get; private set; }

        /// <summary>当前是不是正占着推进器与输入源。<see cref="Stop"/> 之后为 false，但统计仍可查。</summary>
        public bool IsActive => active;

        /// <summary>正在播（没暂停、没放完）。</summary>
        public bool IsPlaying => active && playing && !IsFinished;

        /// <summary>这份回放放完了没有。没载入时是 false。</summary>
        public bool IsFinished { get; private set; }

        /// <summary>当前播到第几个 tick（**录像里的绝对 tick**，不是从 0 数的）。</summary>
        public long CurrentTick => currentTick;

        /// <summary>这份回放从哪个 tick 开始。</summary>
        public long StartTick => startTick;

        /// <summary>这份回放最后一条输入在哪个 tick。播到它之后就算放完了。</summary>
        public long EndTick => endTick;

        /// <summary>播放进度 0~1。没载入或只有一个 tick 时返回 0。</summary>
        public float Progress
        {
            get
            {
                long span = endTick - startTick;
                if (!IsLoaded || span <= 0L)
                {
                    return IsFinished ? 1f : 0f;
                }

                float value = (float)((currentTick - startTick) / (double)span);
                return value < 0f ? 0f : (value > 1f ? 1f : value);
            }
        }

        /// <summary>
        /// 播放速度倍率，写入时夹到 [<see cref="MinSpeed"/>, <see cref="MaxSpeed"/>]。
        /// 变速只改「这一帧推几个 tick」，**不改每个 tick 里发生的事**——所以 8 倍速跑出来的结果
        /// 和 1 倍速逐位相同，这正是变速能用来快进排查的前提。
        /// </summary>
        public float Speed
        {
            get => speed;
            set => speed = value < MinSpeed ? MinSpeed : (value > MaxSpeed ? MaxSpeed : value);
        }

        /// <summary>这份回放的文件头。<see cref="IsLoaded"/> 为 true 之后才有意义。</summary>
        public ReplayHeader Header => header;

        /// <summary>载入的那份文件的路径，没载入过是 null。</summary>
        public string SourcePath => sourcePath;

        /// <summary>最近一次 <see cref="Load"/> 失败的原因，没失败过是 null。</summary>
        public string LastError { get; private set; }

        /// <summary>录像里的输入条数。</summary>
        public int InputCount => inputs.Count;

        /// <summary>录像里的状态哈希条数。为 0 表示这份回放没法做漂移检测。</summary>
        public int StateHashCount => hashCount;

        /// <summary>录像里（文件头之外的）完整快照个数。为 0 表示漂移之后没法把世界拉回正轨。</summary>
        public int SnapshotCount => snapshotCount;

        /// <summary>录像里的 QA 打点条数。</summary>
        public int QaMarkerCount { get; private set; }

        /// <summary>
        /// 已经播到的 tick 里有几个在录像中缺输入（按「没按键」处理了）。
        /// 不为 0 说明这份录像有断口，那几 tick 的重放结果和录制时不同。
        /// </summary>
        public long MissingInputCount => inputs.MissingCount;

        /// <summary>
        /// 被跳过的输入条数。正常播放恒为 0，不为 0 就是**对齐出了问题**（有人在播放器之外也推了推进器）。
        /// </summary>
        public long SkippedInputCount => inputs.SkippedCount;

        /// <summary>这份文件的尾部是不是残缺的。崩溃时自动保存的回放通常是 true，不是错误。</summary>
        public bool IsTailTruncated { get; private set; }

        /// <summary>已经校验过的状态哈希点数。为 0 而 <see cref="StateHashCount"/> 不为 0，说明还没播到第一个校验点。</summary>
        public int CheckedHashCount { get; private set; }

        /// <summary>出现过漂移没有。</summary>
        public bool HasDrifted { get; private set; }

        /// <summary>
        /// **第一次**漂移的 tick，没漂过是 -1。
        /// <para>刻意记第一次而不是最近一次：漂移一旦发生，后面几乎每个校验点都会跟着对不上，
        /// 逐次覆盖的话最后留下的是「最后一个校验点」，那个数对定位真因毫无价值。</para>
        /// </summary>
        public long FirstDriftTick { get; private set; } = -1L;

        /// <summary>漂移了几次（几个校验点对不上）。</summary>
        public int DriftCount { get; private set; }

        /// <summary>从完整快照把世界拉回正轨几次。</summary>
        public int ResyncCount { get; private set; }

        /// <summary>
        /// 这份回放的配置指纹和当前运行的对不上。为 true 时重放结果不可信
        /// （数值表换过一版，同样的输入本来就会推出不同的结果）。
        /// </summary>
        public bool ConfigMismatch { get; private set; }

        /// <summary>带着「配置对不上」的标注在放。只有显式要求继续时才会是 true。</summary>
        public bool ResultsUntrusted { get; private set; }

        /// <summary>
        /// 挂上世界状态注册表。<b>不挂也能放</b>——不挂就只重放输入，既不恢复起点状态，
        /// 也做不了漂移检测（那正是这套系统的价值所在，所以正常接线一定要挂）。
        /// </summary>
        /// <param name="provider">状态注册表，传 null 表示摘掉。</param>
        public void AttachStateProvider(IReplayStateProvider provider)
        {
            stateProvider = provider;
        }

        /// <summary>
        /// 载入一份回放并把世界恢复到它的起点。成功之后调 <see cref="Play"/> 开始放。
        /// <para>配置指纹对不上时**默认拒绝载入**并把原因写进 <paramref name="error"/>，
        /// 要带着「结果不可信」的标注继续看，用 <see cref="Load(string, bool, out string)"/>。</para>
        /// </summary>
        /// <param name="path">回放文件路径。</param>
        /// <param name="error">失败时是一句给人看的说明；成功时为 null。</param>
        /// <returns>载入成功返回 true。</returns>
        public bool Load(string path, out string error)
        {
            return Load(path, false, out error);
        }

        /// <summary>
        /// 载入一份回放。载入流程：打开文件 → 校验步长与配置指纹 → 读全部记录 → 把逻辑时钟定位到起始 tick
        /// → 把主随机种子换回录像里那个 → 从起始快照恢复世界 → 把推进器切成 Driven、输入源切到录像。
        /// <para>
        /// <b>先读完再改状态</b>：所有解析与校验都在动推进器之前做完，中途失败时推进器与输入源
        /// 还保持原样——半切换的状态（模式切了但输入源没切）比干脆没载入难查得多。
        /// </para>
        /// </summary>
        /// <param name="path">回放文件路径。</param>
        /// <param name="ignoreConfigMismatch">
        /// 配置指纹对不上时是否照样载入。true 时会载入并把 <see cref="ResultsUntrusted"/> 置起来——
        /// 有时就是想看看画面，但别拿这次的结果当证据。
        /// </param>
        /// <param name="error">失败时是一句给人看的说明；成功时为 null。</param>
        public bool Load(string path, bool ignoreConfigMismatch, out string error)
        {
            long startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

            // 上一份回放先放开推进器与输入源，再清统计。
            Unload();
            error = null;
            LastError = null;

            ReplayReader reader;
            string openError;
            if (!ReplayReader.TryOpen(path, out reader, out openError))
            {
                return Fail(openError, out error);
            }

            using (reader)
            {
                ReplayHeader candidate = reader.Header;

                // ① 步长。对不上就没有「逐 tick 对齐」可言了：同一段输入在两种步长下推出的
                // 根本不是同一段时间，硬放只会得到满屏漂移。这条是硬失败，不给「继续看」的选项。
                float expectedStep = runner.Clock.FixedDeltaTime;
                if (Mathf.Abs(candidate.FixedDeltaTime - expectedStep) > StepEpsilon)
                {
                    return Fail(
                        $"逻辑步长不匹配：这份回放录于步长 {candidate.FixedDeltaTime}（约 "
                        + $"{Mathf.RoundToInt(1f / candidate.FixedDeltaTime)} Hz），当前工程是 {expectedStep}（约 "
                        + $"{Mathf.RoundToInt(1f / expectedStep)} Hz）。两者逐 tick 对不上，这份回放放不了：{path}",
                        out error);
                }

                // ② 配置指纹。对不上时**明确告诉人是配置换版了**，而不是让它退化成一堆漂移 tick
                // 让人白查一整天——这正是文件头里存这个指纹的全部理由。
                ulong currentConfigHash;
                bool configHashKnown = TryReadConfigHash(out currentConfigHash);
                ConfigMismatch = configHashKnown && currentConfigHash != candidate.ConfigHash;
                if (ConfigMismatch)
                {
                    string mismatch =
                        $"配置版本不匹配：这份回放录于另一版配置表（回放 {candidate.ConfigHash:X16} / "
                        + $"当前 {currentConfigHash:X16}）。同样的输入在两版数值下本来就会推出不同的结果，"
                        + "照放只会得到一堆对不上的 tick。要么把配置切回录制时那一版，"
                        + "要么明确要求「带着结果不可信的标注继续看」。";

                    telemetry.TrackWarn(
                        ConfigMismatchEvent,
                        TelemetryProps.Of(
                            (ExpectedProp, candidate.ConfigHash.ToString("X16")),
                            (ActualProp, currentConfigHash.ToString("X16"))));

                    if (!ignoreConfigMismatch)
                    {
                        return Fail(mismatch, out error);
                    }

                    ResultsUntrusted = true;
                    Log.Warn(mismatch + "（已按要求继续，本次重放结果不可信）");
                }

                // ③ 把全部记录读进内存。读到坏记录就停在那里，前面读到的照常用（同 ReplayReader 的「尽力读」）。
                if (!ReadAllChunks(reader, out string readError))
                {
                    return Fail(readError, out error);
                }

                header = candidate;
                sourcePath = path;
                IsTailTruncated = reader.IsTailTruncated;
            }

            startTick = header.StartTick;
            endTick = inputs.Count > 0 ? inputs.LastTick : startTick;

            // ④ 把逻辑时钟定位到起始 tick。时钟对不上，后面所有按 tick 的对齐（输入、哈希、快照）全都是错的。
            runner.SetMode(SimulationRunner.Mode.Driven);
            SeekClockTo(header.StartTick);

            currentTick = startTick;

            // ⑤ 恢复起点：世界状态来自文件头里的完整快照，logic.* 随机流的状态也在里面
            //    （按约定它们是世界状态的一部分，见 IRandomStream.State 的文档）。
            //    这两步的先后不能换：ApplyHeaderSeed 会把已建好的流按录像的主种子重设回初始状态，
            //    放在恢复快照之后做，等于把刚从快照里恢复好的 logic.* 流状态又冲掉一遍。
            ApplyHeaderSeed();
            RestoreStartSnapshot();

            // ⑥ 接上输入源，占住推进器。
            inputs.Rewind();
            inputSwitch.SwitchToReplay(inputs);
            active = true;
            IsLoaded = true;
            playing = false;
            IsFinished = inputs.Count == 0;
            accumulator = 0d;
            pendingSteps = 0;
            ApplyMute();

            // 起点也校验一次：录像在起始 tick 上通常同时有哈希与快照，
            // 这一次比对验的是「起始快照恢复得对不对」——序列化与反序列化不对称的话，这里就会第一时间暴露。
            CheckCurrentTick();

            double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp)
                * 1000d / System.Diagnostics.Stopwatch.Frequency;
            telemetry.Track(
                LoadedEvent,
                (TelemetryKeys.Props.N, inputs.Count),
                (TickProp, startTick),
                (TelemetryKeys.Props.Ms, elapsedMs));

            Log.Info(
                $"回放已载入：{path}\n{header}\n输入 {inputs.Count} 条（tick {startTick}~{endTick}）、"
                + $"状态哈希 {hashCount} 条、完整快照 {snapshotCount} 个、QA 打点 {QaMarkerCount} 条"
                + (IsTailTruncated ? "；文件尾部残缺（崩溃时自动保存的回放通常如此）" : string.Empty));
            return true;
        }

        /// <summary>
        /// <see cref="Load(string, out string)"/> 的异步外壳，供 async 流程直接 await。
        /// <para>
        /// <b>当前是同步执行的，这是刻意的</b>：恢复世界要写进一堆逻辑线程独占的对象里，
        /// 丢到线程池上只会换来一个「恢复到一半的世界」。保留异步签名的价值在于：
        /// 将来真要把读文件那段挪到工作线程（先读成字节再回主线程装配），调用点一行都不用改。
        /// 失败原因从 <see cref="LastError"/> 取。
        /// </para>
        /// </summary>
        /// <param name="path">回放文件路径。</param>
        /// <param name="ignoreConfigMismatch">配置指纹对不上时是否照样载入。</param>
        /// <param name="ct">取消令牌。已取消时直接抛 <see cref="OperationCanceledException"/>。</param>
        public UniTask<bool> LoadAsync(string path, bool ignoreConfigMismatch = false, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            string error;
            return UniTask.FromResult(Load(path, ignoreConfigMismatch, out error));
        }

        /// <summary>开始/继续播放。没载入、已放完、已停止时只记一条日志，不抛。</summary>
        public void Play()
        {
            if (!active)
            {
                Log.Warn("没有正在放的回放，Play 被忽略。先 Load 一份文件。");
                return;
            }

            if (IsFinished)
            {
                // 不提供倒带：倒回去要重置的不只是逻辑时钟，还有整个世界状态与全部播放统计，
                // 而 Load 本来就把这些一次做齐 —— 再开一条只重置一半的路，只会多出一种半旧半新的状态。
                Log.Warn("这份回放已经放完了，要再放一次请重新 Load（播放器不倒带）。");
                return;
            }

            playing = true;
        }

        /// <summary>暂停。<see cref="Tick"/> 之后一个 tick 都不推，直到 <see cref="Play"/> 或 <see cref="StepOnce"/>。</summary>
        public void Pause()
        {
            playing = false;

            // 清掉没凑满一个 tick 的余量：不清的话，暂停前攒下的零头会在恢复播放的第一帧
            // 突然多推一格，表现为「一按继续就跳了一下」。
            accumulator = 0d;
        }

        /// <summary>
        /// 单步：下一帧推**恰好一个** tick，然后停住。逐帧查「是哪一 tick 开始不对的」时用这个。
        /// 连按几次就排几次队。
        /// </summary>
        public void StepOnce()
        {
            if (!active || IsFinished)
            {
                return;
            }

            playing = false;
            accumulator = 0d;
            pendingSteps++;
        }

        /// <summary>
        /// 停止播放并把推进器与输入源还给实时玩法。载入的数据与统计仍然可查
        /// （放完之后人还要看漂移统计，清掉就白放了）。重复调用无副作用。
        /// </summary>
        public void Stop()
        {
            if (!active)
            {
                return;
            }

            playing = false;
            Deactivate();
            telemetry.Track(
                EndedEvent,
                (TickProp, currentTick),
                (TelemetryKeys.Props.N, DriftCount),
                (TelemetryKeys.Props.Ok, !HasDrifted));
        }

        /// <summary>
        /// 渲染帧回调（<see cref="ITickable"/>）。按当前播放状态决定这一帧推几个 tick：
        /// 暂停 0 个、单步请求 1 个、正常/变速按累积的帧时长 × 速度 ÷ 固定步长算。
        /// <para><b>这不是逻辑 tick</b>——真正推进逻辑的是 <see cref="SimulationRunner.AdvanceOneTick"/>。</para>
        /// </summary>
        public void Tick()
        {
            if (!active || IsFinished)
            {
                return;
            }

            // 单步优先于播放：单步本身就会把 playing 关掉，这里先处理是为了让
            // 「暂停中连按几次单步」在几帧里逐格推完，而不是攒着等某次恢复播放时一起冲出去。
            if (pendingSteps > 0)
            {
                pendingSteps--;
                AdvanceOne();
                return;
            }

            if (!playing)
            {
                return;
            }

            double step = runner.Clock.FixedDeltaTime;
            float frameSeconds = frameClock.DeltaTime;
            if (frameSeconds > 0f)
            {
                // 余量用 double，理由同 SimulationRunner：float 累加下，同一段总时长按不同帧率喂进来
                // 会攒出不同的误差，「这一帧推几个 tick」就隐含依赖了帧率。
                accumulator += frameSeconds * (double)speed;
            }

            int advanced = 0;
            while (accumulator >= step && advanced < MaxTicksPerFrame)
            {
                accumulator -= step;
                AdvanceOne();
                advanced++;

                if (IsFinished)
                {
                    accumulator = 0d;
                    return;
                }
            }

            if (accumulator >= step)
            {
                // 补满上限还剩得下至少一个 tick：这一帧掉得太狠，把超出的部分整段丢掉。
                // 回放不追墙上时间——放慢一点总比让补帧把下一帧也拖垮强。
                accumulator = 0d;
            }
        }

        /// <summary>释放：把推进器与输入源还回去。容器销毁时由 VContainer 调。</summary>
        public void Dispose()
        {
            Stop();
        }

        /// <summary>推进恰好一个 tick，然后校验这一 tick 的状态、判断放完了没有。</summary>
        private void AdvanceOne()
        {
            runner.AdvanceOneTick();
            currentTick++;

            // 时钟对不上说明有别人也在推进器上推了格（比如模式被谁切回了 Live）。
            // 这时按时钟为准继续放，但要报出来——再往后所有按 tick 的对齐都是错的。
            long clockTick = runner.Clock.Tick;
            if (clockTick != currentTick)
            {
                if (!clockDesyncLogged)
                {
                    clockDesyncLogged = true;
                    Log.Warn(
                        $"回放的 tick 和逻辑时钟对不上：播放器认为在 {currentTick}，时钟报 {clockTick}。"
                        + "说明推进器同时还被别处推着（模式被切回 Live？），从这里往后的重放结果不可信。");
                }

                currentTick = clockTick;
            }

            CheckCurrentTick();

            if (currentTick > endTick)
            {
                Finish();
            }
        }

        /// <summary>
        /// 校验当前 tick：有哈希就比对，漂移过且这一 tick 有完整快照就把世界拉回正轨。
        /// <para>
        /// <b>tick 口径</b>：录像里 tick T 的哈希与快照记的都是「T 这一 tick 开始之前的世界」
        /// （录制接线件 <see cref="ReplayRecordDriver"/> 是推进器的第一个步骤）。
        /// 而 <see cref="SimulationRunner.AdvanceOneTick"/> 跑完之后逻辑时钟正好指向下一个要执行的 tick，
        /// 所以推完一格之后拿 <c>Clock.Tick</c> 去查，查到的就是同一个口径的那条记录。
        /// </para>
        /// </summary>
        private void CheckCurrentTick()
        {
            if (stateProvider == null || currentTick < 0L || currentTick > uint.MaxValue)
            {
                return;
            }

            uint tick = (uint)currentTick;

            while (hashCursor < hashCount && hashTicks[hashCursor] < tick)
            {
                hashCursor++;
            }

            if (hashCursor < hashCount && hashTicks[hashCursor] == tick)
            {
                ulong expected = hashValues[hashCursor];
                hashCursor++;

                scratch.Reset();
                stateProvider.SerializeAll(scratch);
                ulong actual = StateHasher.Compute(scratch);
                CheckedHashCount++;

                if (actual != expected)
                {
                    RecordDrift(currentTick, expected, actual);
                }
            }

            while (snapshotCursor < snapshotCount && snapshotTicks[snapshotCursor] < tick)
            {
                snapshotCursor++;
            }

            if (snapshotCursor < snapshotCount && snapshotTicks[snapshotCursor] == tick)
            {
                int index = snapshotCursor;
                snapshotCursor++;

                // 只在漂移之后才拿快照覆盖世界。没漂的时候也覆盖的话，两次漂移之间的分叉会被
                // 悄悄抹平——校验就从「查漂移」变成了「定期把结果改对」，那比不查还糟。
                if (pendingResync)
                {
                    RestoreSnapshot(index, tick);
                }
            }
        }

        /// <summary>记一次漂移。只有**第一次**会打日志与埋点，之后只累加计数。</summary>
        private void RecordDrift(long tick, ulong expected, ulong actual)
        {
            DriftCount++;
            pendingResync = true;

            if (HasDrifted)
            {
                return;
            }

            HasDrifted = true;
            FirstDriftTick = tick;

            // 用 Warn 不用 Error：ReplayRecorder 挂在 Application.logMessageReceived 上，
            // 收到 Error 就会自动存一份现场——放录像时报 Error 等于每漂一次就往磁盘写一份回放。
            // 漂移该有多显眼靠的是这条消息本身，不是日志级别。
            Log.Warn(
                $"回放在 tick {tick} 出现状态漂移：录像里记的是 {expected:X16}，这次重放算出 {actual:X16}。"
                + "这说明逻辑里有一处不确定来源（读了渲染帧时间 / UnityEngine.Random / 未进快照的状态，"
                + "或者用了随机的容器遍历顺序）。回放不中断，会在下一个完整快照处把世界拉回正轨继续放——"
                + "后面的内容仍有诊断价值。只报这一条，后续漂移看 DriftCount。");

            telemetry.TrackWarn(
                DriftEvent,
                TelemetryProps.Of(
                    (TickProp, tick),
                    (ExpectedProp, expected.ToString("X16")),
                    (ActualProp, actual.ToString("X16"))));
        }

        /// <summary>把世界恢复成录像里 <paramref name="tick"/> 那一刻的完整快照，然后继续往下放。</summary>
        private void RestoreSnapshot(int index, uint tick)
        {
            byte[] payload = snapshotPayloads[index];
            int length = snapshotLengths[index];
            if (payload == null || length <= 0)
            {
                return;
            }

            scratch.LoadFrom(payload, 0, length);
            stateProvider.DeserializeAll(scratch);
            pendingResync = false;
            ResyncCount++;

            telemetry.TrackWarn(
                ResyncEvent,
                TelemetryProps.Of((TickProp, (long)tick), (TelemetryKeys.Props.N, ResyncCount)));

            if (resyncLogged)
            {
                return;
            }

            resyncLogged = true;
            Log.Info(
                $"回放在 tick {tick} 用录像里的完整快照把世界拉回了正轨，继续往下放。"
                + "漂移点之后的内容照样能看，只是要记住它是被纠正过的。只报这一条，次数看 ResyncCount。");
        }

        /// <summary>放完了：切回实时，保留统计。</summary>
        private void Finish()
        {
            IsFinished = true;
            playing = false;
            accumulator = 0d;
            pendingSteps = 0;
            Deactivate();

            telemetry.Track(
                EndedEvent,
                (TickProp, currentTick),
                (TelemetryKeys.Props.N, DriftCount),
                (TelemetryKeys.Props.Ok, !HasDrifted));

            Log.Info(
                $"回放放完（tick {startTick}~{endTick}）："
                + (HasDrifted
                    ? $"有漂移，首次在 tick {FirstDriftTick}，共 {DriftCount} 个校验点对不上，"
                      + $"从快照拉回 {ResyncCount} 次"
                    : $"全程零漂移（校验了 {CheckedHashCount} 个状态哈希点）")
                + (inputs.MissingCount > 0L ? $"；有 {inputs.MissingCount} 个 tick 的输入是缺的" : string.Empty)
                + (inputs.SkippedCount > 0L ? $"；有 {inputs.SkippedCount} 条输入被跳过（对齐出过问题）" : string.Empty));
        }

        /// <summary>把推进器与输入源还给实时玩法，并恢复音量。</summary>
        private void Deactivate()
        {
            active = false;
            runner.SetMode(SimulationRunner.Mode.Live);
            inputSwitch.SwitchToLive();
            RestoreMute();
        }

        /// <summary>放开上一份回放并清空全部数据与统计。只在 <see cref="Load"/> 开头调。</summary>
        private void Unload()
        {
            if (active)
            {
                playing = false;
                Deactivate();
            }

            IsLoaded = false;
            IsFinished = false;
            sourcePath = null;
            header = default;
            startTick = 0L;
            endTick = 0L;
            currentTick = 0L;
            accumulator = 0d;
            pendingSteps = 0;

            inputs.Clear();
            hashCount = 0;
            hashCursor = 0;
            snapshotCount = 0;
            snapshotCursor = 0;
            QaMarkerCount = 0;
            IsTailTruncated = false;

            CheckedHashCount = 0;
            HasDrifted = false;
            FirstDriftTick = -1L;
            DriftCount = 0;
            ResyncCount = 0;
            pendingResync = false;
            clockDesyncLogged = false;
            resyncLogged = false;
            ConfigMismatch = false;
            ResultsUntrusted = false;
        }

        /// <summary>把整份文件的记录读进内存。返回 false 表示连一条都读不成（此时 <paramref name="error"/> 有值）。</summary>
        private bool ReadAllChunks(ReplayReader reader, out string error)
        {
            error = null;
            inputs.Clear();
            hashCount = 0;
            snapshotCount = 0;
            QaMarkerCount = 0;

            try
            {
                ReplayChunk chunk;
                while (reader.ReadNext(out chunk))
                {
                    switch (chunk.Type)
                    {
                        case ReplayFormat.ChunkType.Input:
                            inputs.Add(chunk.Tick, chunk.ReadInputCommand());
                            break;

                        case ReplayFormat.ChunkType.StateHash:
                            if (chunk.PayloadLength >= StateHashPayloadSize)
                            {
                                chunk.LoadPayloadInto(scratch);
                                AddStateHash(chunk.Tick, scratch.ReadULong());
                            }

                            break;

                        case ReplayFormat.ChunkType.Snapshot:
                            AddSnapshot(chunk);
                            break;

                        case ReplayFormat.ChunkType.QaMarker:
                            QaMarkerCount++;
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                // 一条坏记录不该让整份回放打不开：前面读到的照常用，后面的丢掉。
                // 这和 ReplayReader 的「尾部残缺尽力读」是同一条取舍——最该能读的那批文件恰恰是坏的。
                Log.Warn(
                    $"读回放记录时在第 {reader.ChunkCount} 条上出错，已停在这里，前面读到的照常用："
                    + $"{e.GetType().Name}：{e.Message}");
            }

            if (inputs.Count == 0 && snapshotCount == 0)
            {
                error = $"这份回放里既没有输入也没有快照，放不出任何东西：{reader.FilePath}";
                return false;
            }

            return true;
        }

        /// <summary>把一条状态哈希装进表里（按 tick 升序追加）。</summary>
        private void AddStateHash(uint tick, ulong value)
        {
            if (hashCount == hashTicks.Length)
            {
                int capacity = hashTicks.Length * 2;
                uint[] grownTicks = new uint[capacity];
                ulong[] grownValues = new ulong[capacity];
                Array.Copy(hashTicks, grownTicks, hashCount);
                Array.Copy(hashValues, grownValues, hashCount);
                hashTicks = grownTicks;
                hashValues = grownValues;
            }

            hashTicks[hashCount] = tick;
            hashValues[hashCount] = value;
            hashCount++;
        }

        /// <summary>
        /// 把一个完整快照拷进表里。**必须拷**：<see cref="ReplayChunk"/> 给的是
        /// <see cref="ReplayReader"/> 复用的缓冲，下一次 ReadNext 就会把它覆盖掉。
        /// </summary>
        private void AddSnapshot(ReplayChunk chunk)
        {
            if (chunk.PayloadLength <= 0)
            {
                return;
            }

            if (snapshotCount == snapshotTicks.Length)
            {
                int capacity = snapshotTicks.Length * 2;
                uint[] grownTicks = new uint[capacity];
                byte[][] grownPayloads = new byte[capacity][];
                int[] grownLengths = new int[capacity];
                Array.Copy(snapshotTicks, grownTicks, snapshotCount);
                Array.Copy(snapshotPayloads, grownPayloads, snapshotCount);
                Array.Copy(snapshotLengths, grownLengths, snapshotCount);
                snapshotTicks = grownTicks;
                snapshotPayloads = grownPayloads;
                snapshotLengths = grownLengths;
            }

            byte[] copy = new byte[chunk.PayloadLength];
            Array.Copy(chunk.GetPayloadBuffer(), chunk.PayloadOffset, copy, 0, chunk.PayloadLength);
            snapshotTicks[snapshotCount] = chunk.Tick;
            snapshotPayloads[snapshotCount] = copy;
            snapshotLengths[snapshotCount] = chunk.PayloadLength;
            snapshotCount++;
        }

        /// <summary>
        /// 把逻辑时钟定位到录像的起始 tick，走推进器的正规入口
        /// <see cref="SimulationRunner.SeekClockTo"/>（只挪计数，不跑任何逻辑）。
        /// <para>
        /// <b>为什么必须定位而不能把 tick 整体平移到从 0 开始</b>：重放常常从一份崩溃现场的中途快照开始，
        /// 起始 tick 是个大数，平移看着省事，但玩法只要读过一次 <c>context.Tick</c> 做取模判断
        /// （每 60 tick 结算一次之类），平移之后重放就和录制时走了不同的分支，而这种漂移查起来毫无头绪。
        /// </para>
        /// <para>
        /// 这里只对得上「时钟」这一半，世界状态那一半紧接着由 <see cref="RestoreStartSnapshot"/> 从
        /// 文件头的完整快照恢复。两半缺一不可——只挪时钟不恢复世界，正是
        /// <see cref="SimulationRunner.SeekClockTo"/> 文档里警告的那种错乱状态。
        /// </para>
        /// </summary>
        /// <param name="tick">录像头部记的起始 tick。</param>
        private void SeekClockTo(uint tick)
        {
            runner.SeekClockTo(tick);
        }

        /// <summary>从文件头里的起始完整快照恢复世界。没有注册表或没有快照时只记一条日志。</summary>
        private void RestoreStartSnapshot()
        {
            if (stateProvider == null)
            {
                Log.Warn(
                    "回放没有挂世界状态注册表（AttachStateProvider）：只会重放输入，"
                    + "既不恢复起点状态、也做不了漂移检测。这份回放的结果不能当证据用。");
                return;
            }

            if (header.SnapshotLength <= 0)
            {
                Log.Warn(
                    $"这份回放没有起始完整快照（起始 tick {header.StartTick}）：世界会从它当前的样子开始跑，"
                    + "而不是录制时那一刻的样子。多半是录制时没配快照间隔，或者崩得太早还没打上第一个快照。");
                return;
            }

            scratch.LoadFrom(header.GetSnapshotBuffer(), header.SnapshotOffset, header.SnapshotLength);
            stateProvider.DeserializeAll(scratch);
        }

        /// <summary>
        /// 把随机源的主种子换成录像头部记的那个值（<see cref="RandomService.Reseed"/>）。
        /// <para>
        /// <b>这一步真正救的是「重放途中第一次被取用的新流」</b>：已经建好的 <c>logic.*</c> 流，
        /// 状态紧接着会被起始快照覆盖，换不换种子都一样；但玩法第一次用到某条流时才会
        /// <see cref="IRandomService.Stream"/> 把它建出来，种子按**当时**的主种子派生——
        /// 主种子不对，那条流从出生起就和录制时不是同一串数，而且全程没有任何报错，
        /// 只表现为后面某个 tick 突然开始漂。
        /// </para>
        /// <para>
        /// 没有随机源（<c>random</c> 为 null，EditMode 里直接 new 播放器时如此）就什么都不做：
        /// 这种接线本来就没有确定性随机可言，报一条警告只会在每份回放的日志里重复一遍同样的废话。
        /// </para>
        /// </summary>
        private void ApplyHeaderSeed()
        {
            if (random == null || random.MasterSeed == header.Seed)
            {
                return;
            }

            ulong previous = random.MasterSeed;
            random.Reseed(header.Seed);

            Log.Info(
                $"回放的主随机种子是 {header.Seed}，本次运行原本是 {previous}，已按录像改回去"
                + "（已建好的流按新种子原地重设，实例不变；logic.* 流的状态随后由起始快照覆盖）。");
        }

        /// <summary>取当前配置指纹。配置服务还没初始化完时它会抛，这里按「取不到」处理，不让回放打不开。</summary>
        private bool TryReadConfigHash(out ulong hash)
        {
            hash = 0UL;
            if (config == null)
            {
                return false;
            }

            try
            {
                hash = config.ContentHash;
                return true;
            }
            catch (Exception e)
            {
                Log.Debug($"取配置指纹失败，本次回放跳过配置版本校验：{e.GetType().Name}：{e.Message}");
                return false;
            }
        }

        /// <summary>按配置静音。重放常要连着放好几遍同一段，音效跟着重复十几次很折磨人。</summary>
        private void ApplyMute()
        {
            if (muted || audioService == null || replayConfig == null || !replayConfig.MuteDuringPlayback)
            {
                return;
            }

            savedMasterVolume = audioService.MasterVolume;
            audioService.MasterVolume = 0f;
            muted = true;
        }

        /// <summary>把音量还回去。和 <see cref="ApplyMute"/> 成对，漏一个就会留下一个永远静音的游戏。</summary>
        private void RestoreMute()
        {
            if (!muted)
            {
                return;
            }

            muted = false;
            if (audioService != null)
            {
                audioService.MasterVolume = savedMasterVolume;
            }
        }

        /// <summary>统一的失败出口：记下原因、埋一条点、返回 false。</summary>
        private bool Fail(string message, out string error)
        {
            error = message;
            LastError = message;
            telemetry.TrackWarn(LoadFailedEvent, TelemetryProps.Of((TelemetryKeys.Props.Reason, message)));
            return false;
        }
    }
}
