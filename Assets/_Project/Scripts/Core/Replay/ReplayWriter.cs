// 职责：把回放**流式**写进文件——先写一次头部，之后逐条追加 chunk，最后收尾时原子替换正档。
//   录制器保存时会遍历它的环形缓冲逐条写出，所以「追加一条」这条路径必须零堆分配。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：工程内唯一的落盘设施是 Core/Save/JsonSaveService。它是 JSON 存档——
//      整份内容先在内存里拼成一个字符串再一次性 WriteAllText，面向人读与逐分区版本迁移。
//      这里要的是定长二进制、边录边追加、崩溃后尾部残缺仍要能读，两者除了
//      「先写临时文件再原子替换」这一条做法之外没有一行可共用（那一条做法在下面照抄了，
//      见 CommitAtomic 的注释）。让存档服务再长出一个二进制流式接口，等于把两套相反的
//      兼容策略绑进同一个类。
//   2. 扩展不行：不能建在 BinaryWriter 之上。BinaryWriter 的字节序跟运行平台走
//      （文档只保证「以特定字节序写入」，不保证跨平台一致），字符串写的是 7 位压缩长度前缀的私有格式，
//      而回放文件要能在安卓上录、PC 上放、半年后还能按文件里那段注释手工解析出来。
//      工程里的小端规则只有 StateBuffer 一份实现，这里就用它拼字节，不引入第二套。
//   3. 不和 ReplayReader 合成一个类：读写的生命周期完全不同（写的持有临时文件与提交语义，
//      读的持有文件长度与截断状态），合一只会让两边的状态机互相污染。

using System;
using System.IO;
using System.Text;
using Game.Core.Logging;
using Game.Core.Simulation;

namespace Game.Core.Replay
{
    /// <summary>
    /// 回放文件写入器。用法固定三步，顺序不能乱：
    /// <code>
    /// using (ReplayWriter writer = new ReplayWriter(path))
    /// {
    ///     writer.WriteHeader(header);                       // 有且只有一次
    ///     writer.WriteInputChunk(tick, command);            // 逐条追加，想写多少写多少
    ///     writer.WriteStateHashChunk(tick, hash);
    ///     writer.Complete();                                // 收尾：落盘 + 原子替换正档
    /// }
    /// </code>
    /// <para>
    /// <b>先写临时文件再原子替换</b>：内容全程写在 <c>&lt;path&gt;.tmp</c> 上，
    /// <see cref="Complete"/> 成功了才替换正档。所以「录着录着崩了」不会毁掉上一份完好的回放——
    /// 那才是最不该丢的东西。没调 <see cref="Complete"/> 就 <see cref="Dispose"/>（进程被杀、异常退出）时，
    /// <c>.tmp</c> **原样留在磁盘上**，它就是崩溃现场：<see cref="ReplayReader"/> 能直接打开它，
    /// 读出尾部残缺之前的全部内容（<c>ReplayReader</c> 只认魔数，不看扩展名）。
    /// </para>
    /// <para>
    /// <b>分配约定</b>：构造时分配两块小缓冲（帧头拼装 + 一条输入命令）之后，
    /// 追加 chunk 的路径一次堆分配都没有。写头部会分配（字符串编码），但一份文件只走一次。
    /// </para>
    /// <para>线程约定：只在一个线程上使用，内部不加锁。</para>
    /// </summary>
    public sealed class ReplayWriter : IDisposable
    {
        /// <summary>临时文件后缀。和 JsonSaveService 用同一个后缀，清残片的习惯也一致。</summary>
        private const string TempSuffix = ".tmp";

        /// <summary>FileStream 的内部缓冲大小。给得比默认的 4KB 大，是为了让每 tick 一条的追加尽量不落到系统调用上。</summary>
        private const int StreamBufferSize = 64 * 1024;

        /// <summary>
        /// 帧头拼装缓冲。容量按头部固定部分给（45 字节），比任何一次拼装都大：
        /// 头部前段 17、头部后段 28、chunk 头 7（状态哈希那条连 payload 一起拼也才 15）。
        /// 所以运行期永远不会触发它的扩容，稳态零分配。
        /// </summary>
        private readonly StateBuffer framing = new StateBuffer(ReplayFormat.HeaderFixedSize);

        /// <summary>一条输入命令的定长暂存区。复用它，避免每 tick new 一个 byte[31]。</summary>
        private readonly byte[] inputScratch = new byte[InputCommand.SerializedSize];

        private readonly string filePath;
        private readonly string tempPath;

        private FileStream stream;
        private bool headerWritten;
        private bool completed;
        private int chunkCount;

        /// <summary>关流时记下的最终字节数，好让 <see cref="BytesWritten"/> 收尾之后仍然说得出话。</summary>
        private long closedBytesWritten;

        /// <summary>
        /// 打开一个待写入的回放文件。立刻建好目录并创建临时文件（<c>&lt;path&gt;.tmp</c>，已存在会被覆盖）。
        /// </summary>
        /// <param name="filePath">最终的回放文件路径。</param>
        /// <exception cref="ArgumentException">路径为空。</exception>
        /// <exception cref="IOException">目录建不出来或临时文件创建失败。</exception>
        public ReplayWriter(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentException("回放文件路径不能为空", nameof(filePath));
            }

            this.filePath = filePath;
            tempPath = filePath + TempSuffix;

            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // FileShare.Read：允许别的进程在录制过程中读这个临时文件（看一眼录到哪了），但不许同时写。
            stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.Read, StreamBufferSize);
        }

        /// <summary>最终的回放文件路径（<see cref="Complete"/> 之后才会出现内容）。</summary>
        public string FilePath => filePath;

        /// <summary>正在写的临时文件路径。没收尾就崩了的话，现场就在这个文件里。</summary>
        public string TempFilePath => tempPath;

        /// <summary>已经写出的 chunk 条数。</summary>
        public int ChunkCount => chunkCount;

        /// <summary>头部写过了没有。</summary>
        public bool IsHeaderWritten => headerWritten;

        /// <summary>已经收尾并替换正档了没有。</summary>
        public bool IsCompleted => completed;

        /// <summary>
        /// 已写出的字节数（含还留在 FileStream 缓冲里没落盘的部分）。关流之后保持最终值不变。
        /// </summary>
        public long BytesWritten => stream == null ? closedBytesWritten : stream.Position;

        /// <summary>
        /// 写文件头，**必须是第一个调用，且只能调一次**。字节布局见 <see cref="ReplayFormat"/>。
        /// </summary>
        /// <param name="header">头部内容。它的 <c>FormatVersion</c> 会被原样写出去——
        /// 录制端请用 <see cref="ReplayHeader.Create"/> 构造，那个会填当前版本。</param>
        /// <exception cref="InvalidOperationException">头部已经写过，或写入器已收尾/已释放。</exception>
        /// <exception cref="ArgumentException">构建版本号的 UTF-8 字节数超过 65535。</exception>
        public void WriteHeader(ReplayHeader header)
        {
            EnsureWritable();
            if (headerWritten)
            {
                throw new InvalidOperationException("文件头只能写一次；再写一遍会让文件里出现两个头，读的时候第二个会被当成 chunk 解读");
            }

            // 一份文件只走一次，这里允许分配。
            byte[] buildVersionBytes = Encoding.UTF8.GetBytes(header.BuildVersion);
            if (buildVersionBytes.Length > ReplayFormat.MaxBuildVersionByteCount)
            {
                throw new ArgumentException(
                    $"构建版本号编码后有 {buildVersionBytes.Length} 字节，超过了长度前缀能表示的 "
                    + $"{ReplayFormat.MaxBuildVersionByteCount} 字节上限",
                    nameof(header));
            }

            // 头部前段：magic | formatVersion | platform | unixUtc | buildVersion 的长度前缀
            framing.Reset();
            framing.WriteUInt(ReplayFormat.Magic);
            framing.WriteUShort(header.FormatVersion);
            framing.WriteByte((byte)header.Platform);
            framing.WriteLong(header.UnixUtcSeconds);
            framing.WriteUShort((ushort)buildVersionBytes.Length);
            FlushFraming();

            if (buildVersionBytes.Length > 0)
            {
                stream.Write(buildVersionBytes, 0, buildVersionBytes.Length);
            }

            // 头部后段：seed | configHash | fixedDeltaTime | startTick | 起始快照的长度前缀
            // WriteFloat 会把 NaN 与 -0 规范化，这里不会碰上：ReplayHeader 构造时已经
            // 把「非有限或非正的步长」挡掉了，能走到这儿的步长一定是正常值。
            framing.Reset();
            framing.WriteULong(header.Seed);
            framing.WriteULong(header.ConfigHash);
            framing.WriteFloat(header.FixedDeltaTime);
            framing.WriteUInt(header.StartTick);
            framing.WriteUInt((uint)header.SnapshotLength);
            FlushFraming();

            if (header.SnapshotLength > 0)
            {
                stream.Write(header.GetSnapshotBuffer(), header.SnapshotOffset, header.SnapshotLength);
            }

            headerWritten = true;
        }

        /// <summary>
        /// 追加一条输入命令（<see cref="ReplayFormat.ChunkType.Input"/>）。每 tick 一条，零分配。
        /// </summary>
        /// <param name="tick">这条输入属于哪个逻辑 tick。</param>
        /// <param name="command">输入命令，按 <see cref="InputCommand.SerializedSize"/> 字节定长写出。</param>
        public void WriteInputChunk(uint tick, InputCommand command)
        {
            command.WriteTo(inputScratch, 0);
            WriteChunk((byte)ReplayFormat.ChunkType.Input, tick, inputScratch, 0, InputCommand.SerializedSize);
        }

        /// <summary>
        /// 追加一条状态哈希（<see cref="ReplayFormat.ChunkType.StateHash"/>），payload 是小端 u64。零分配。
        /// </summary>
        /// <param name="tick">算这个哈希时世界处于哪个 tick。</param>
        /// <param name="hash"><see cref="StateHasher"/> 算出来的值。</param>
        public void WriteStateHashChunk(uint tick, ulong hash)
        {
            EnsureChunkWritable(sizeof(ulong));

            // chunk 头和 8 字节 payload 一起拼进帧头缓冲，一次写出去——
            // 小端规则仍然只有 StateBuffer 一份实现，这里不另写解码/编码。
            framing.Reset();
            framing.WriteByte((byte)ReplayFormat.ChunkType.StateHash);
            framing.WriteUInt(tick);
            framing.WriteUShort((ushort)sizeof(ulong));
            framing.WriteULong(hash);
            FlushFraming();
            chunkCount++;
        }

        /// <summary>
        /// 追加一条完整快照（<see cref="ReplayFormat.ChunkType.Snapshot"/>）。
        /// </summary>
        /// <param name="tick">快照对应的 tick。</param>
        /// <param name="buffer">刚写完 <see cref="IReplayStateProvider.SerializeAll"/> 的缓冲，取它的有效长度那一段。</param>
        /// <exception cref="ArgumentNullException">buffer 为 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException">快照超过了 <see cref="ReplayFormat.MaxChunkPayloadLength"/>。</exception>
        public void WriteSnapshotChunk(uint tick, StateBuffer buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            WriteChunk((byte)ReplayFormat.ChunkType.Snapshot, tick, buffer.GetBuffer(), 0, buffer.Length);
        }

        /// <summary>
        /// 追加一条 QA 打点（<see cref="ReplayFormat.ChunkType.QaMarker"/>），payload 是 UTF-8 文本。
        /// <para><b>会分配</b>（字符串编码）——打点由人按热键产生，一局也就几条，不在高频路径上。</para>
        /// </summary>
        /// <param name="tick">打点打在哪个 tick 上。</param>
        /// <param name="label">说明文本，可为 null 或空串（那就写一条空 payload 的打点）。</param>
        public void WriteQaMarkerChunk(uint tick, string label)
        {
            if (string.IsNullOrEmpty(label))
            {
                WriteChunk((byte)ReplayFormat.ChunkType.QaMarker, tick, null, 0, 0);
                return;
            }

            byte[] labelBytes = Encoding.UTF8.GetBytes(label);
            WriteChunk((byte)ReplayFormat.ChunkType.QaMarker, tick, labelBytes, 0, labelBytes.Length);
        }

        /// <summary>
        /// 追加一条任意类型的 chunk。给两种场合用：本类没有提供专用方法的类型，
        /// 以及将来新增的、本版本还不认识的类型（剪辑 / 合并工具把读到的原样写回去）。
        /// </summary>
        /// <param name="rawType">类型字节。新增类型往 <see cref="ReplayFormat.ChunkType"/> 后面加，别复用旧号。</param>
        /// <param name="tick">所属 tick。</param>
        /// <param name="payload">payload 所在数组，可为 null（此时 count 必须为 0）。</param>
        /// <param name="offset">payload 起始偏移。</param>
        /// <param name="count">payload 字节数，不能超过 <see cref="ReplayFormat.MaxChunkPayloadLength"/>。</param>
        /// <exception cref="InvalidOperationException">头部还没写，或写入器已收尾/已释放。</exception>
        /// <exception cref="ArgumentNullException">count 大于 0 但 payload 为 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException">长度为负、超上限，或这段超出了数组范围。</exception>
        public void WriteChunk(byte rawType, uint tick, byte[] payload, int offset, int count)
        {
            EnsureChunkWritable(count);

            if (count > 0)
            {
                if (payload == null)
                {
                    throw new ArgumentNullException(nameof(payload), "payload 是 null，长度就必须为 0");
                }

                if (offset < 0 || payload.Length - offset < count)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(count), count, "要写出的 payload 区间超出了所给数组的范围");
                }
            }

            framing.Reset();
            framing.WriteByte(rawType);
            framing.WriteUInt(tick);
            framing.WriteUShort((ushort)count);
            FlushFraming();

            if (count > 0)
            {
                stream.Write(payload, offset, count);
            }

            chunkCount++;
        }

        /// <summary>
        /// 把 FileStream 缓冲里的内容推到操作系统。边玩边录时定期调一次，
        /// 这样进程被强杀也只会丢掉最后没刷出去的那一点，而不是整段。
        /// <para>不做 <c>FlushToDisk</c>（强制落到物理介质）：那一步很慢，放在每秒都跑的路径上会卡帧。</para>
        /// </summary>
        public void Flush()
        {
            if (stream != null)
            {
                stream.Flush();
            }
        }

        /// <summary>
        /// 收尾：落盘、关闭临时文件、原子替换正档。**调过一次之后再调是空操作**（幂等，
        /// 这样 <c>Complete(); Dispose();</c> 和忘了 Complete 的 <c>Dispose()</c> 都不会出意外）。
        /// </summary>
        /// <exception cref="InvalidOperationException">一个头都没写就收尾（那会产生一份读不了的文件）。</exception>
        /// <exception cref="IOException">替换正档失败；此时临时文件会保留下来供手工恢复。</exception>
        public void Complete()
        {
            if (completed)
            {
                return;
            }

            if (stream == null)
            {
                throw new ObjectDisposedException(nameof(ReplayWriter), "写入器已经释放，不能再收尾");
            }

            if (!headerWritten)
            {
                throw new InvalidOperationException(
                    $"没有写文件头就收尾，这会产生一份读不了的回放文件：{filePath}。"
                    + "要主动放弃这次录制，直接 Dispose()，临时文件会留在原地。");
            }

            stream.Flush();
            closedBytesWritten = stream.Position;
            stream.Dispose();
            stream = null;

            CommitAtomic();
            completed = true;
        }

        /// <summary>
        /// 释放。**没调过 <see cref="Complete"/> 就释放时不会替换正档**，
        /// 临时文件原样留在磁盘上并打一条 Warn 指出它在哪——那就是崩溃现场，
        /// 直接用 <see cref="ReplayReader"/> 打开它就能读出尾部残缺之前的全部内容。
        /// </summary>
        public void Dispose()
        {
            if (stream == null)
            {
                return;
            }

            try
            {
                stream.Flush();
            }
            catch (IOException e)
            {
                // 释放路径上不该再抛：这里通常已经是在处理另一个错误了，
                // 再抛一次会把真正的原因盖掉。记下来就够。
                Log.Warn($"回放临时文件最后一次刷盘失败：{tempPath}，{e.Message}");
            }

            closedBytesWritten = stream.Position;
            stream.Dispose();
            stream = null;

            if (!completed)
            {
                Log.Warn(
                    $"回放没有调用 Complete() 就结束了，内容留在临时文件里：{tempPath}。"
                    + $"正档没有被覆盖；这份临时文件可以直接用 ReplayReader 打开（尾部残缺的部分会被忽略）。");
            }
        }

        /// <summary>
        /// 原子替换正档。做法照抄 Core/Save/JsonSaveService.WriteAtomic，理由也一样：
        /// <see cref="File.Replace(string, string, string)"/> 才是真正的原子替换，
        /// 但它只在同卷的常规文件系统上可用；退路用 <see cref="File.Copy(string, string, bool)"/> 覆盖，
        /// 而**不是**「先删正档再 Move」——删完万一 Move 炸了，正档和临时文件会同时没了。
        /// </summary>
        private void CommitAtomic()
        {
            if (File.Exists(filePath))
            {
                try
                {
                    File.Replace(tempPath, filePath, null);
                }
                catch (Exception)
                {
                    File.Copy(tempPath, filePath, true);
                }
            }
            else
            {
                File.Move(tempPath, filePath);
            }

            // Replace 会把临时文件吃掉，Copy 那条退路不会；清干净，别给下次录制留残片。
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException e)
                {
                    // 正档已经就位了，清残片失败不影响这次录制的成败。
                    Log.Warn($"回放临时文件没清掉：{tempPath}，{e.Message}");
                }
            }
        }

        /// <summary>把帧头缓冲里拼好的那几个字节写出去。</summary>
        private void FlushFraming()
        {
            stream.Write(framing.GetBuffer(), 0, framing.Length);
        }

        /// <summary>写任何东西之前都要过这一关：流还在、还没收尾。</summary>
        private void EnsureWritable()
        {
            if (stream == null)
            {
                throw new ObjectDisposedException(nameof(ReplayWriter), $"写入器已经释放：{filePath}");
            }

            if (completed)
            {
                throw new InvalidOperationException($"这份回放已经收尾了，不能再往里写：{filePath}");
            }
        }

        /// <summary>写 chunk 之前额外要过的两关：头部写过了、payload 不超上限。</summary>
        private void EnsureChunkWritable(int payloadLength)
        {
            EnsureWritable();

            if (!headerWritten)
            {
                throw new InvalidOperationException(
                    $"必须先 WriteHeader 再写 chunk：{filePath}。没有头的文件读的时候会被判成「这不是回放文件」");
            }

            if (payloadLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(payloadLength), payloadLength, "payload 长度不能为负");
            }

            if (payloadLength > ReplayFormat.MaxChunkPayloadLength)
            {
                // 明确报错而不是截断：截出来的快照恢复后是个「错误的世界」，
                // 而且没有任何迹象表明它错了——比这次录不上糟得多。
                throw new ArgumentOutOfRangeException(
                    nameof(payloadLength),
                    payloadLength,
                    $"单条 chunk 的 payload 上限是 {ReplayFormat.MaxChunkPayloadLength} 字节（长度前缀是 u16）。"
                    + "快照顶到上限说明世界状态已经很大了，正解是升格式版本把长度前缀改宽，或者把快照拆条写，"
                    + "绝不能截断——截断后的快照恢复出来是个错误的世界，而且看不出错。");
            }
        }
    }
}
