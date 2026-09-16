// 职责：钉住回放系统的收口能力——漂移检测。哈希一致时不许误报、不一致时必须报出**第一个**漂移点、
//   漂移之后能从快照续跑到终点、配置换版时给的是「配置版本不匹配」而不是一堆漂移点。
// 为什么新建：ReplayPlayer 一条测试都没有，而它错了的表现全是「安静地不对」——
//   误报让人花半天查一个不存在的问题，漏报让人以为回放机制还能信。
//   为什么不并进 ReplayFormatTests：那个文件只跟字节和文件打交道，这里要拉起推进器、输入源、
//   世界状态注册表一整套接线，两边的 SetUp 完全不同。

using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.Config;
using Game.Core.Platform;
using Game.Core.Replay;
using Game.Core.Simulation;
using Game.Core.Timing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Replay
{
    /// <summary>
    /// <see cref="ReplayPlayer"/> 的漂移检测 EditMode 测试。
    /// <para>
    /// 世界状态简化成一个整数计数器：录制时每 tick 加 1，重放时把步骤换成「每 tick 加 2」就制造出漂移。
    /// 这样「漂没漂、从哪一 tick 开始漂」是算得出来的确定值，判据不依赖任何玩法。
    /// </para>
    /// <para>
    /// 播放一律用 <see cref="ReplayPlayer.StepOnce"/> 单步驱动，不喂渲染帧时长：
    /// 推几个 tick 完全由测试说了算，不掺帧率与浮点累加。
    /// </para>
    /// </summary>
    public sealed class DriftDetectionTests
    {
        /// <summary>逻辑帧率。1/64 = 0.015625，在 float 里是精确值，和播放器的步长校验不会擦边。</summary>
        private const int TickRate = 64;

        /// <summary>固定步长，必须和 <see cref="TickRate"/> 对得上，否则播放器会以「步长不匹配」拒载。</summary>
        private const float FixedDeltaTime = 1f / TickRate;

        /// <summary>录像里有几个 tick 的输入（tick 0 ~ RecordedTickCount-1）。</summary>
        private const int RecordedTickCount = 6;

        /// <summary>
        /// 录像里的状态哈希点数：tick 0 ~ RecordedTickCount，共 7 个。
        /// 比输入多一个是因为最后一个 tick 跑完之后还要校验一次结果。
        /// </summary>
        private const int RecordedHashCount = RecordedTickCount + 1;

        /// <summary>录制时那一版配置的指纹。</summary>
        private const ulong RecordedConfigHash = 0x0123456789ABCDEFul;

        /// <summary>单步驱动的次数上限。播放器卡住不前进时靠它兜底，免得测试死循环。</summary>
        private const int MaxDriveSteps = 1000;

        /// <summary>重放时世界的初始值，故意取一个录像里不可能出现的数——起始快照没恢复上就会当场露馅。</summary>
        private const int UnrestoredValue = 999;

        private string workRoot;

        /// <summary>CreateInstance 出来的配置资产不属于任何场景，得自己销毁，否则 EditMode 下会泄漏。</summary>
        private readonly List<SimulationConfig> createdConfigs = new List<SimulationConfig>();

        [SetUp]
        public void SetUp()
        {
            workRoot = Path.Combine(Path.GetTempPath(), "21Days-drift-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workRoot);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < createdConfigs.Count; i++)
            {
                UnityEngine.Object.DestroyImmediate(createdConfigs[i]);
            }

            createdConfigs.Clear();

            if (Directory.Exists(workRoot))
            {
                Directory.Delete(workRoot, true);
            }
        }

        [Test]
        public void Playback_WhenReplayedLogicMatchesTheRecording_ChecksEveryHashAndReportsNoDrift()
        {
            string path = Path.Combine(workRoot, "clean.21dr");
            WriteRecording(path, RecordedConfigHash, null);

            CounterState state;
            ReplayPlayer player = CreatePlayer(1, new FakeConfigService(RecordedConfigHash), out state);

            string error;
            Assert.That(player.Load(path, out error), Is.True, $"这份回放应当能正常载入，实际报的是：{error}");
            Assert.That(
                state.Value,
                Is.EqualTo(0),
                "载入之后世界必须被起始快照恢复成录制时那一刻的样子，而不是留着重放进程自己的初值");

            RunToEnd(player);

            // 先钉死「确实走到了校验点」：没走到校验点的话，「没有漂移」是一句空话。
            Assert.That(
                player.CheckedHashCount,
                Is.GreaterThan(0),
                "一个状态哈希都没校验到，「没有漂移」就是空断言");
            Assert.That(
                player.CheckedHashCount,
                Is.EqualTo(RecordedHashCount),
                "录像里的每一个状态哈希都该被校验到，少校验一个就等于少守一段");
            Assert.That(player.StateHashCount, Is.EqualTo(RecordedHashCount), "录像里的哈希条数没全部读进来");

            Assert.That(player.HasDrifted, Is.False, "重放逻辑和录制时一模一样，不许报漂移——误报会让人去查一个不存在的问题");
            Assert.That(player.DriftCount, Is.EqualTo(0), "零漂移时漂移计数必须是 0");
            Assert.That(player.FirstDriftTick, Is.EqualTo(-1L), "没漂过时首次漂移 tick 必须保持 -1");
            Assert.That(player.ResyncCount, Is.EqualTo(0), "没漂移就不该动用快照纠偏");
            Assert.That(state.Value, Is.EqualTo(RecordedTickCount), "跑完之后世界的值应当和录制时的终点一致");
            Assert.That(player.MissingInputCount, Is.EqualTo(0L), "每个 tick 都该取到录像里那条输入");
            Assert.That(player.SkippedInputCount, Is.EqualTo(0L), "正常播放不该有输入被跳过，有就是对齐出了问题");
        }

        [Test]
        public void Playback_WhenSeveralConsecutiveCheckpointsMismatch_ReportsTheFirstDriftTickNotTheLast()
        {
            string path = Path.Combine(workRoot, "drift.21dr");
            WriteRecording(path, RecordedConfigHash, null);

            // 每 tick 加 2 而不是加 1：从第一个 tick 起就和录像分叉，之后每个校验点都会对不上。
            CounterState state;
            ReplayPlayer player = CreatePlayer(2, new FakeConfigService(RecordedConfigHash), out state);

            string error;
            Assert.That(player.Load(path, out error), Is.True, $"这份回放应当能正常载入，实际报的是：{error}");

            RunToEnd(player);

            Assert.That(player.HasDrifted, Is.True, "重放逻辑和录制时不一样，必须报出漂移");
            Assert.That(
                player.DriftCount,
                Is.EqualTo(RecordedHashCount - 1),
                "tick 0 的校验点靠起始快照对得上，之后每个校验点都该判成漂移");
            Assert.That(
                player.DriftCount,
                Is.GreaterThan(1),
                "这条用例要构造**多个**连续漂移点，只有一个的话「记第一个还是最后一个」根本区分不开");
            Assert.That(
                player.FirstDriftTick,
                Is.EqualTo(1L),
                $"记的必须是**第一个**漂移点（tick 1），而不是最后一个（tick {RecordedTickCount}）、"
                + "也不是每次都覆盖成最近一次——漂移一旦发生，后面几乎每个校验点都会跟着对不上，"
                + "最后那个数对定位真因毫无价值");
            Assert.That(
                player.CheckedHashCount,
                Is.EqualTo(RecordedHashCount),
                "漂移之后校验必须继续做下去，不能一漂就不查了");
        }

        [Test]
        public void Playback_WhenDriftIsFollowedBySnapshots_ResyncsFromThemAndStillRunsToTheEnd()
        {
            string path = Path.Combine(workRoot, "drift-with-snapshots.21dr");
            uint[] snapshotTicks = { 2u, 4u };
            WriteRecording(path, RecordedConfigHash, snapshotTicks);

            CounterState state;
            ReplayPlayer player = CreatePlayer(2, new FakeConfigService(RecordedConfigHash), out state);

            string error;
            Assert.That(player.Load(path, out error), Is.True, $"这份回放应当能正常载入，实际报的是：{error}");
            Assert.That(player.SnapshotCount, Is.EqualTo(snapshotTicks.Length), "录像里的完整快照没全部读进来");

            RunToEnd(player);

            Assert.That(player.HasDrifted, Is.True, "这条用例的前提就是漂移，没漂就什么都没验到");
            Assert.That(
                player.ResyncCount,
                Is.EqualTo(snapshotTicks.Length),
                "漂移之后遇到的每一个完整快照都该把世界拉回正轨一次");
            Assert.That(
                player.IsFinished,
                Is.True,
                "漂移不许中断回放：漂移点之后的内容仍有诊断价值，人也需要看到「后面变成了什么样」");
            Assert.That(
                player.CurrentTick,
                Is.EqualTo(player.EndTick + 1L),
                "必须一路推到录像的最后一个 tick 之后才收工");
            Assert.That(
                player.CheckedHashCount,
                Is.EqualTo(RecordedHashCount),
                "跑到终点意味着每个校验点都经过了，少一个就说明中途停了");
        }

        [Test]
        public void Load_WhenConfigHashDiffers_ReportsAConfigMismatchInsteadOfDegradingIntoDriftPoints()
        {
            string path = Path.Combine(workRoot, "other-config.21dr");
            WriteRecording(path, RecordedConfigHash, null);

            // 当前跑的是另一版配置表：同样的输入在两版数值下本来就会推出不同的结果。
            CounterState state;
            ReplayPlayer player = CreatePlayer(1, new FakeConfigService(RecordedConfigHash ^ 1ul), out state);

            string error;
            Assert.That(
                player.Load(path, out error),
                Is.False,
                "配置指纹对不上时默认应当拒载，而不是照放");
            Assert.That(player.ConfigMismatch, Is.True, "拒载的原因要能被程序读出来，不能只藏在一句话里");
            Assert.That(
                error,
                Does.Contain("配置版本不匹配"),
                $"给人看的那句话必须点明是配置换版了，实际是：{error}");

            Assert.That(
                player.DriftCount,
                Is.EqualTo(0),
                "配置对不上必须在载入时就拦住，绝不能退化成一堆漂移点让人对着代码查一整天——"
                + "文件头里存这个指纹的全部理由就在这里");
            Assert.That(player.CheckedHashCount, Is.EqualTo(0), "既然没载入，就不该有任何校验点被走过");
            Assert.That(player.HasDrifted, Is.False, "没放过的回放不该带着「漂移过」的标记");
            Assert.That(player.IsLoaded, Is.False, "拒载之后不许留下一个半载入的播放器");
        }

        /// <summary>
        /// 写一份「每 tick 加 1」的录像：头部带 tick 0 的起始快照，
        /// 每个 tick 一条输入 + 一条状态哈希，最后再补一条「跑完最后一个 tick 之后」的哈希。
        /// </summary>
        /// <param name="path">目标文件路径。</param>
        /// <param name="configHash">写进文件头的配置指纹。</param>
        /// <param name="snapshotTicks">要额外写完整快照的 tick（可为 null）。</param>
        private static void WriteRecording(string path, ulong configHash, uint[] snapshotTicks)
        {
            StateBuffer payload = new StateBuffer();
            WriteCounter(payload, 0);

            using (ReplayWriter writer = new ReplayWriter(path))
            {
                writer.WriteHeader(ReplayHeader.Create(
                    PlatformKind.Standalone,
                    1_700_000_000L,
                    "1.0.0",
                    7UL,
                    configHash,
                    FixedDeltaTime,
                    0u,
                    payload.GetBuffer(),
                    0,
                    payload.Length));

                for (uint tick = 0; tick < RecordedTickCount; tick++)
                {
                    writer.WriteInputChunk(tick, CommandFor(tick));

                    // tick T 的哈希记的是「T 这一 tick 开始之前的世界」，此时计数器正好等于 T。
                    writer.WriteStateHashChunk(tick, HashOfCounter((int)tick));

                    if (Contains(snapshotTicks, tick))
                    {
                        WriteCounter(payload, (int)tick);
                        writer.WriteSnapshotChunk(tick, payload);
                    }
                }

                writer.WriteStateHashChunk(RecordedTickCount, HashOfCounter(RecordedTickCount));
                writer.Complete();
            }
        }

        /// <summary>造一条这个 tick 专属的输入命令，好让「输入有没有对齐」也能被区分开。</summary>
        private static InputCommand CommandFor(uint tick)
        {
            return new InputCommand(new Vector2(tick, -(float)tick), Vector2.zero, tick + 1u, Vector2.zero, 0);
        }

        /// <summary>把计数器写进缓冲，布局必须和 <see cref="CounterState.Serialize"/> 一模一样。</summary>
        private static void WriteCounter(StateBuffer buffer, int value)
        {
            buffer.Reset();
            buffer.WriteInt(value);
        }

        /// <summary>算出「计数器等于这个值」时世界状态的哈希，和播放器重放时算的是同一条路径。</summary>
        private static ulong HashOfCounter(int value)
        {
            StateBuffer buffer = new StateBuffer();
            WriteCounter(buffer, value);
            return StateHasher.Compute(buffer);
        }

        /// <summary>数组里有没有这个 tick。数组为 null 当作空处理。</summary>
        private static bool Contains(uint[] ticks, uint tick)
        {
            if (ticks == null)
            {
                return false;
            }

            for (int i = 0; i < ticks.Length; i++)
            {
                if (ticks[i] == tick)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 单步把整份回放放完。**不喂渲染帧时长**：推几个 tick 完全由测试说了算，
        /// 判据也就不会被帧率与浮点累加搅动。
        /// </summary>
        private static void RunToEnd(ReplayPlayer player)
        {
            int steps = 0;
            while (!player.IsFinished && steps < MaxDriveSteps)
            {
                player.StepOnce();
                player.Tick();
                steps++;
            }

            Assert.That(
                player.IsFinished,
                Is.True,
                $"单步驱动 {steps} 次都没放完（录像只有 {RecordedTickCount} 个 tick）：播放器卡住了");
        }

        /// <summary>
        /// 组一套能放回放的最小接线：推进器 + 输入源切换壳 + 世界状态注册表 + 播放器。
        /// </summary>
        /// <param name="incrementPerTick">重放时每个 tick 给计数器加多少。加 1 = 和录制时一致，加 2 = 制造漂移。</param>
        /// <param name="config">配置服务，用来考察配置指纹校验。</param>
        /// <param name="state">接线里那份世界状态，供断言查看。</param>
        private ReplayPlayer CreatePlayer(int incrementPerTick, IConfigService config, out CounterState state)
        {
            state = new CounterState(UnrestoredValue);

            CounterStateProvider provider = new CounterStateProvider();
            provider.Register(state);

            FakeFrameClock frameClock = new FakeFrameClock();
            InputSourceSwitch inputSwitch = new InputSourceSwitch(new SilentInputSource());
            SimulationRunner runner = new SimulationRunner(
                CreateSimulationConfig(), frameClock, inputSwitch, new RandomService(1UL), null);
            runner.AddStep(new CounterStep(state, incrementPerTick));

            // 随机源 / 回放配置 / 音频 / 埋点都传 null：构造函数文档写明允许，
            // 这几条用例只考察漂移检测，接上它们只会引入与判据无关的噪声。
            ReplayPlayer player = new ReplayPlayer(runner, inputSwitch, frameClock, null, config, null, null, null);
            player.AttachStateProvider(provider);
            return player;
        }

        /// <summary>
        /// 造一份内存里的 <see cref="SimulationConfig"/>。两个字段都是 <c>[SerializeField] private</c>、
        /// 没有公开写入口，所以走 <see cref="SerializedObject"/> 改（测试程序集是 Editor-only，用得了）。
        /// </summary>
        private SimulationConfig CreateSimulationConfig()
        {
            SimulationConfig config = ScriptableObject.CreateInstance<SimulationConfig>();
            createdConfigs.Add(config);

            SerializedObject serialized = new SerializedObject(config);
            serialized.FindProperty("tickRate").intValue = TickRate;
            serialized.FindProperty("maxCatchUpTicks").intValue = 256;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(config.TickRate, Is.EqualTo(TickRate), "配置没改成功，后面的判据全不成立");
            return config;
        }

        /// <summary>
        /// 简化到极致的世界状态：一个整数。录制时它每 tick 加 1，
        /// 于是「tick T 的世界」就是「计数器等于 T」，漂没漂是算得出来的。
        /// </summary>
        private sealed class CounterState : IReplayState
        {
            public CounterState(int value)
            {
                Value = value;
            }

            /// <summary>当前计数值。</summary>
            public int Value { get; set; }

            public void Serialize(IStateWriter writer)
            {
                writer.WriteInt(Value);
            }

            public void Deserialize(IStateReader reader)
            {
                Value = reader.ReadInt();
            }
        }

        /// <summary>按注册顺序收集世界状态的最小注册表。</summary>
        private sealed class CounterStateProvider : IReplayStateProvider
        {
            private readonly List<IReplayState> states = new List<IReplayState>();

            public int Count => states.Count;

            public void Register(IReplayState state)
            {
                if (state == null)
                {
                    throw new ArgumentNullException(nameof(state));
                }

                states.Add(state);
            }

            public void SerializeAll(IStateWriter writer)
            {
                for (int i = 0; i < states.Count; i++)
                {
                    states[i].Serialize(writer);
                }
            }

            public void DeserializeAll(IStateReader reader)
            {
                for (int i = 0; i < states.Count; i++)
                {
                    states[i].Deserialize(reader);
                }
            }
        }

        /// <summary>每个 tick 给计数器加一个固定值的逻辑步骤。加的数不同，就是两套不同的逻辑。</summary>
        private sealed class CounterStep : ISimulationStep
        {
            private readonly CounterState state;
            private readonly int increment;

            public CounterStep(CounterState state, int increment)
            {
                this.state = state;
                this.increment = increment;
            }

            public void Step(in SimulationContext context)
            {
                state.Value += increment;
            }
        }

        /// <summary>
        /// 假渲染帧时钟。本文件全程单步驱动，播放器根本读不到 <see cref="DeltaTime"/>，
        /// 这里给一组自洽的零值即可。
        /// </summary>
        private sealed class FakeFrameClock : IClock
        {
            public DateTime UtcNow => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            public float GameTime => 0f;

            public float UnscaledTime => 0f;

            public float DeltaTime => 0f;

            public float UnscaledDeltaTime => 0f;
        }

        /// <summary>
        /// 实时输入源的占位实现。播放期间输入源会被切到录像源，它一次都不会被采样；
        /// 存在的意义只是让 <see cref="InputSourceSwitch"/> 不用走「没拿到实时源」那条警告分支。
        /// </summary>
        private sealed class SilentInputSource : IInputSource
        {
            public InputCommand Current => InputCommand.Empty;

            public void Sample(long tick)
            {
            }
        }

        /// <summary>
        /// 假的配置服务：只回答内容指纹这一个问题。
        /// <para>配置表本身抛而不是返回 null——播放器要是哪天偷偷读了表，测试会当场炸，而不是静默通过。</para>
        /// </summary>
        private sealed class FakeConfigService : IConfigService
        {
            private readonly ulong contentHash;

            public FakeConfigService(ulong contentHash)
            {
                this.contentHash = contentHash;
            }

            public global::cfg.Tables Tables => throw new NotSupportedException("假配置服务不提供配置表");

            public ulong ContentHash => contentHash;
        }
    }
}
