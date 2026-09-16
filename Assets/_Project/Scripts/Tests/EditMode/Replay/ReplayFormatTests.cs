// 职责：钉住回放文件格式的读写契约——头部与各类 chunk 原样往返、未知类型按长度跳过、
//   尾部残缺尽力读且不抛、头部损坏各给一句可读报错、格式常量不许随手改。
// 为什么新建：ReplayWriter / ReplayReader / ReplayFormat 一条测试都没有。
//   这层的错法有个共同点——**不报错**：老回放被按新布局读出一堆看起来合理的垃圾值，
//   或者崩溃现场那份尾部残缺的文件被整份判损坏。两种都只能靠测试守着。
//   为什么和 StateBufferTests 分文件：那个管「一段字节怎么摆」，这个管「一份文件怎么摆」，
//   前者不认识文件、也不落盘；合成一个文件会让「要不要 TearDown 删目录」这种事污染纯内存的用例。

using System;
using System.IO;
using System.Text;
using Game.Core.Platform;
using Game.Core.Replay;
using Game.Core.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Replay
{
    /// <summary>
    /// <see cref="ReplayWriter"/> / <see cref="ReplayReader"/> / <see cref="ReplayFormat"/> 的 EditMode 测试。
    /// 文件一律写进系统临时目录下的随机子目录，<see cref="TearDown"/> 整个删掉：
    /// 不碰工程内的资产路径，也不会和别的用例互相干扰。
    /// </summary>
    public sealed class ReplayFormatTests
    {
        /// <summary>本文件统一用 64 Hz 的步长：1/64 = 0.015625，在 float 里是精确值。</summary>
        private const float FixedDeltaTime = 1f / 64f;

        /// <summary>当前版本不认识的 chunk 类型号。取一个离已有类型很远的数，免得哪天新增类型撞上。</summary>
        private const byte UnknownChunkType = 200;

        private string workRoot;

        [SetUp]
        public void SetUp()
        {
            workRoot = Path.Combine(Path.GetTempPath(), "21Days-replay-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(workRoot))
            {
                Directory.Delete(workRoot, true);
            }
        }

        [Test]
        public void WriteThenRead_Header_RoundTripsEveryField()
        {
            string path = PathFor("header.21dr");
            byte[] snapshot = { 1, 2, 3, 250, 251, 252 };

            using (ReplayWriter writer = new ReplayWriter(path))
            {
                writer.WriteHeader(ReplayHeader.Create(
                    PlatformKind.Android,
                    1_700_000_000L,
                    "1.2.3-测试",
                    0xFEEDFACECAFEBEEFul,
                    0x0123456789ABCDEFul,
                    FixedDeltaTime,
                    4096u,
                    snapshot,
                    0,
                    snapshot.Length));
                writer.WriteQaMarkerChunk(4096u, "占位，头部之后必须至少有一条记录");
                writer.Complete();
            }

            using (ReplayReader reader = ReplayReader.Open(path))
            {
                ReplayHeader header = reader.Header;

                Assert.That(header.FormatVersion, Is.EqualTo(ReplayFormat.CurrentFormatVersion), "格式版本没原样写回来");
                Assert.That(header.Platform, Is.EqualTo(PlatformKind.Android), "平台标记没原样写回来");
                Assert.That(header.UnixUtcSeconds, Is.EqualTo(1_700_000_000L), "录制时间没原样写回来（注意是秒不是毫秒）");
                Assert.That(header.BuildVersion, Is.EqualTo("1.2.3-测试"), "构建版本号没原样写回来（UTF-8 变长字段）");
                Assert.That(header.Seed, Is.EqualTo(0xFEEDFACECAFEBEEFul), "随机种子没原样写回来——错了重放第一帧就分叉");
                Assert.That(header.ConfigHash, Is.EqualTo(0x0123456789ABCDEFul), "配置指纹没原样写回来");
                Assert.That(header.FixedDeltaTime, Is.EqualTo(FixedDeltaTime), "固定步长没原样写回来");
                Assert.That(header.StartTick, Is.EqualTo(4096u), "起始 tick 没原样写回来");
                Assert.That(header.SnapshotLength, Is.EqualTo(snapshot.Length), "起始快照长度没原样写回来");

                byte[] readSnapshot = header.GetSnapshotBuffer();
                for (int i = 0; i < snapshot.Length; i++)
                {
                    Assert.That(
                        readSnapshot[header.SnapshotOffset + i],
                        Is.EqualTo(snapshot[i]),
                        $"起始快照第 {i} 个字节对不上——没有正确的起点，整份回放都放不了");
                }
            }
        }

        [Test]
        public void WriteThenRead_ChunksOfEveryKnownType_ReturnsTheSameSequenceAndContent()
        {
            string path = PathFor("chunks.21dr");
            InputCommand firstCommand = new InputCommand(
                new Vector2(0.5f, -0.25f), new Vector2(1f, 0f), 0xDEADBEEFu, new Vector2(-2f, 3.5f), 7);
            InputCommand lastCommand = new InputCommand(
                Vector2.zero, new Vector2(-0.75f, 0.125f), InputCommand.ButtonPause, Vector2.zero, 0);

            StateBuffer snapshotPayload = new StateBuffer();
            snapshotPayload.WriteInt(-9);
            snapshotPayload.WriteFloat(2.5f);

            using (ReplayWriter writer = new ReplayWriter(path))
            {
                writer.WriteHeader(CreateHeader(0u, null, 0UL));
                writer.WriteInputChunk(0u, firstCommand);
                writer.WriteStateHashChunk(0u, 0xA1B2C3D4E5F60718ul);
                writer.WriteSnapshotChunk(1u, snapshotPayload);
                writer.WriteQaMarkerChunk(2u, "就是这一帧卡住的");
                writer.WriteInputChunk(3u, lastCommand);

                Assert.That(writer.ChunkCount, Is.EqualTo(5), "写出去的条数不对，后面的判据就都不成立了");
                writer.Complete();
            }

            StateBuffer scratch = new StateBuffer();
            using (ReplayReader reader = ReplayReader.Open(path))
            {
                ReplayChunk chunk;

                Assert.That(reader.ReadNext(out chunk), Is.True, "第 1 条读不出来");
                Assert.That(chunk.Type, Is.EqualTo(ReplayFormat.ChunkType.Input), "第 1 条应当是输入");
                Assert.That(chunk.Tick, Is.EqualTo(0u), "第 1 条的 tick 不对");
                Assert.That(chunk.PayloadLength, Is.EqualTo(InputCommand.SerializedSize), "输入 payload 应当是定长的");
                Assert.That(chunk.ReadInputCommand(), Is.EqualTo(firstCommand), "输入命令没原样读回来");

                Assert.That(reader.ReadNext(out chunk), Is.True, "第 2 条读不出来");
                Assert.That(chunk.Type, Is.EqualTo(ReplayFormat.ChunkType.StateHash), "第 2 条应当是状态哈希");
                Assert.That(chunk.Tick, Is.EqualTo(0u), "第 2 条的 tick 不对");
                chunk.LoadPayloadInto(scratch);
                Assert.That(scratch.ReadULong(), Is.EqualTo(0xA1B2C3D4E5F60718ul), "状态哈希没原样读回来");

                Assert.That(reader.ReadNext(out chunk), Is.True, "第 3 条读不出来");
                Assert.That(chunk.Type, Is.EqualTo(ReplayFormat.ChunkType.Snapshot), "第 3 条应当是完整快照");
                Assert.That(chunk.Tick, Is.EqualTo(1u), "第 3 条的 tick 不对");
                chunk.LoadPayloadInto(scratch);
                Assert.That(scratch.ReadInt(), Is.EqualTo(-9), "快照第 1 个字段没读回来");
                Assert.That(scratch.ReadFloat(), Is.EqualTo(2.5f), "快照第 2 个字段没读回来");
                Assert.That(scratch.Remaining, Is.EqualTo(0), "快照 payload 长度对不上，说明多写或少写了字节");

                Assert.That(reader.ReadNext(out chunk), Is.True, "第 4 条读不出来");
                Assert.That(chunk.Type, Is.EqualTo(ReplayFormat.ChunkType.QaMarker), "第 4 条应当是 QA 打点");
                Assert.That(chunk.Tick, Is.EqualTo(2u), "第 4 条的 tick 不对");
                Assert.That(chunk.ReadQaMarkerLabel(), Is.EqualTo("就是这一帧卡住的"), "打点说明没原样读回来");

                Assert.That(reader.ReadNext(out chunk), Is.True, "第 5 条读不出来");
                Assert.That(chunk.Type, Is.EqualTo(ReplayFormat.ChunkType.Input), "第 5 条应当是输入");
                Assert.That(chunk.Tick, Is.EqualTo(3u), "第 5 条的 tick 不对");
                Assert.That(chunk.ReadInputCommand(), Is.EqualTo(lastCommand), "最后一条输入命令没原样读回来");

                Assert.That(reader.ReadNext(out chunk), Is.False, "5 条之后应当干净地读到文件尾");
                Assert.That(reader.ChunkCount, Is.EqualTo(5), "读出来的条数和写进去的不一致");
                Assert.That(reader.IsTailTruncated, Is.False, "完整写完的文件不该被判成尾部残缺");
                Assert.That(reader.SkippedUnknownChunkCount, Is.EqualTo(0), "这份文件里没有未知类型");
            }
        }

        [Test]
        public void ReadNext_WhenAnUnknownChunkTypeSitsInTheMiddle_SkipsItAndStillReadsBothNeighbours()
        {
            string path = PathFor("unknown-type.21dr");
            byte[] unknownPayload = Encoding.UTF8.GetBytes("将来某个版本才认识的记录");

            using (ReplayWriter writer = new ReplayWriter(path))
            {
                writer.WriteHeader(CreateHeader(0u, null, 0UL));
                writer.WriteStateHashChunk(1u, 0x1111111111111111ul);
                writer.WriteChunk(UnknownChunkType, 2u, unknownPayload, 0, unknownPayload.Length);
                writer.WriteStateHashChunk(3u, 0x3333333333333333ul);
                writer.Complete();
            }

            Assert.That(
                ReplayFormat.IsKnownChunkType(UnknownChunkType),
                Is.False,
                "这条用例的前提是这个类型号当前不认识，认识了就什么都没验到");

            StateBuffer scratch = new StateBuffer();
            using (ReplayReader reader = ReplayReader.Open(path))
            {
                ReplayChunk chunk;

                // 前一条：未知类型之前的记录必须正常读出。
                Assert.That(reader.ReadNext(out chunk), Is.True, "未知类型**之前**的那条没读出来");
                Assert.That(chunk.Tick, Is.EqualTo(1u), "读出来的不是前一条");
                chunk.LoadPayloadInto(scratch);
                Assert.That(scratch.ReadULong(), Is.EqualTo(0x1111111111111111ul), "前一条的内容被未知类型带偏了");

                // 后一条：说明未知类型是按 length 整条跳过的，游标没错位。
                Assert.That(reader.ReadNext(out chunk), Is.True, "未知类型**之后**的那条没读出来——多半是没按 length 跳过");
                Assert.That(chunk.Tick, Is.EqualTo(3u), "读出来的不是后一条，说明跳过的字节数不对");
                chunk.LoadPayloadInto(scratch);
                Assert.That(scratch.ReadULong(), Is.EqualTo(0x3333333333333333ul), "后一条的内容对不上，游标错位了");

                Assert.That(reader.ReadNext(out chunk), Is.False, "两条认得的记录之后就该到文件尾");
                Assert.That(
                    reader.SkippedUnknownChunkCount,
                    Is.EqualTo(1),
                    "未知类型应当被记成「跳过 1 条」——这是格式留给未来的余地，不是错误");
                Assert.That(reader.ChunkCount, Is.EqualTo(2), "交出去的条数不该把跳过的那条算进来");
                Assert.That(reader.IsTailTruncated, Is.False, "跳过未知类型不该被误判成尾部残缺");
            }
        }

        [Test]
        public void ReadNext_WhenTailIsCutInsideAPayload_ReadsEveryCompleteChunkAndReportsDiscardedBytes()
        {
            string path = PathFor("truncated-payload.21dr");

            // 记下「最后一条写出去之前」的文件长度，好精确地从那一条的半截处切断。
            long lengthBeforeLastChunk;
            using (ReplayWriter writer = new ReplayWriter(path))
            {
                writer.WriteHeader(CreateHeader(0u, null, 0UL));
                writer.WriteStateHashChunk(0u, 0x1111111111111111ul);
                writer.WriteStateHashChunk(1u, 0x2222222222222222ul);
                writer.WriteStateHashChunk(2u, 0x3333333333333333ul);
                lengthBeforeLastChunk = writer.BytesWritten;
                writer.WriteStateHashChunk(3u, 0x4444444444444444ul);
                writer.Complete();
            }

            // 只留下最后一条的 chunk 头（7 字节）和 8 字节 payload 里的前 3 个——典型的「写到一半进程死了」。
            const int keptBytesOfLastChunk = ReplayFormat.ChunkHeaderSize + 3;
            TruncateTo(path, lengthBeforeLastChunk + keptBytesOfLastChunk);

            ReplayReader opened;
            string error;
            Assert.That(
                ReplayReader.TryOpen(path, out opened, out error),
                Is.True,
                "尾部残缺不该让整份文件打不开：崩溃时自动保存的回放十有八九就长这样，"
                + "整份作废等于最想要的那批现场全拿不回来");

            using (opened)
            {
                int read = 0;
                Assert.DoesNotThrow(
                    () =>
                    {
                        ReplayChunk chunk;
                        while (opened.ReadNext(out chunk))
                        {
                            read++;
                        }
                    },
                    "读到残缺尾部必须安静收尾，不许抛异常");

                Assert.That(read, Is.EqualTo(3), "尾部那条残缺记录之前的 3 条完整记录必须全部读出来");
                Assert.That(opened.IsTailTruncated, Is.True, "尾部确实残缺，必须如实报告");
                Assert.That(
                    opened.TruncatedTailBytes,
                    Is.EqualTo(keptBytesOfLastChunk),
                    "丢弃的字节数要从那条残缺记录的起点算起（含已经读掉的 chunk 头）");
            }
        }

        [Test]
        public void ReadNext_WhenTailIsCutInsideAChunkHeader_ReadsEveryCompleteChunkAndReportsDiscardedBytes()
        {
            string path = PathFor("truncated-header.21dr");

            long lengthBeforeLastChunk;
            using (ReplayWriter writer = new ReplayWriter(path))
            {
                writer.WriteHeader(CreateHeader(0u, null, 0UL));
                writer.WriteStateHashChunk(0u, 0x1111111111111111ul);
                writer.WriteStateHashChunk(1u, 0x2222222222222222ul);
                lengthBeforeLastChunk = writer.BytesWritten;
                writer.WriteStateHashChunk(2u, 0x3333333333333333ul);
                writer.Complete();
            }

            // 这次连 chunk 头都没写完（7 字节只留下 4 个），走的是和上一条用例不同的那条分支。
            const int keptBytesOfLastChunk = 4;
            TruncateTo(path, lengthBeforeLastChunk + keptBytesOfLastChunk);

            ReplayReader opened;
            string error;
            Assert.That(ReplayReader.TryOpen(path, out opened, out error), Is.True, "尾部残缺不该让整份文件打不开");

            using (opened)
            {
                int read = 0;
                Assert.DoesNotThrow(
                    () =>
                    {
                        ReplayChunk chunk;
                        while (opened.ReadNext(out chunk))
                        {
                            read++;
                        }
                    },
                    "连 chunk 头都凑不齐时也不许抛");

                Assert.That(read, Is.EqualTo(2), "残缺记录之前的 2 条必须全部读出来");
                Assert.That(opened.IsTailTruncated, Is.True, "尾部确实残缺，必须如实报告");
                Assert.That(opened.TruncatedTailBytes, Is.EqualTo(keptBytesOfLastChunk), "丢弃的字节数不对");
            }
        }

        [Test]
        public void TryOpen_WhenMagicIsWrong_FailsWithAReadableErrorAndNoReader()
        {
            string path = PathFor("not-a-replay.bin");
            byte[] garbage = new byte[64];
            for (int i = 0; i < garbage.Length; i++)
            {
                garbage[i] = 0xFF;
            }

            File.WriteAllBytes(path, garbage);

            ReplayReader reader;
            string error;
            Assert.That(ReplayReader.TryOpen(path, out reader, out error), Is.False, "魔数不对的文件不该被打开");
            Assert.That(reader, Is.Null, "失败时不许交出一个半开的读取器");
            Assert.That(error, Does.Contain("不是回放文件"), $"报错要说清「这不是回放文件」，实际是：{error}");
        }

        [Test]
        public void TryOpen_WhenFormatVersionIsNewerThanCurrent_FailsAndAsksToUpdate()
        {
            string path = PathFor("from-the-future.21dr");
            using (ReplayWriter writer = new ReplayWriter(path))
            {
                writer.WriteHeader(CreateHeader(0u, null, 0UL));
                writer.WriteStateHashChunk(0u, 1ul);
                writer.Complete();
            }

            // 把版本号那两个字节（紧跟在 4 字节魔数后面，小端 u16）改成一个更新的版本。
            ushort future = (ushort)(ReplayFormat.CurrentFormatVersion + 1);
            byte[] bytes = File.ReadAllBytes(path);
            bytes[ReplayFormat.MagicSize] = (byte)future;
            bytes[ReplayFormat.MagicSize + 1] = (byte)(future >> 8);
            File.WriteAllBytes(path, bytes);

            ReplayReader reader;
            string error;
            Assert.That(
                ReplayReader.TryOpen(path, out reader, out error),
                Is.False,
                "比当前新的格式版本必须被硬拒：硬读只会按老布局读出一堆看起来合理的垃圾值");
            Assert.That(reader, Is.Null, "失败时不许交出一个半开的读取器");
            Assert.That(error, Does.Contain("更新版本"), $"报错要说清是版本太新、该更新编辑器，实际是：{error}");
        }

        [Test]
        public void TryOpen_WhenFileIsOnlyAFewBytes_FailsWithAReadableError()
        {
            string path = PathFor("too-short.21dr");
            File.WriteAllBytes(path, new byte[] { 0x32, 0x31, 0x44 });

            ReplayReader reader;
            string error;
            Assert.That(ReplayReader.TryOpen(path, out reader, out error), Is.False, "连魔数都放不下的文件不该被打开");
            Assert.That(reader, Is.Null, "失败时不许交出一个半开的读取器");
            Assert.That(error, Does.Contain("魔数"), $"报错要说清连魔数都不够，实际是：{error}");
        }

        [Test]
        public void TryOpen_WhenHeaderIsCutMidway_FailsWithAReadableError()
        {
            string path = PathFor("half-a-header.21dr");
            byte[] snapshot = { 9, 8, 7, 6 };

            using (ReplayWriter writer = new ReplayWriter(path))
            {
                writer.WriteHeader(ReplayHeader.Create(
                    PlatformKind.Standalone,
                    1_700_000_000L,
                    "1.2.3",
                    1UL,
                    2UL,
                    FixedDeltaTime,
                    0u,
                    snapshot,
                    0,
                    snapshot.Length));
                writer.WriteStateHashChunk(0u, 1ul);
                writer.Complete();
            }

            // 切在头部固定部分中间：魔数和版本号都还在，断的是后半段（种子/指纹/步长那一串）。
            TruncateTo(path, ReplayFormat.HeaderFixedSize - 1);

            ReplayReader reader;
            string error;
            Assert.That(
                ReplayReader.TryOpen(path, out reader, out error),
                Is.False,
                "头部残缺必须判整份不可读：没有种子、没有步长、没有起始快照，后面的输入一条都没法重放");
            Assert.That(reader, Is.Null, "失败时不许交出一个半开的读取器");
            Assert.That(error, Does.Contain("文件头不完整"), $"报错要说清头部不完整，实际是：{error}");
        }

        [Test]
        public void FormatConstants_AreFrozen_BecauseChangingThemInvalidatesEveryExistingReplay()
        {
            // ⚠ 改了下面任何一个数，**已有的回放文件全部失效**——老文件会被按新布局读出
            // 一堆看起来合理的垃圾值，而且不会报错。真要改，必须同时升 CurrentFormatVersion，
            // 并且明确接受「此前录的所有回放都读不了了」。这条用例就是那道闸。
            Assert.That(ReplayFormat.CurrentFormatVersion, Is.EqualTo(1), "格式版本改了：已有回放全部失效");
            Assert.That(ReplayFormat.MinimumReadableFormatVersion, Is.EqualTo(1), "最老可读版本改了：老回放会被拒收");
            Assert.That(ReplayFormat.Magic, Is.EqualTo(0x52443132u), "魔数改了：已有回放一份都认不出来");
            Assert.That(ReplayFormat.MagicAscii, Is.EqualTo("21DR"), "魔数的可读写法要和字节值对得上");

            Assert.That(ReplayFormat.MagicSize, Is.EqualTo(4), "魔数宽度改了：头部所有偏移跟着错位");
            Assert.That(ReplayFormat.FormatVersionSize, Is.EqualTo(2), "版本号宽度改了：头部偏移错位");
            Assert.That(ReplayFormat.PlatformSize, Is.EqualTo(1), "平台标记宽度改了：头部偏移错位");
            Assert.That(ReplayFormat.TimestampSize, Is.EqualTo(8), "时间戳宽度改了：头部偏移错位");
            Assert.That(ReplayFormat.StringLengthPrefixSize, Is.EqualTo(2), "字符串长度前缀宽度改了：头部偏移错位");
            Assert.That(ReplayFormat.SeedSize, Is.EqualTo(8), "种子宽度改了：头部偏移错位");
            Assert.That(ReplayFormat.ConfigHashSize, Is.EqualTo(8), "配置指纹宽度改了：头部偏移错位");
            Assert.That(ReplayFormat.FixedDeltaTimeSize, Is.EqualTo(4), "步长宽度改了：头部偏移错位");
            Assert.That(ReplayFormat.StartTickSize, Is.EqualTo(4), "起始 tick 宽度改了：头部偏移错位");
            Assert.That(ReplayFormat.BlobLengthPrefixSize, Is.EqualTo(4), "快照长度前缀宽度改了：头部偏移错位");

            Assert.That(ReplayFormat.HeaderPrefixSize, Is.EqualTo(17), "头部前段长度必须是各字段宽度之和");
            Assert.That(ReplayFormat.HeaderSuffixSize, Is.EqualTo(28), "头部后段长度必须是各字段宽度之和");
            Assert.That(ReplayFormat.HeaderFixedSize, Is.EqualTo(45), "头部固定部分总长改了：老文件全部读错");

            Assert.That(ReplayFormat.ChunkTypeSize, Is.EqualTo(1), "chunk 类型宽度改了：每一条记录都错位");
            Assert.That(ReplayFormat.ChunkTickSize, Is.EqualTo(4), "chunk tick 宽度改了：每一条记录都错位");
            Assert.That(ReplayFormat.ChunkLengthSize, Is.EqualTo(2), "chunk 长度前缀宽度改了：每一条记录都错位");
            Assert.That(
                ReplayFormat.ChunkHeaderSize,
                Is.EqualTo(7),
                "chunk 头总长改了：「按 length 跳过未知类型」这条承诺跟着一起坏掉");
            Assert.That(
                ReplayFormat.MaxChunkPayloadLength,
                Is.EqualTo(ushort.MaxValue),
                "payload 上限由 u16 长度前缀决定，改它就是改格式");
            Assert.That(ReplayFormat.MaxBuildVersionByteCount, Is.EqualTo(ushort.MaxValue), "构建版本号上限由 u16 前缀决定");
            Assert.That(ReplayFormat.FileExtension, Is.EqualTo(".21dr"), "推荐扩展名变了会让现有的回放列表找不到文件");

            // 输入 chunk 的 payload 就是一条定长输入命令，它的宽度同样是文件格式的一部分。
            Assert.That(
                InputCommand.SerializedSize,
                Is.EqualTo(31),
                "输入命令的字节宽度改了：已有回放里每一条输入都会被读错，必须同时升格式版本");
        }

        [Test]
        public void ChunkTypeNumbers_AreFrozen_AndUnknownNumbersStayUnknown()
        {
            // ⚠ 这几个数字已经写在老回放文件里了，**改任何一个都会让已有回放读错类型**。
            // 新增类型一律往后加（加新类型不用升格式版本，老读者会按 length 跳过）。
            Assert.That((byte)ReplayFormat.ChunkType.Input, Is.EqualTo(0), "输入的类型号改了：老回放里的输入会被当成别的记录");
            Assert.That((byte)ReplayFormat.ChunkType.StateHash, Is.EqualTo(1), "状态哈希的类型号改了：漂移检测全部失灵");
            Assert.That((byte)ReplayFormat.ChunkType.Snapshot, Is.EqualTo(2), "完整快照的类型号改了：跳转与纠偏全部失灵");
            Assert.That((byte)ReplayFormat.ChunkType.QaMarker, Is.EqualTo(3), "QA 打点的类型号改了");

            Assert.That(ReplayFormat.IsKnownChunkType(0), Is.True, "已定义的类型必须认得");
            Assert.That(ReplayFormat.IsKnownChunkType(3), Is.True, "已定义的类型必须认得");
            Assert.That(
                ReplayFormat.IsKnownChunkType(4),
                Is.False,
                "还没定义的类型号必须判成「不认识」，好让读取器按 length 跳过而不是拿错误的布局去解读");
            Assert.That(ReplayFormat.IsKnownChunkType(255), Is.False, "没定义的类型号一律不认识");
        }

        /// <summary>临时工作目录下的一个文件路径。</summary>
        private string PathFor(string fileName)
        {
            return Path.Combine(workRoot, fileName);
        }

        /// <summary>造一个参数最简的文件头，只有确实要考察的用例才自己写全套参数。</summary>
        private static ReplayHeader CreateHeader(uint startTick, byte[] snapshot, ulong configHash)
        {
            return ReplayHeader.Create(
                PlatformKind.Standalone,
                1_700_000_000L,
                "1.0.0",
                1234UL,
                configHash,
                FixedDeltaTime,
                startTick,
                snapshot,
                0,
                snapshot == null ? 0 : snapshot.Length);
        }

        /// <summary>把文件截到指定长度，模拟「写到一半进程被杀」。</summary>
        private static void TruncateTo(string path, long length)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Write))
            {
                Assert.That(
                    stream.Length,
                    Is.GreaterThan(length),
                    "要截断的文件本来就不比目标长度长，这条用例什么都没验到");
                stream.SetLength(length);
            }
        }
    }
}
