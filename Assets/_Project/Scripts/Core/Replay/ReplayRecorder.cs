// 职责：常驻录最近一段时间的现场，出事时把它存成一份回放文件。
//   三个独立环形缓冲（输入 / 状态哈希 / 完整快照）常驻在内存里滚动覆盖，稳态零堆分配；
//   三种触发（Unity 报错、热键、代码 API）都汇到同一条保存路径上，把环里的内容按 tick 顺序
//   交给 ReplayWriter 写成文件。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：工程内唯一「常驻一段最近数据、出事时导出」的设施是
//      Telemetry/EditorMirrorTelemetrySink（把埋点行镜像到 Logs/），但它存的是**文本行**、
//      逐条追加落盘、不回卷、也没有「按 tick 取回最近 N 条」的概念。回放要的是定长二进制记录、
//      写满原地覆盖、导出时按 tick 归并——除了「出事时要有东西可看」这个动机之外没有一行可共用。
//      Core/Save/JsonSaveService 是整份 JSON 一次性写出的存档，更不沾边。
//   2. 扩展不行：不能把它加进 ReplayWriter。那个类的职责是「一条记录怎么变成字节」，
//      生命周期是一份文件的开与合；录制器的生命周期是**整局游戏**，它要管环、管热键、管日志回调、
//      管限流与重入。塞进去之后 ReplayWriter 就得同时是「文件」和「常驻服务」，
//      而回放播放器、剪辑工具要用的只是前者。
//      也不能挂到 Core/Simulation/SimulationRunner 上：那是确定性内核，依赖方向只许 Replay → Simulation，
//      内核不认识回放系统（理由见 IReplayStateProvider 的文件注释）。所以录制器**不订阅**推进器，
//      而是由接线层每 tick 调一次 RecordTick——谁驱动谁，方向是反过来的。
//   3. 不和 ReplayConfig 合成一个文件：那是资产（数值），这是服务（行为），
//      合一会让「改一个默认值」和「改一段保存逻辑」落在同一个文件上。

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Boot;
using Game.Core.Logging;
using Game.Core.Platform;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using VContainer.Unity;

namespace Game.Core.Replay
{
    /// <summary>
    /// 回放录制器。常驻录最近 <see cref="ReplayConfig.Options.BufferSeconds"/> 秒，出事时把现场存下来。
    ///
    /// <para><b>接线</b>（接线本身是后续任务，这里写清它期望怎么被用）：</para>
    /// <code>
    /// recorder.AttachStateProvider(provider);                 // 想要哈希与快照才需要
    /// recorder.BeginSession(seed, configHash, fixedDeltaTime); // 开局一次，清空环并记下头部元信息
    /// recorder.RecordTick(tick, command);                     // 每个逻辑 tick 一次（接线层调）
    /// string path = recorder.Save(ReplayRecorder.SaveReason.Api);
    /// </code>
    ///
    /// <para>
    /// <b>三个环各自独立</b>，不是一个环里混装：输入每 tick 一条、状态哈希默认每秒一条、
    /// 完整快照默认每 10 秒一条，频率差两个数量级。挤在同一个环里，要么快照把输入挤掉
    /// （崩溃现场最不能丢的恰恰是输入），要么为了留住输入把环开到几十 MB。
    /// </para>
    ///
    /// <para>
    /// <b>稳态零堆分配</b>：三个环都是构造时按容量预分配的定长数组，写满回卷。
    /// 刻意不用 <c>List&lt;T&gt;</c> / <c>MemoryStream</c>——它们中途扩容会分配，
    /// 而这条路径每 tick 都要走。稳态还在触发扩容说明容量算错了，去改容量，不是容忍它。
    /// （唯一的例外是快照环的槽位：世界第一次变大时那个槽会重新分配一次，见 <see cref="PushSnapshot"/>。）
    /// </para>
    ///
    /// <para>
    /// <b>自我放大的防护</b>：<c>Application.logMessageReceived</c> 上已经挂着
    /// <see cref="UnityLogTelemetryBridge"/>，而保存过程自己也可能报错（磁盘满、路径非法、
    /// 序列化抛异常）。那条 Error 会再次触发自动保存 → 又失败 → 又报错，几秒钟就能把磁盘写满。
    /// 这里用三道闸挡住，缺一不可，见 <see cref="HandleLog"/>。
    /// </para>
    ///
    /// <para>线程约定：只在主线程 / 逻辑线程上用，内部不加锁。</para>
    /// </summary>
    public sealed class ReplayRecorder : IGameService, ITickable, IDisposable
    {
        /// <summary>回放文件在 <see cref="IPlatformService.SaveRoot"/> 下的子目录名。</summary>
        public const string ReplayFolderName = "Replays";

        /// <summary>埋点模块名。不往 TelemetryKeys 里加常量，理由同 SimulationRunner 的 tick_dropped——
        /// 那张表是框架各服务的公共键表，本服务只有自己用的这几个名字，就地写清楚更省一次跳转。</summary>
        private const string TelemetryModule = "core.replay";

        /// <summary>存出一份回放，属性 <c>reason</c> <c>n</c>(chunk 条数) <c>bytes</c> <c>ms</c>。</summary>
        private const string SavedEvent = "saved";

        /// <summary>保存失败，带 <c>err</c> / <c>st</c>。**同一次失败只埋这一条，且不重试**。</summary>
        private const string SaveFailedEvent = "save_failed";

        /// <summary>快照超过单条 chunk 上限、本次快照未记录，属性 <c>bytes</c> <c>tick</c> <c>n</c>。</summary>
        private const string SnapshotOversizeEvent = "snapshot_oversize";

        /// <summary>QA 打点热键产生的默认说明文本。写成常量，按一次键不分配字符串。</summary>
        private const string HotkeyMarkerLabel = "hotkey";

        /// <summary>属性键：逻辑 tick 号。TelemetryKeys.Props 里还没有这个语义，按那张表的规矩新键随便加。</summary>
        private const string TickProp = "tick";

        /// <summary>QA 打点环的容量。人按热键产生，一局几条到几十条，写死够用，不值得再多一个可调参数。</summary>
        private const int QaMarkerCapacity = 64;

        /// <summary>快照槽位容量的对齐粒度（字节）。按需分配时向上取整到它的整数倍，留点余量，
        /// 世界略微变大时不必立刻换一个数组。</summary>
        private const int SnapshotSlotAlignment = 1024;

        /// <summary>tickRate 的合理区间，和 SimulationConfig 上那条 [Range(1, 240)] 对齐。</summary>
        private const int MinTickRate = 1;

        private const int MaxTickRate = 240;

        /// <summary>缓冲秒数的合理区间，和 ReplayConfig 上那条 [Range(1, 1800)] 对齐。</summary>
        private const float MinBufferSeconds = 1f;

        private const float MaxBufferSeconds = 1800f;

        /// <summary>同一秒内重名时最多试几个后缀。试完还重名就让它覆盖——那说明一秒内存了 99 份，
        /// 比丢一份更该关心的是为什么会这样。</summary>
        private const int MaxNameCollisionRetries = 99;

        private readonly ReplayConfig.Options options;
        private readonly IPlatformService platform;
        private readonly ITelemetryScope telemetry;

        // ── 输入环：每 tick 一条，回放的命根子 ──────────────────────────────
        // 存 InputCommand 结构体而不是它的 31 字节序列化形式：数组是值类型数组（无堆分配），
        // 容量只差一点点（结构体对齐后 32 字节 vs 31），却省掉了「录时编码、存盘时解码再编码」
        // 这一趟往返——多一次编解码就多一处「录的布局和放的布局不一致」的可能。
        private readonly InputCommand[] inputCommands;
        private readonly uint[] inputTicks;
        private int inputHead;
        private int inputCount;

        // ── 状态哈希环 ──────────────────────────────────────────────────
        private readonly ulong[] hashValues;
        private readonly uint[] hashTicks;
        private int hashHead;
        private int hashCount;

        // ── 快照环 ─────────────────────────────────────────────────────
        private readonly byte[][] snapshotPayloads;
        private readonly int[] snapshotLengths;
        private readonly uint[] snapshotTicks;
        private int snapshotHead;
        private int snapshotCount;

        // ── QA 打点环 ──────────────────────────────────────────────────
        private readonly string[] qaLabels;
        private readonly uint[] qaTicks;
        private int qaHead;
        private int qaCount;

        /// <summary>序列化世界状态用的复用缓冲。哈希与快照同一 tick 都要时只序列化一次，两处共用这一份字节。</summary>
        private readonly StateBuffer scratch = new StateBuffer();

        private readonly uint stateHashIntervalTicks;
        private readonly uint snapshotIntervalTicks;

        private IReplayStateProvider stateProvider;

        private ulong seed;
        private ulong configHash;
        private float fixedDeltaTime;
        private uint lastRecordedTick;

        private bool attached;

        /// <summary>保存重入标志。**这是防自我放大的那道核心闸**，见 <see cref="HandleLog"/>。</summary>
        private bool saving;

        private float lastAutoSaveRealtime = float.NegativeInfinity;
        private int suppressedAutoSaveCount;
        private int saveCount;
        private int failedSaveCount;
        private string lastSavedPath;

        /// <summary>快照超限是否已经埋过点。连着超限只埋第一条；掉回正常再超限时允许再埋一条。</summary>
        private bool snapshotOversizeReported;

        private int droppedOversizeSnapshotCount;

        /// <summary>
        /// 从配置资产构造（接线走这条）。步长参数不在 <see cref="ReplayConfig"/> 里重复定义，
        /// 而是从 <see cref="SimulationConfig.TickRate"/> 取——同一个数在两份资产里各写一遍，
        /// 迟早有一天只改了其中一份，表现是「缓冲说是 5 分钟，实际只有 2 分半」。
        /// </summary>
        /// <param name="config">回放配置资产，不可为空。</param>
        /// <param name="simulation">确定性内核配置资产，只取 <c>TickRate</c>，不可为空。</param>
        /// <param name="platform">平台服务，回放文件写在它的 <c>SaveRoot</c> 下，不可为空。</param>
        /// <param name="telemetry">埋点服务，可为 null（EditMode 测试里直接 new 时没有容器）。</param>
        public ReplayRecorder(
            ReplayConfig config,
            SimulationConfig simulation,
            IPlatformService platform,
            ITelemetryService telemetry)
            : this(
                ReadOptions(config),
                ReadTickRate(simulation),
                platform,
                telemetry)
        {
        }

        /// <summary>
        /// 从一份现成的配置快照构造。给 EditMode 测试与 <c>execute_code</c> 用：
        /// 调一个参数不必先造一个 <c>.asset</c>。
        /// </summary>
        /// <param name="options">配置快照，通常来自 <see cref="ReplayConfig.ToOptions"/>。</param>
        /// <param name="tickRate">每秒多少个逻辑 tick，用来把缓冲秒数换算成环容量。</param>
        /// <param name="platform">平台服务，不可为空。</param>
        /// <param name="telemetry">埋点服务，可为 null。</param>
        public ReplayRecorder(
            ReplayConfig.Options options,
            int tickRate,
            IPlatformService platform,
            ITelemetryService telemetry)
        {
            this.options = options;
            this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
            this.telemetry = telemetry == null
                ? (ITelemetryScope)NullTelemetryScope.Instance
                : telemetry.Scope(TelemetryModule);

            int clampedTickRate = Clamp(tickRate, MinTickRate, MaxTickRate);
            float clampedSeconds = Clamp(options.BufferSeconds, MinBufferSeconds, MaxBufferSeconds);
            fixedDeltaTime = 1f / clampedTickRate;

            // 输入环容量 = 缓冲秒数 × tickRate。默认 300 × 60 = 18000 条。
            int capacity = (int)(clampedSeconds * clampedTickRate);
            InputCapacity = capacity < 1 ? 1 : capacity;
            inputCommands = new InputCommand[InputCapacity];
            inputTicks = new uint[InputCapacity];

            // 状态哈希环容量按间隔算：同一段时间里能产出多少条就留多少条，多留一条兜边界。
            stateHashIntervalTicks = options.StateHashIntervalTicks <= 0
                ? 0u
                : (uint)options.StateHashIntervalTicks;
            StateHashCapacity = stateHashIntervalTicks == 0
                ? 0
                : (int)(InputCapacity / stateHashIntervalTicks) + 1;
            hashValues = new ulong[StateHashCapacity];
            hashTicks = new uint[StateHashCapacity];

            // 快照环容量是**独立配的个数**，不跟着秒数走：快照单条几十 KB，
            // 跟输入一样按秒数算会直接把内存吃干净。
            snapshotIntervalTicks = options.SnapshotIntervalTicks <= 0
                ? 0u
                : (uint)options.SnapshotIntervalTicks;
            SnapshotCapacity = snapshotIntervalTicks == 0 || options.SnapshotRingSize <= 0
                ? 0
                : options.SnapshotRingSize;
            snapshotPayloads = new byte[SnapshotCapacity][];
            snapshotLengths = new int[SnapshotCapacity];
            snapshotTicks = new uint[SnapshotCapacity];

            qaLabels = new string[QaMarkerCapacity];
            qaTicks = new uint[QaMarkerCapacity];
        }

        /// <summary>保存的触发来源。会写进文件名，好让人一眼看出这份现场是怎么来的。</summary>
        public enum SaveReason
        {
            /// <summary>代码主动调 <see cref="Save"/> / <see cref="SaveAsync"/>。</summary>
            Api = 0,

            /// <summary>按了保存热键。</summary>
            Hotkey = 1,

            /// <summary>收到 Error / Exception / Assert，自动存的。</summary>
            Error = 2,
        }

        /// <summary>录制总开关（配置快照里已经把 Auto 解析好了）。关掉时所有 Record / Save 都是空调用。</summary>
        public bool Enabled => options.Enabled;

        /// <summary>这份配置快照。调试台要显示当前参数时读它。</summary>
        public ReplayConfig.Options CurrentOptions => options;

        /// <summary>输入环容量（条）。</summary>
        public int InputCapacity { get; }

        /// <summary>状态哈希环容量（条），0 表示不记哈希。</summary>
        public int StateHashCapacity { get; }

        /// <summary>快照环容量（个），0 表示不留快照。</summary>
        public int SnapshotCapacity { get; }

        /// <summary>环里现有多少条输入。</summary>
        public int BufferedInputCount => inputCount;

        /// <summary>环里现有多少条状态哈希。</summary>
        public int BufferedStateHashCount => hashCount;

        /// <summary>环里现有几个完整快照。</summary>
        public int BufferedSnapshotCount => snapshotCount;

        /// <summary>环里现有几条 QA 打点。</summary>
        public int BufferedQaMarkerCount => qaCount;

        /// <summary>环里最旧那条输入的 tick。环是空的时候返回 0。</summary>
        public uint OldestBufferedTick => inputCount == 0 ? 0u : inputTicks[IndexAt(inputHead, inputCount, InputCapacity, 0)];

        /// <summary>环里最新那条输入的 tick。环是空的时候返回 0。</summary>
        public uint NewestBufferedTick =>
            inputCount == 0 ? 0u : inputTicks[IndexAt(inputHead, inputCount, InputCapacity, inputCount - 1)];

        /// <summary>正在保存。<b>这个标志期内的任何日志都不会再触发保存</b>，见 <see cref="HandleLog"/>。</summary>
        public bool IsSaving => saving;

        /// <summary>成功存出去几份。</summary>
        public int SaveCount => saveCount;

        /// <summary>失败几次。失败只埋一条点、不重试，所以这个数就是「有几次现场没存下来」。</summary>
        public int FailedSaveCount => failedSaveCount;

        /// <summary>被限流挡掉的自动保存次数。一次崩溃连报十几条 Error 时，这个数就是被合并掉的那些。</summary>
        public int SuppressedAutoSaveCount => suppressedAutoSaveCount;

        /// <summary>因为超过单条 chunk 上限而没能记下的快照条数。不为 0 就说明世界状态该拆条写了。</summary>
        public int DroppedOversizeSnapshotCount => droppedOversizeSnapshotCount;

        /// <summary>最近一次成功保存的文件路径，没存过是 null。</summary>
        public string LastSavedPath => lastSavedPath;

        /// <summary>已经接上 Unity 日志回调了没有。</summary>
        public bool IsAttached => attached;

        /// <summary>
        /// 挂上世界状态注册表。<b>不挂也能录</b>——不挂就只有输入流，没有状态哈希与完整快照
        /// （能重放，但没法跳到中途，也说不出是从哪个 tick 开始漂的）。
        /// </summary>
        /// <param name="provider">状态注册表，传 null 表示摘掉。</param>
        public void AttachStateProvider(IReplayStateProvider provider)
        {
            stateProvider = provider;
        }

        /// <summary>
        /// 开一局新的录制：清空三个环与 QA 打点，记下要写进文件头的元信息。
        /// </summary>
        /// <param name="seed">本局随机种子。重放时必须用它重建随机流，否则第一帧就分叉。</param>
        /// <param name="configHash">配置内容指纹，来自配置服务的 ContentHash。</param>
        /// <param name="fixedDeltaTime">逻辑固定步长（秒），必须是大于 0 的有限值。</param>
        /// <exception cref="ArgumentOutOfRangeException">步长不是正的有限值。</exception>
        public void BeginSession(ulong seed, ulong configHash, float fixedDeltaTime)
        {
            if (float.IsNaN(fixedDeltaTime) || float.IsInfinity(fixedDeltaTime) || fixedDeltaTime <= 0f)
            {
                // 和 ReplayHeader 一样在能报错的最早时刻报错：坏步长写进文件要等到放录像时才炸，
                // 那时人已经在查一个不存在的 bug 了。
                throw new ArgumentOutOfRangeException(
                    nameof(fixedDeltaTime), fixedDeltaTime, "固定步长必须是大于 0 的有限值");
            }

            this.seed = seed;
            this.configHash = configHash;
            this.fixedDeltaTime = fixedDeltaTime;

            ClearBuffers();
        }

        /// <summary>
        /// 录一个逻辑 tick。<b>接线层每 tick 调一次</b>，稳态零堆分配。
        /// <para>
        /// 输入一定会进环；状态哈希与完整快照按各自的间隔判断要不要采——
        /// 同一 tick 两者都要时只序列化一次世界状态，两处共用那一段字节
        /// （这也保证了「进了快照的字段一定进了哈希」）。
        /// </para>
        /// </summary>
        /// <param name="tick">这一 tick 的序号，取 <see cref="ILogicClock.Tick"/>。</param>
        /// <param name="command">这一 tick 用掉的那条输入命令。</param>
        public void RecordTick(uint tick, InputCommand command)
        {
            if (!options.Enabled)
            {
                return;
            }

            PushInput(tick, command);
            lastRecordedTick = tick;

            if (stateProvider == null)
            {
                return;
            }

            bool wantHash = StateHashCapacity > 0 && tick % stateHashIntervalTicks == 0u;
            bool wantSnapshot = SnapshotCapacity > 0 && tick % snapshotIntervalTicks == 0u;
            if (!wantHash && !wantSnapshot)
            {
                return;
            }

            scratch.Reset();
            stateProvider.SerializeAll(scratch);

            if (wantHash)
            {
                PushStateHash(tick, StateHasher.Compute(scratch));
            }

            if (wantSnapshot)
            {
                PushSnapshot(tick, scratch);
            }
        }

        /// <summary>
        /// 打一条 QA 标记（「就是这一帧卡住的」）。走 <see cref="ReplayFormat.ChunkType.QaMarker"/>
        /// 独立 chunk，<b>不占每 tick 的字节，也不污染输入流</b>。
        /// </summary>
        /// <param name="label">说明文本，可为 null / 空串（那就是一条没写说明的打点）。</param>
        public void RecordQaMarker(string label)
        {
            if (!options.Enabled)
            {
                return;
            }

            qaLabels[qaHead] = label;
            qaTicks[qaHead] = lastRecordedTick;
            qaHead = Advance(qaHead, QaMarkerCapacity);
            if (qaCount < QaMarkerCapacity)
            {
                qaCount++;
            }
        }

        /// <summary>
        /// 把环里的现场存成一份回放文件，返回文件路径；没存成（关着、重入、环是空的、写盘失败）返回 null。
        /// <para>
        /// <b>失败不抛、也不重试</b>：录制器是调试设施，它绝不该把游戏带崩，更不该在磁盘出问题时
        /// 反复重试把问题放大。失败只埋一条 <c>save_failed</c>。
        /// </para>
        /// </summary>
        /// <param name="reason">触发来源，会写进文件名。</param>
        public string Save(SaveReason reason)
        {
            if (!options.Enabled)
            {
                return null;
            }

            // 重入：保存过程里又有人要保存（多半是保存自己报的那条 Error 触发的），直接拒。
            if (saving)
            {
                return null;
            }

            // 环里一点东西都没有：存出来会是一份只有头的空文件，徒增磁盘噪音。
            if (inputCount == 0 && snapshotCount == 0 && qaCount == 0)
            {
                return null;
            }

            saving = true;
            try
            {
                return SaveCore(reason);
            }
            finally
            {
                // 无论成败都要放开，否则一次失败就会让录制器永远存不出第二份。
                saving = false;
            }
        }

        /// <summary>
        /// <see cref="Save"/> 的异步外壳，供 async 流程直接 await，不用为它写特例。
        /// <para>
        /// <b>它当前是同步执行的，这是刻意的</b>：三个环归逻辑线程所有，把写盘丢到线程池上只有两种结局——
        /// 要么录制继续往环里写，工作线程读到的是被撕开的半旧半新内容；要么保存期间暂停录制，
        /// 那就凭空丢掉几十毫秒的输入。而这条路径只在**已经出事**时才走，主线程卡这几十毫秒无关紧要。
        /// 保留异步签名的价值在于：将来真要挪到工作线程（先把环整段拷出来再交出去），
        /// 调用点一行都不用改。
        /// </para>
        /// </summary>
        /// <param name="reason">触发来源。</param>
        /// <param name="ct">取消令牌。已取消时直接抛 <see cref="OperationCanceledException"/>。</param>
        public UniTask<string> SaveAsync(SaveReason reason, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return UniTask.FromResult(Save(reason));
        }

        /// <summary>
        /// 轮询调试热键。由 <see cref="Tick"/>（VContainer 的每帧回调）驱动，
        /// EditMode 测试可以直接调。
        /// <para>
        /// 录制器在框架层，这里**直接读 <c>Keyboard.current</c>** 而不走玩法的 Action Map：
        /// 保存键与打点键是调试键，不该出现在玩家的按键设置里，也不该经过会被录进回放的输入通道。
        /// （<c>Runtime/</c> 那条「玩法不准直接读输入」的 lint 规则只管 <c>Scripts/Runtime/</c>。）
        /// </para>
        /// </summary>
        public void PollHotkeys()
        {
            if (!options.Enabled)
            {
                return;
            }

            if (options.SaveHotkey == Key.None && options.QaMarkerHotkey == Key.None)
            {
                return;
            }

            // Keyboard 是 InputSystem 的普通托管对象，不是 UnityEngine.Object，== null 就是真的判空。
            // 没有键盘的设备（真机手游）上它一直是 null，这条路径必须判。
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (options.SaveHotkey != Key.None && WasPressed(keyboard, options.SaveHotkey))
            {
                Save(SaveReason.Hotkey);
            }

            if (options.QaMarkerHotkey != Key.None && WasPressed(keyboard, options.QaMarkerHotkey))
            {
                RecordQaMarker(HotkeyMarkerLabel);
            }
        }

        /// <summary>接上 Unity 日志回调，开始自动保存。重复调用无副作用；总开关或自动保存开关关着时不接。</summary>
        public void Attach()
        {
            if (attached || !options.Enabled || !options.AutoSaveOnError)
            {
                return;
            }

            Application.logMessageReceived += HandleLog;
            attached = true;
        }

        /// <summary>摘掉 Unity 日志回调。重复调用无副作用。</summary>
        public void Detach()
        {
            if (!attached)
            {
                return;
            }

            Application.logMessageReceived -= HandleLog;
            attached = false;
        }

        /// <summary>
        /// Unity 日志回调：收到 Error / Exception / Assert 就自动把现场存下来。
        /// 做成 public 是为了测试能直接喂一条消息进来验重入防护，不用真打一条 Unity 日志。
        ///
        /// <para><b>三道闸，缺一不可</b>（少了任何一道都会变成「保存失败 → 报错 → 保存 → 又失败」的自我放大）：</para>
        /// <list type="number">
        /// <item><b>前缀过滤</b>：以 <see cref="TelemetryFormat.LinePrefix"/> 开头的行是埋点自己打的。
        /// 本类失败时埋的那条 <c>save_failed</c> 最终就是一次 <c>Debug.LogError</c>，
        /// 不挡住它，第一次失败就会立刻引出第二次保存。</item>
        /// <item><b>重入标志</b>：<see cref="IsSaving"/> 期间收到的任何日志一律不触发保存。
        /// 这一道管的是「不是埋点打的，但是在保存过程中冒出来的」——序列化抛异常、
        /// 磁盘满时 FileStream 自己打的警告，都在这个窗口里。</item>
        /// <item><b>限流</b>：两次自动保存至少隔
        /// <see cref="ReplayConfig.Options.AutoSaveMinIntervalSeconds"/> 秒。一次崩溃常连报十几条 Error，
        /// 前两道闸挡的是「同一次保存过程中」的递归，挡不住「十几条独立的 Error 各存一份」。</item>
        /// </list>
        /// </summary>
        public void HandleLog(string condition, string stackTrace, LogType type)
        {
            if (!options.Enabled || !options.AutoSaveOnError)
            {
                return;
            }

            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
            {
                return;
            }

            // 第一道闸
            if (condition != null && condition.StartsWith(TelemetryFormat.LinePrefix, StringComparison.Ordinal))
            {
                return;
            }

            // 第二道闸
            if (saving)
            {
                return;
            }

            // 第三道闸
            float now = Time.realtimeSinceStartup;
            if (options.AutoSaveMinIntervalSeconds > 0f
                && now - lastAutoSaveRealtime < options.AutoSaveMinIntervalSeconds)
            {
                suppressedAutoSaveCount++;
                return;
            }

            // 先记时间再保存：保存失败也要占住这个窗口，否则「失败 → 不更新时间 → 下一条 Error 立刻再试」
            // 就把限流绕过去了，而保存失败恰恰是最容易连着发生的情况。
            lastAutoSaveRealtime = now;
            Save(SaveReason.Error);
        }

        /// <summary>
        /// 框架启动时接上日志回调（<see cref="IGameService"/>）。不在这里建目录——
        /// <see cref="ReplayWriter"/> 写的时候会建，真存了才留下痕迹。
        /// </summary>
        public UniTask InitializeAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Attach();
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// VContainer 的每帧回调（<see cref="ITickable"/>），只用来轮询热键。
        /// <b>这不是逻辑 tick</b>——录一个逻辑 tick 请调 <see cref="RecordTick"/>。
        /// </summary>
        public void Tick()
        {
            PollHotkeys();
        }

        /// <summary>释放：摘掉日志回调。订阅与退订成对，漏一个就会留下一个继续收日志的死对象。</summary>
        public void Dispose()
        {
            Detach();
        }

        /// <summary>取配置快照。ScriptableObject 判空只能用 ==（Unity 重载了它来识别已销毁对象）。</summary>
        private static ReplayConfig.Options ReadOptions(ReplayConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            return config.ToOptions();
        }

        /// <summary>取 tickRate。同上，ScriptableObject 判空只能用 ==。</summary>
        private static int ReadTickRate(SimulationConfig simulation)
        {
            if (simulation == null)
            {
                throw new ArgumentNullException(nameof(simulation));
            }

            return simulation.TickRate;
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        private static float Clamp(float value, float min, float max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        /// <summary>环下标前进一格，写满回卷。</summary>
        private static int Advance(int index, int capacity)
        {
            int next = index + 1;
            return next >= capacity ? 0 : next;
        }

        /// <summary>
        /// 取环里第 <paramref name="order"/> 旧的那一项的下标（0 = 最旧）。
        /// 没写满时从 0 开始数，写满了就从 head 开始数——head 指向的正是最旧的那一格。
        /// </summary>
        private static int IndexAt(int head, int count, int capacity, int order)
        {
            int start = count == capacity ? head : 0;
            int index = start + order;
            return index >= capacity ? index - capacity : index;
        }

        /// <summary>一个键这一帧刚按下没有。</summary>
        private static bool WasPressed(Keyboard keyboard, Key key)
        {
            KeyControl control = keyboard[key];
            return control != null && control.wasPressedThisFrame;
        }

        private void ClearBuffers()
        {
            inputHead = 0;
            inputCount = 0;
            hashHead = 0;
            hashCount = 0;
            snapshotHead = 0;
            snapshotCount = 0;
            qaHead = 0;
            qaCount = 0;
            lastRecordedTick = 0u;
            snapshotOversizeReported = false;

            // 快照槽位的数组**不清掉**：留着下一局继续用，那正是预分配的意义；
            // 有效长度由 snapshotLengths 说了算，残留字节既进不了文件也读不出来。
        }

        private void PushInput(uint tick, InputCommand command)
        {
            inputCommands[inputHead] = command;
            inputTicks[inputHead] = tick;
            inputHead = Advance(inputHead, InputCapacity);
            if (inputCount < InputCapacity)
            {
                inputCount++;
            }
        }

        private void PushStateHash(uint tick, ulong hash)
        {
            hashValues[hashHead] = hash;
            hashTicks[hashHead] = tick;
            hashHead = Advance(hashHead, StateHashCapacity);
            if (hashCount < StateHashCapacity)
            {
                hashCount++;
            }
        }

        /// <summary>
        /// 把一份完整快照拷进快照环。
        /// <para>
        /// <b>超过单条 chunk 上限就不记，但输入流照录</b>：payload 的长度前缀是 u16，上限 65535 字节，
        /// 顶到了说明世界状态已经很大了。这时埋一条点说清楚，然后继续录——
        /// 输入才是重放的命根子，绝不能因为快照写不下就把它一起断掉。也绝不静默截断：
        /// 截出来的快照恢复后是个「错误的世界」，而且没有任何迹象表明它错了。
        /// </para>
        /// <para>
        /// 槽位按需分配并向上取整到 <see cref="SnapshotSlotAlignment"/> 的整数倍：
        /// 预分配 30 × 65535 字节（约 2MB）大多是白占，而世界大小稳定之后这里一次也不会再分配。
        /// </para>
        /// </summary>
        private void PushSnapshot(uint tick, StateBuffer buffer)
        {
            int length = buffer.Length;
            if (length > ReplayFormat.MaxChunkPayloadLength)
            {
                droppedOversizeSnapshotCount++;
                if (!snapshotOversizeReported)
                {
                    snapshotOversizeReported = true;
                    telemetry.TrackWarn(
                        SnapshotOversizeEvent,
                        TelemetryProps.Of(
                            (TelemetryKeys.Props.Bytes, length),
                            (TickProp, (long)tick),
                            (TelemetryKeys.Props.N, droppedOversizeSnapshotCount)));

                    // 埋点是给脚本吃的结构化事实，这一句是给人看的。用 Warn 不用 Error：
                    // Error 会被自己的日志回调接住，变成一次自动保存——而这本来只是一条「快照太大了」。
                    Log.Warn(
                        $"世界状态快照 {length} 字节超过单条 chunk 上限 "
                        + $"{ReplayFormat.MaxChunkPayloadLength} 字节，本次快照未记录（tick {tick}）；"
                        + "输入流继续录制。正解是升回放格式版本把长度前缀改宽，或者把快照拆成多条 chunk。");
                }

                return;
            }

            // 掉回正常尺寸了，下次再超限允许再报一条——否则世界一度变大又缩回去之后，
            // 后面真正的超限就再也没人知道了。
            snapshotOversizeReported = false;

            byte[] slot = snapshotPayloads[snapshotHead];
            if (slot == null || slot.Length < length)
            {
                int capacity = (length + SnapshotSlotAlignment - 1) / SnapshotSlotAlignment * SnapshotSlotAlignment;
                slot = new byte[capacity < SnapshotSlotAlignment ? SnapshotSlotAlignment : capacity];
                snapshotPayloads[snapshotHead] = slot;
            }

            Array.Copy(buffer.GetBuffer(), 0, slot, 0, length);
            snapshotLengths[snapshotHead] = length;
            snapshotTicks[snapshotHead] = tick;
            snapshotHead = Advance(snapshotHead, SnapshotCapacity);
            if (snapshotCount < SnapshotCapacity)
            {
                snapshotCount++;
            }
        }

        /// <summary>真正写文件的那一段。只从 <see cref="Save"/> 调，进来时重入标志已经立起来了。</summary>
        private string SaveCore(SaveReason reason)
        {
            string reasonTag = ReasonTag(reason);
            string path = BuildPath(reasonTag);
            long startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

            try
            {
                int chunks;
                long bytes;
                using (ReplayWriter writer = new ReplayWriter(path))
                {
                    int headerSnapshotOrder = PickHeaderSnapshot(out uint startTick);
                    writer.WriteHeader(BuildHeader(headerSnapshotOrder, startTick));
                    WriteBufferedChunks(writer, headerSnapshotOrder, startTick);
                    writer.Complete();
                    chunks = writer.ChunkCount;
                    bytes = writer.BytesWritten;
                }

                double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp)
                    * 1000d / System.Diagnostics.Stopwatch.Frequency;

                saveCount++;
                lastSavedPath = path;
                telemetry.Track(
                    SavedEvent,
                    (TelemetryKeys.Props.Reason, reasonTag),
                    (TelemetryKeys.Props.N, chunks),
                    (TelemetryKeys.Props.Bytes, bytes),
                    (TelemetryKeys.Props.Ms, elapsedMs));
                return path;
            }
            catch (Exception e)
            {
                // 故意兜住所有异常：录制器是调试设施，磁盘满 / 路径非法 / 序列化抛异常
                // 都不该把游戏带崩。**只埋这一条，不重试**——重试只会让磁盘问题被放大成写盘风暴。
                failedSaveCount++;
                telemetry.TrackError(
                    SaveFailedEvent,
                    e,
                    TelemetryProps.Of((TelemetryKeys.Props.Reason, reasonTag)));
                return null;
            }
        }

        /// <summary>
        /// 挑一个当文件头起始快照的快照，返回它在快照环里的序号（0 = 最旧），没有合适的返回 -1。
        /// <para>
        /// 挑的是**最旧的那个「tick 不早于环里最旧输入」的快照**：重放要从一个快照恢复世界、
        /// 再从那一 tick 起逐条喂输入，所以快照必须落在输入还留着的区间里；
        /// 在满足这个前提的快照里取最旧的一个，能重放的时间最长。
        /// </para>
        /// </summary>
        /// <param name="startTick">文件头的起始 tick：挑中了就是那个快照的 tick，没挑中就是最旧输入的 tick。</param>
        private int PickHeaderSnapshot(out uint startTick)
        {
            startTick = inputCount > 0 ? OldestBufferedTick : 0u;

            if (snapshotCount == 0)
            {
                return -1;
            }

            if (inputCount == 0)
            {
                // 一条输入都没有（比如只打了几条 QA 打点就存了）：拿最旧的快照当起点，聊胜于无。
                startTick = snapshotTicks[IndexAt(snapshotHead, snapshotCount, SnapshotCapacity, 0)];
                return 0;
            }

            uint oldestInputTick = startTick;
            for (int order = 0; order < snapshotCount; order++)
            {
                uint tick = snapshotTicks[IndexAt(snapshotHead, snapshotCount, SnapshotCapacity, order)];
                if (tick >= oldestInputTick)
                {
                    startTick = tick;
                    return order;
                }
            }

            // 所有快照都比最旧的输入还老：从它们恢复之后缺了一段输入，接着喂会直接分叉，
            // 不如不给起始快照——让播放器明确知道「这份录像只有输入，没有起点状态」。
            return -1;
        }

        private ReplayHeader BuildHeader(int headerSnapshotOrder, uint startTick)
        {
            byte[] snapshot = null;
            int snapshotLength = 0;
            if (headerSnapshotOrder >= 0)
            {
                int index = IndexAt(snapshotHead, snapshotCount, SnapshotCapacity, headerSnapshotOrder);
                snapshot = snapshotPayloads[index];
                snapshotLength = snapshotLengths[index];
            }

            return ReplayHeader.Create(
                platform.Kind,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Application.version,
                seed,
                configHash,
                fixedDeltaTime,
                startTick,
                snapshot,
                0,
                snapshotLength);
        }

        /// <summary>
        /// 把四个环里 tick 不早于 <paramref name="startTick"/> 的内容按 tick 升序写出去。
        /// <para>
        /// 每个环内部本来就是 tick 升序（按写入顺序），所以只要拿输入当主序列、
        /// 每写一条输入之前把另外三个环里 tick 不晚于它的都先排掉，整份文件就是有序的。
        /// 有序不是格式的要求（<see cref="ReplayReader"/> 顺序读就行），是给人和播放器省事：
        /// 播放时不必先把整份文件读进内存排一遍。
        /// </para>
        /// </summary>
        private void WriteBufferedChunks(ReplayWriter writer, int headerSnapshotOrder, uint startTick)
        {
            int hashOrder = 0;
            int snapshotOrder = 0;
            int qaOrder = 0;

            for (int order = 0; order < inputCount; order++)
            {
                int index = IndexAt(inputHead, inputCount, InputCapacity, order);
                uint tick = inputTicks[index];
                if (tick < startTick)
                {
                    continue;
                }

                DrainAux(writer, tick, startTick, headerSnapshotOrder, ref hashOrder, ref snapshotOrder, ref qaOrder);
                writer.WriteInputChunk(tick, inputCommands[index]);
            }

            DrainAux(writer, uint.MaxValue, startTick, headerSnapshotOrder, ref hashOrder, ref snapshotOrder, ref qaOrder);
        }

        /// <summary>把三个辅助环里 tick 不晚于 <paramref name="upToTick"/> 的内容排出去。</summary>
        private void DrainAux(
            ReplayWriter writer,
            uint upToTick,
            uint startTick,
            int headerSnapshotOrder,
            ref int hashOrder,
            ref int snapshotOrder,
            ref int qaOrder)
        {
            while (snapshotOrder < snapshotCount)
            {
                int index = IndexAt(snapshotHead, snapshotCount, SnapshotCapacity, snapshotOrder);
                uint tick = snapshotTicks[index];
                if (tick > upToTick)
                {
                    break;
                }

                int current = snapshotOrder;
                snapshotOrder++;

                // 起点快照已经在文件头里了，别再写一份；比起点还老的快照对重放没用。
                if (tick < startTick || current == headerSnapshotOrder)
                {
                    continue;
                }

                WriteSnapshotChunk(writer, tick, snapshotPayloads[index], snapshotLengths[index]);
            }

            while (hashOrder < hashCount)
            {
                int index = IndexAt(hashHead, hashCount, StateHashCapacity, hashOrder);
                uint tick = hashTicks[index];
                if (tick > upToTick)
                {
                    break;
                }

                hashOrder++;
                if (tick < startTick)
                {
                    continue;
                }

                writer.WriteStateHashChunk(tick, hashValues[index]);
            }

            while (qaOrder < qaCount)
            {
                int index = IndexAt(qaHead, qaCount, QaMarkerCapacity, qaOrder);
                uint tick = qaTicks[index];
                if (tick > upToTick)
                {
                    break;
                }

                qaOrder++;
                if (tick < startTick)
                {
                    continue;
                }

                writer.WriteQaMarkerChunk(tick, qaLabels[index]);
            }
        }

        /// <summary>
        /// 写一条快照 chunk，<b>接住超限那个错误</b>。
        /// <para>
        /// 进环时已经挡过一次超限（见 <see cref="PushSnapshot"/>），这里是第二道网：
        /// 上限是 <see cref="ReplayWriter"/> 的契约，万一哪天它收紧了，这里也不能让整份保存炸掉——
        /// 丢一个快照对这份录像来说是「少一个跳转点」，丢掉后面全部输入才是真的没救了。
        /// </para>
        /// </summary>
        private void WriteSnapshotChunk(ReplayWriter writer, uint tick, byte[] payload, int length)
        {
            try
            {
                writer.WriteChunk((byte)ReplayFormat.ChunkType.Snapshot, tick, payload, 0, length);
            }
            catch (ArgumentOutOfRangeException)
            {
                droppedOversizeSnapshotCount++;
                telemetry.TrackWarn(
                    SnapshotOversizeEvent,
                    TelemetryProps.Of(
                        (TelemetryKeys.Props.Bytes, length),
                        (TickProp, (long)tick),
                        (TelemetryKeys.Props.N, droppedOversizeSnapshotCount)));
                Log.Warn(
                    $"世界状态快照 {length} 字节超过单条 chunk 上限 "
                    + $"{ReplayFormat.MaxChunkPayloadLength} 字节，本次快照未记录（tick {tick}）；"
                    + "输入流继续写出。");
            }
        }

        /// <summary>
        /// 拼出这次保存的文件路径：<c>&lt;SaveRoot&gt;/Replays/replay_&lt;UTC 时间戳&gt;_&lt;原因&gt;.21dr</c>。
        /// <para>
        /// 扩展名取 <see cref="ReplayFormat.FileExtension"/>，不在这里另写一个字面量——
        /// 那个常量就是这套格式对外的约定，两处各写一遍迟早对不上
        /// （<see cref="ReplayReader"/> 只认魔数不看扩展名，但人和文件管理器看的就是扩展名）。
        /// </para>
        /// <para>同一秒内存第二份时按 <c>_2</c> <c>_3</c> 递增避让，免得后一份把前一份覆盖掉。</para>
        /// </summary>
        private string BuildPath(string reasonTag)
        {
            string directory = Path.Combine(platform.SaveRoot, ReplayFolderName);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            string baseName = "replay_" + stamp + "_" + reasonTag;
            string path = Path.Combine(directory, baseName + ReplayFormat.FileExtension);

            for (int i = 2; i <= MaxNameCollisionRetries && File.Exists(path); i++)
            {
                path = Path.Combine(
                    directory,
                    baseName + "_" + i.ToString(CultureInfo.InvariantCulture) + ReplayFormat.FileExtension);
            }

            return path;
        }

        /// <summary>触发来源的短代码，进文件名也进埋点的 <c>reason</c> 属性（两处同一个词，好对上）。</summary>
        private static string ReasonTag(SaveReason reason)
        {
            switch (reason)
            {
                case SaveReason.Hotkey:
                    return "hotkey";
                case SaveReason.Error:
                    return "error";
                default:
                    return "api";
            }
        }
    }
}
