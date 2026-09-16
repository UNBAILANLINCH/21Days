// 职责：顺序读回放文件——先把头部解析出来（坏了就给一句人看得懂的话），然后一条条往下交 chunk。
//   这个类扛着回放格式的两条核心承诺：**认不得的 chunk 类型按 length 跳过**、
//   **尾部残缺的文件尽力读到最后一条完整记录**。两条的理由都写在下面对应的方法上。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：工程内唯一的读档设施是 Core/Save/JsonSaveService.LoadAsync——
//      它把整份 JSON 一次读进字符串再整体反序列化，任何一处解析失败整份存档判损坏。
//      那套策略用在回放上恰好是最糟的：崩溃时自动保存的回放尾部十有八九是残的，
//      整份作废意味着我们最想要的那批现场全都拿不回来。
//   2. 扩展不行：不能把「读」塞进 ReplayWriter。写的状态机是「临时文件 + 提交语义」，
//      读的状态机是「文件长度 + 截断状态 + 跳过计数」，合一之后每个字段都要先问一句
//      「现在是读还是写」，而这类分支正是错位读写的温床。
//   3. 也不用 BinaryReader：它的字节序跟平台走、字符串是私有的 7 位长度前缀格式，
//      而且读不到字节时直接抛 EndOfStreamException——把「尾部残缺」变成异常，
//      正好跟本类要做的「尽力读」相反。小端规则在工程里只有 StateBuffer 一份实现，这里复用它。

using System;
using System.IO;
using System.Text;
using Game.Core.Logging;
using Game.Core.Platform;

namespace Game.Core.Replay
{
    /// <summary>
    /// 回放文件读取器。用法：
    /// <code>
    /// if (!ReplayReader.TryOpen(path, out ReplayReader reader, out string error))
    /// {
    ///     Log.Error(error);   // error 是给人看的一句话，不是异常堆栈
    ///     return;
    /// }
    ///
    /// using (reader)
    /// {
    ///     ReplayHeader header = reader.Header;
    ///     while (reader.ReadNext(out ReplayChunk chunk))
    ///     {
    ///         switch (chunk.Type) { ... }
    ///     }
    ///
    ///     if (reader.IsTailTruncated) { /* 尾部丢了 reader.TruncatedTailBytes 字节，前面的都有效 */ }
    /// }
    /// </code>
    ///
    /// <para><b>两条承诺</b>（它们决定了这个类的全部设计）：</para>
    /// <para>
    /// ① <b>认不得的 chunk 类型按 length 跳过，不报错</b>。所以将来加新记录类型
    /// （埋点引用、相机轨迹）不用升格式版本：新版编辑器读老回放没问题，
    /// 老版编辑器读新回放也能读出它认识的那部分。见 <see cref="ReadNext"/>。
    /// </para>
    /// <para>
    /// ② <b>尾部残缺的文件尽力读</b>。读到最后一条完整 chunk 为止，前面的数据正常交出，
    /// 同时记一条 Warn 说明尾部丢了多少字节——**不抛异常、不整份作废**。
    /// 只有**头部**本身损坏或不完整，才算整个文件读不了。见 <see cref="ReadNext"/> 与 <see cref="TryOpen"/>。
    /// </para>
    ///
    /// <para>
    /// <b>绝不静默播错</b>：这三种情况都会拿到一句说清了坏在哪的话——不是回放文件、
    /// 由更新版本录制、头部截断。静默播错比报错糟得多：人会对着一个根本没正确加载的回放查半天 bug。
    /// </para>
    ///
    /// <para>
    /// <b>分配约定</b>：打开时分配帧头缓冲、payload 缓冲与起始快照数组；
    /// 之后遍历 chunk 的路径一次堆分配都没有（payload 复用同一块缓冲，见 <see cref="ReplayChunk"/>）。
    /// </para>
    /// <para>线程约定：只在一个线程上使用，内部不加锁。</para>
    /// </summary>
    public sealed class ReplayReader : IDisposable
    {
        /// <summary>FileStream 的内部缓冲大小，和写入端对称。</summary>
        private const int StreamBufferSize = 64 * 1024;

        /// <summary>帧头拆解缓冲。容量按头部固定部分给（45），比任何一次拆解都大，运行期不会扩容。</summary>
        private readonly StateBuffer framing = new StateBuffer(ReplayFormat.HeaderFixedSize);

        /// <summary>
        /// payload 缓冲。直接按 <see cref="ReplayFormat.MaxChunkPayloadLength"/>（64KB）一次分配到位：
        /// payload 长度前缀是 u16，这就是硬上限，所以这块缓冲永远够用、永远不用扩容，
        /// 遍历几万条记录一次分配都没有。头部解析时也借它当暂存区。
        /// </summary>
        private readonly byte[] payloadScratch = new byte[ReplayFormat.MaxChunkPayloadLength];

        private readonly string filePath;

        private FileStream stream;
        private ReplayHeader header;
        private long fileLength;
        private long firstChunkOffset;
        private bool finished;
        private bool tailTruncated;
        private long truncatedTailBytes;
        private int chunkCount;
        private int skippedUnknownChunkCount;
        private bool unknownTypeLogged;

        private ReplayReader(string filePath, FileStream stream)
        {
            this.filePath = filePath;
            this.stream = stream;
        }

        /// <summary>
        /// 打开一份回放并解析头部。**这是推荐入口**：打不开是常见情况（选错文件、版本对不上、
        /// 录到一半崩了连头都没写完），用返回值表达比用异常表达更顺手。
        /// </summary>
        /// <param name="filePath">回放文件路径。不看扩展名，只认魔数——崩溃留下的 <c>.tmp</c> 可以直接开。</param>
        /// <param name="reader">成功时是打开好的读取器（用完请 <see cref="Dispose"/>），失败时为 null。</param>
        /// <param name="error">失败时是一句**给人看的**说明，说清坏在哪；成功时为 null。</param>
        /// <returns>头部解析成功返回 true。注意：尾部残缺**不**算失败，那属于能读的情况。</returns>
        public static bool TryOpen(string filePath, out ReplayReader reader, out string error)
        {
            reader = null;
            error = null;

            if (string.IsNullOrEmpty(filePath))
            {
                error = "回放文件路径是空的";
                return false;
            }

            FileStream opened = null;
            try
            {
                if (!File.Exists(filePath))
                {
                    error = $"找不到回放文件：{filePath}";
                    return false;
                }

                // FileShare.ReadWrite：允许打开一个**正在被写**的文件。
                // 崩溃现场往往就是这种状态（录制进程还没退干净），此时不让读等于把现场关在门外。
                opened = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, StreamBufferSize);

                ReplayReader candidate = new ReplayReader(filePath, opened);
                opened = null; // 所有权已经交给 candidate，后面由它负责释放

                error = candidate.ParseHeader();
                if (error != null)
                {
                    candidate.Dispose();
                    return false;
                }

                reader = candidate;
                return true;
            }
            catch (Exception e)
            {
                // 故意捕获所有异常：这是一个调试工具，一份坏文件（甚至一份根本不是文件的东西）
                // 绝不该把编辑器或游戏带崩。异常细节进 Debug 日志，给人看的那句话进 error。
                if (opened != null)
                {
                    opened.Dispose();
                }

                error = $"打不开回放文件：{filePath}（{e.GetType().Name}：{e.Message}）";
                Log.Debug($"打开回放文件时抛异常：{filePath}\n{e}");
                return false;
            }
        }

        /// <summary>
        /// 打开一份回放，失败直接抛。只在「打不开就是 bug」的场合用（比如刚写完就读回来自检的测试）；
        /// 面向用户的路径请用 <see cref="TryOpen"/>。
        /// </summary>
        /// <exception cref="InvalidDataException">文件不是回放、版本不兼容或头部损坏，消息与 <see cref="TryOpen"/> 的 error 一致。</exception>
        public static ReplayReader Open(string filePath)
        {
            if (TryOpen(filePath, out ReplayReader reader, out string error))
            {
                return reader;
            }

            throw new InvalidDataException(error);
        }

        /// <summary>这份回放的头部。<see cref="TryOpen"/> 返回 true 之后才有意义。</summary>
        public ReplayHeader Header => header;

        /// <summary>被读的文件路径。</summary>
        public string FilePath => filePath;

        /// <summary>打开时看到的文件字节数（<see cref="SeekToFirstChunk"/> 会重新取一次）。</summary>
        public long FileLength => fileLength;

        /// <summary>已经交出去的完整 chunk 条数（不含被跳过的未知类型）。</summary>
        public int ChunkCount => chunkCount;

        /// <summary>
        /// 因为类型不认识而被跳过的 chunk 条数。不是错误——这正是格式留给未来的余地；
        /// 但值得显示出来，好让人知道「这份回放里还有一些本版本看不懂的信息」。
        /// </summary>
        public int SkippedUnknownChunkCount => skippedUnknownChunkCount;

        /// <summary>尾部是不是有残缺的记录被丢掉了。崩溃时自动保存的回放**通常都是 true**，这不是错误。</summary>
        public bool IsTailTruncated => tailTruncated;

        /// <summary>尾部被丢掉的字节数（从那条残缺记录的起点算到文件尾）。<see cref="IsTailTruncated"/> 为 false 时是 0。</summary>
        public long TruncatedTailBytes => truncatedTailBytes;

        /// <summary>
        /// 读下一条 chunk。读到文件尾（或尾部残缺）返回 false。
        ///
        /// <para><b>承诺①：认不得的类型按 length 跳过。</b>
        /// 遇到当前版本不认识的 <c>type</c> 时，靠 <c>length</c> 直接跳过整条记录继续往下读，
        /// **不报错、不中断**。这是格式对未来的核心承诺：将来加新记录类型（埋点引用、相机轨迹）
        /// 不需要升格式版本——新版编辑器能读旧回放，旧编辑器也能读新回放里它认识的那部分。
        /// 正因如此，<c>length</c> 必须是**每一条**记录都带的（哪怕定长类型也带），
        /// 否则跳过就无从谈起。</para>
        ///
        /// <para><b>承诺②：尾部残缺尽力读。</b>
        /// 剩余字节凑不齐一个 chunk 头、或者凑不齐 <c>length</c> 声明的 payload 时，
        /// 把这一条连同后面的字节整个丢掉，**返回 false 而不是抛异常**，同时记一条 Warn
        /// 说明丢了多少字节（见 <see cref="IsTailTruncated"/> / <see cref="TruncatedTailBytes"/>）。
        /// 理由：回放文件最常见的产生方式就是崩溃时自动保存，那种文件的尾部十有八九是不完整的
        /// （进程死在写一半的时候）。见到残缺尾部就判整份作废，等于**恰恰是最该能读的那批文件读不了**。</para>
        /// </summary>
        /// <param name="chunk">读到的记录。它的 payload 指向内部复用缓冲，下一次调用就会被覆盖。</param>
        /// <returns>读到一条完整且类型认得的记录返回 true。</returns>
        /// <exception cref="ObjectDisposedException">读取器已释放。</exception>
        public bool ReadNext(out ReplayChunk chunk)
        {
            chunk = default;
            if (stream == null)
            {
                throw new ObjectDisposedException(nameof(ReplayReader), $"读取器已经释放：{filePath}");
            }

            while (true)
            {
                if (finished)
                {
                    return false;
                }

                long remaining = fileLength - stream.Position;
                if (remaining <= 0)
                {
                    // 干干净净读到文件尾：最理想的情况。
                    finished = true;
                    return false;
                }

                if (remaining < ReplayFormat.ChunkHeaderSize)
                {
                    // 连 chunk 头都凑不齐——承诺②。
                    MarkTailTruncated(remaining);
                    return false;
                }

                if (ReadExact(payloadScratch, 0, ReplayFormat.ChunkHeaderSize) != ReplayFormat.ChunkHeaderSize)
                {
                    // 文件在读的过程中被截短了（还在被写的现场会这样）。同样按截断处理，不抛。
                    MarkTailTruncated(remaining);
                    return false;
                }

                framing.LoadFrom(payloadScratch, 0, ReplayFormat.ChunkHeaderSize);
                byte rawType = framing.ReadByte();
                uint tick = framing.ReadUInt();
                int payloadLength = framing.ReadUShort();

                long payloadAvailable = fileLength - stream.Position;
                if (payloadAvailable < payloadLength)
                {
                    // payload 写了一半就断了——承诺②。丢掉的字节要把已经读掉的 chunk 头也算进去。
                    MarkTailTruncated(ReplayFormat.ChunkHeaderSize + payloadAvailable);
                    return false;
                }

                if (!ReplayFormat.IsKnownChunkType(rawType))
                {
                    // 承诺①：按 length 跳过，连 payload 都不读进来。
                    if (payloadLength > 0)
                    {
                        stream.Seek(payloadLength, SeekOrigin.Current);
                    }

                    skippedUnknownChunkCount++;
                    if (!unknownTypeLogged)
                    {
                        // 只报第一条：一份回放里这种记录可能成千上万，逐条打日志会把控制台淹了。
                        unknownTypeLogged = true;
                        Log.Info(
                            $"回放里有当前版本不认识的 chunk 类型 {rawType}（首次出现在 tick {tick}），"
                            + $"按长度跳过继续读，不影响其它记录：{filePath}");
                    }

                    continue;
                }

                if (payloadLength > 0
                    && ReadExact(payloadScratch, 0, payloadLength) != payloadLength)
                {
                    MarkTailTruncated(ReplayFormat.ChunkHeaderSize + payloadAvailable);
                    return false;
                }

                chunkCount++;
                chunk = new ReplayChunk(rawType, tick, payloadLength > 0 ? payloadScratch : null, 0, payloadLength);
                return true;
            }
        }

        /// <summary>
        /// 回到第一条 chunk 重新遍历（计数、截断状态一并归零），并重新取一次文件长度——
        /// 文件还在被写的话，这一趟能多读到上一趟之后追加的内容。
        /// 给「先扫一遍建索引、再放一遍」这类两趟流程用。
        /// </summary>
        /// <exception cref="ObjectDisposedException">读取器已释放。</exception>
        public void SeekToFirstChunk()
        {
            if (stream == null)
            {
                throw new ObjectDisposedException(nameof(ReplayReader), $"读取器已经释放：{filePath}");
            }

            stream.Seek(firstChunkOffset, SeekOrigin.Begin);
            fileLength = stream.Length;
            finished = false;
            tailTruncated = false;
            truncatedTailBytes = 0;
            chunkCount = 0;
            skippedUnknownChunkCount = 0;
            unknownTypeLogged = false;
        }

        /// <summary>释放文件句柄。头部与已读出的计数仍然可以看。</summary>
        public void Dispose()
        {
            if (stream == null)
            {
                return;
            }

            stream.Dispose();
            stream = null;
        }

        /// <summary>
        /// 解析头部。返回 null 表示成功，否则返回一句**给人看的**错误说明。
        /// <para>
        /// 判断顺序是有讲究的：**先认魔数、再看版本、最后才谈截断**。
        /// 一份由更新版本录的、又刚好被截断的文件，「请更新编辑器」比「文件头不完整」有用得多。
        /// </para>
        /// <para>
        /// 头部残缺一律判整份不可读，和 chunk 的「尽力读」相反——这不是双标：
        /// 没有种子、没有起始快照、没有步长，后面的输入一条都没法重放，
        /// 硬撑着打开只会让人对着一个根本没正确加载的回放查半天 bug。
        /// </para>
        /// </summary>
        private string ParseHeader()
        {
            fileLength = stream.Length;

            if (fileLength < ReplayFormat.MagicSize)
            {
                return $"这不是回放文件：整个文件只有 {fileLength} 字节，连 {ReplayFormat.MagicSize} 字节的魔数都放不下：{filePath}";
            }

            int prefixAvailable = (int)Math.Min(fileLength, ReplayFormat.HeaderPrefixSize);
            if (ReadExact(payloadScratch, 0, prefixAvailable) != prefixAvailable)
            {
                return $"读文件头时读不到预期的字节数（文件正在被改动？）：{filePath}";
            }

            framing.LoadFrom(payloadScratch, 0, prefixAvailable);

            uint magic = framing.ReadUInt();
            if (magic != ReplayFormat.Magic)
            {
                return $"这不是回放文件：开头 4 字节是 {DescribeMagic(magic)}，期望 \"{ReplayFormat.MagicAscii}\"：{filePath}";
            }

            const int afterVersion = ReplayFormat.MagicSize + ReplayFormat.FormatVersionSize;
            if (prefixAvailable < afterVersion)
            {
                return DescribeTruncatedHeader("格式版本号", prefixAvailable);
            }

            ushort formatVersion = framing.ReadUShort();
            if (formatVersion > ReplayFormat.CurrentFormatVersion)
            {
                return $"这份回放由更新版本录制（文件 v{formatVersion} / 当前支持 v{ReplayFormat.CurrentFormatVersion}），"
                    + $"请更新编辑器：{filePath}";
            }

            if (formatVersion < ReplayFormat.MinimumReadableFormatVersion)
            {
                return $"这份回放是 v{formatVersion} 格式，当前版本只能读 v{ReplayFormat.MinimumReadableFormatVersion} "
                    + $"及以上（当前写出的是 v{ReplayFormat.CurrentFormatVersion}）：v{formatVersion} 的字段布局和现在不一样，"
                    + $"硬读只会读出一堆看起来合理的垃圾值：{filePath}";
            }

            if (prefixAvailable < ReplayFormat.HeaderPrefixSize)
            {
                return DescribeTruncatedHeader(DescribeHeaderPrefixField(prefixAvailable), prefixAvailable);
            }

            byte platformCode = framing.ReadByte();
            long unixUtcSeconds = framing.ReadLong();
            int buildVersionByteCount = framing.ReadUShort();

            long remaining = fileLength - stream.Position;
            if (remaining < buildVersionByteCount)
            {
                return $"文件头不完整：构建版本号声明有 {buildVersionByteCount} 字节，文件里却只剩 {remaining} 字节"
                    + $"（整个文件 {fileLength} 字节）：{filePath}";
            }

            string buildVersion = string.Empty;
            if (buildVersionByteCount > 0)
            {
                if (ReadExact(payloadScratch, 0, buildVersionByteCount) != buildVersionByteCount)
                {
                    return $"读构建版本号时读不到预期的字节数（文件正在被改动？）：{filePath}";
                }

                buildVersion = Encoding.UTF8.GetString(payloadScratch, 0, buildVersionByteCount);
            }

            remaining = fileLength - stream.Position;
            int suffixAvailable = (int)Math.Min(remaining, ReplayFormat.HeaderSuffixSize);
            if (suffixAvailable > 0 && ReadExact(payloadScratch, 0, suffixAvailable) != suffixAvailable)
            {
                return $"读文件头时读不到预期的字节数（文件正在被改动？）：{filePath}";
            }

            if (suffixAvailable < ReplayFormat.HeaderSuffixSize)
            {
                return DescribeTruncatedHeader(DescribeHeaderSuffixField(suffixAvailable), fileLength);
            }

            framing.LoadFrom(payloadScratch, 0, suffixAvailable);
            ulong seed = framing.ReadULong();
            ulong configHash = framing.ReadULong();
            float fixedDeltaTime = framing.ReadFloat();
            uint startTick = framing.ReadUInt();
            uint snapshotLength = framing.ReadUInt();

            if (float.IsNaN(fixedDeltaTime) || float.IsInfinity(fixedDeltaTime) || fixedDeltaTime <= 0f)
            {
                return $"文件头损坏：里面的固定步长是 {fixedDeltaTime}，不是一个合法的步长（必须是大于 0 的有限值）。"
                    + $"这份回放没法重放：{filePath}";
            }

            remaining = fileLength - stream.Position;
            if (snapshotLength > int.MaxValue || remaining < snapshotLength)
            {
                return $"文件头不完整：起始快照声明有 {snapshotLength} 字节，文件里却只剩 {remaining} 字节"
                    + $"（整个文件 {fileLength} 字节）。多半是录制刚开始、快照还没写完就崩了；"
                    + $"没有起始快照就没有重放的起点，这份文件救不回来：{filePath}";
            }

            byte[] snapshot = null;
            if (snapshotLength > 0)
            {
                // 起始快照按实际大小单独分配一份留着（它要在整个回放期间有效，
                // 不能借用会被 chunk 覆盖的那块复用缓冲）。一份文件只分配这一次。
                snapshot = new byte[snapshotLength];
                if (ReadExact(snapshot, 0, (int)snapshotLength) != (int)snapshotLength)
                {
                    return $"读起始快照时读不到预期的字节数（文件正在被改动？）：{filePath}";
                }
            }

            header = new ReplayHeader(
                formatVersion,
                (PlatformKind)platformCode,
                unixUtcSeconds,
                buildVersion,
                seed,
                configHash,
                fixedDeltaTime,
                startTick,
                snapshot,
                0,
                (int)snapshotLength);

            firstChunkOffset = stream.Position;
            return null;
        }

        /// <summary>记下「尾部有残缺记录被丢掉」并打一条 Warn。**不抛异常**，理由见 <see cref="ReadNext"/> 的承诺②。</summary>
        private void MarkTailTruncated(long incompleteBytes)
        {
            finished = true;
            tailTruncated = true;
            truncatedTailBytes = incompleteBytes;

            Log.Warn(
                $"回放文件尾部有 {incompleteBytes} 字节不完整，已忽略：{filePath}。"
                + $"前面 {chunkCount} 条完整记录已正常读出"
                + (skippedUnknownChunkCount > 0 ? $"（另有 {skippedUnknownChunkCount} 条未知类型被跳过）" : string.Empty)
                + "。崩溃时自动保存的回放通常就长这样，这不是错误。");
        }

        /// <summary>
        /// 从流里读满 <paramref name="count"/> 个字节，返回实际读到的数量。
        /// <para>
        /// 一次 <c>Read</c> 不保证读满是 Stream 的通用约定（不是理论问题：读一个正在被写的文件就会碰上），
        /// 所以这里循环补齐；补不齐就如实返回少读的数量，交给调用方按截断处理，而不是抛异常。
        /// </para>
        /// </summary>
        private int ReadExact(byte[] target, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(target, offset + total, count - total);
                if (read <= 0)
                {
                    break;
                }

                total += read;
            }

            return total;
        }

        /// <summary>头部截断时那句话。<paramref name="field"/> 是断在哪个字段上。</summary>
        private string DescribeTruncatedHeader(string field, long availableBytes)
        {
            return $"文件头不完整，只有 {availableBytes} 字节（读到「{field}」时就没了；"
                + $"头部固定部分至少需要 {ReplayFormat.HeaderFixedSize} 字节，另外还有构建版本号与起始快照两段变长内容）："
                + filePath;
        }

        /// <summary>按已读到的字节数推断头部前段断在哪个字段上。</summary>
        private static string DescribeHeaderPrefixField(int available)
        {
            const int afterMagic = ReplayFormat.MagicSize;
            const int afterVersion = afterMagic + ReplayFormat.FormatVersionSize;
            const int afterPlatform = afterVersion + ReplayFormat.PlatformSize;
            const int afterTimestamp = afterPlatform + ReplayFormat.TimestampSize;

            if (available < afterMagic)
            {
                return "魔数";
            }

            if (available < afterVersion)
            {
                return "格式版本号";
            }

            if (available < afterPlatform)
            {
                return "平台标记";
            }

            if (available < afterTimestamp)
            {
                return "录制时间";
            }

            return "构建版本号的长度前缀";
        }

        /// <summary>按已读到的字节数推断头部后段断在哪个字段上。</summary>
        private static string DescribeHeaderSuffixField(int available)
        {
            const int afterSeed = ReplayFormat.SeedSize;
            const int afterConfigHash = afterSeed + ReplayFormat.ConfigHashSize;
            const int afterFixedDeltaTime = afterConfigHash + ReplayFormat.FixedDeltaTimeSize;
            const int afterStartTick = afterFixedDeltaTime + ReplayFormat.StartTickSize;

            if (available < afterSeed)
            {
                return "随机种子";
            }

            if (available < afterConfigHash)
            {
                return "配置指纹";
            }

            if (available < afterFixedDeltaTime)
            {
                return "固定步长";
            }

            if (available < afterStartTick)
            {
                return "起始 tick";
            }

            return "起始快照的长度前缀";
        }

        /// <summary>把读到的 4 个魔数字节渲染成人能看的样子：可打印字符原样显示，其它写成点，后面附十六进制。</summary>
        private static string DescribeMagic(uint value)
        {
            StringBuilder ascii = new StringBuilder(ReplayFormat.MagicSize);
            StringBuilder hex = new StringBuilder(ReplayFormat.MagicSize * 3);

            for (int i = 0; i < ReplayFormat.MagicSize; i++)
            {
                byte b = (byte)(value >> (i * 8));
                ascii.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');

                if (i > 0)
                {
                    hex.Append(' ');
                }

                hex.Append(b.ToString("X2"));
            }

            return $"\"{ascii}\"（{hex}）";
        }
    }
}
