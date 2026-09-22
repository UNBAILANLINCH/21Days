// 职责：回放文件二进制格式的**唯一**定义处——魔数、格式版本、头部与 chunk 的字段宽度、chunk 类型枚举。
//   ReplayWriter / ReplayReader / ReplayHeader / ReplayChunk 全部从这里取数字。
//   任何一个偏移量或宽度在别处出现第二遍，就是「改了一处忘了另一处」的温床，
//   而这类 bug 的表现形式恰好是「老回放读出一堆看起来合理的垃圾值」——最难查的那一类。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：工程内唯一的「文件格式定义」是 Core/Save/JsonSaveService 的存档信封
//      （formatVersion + partitions）。它是 JSON：字段靠名字匹配、允许缺失、面向人读、逐分区迁移，
//      目标是「再老的存档也要能读进来」。这里要的正好相反——定长二进制、每 tick 追加一条、
//      字段布局一变就必须硬拒老回放（半读半错的回放比读不了的回放糟得多）。
//      两套相反的兼容策略共用一个信封，加字段时必然有一边写错。
//   2. 扩展不行：不能把这些常量塞进 StateBuffer 或 StateHasher。那两个的职责是
//      「一段字节怎么摆」「一段字节怎么折成一个数」，它们不认识文件、不认识 tick、不认识版本；
//      把文件格式常量挂上去，等于让「从网络收到的一段快照」也被迫带上文件头的概念。
//   3. 不建 ReplayChunkType.cs 单独放枚举：这个枚举的取值就是文件里那一个字节的含义，
//      和 ChunkHeaderSize / MaxChunkPayloadLength 是同一件事的不同侧面，拆开放两个文件
//      只会让「加一个新 chunk 类型」要改两处。所以嵌在本类里（ReplayFormat.ChunkType）。

namespace Game.Core.Replay
{
    /// <summary>
    /// 回放文件（<c>*.21dr</c>）的二进制格式定义。全部是常量与纯判断，不持有任何状态。
    ///
    /// <para><b>整体布局</b>（小端，所有多字节整数都是小端；头部一份，chunk 重复到文件尾）：</para>
    /// <code>
    /// Header  magic "21DR"(4) | formatVersion(u16) | platform(u8) | unixUtc(i64)
    ///         | buildVersionLength(u16) | buildVersion(UTF-8, 变长)
    ///         | seed(u64) | configHash(u64) | fixedDeltaTime(f32) | startTick(u32)
    ///         | snapshotLength(u32) | 起始完整快照(变长)
    /// Chunk   type(u8) | tick(u32) | length(u16) | payload(length 字节)      ← 重复到文件尾
    /// </code>
    /// <para>
    /// 头部固定部分共 <see cref="HeaderFixedSize"/> 字节（45），加上 buildVersion 与起始快照两段变长内容
    /// 才是实际头长；chunk 头恒为 <see cref="ChunkHeaderSize"/> 字节（7）。
    /// </para>
    ///
    /// <para><b>格式版本号（<see cref="CurrentFormatVersion"/>）什么时候要升、什么时候不用升</b>
    /// ——这是本文件最重要的一段注释，改格式前先读完：</para>
    /// <para>
    /// <b>必须升版本</b>（老读者按老布局解读新文件，会读出「看起来合理的垃圾」而不是报错，
    /// 所以只能靠版本号把它硬拒在门外）：
    /// ① 头部增删字段、改字段顺序、改字段宽度；
    /// ② chunk 头的结构变了（type / tick / length 的宽度或顺序）；
    /// ③ **已有** chunk type 的 payload 布局变了（比如 <see cref="ChunkType.Input"/> 的
    /// <c>InputCommand</c> 字节布局改了）；
    /// ④ <see cref="IReplayStateProvider"/> 的注册顺序变了——那是快照的字节布局本身变了
    /// （理由见 IReplayStateProvider 文件注释）。
    /// </para>
    /// <para>
    /// <b>不用升版本</b>：**新增一个 chunk type**（埋点引用、相机轨迹、音频标记……）。
    /// 这是本格式对未来的核心承诺：<see cref="ReplayReader"/> 遇到不认识的 type 一律按
    /// <c>length</c> 跳过，所以新版编辑器能读老回放、老版编辑器也能读新回放里它认识的那部分，
    /// 两边都不用改版本号、都不会报错。加记录类型时请走这条路，别去动头部。
    /// </para>
    ///
    /// <para><b>为什么不用第三方序列化库</b>：这个文件要长期可读——半年前录的回放必须还能放。
    /// 库一升版本，默认布局、默认压缩、默认类型标记都可能悄悄变，而变化通常不会报错，
    /// 只会让老文件读出错值。手写的定长布局没有这个风险：布局写在上面那段注释里，
    /// 改它就得改本文件，改本文件就得动版本号。</para>
    /// </summary>
    public static class ReplayFormat
    {
        /// <summary>魔数的可读写法，用于报错信息。</summary>
        public const string MagicAscii = "21DR";

        /// <summary>
        /// 魔数，按小端 u32 写出去正好是 ASCII 的 <c>21DR</c>（0x32 '2'、0x31 '1'、0x44 'D'、0x52 'R'）。
        /// <para>
        /// 存成 <c>uint</c> 而不是 <c>byte[]</c>：常量数组只能是 <c>static readonly</c>，
        /// 那是一个**可以被外部改写**的公开数组；而且比对时要写循环。存成整数则一次读写一次比较，零分配。
        /// </para>
        /// </summary>
        public const uint Magic = 0x52443132u;

        /// <summary>
        /// 当前写出去的格式版本。什么改动要升它、什么不用，见类文档那一段——**不要凭感觉改这个数字**。
        /// </summary>
        public const ushort CurrentFormatVersion = 3;

        /// <summary>
        /// 当前代码还能正确解读的最老格式版本。比它更老的文件会被明确拒掉（报「哪个版本」），
        /// 而不是硬按新布局读出垃圾值。
        /// <para>
        /// 现在两者相等（都是 1），所以「太旧」这条分支暂时走不到。它照样要写对：
        /// 等真的升到 v2 时，把这里留在 1 就意味着 v1 仍可读（需要 Reader 里补上按 v1 布局解析的分支），
        /// 抬到 2 就意味着 v1 回放一律拒收。到那一刻再做决定，但别让代码在那天才第一次长出这个概念。
        /// </para>
        /// </summary>
        public const ushort MinimumReadableFormatVersion = 3;

        /// <summary>
        /// 回放文件的推荐扩展名。**只是约定，不是格式的一部分**：
        /// <see cref="ReplayReader"/> 只认魔数，不看扩展名（崩溃现场留下的 <c>.tmp</c> 也要能直接打开）。
        /// </summary>
        public const string FileExtension = ".21dr";

        /// <summary>魔数字节数。</summary>
        public const int MagicSize = 4;

        /// <summary>格式版本号字节数（u16）。</summary>
        public const int FormatVersionSize = 2;

        /// <summary>平台标记字节数（u8，取值见 <see cref="Game.Core.Platform.PlatformKind"/>）。</summary>
        public const int PlatformSize = 1;

        /// <summary>录制时间字节数（i64，Unix 纪元起的**秒**数，UTC）。</summary>
        public const int TimestampSize = 8;

        /// <summary>变长字符串的长度前缀字节数（u16，所以字符串的 UTF-8 字节数上限是 65535）。</summary>
        public const int StringLengthPrefixSize = 2;

        /// <summary>随机种子字节数（u64）。</summary>
        public const int SeedSize = 8;

        /// <summary>配置指纹字节数（u64，来自配置服务的 ContentHash，本层不关心它怎么算出来）。</summary>
        public const int ConfigHashSize = 8;

        /// <summary>固定步长字节数（f32）。</summary>
        public const int FixedDeltaTimeSize = 4;

        /// <summary>起始 tick 字节数（u32）。</summary>
        public const int StartTickSize = 4;

        /// <summary>
        /// 变长字节块的长度前缀字节数（u32）。**只用于头部里的起始完整快照**——
        /// 它一份文件只出现一次，可以很大；chunk 的 payload 走 <see cref="ChunkLengthSize"/>（u16）。
        /// 两者宽度不同是有意的：chunk 头每条记录都要占空间，多两个字节乘以几万条就是几十 KB。
        /// </summary>
        public const int BlobLengthPrefixSize = 4;

        /// <summary>
        /// 头部里 buildVersion 之前那一段的字节数：magic + formatVersion + platform + unixUtc + 字符串长度前缀。
        /// Writer 按这一段凑够一次性写出，Reader 按这一段一次性读进来。
        /// </summary>
        public const int HeaderPrefixSize =
            MagicSize + FormatVersionSize + PlatformSize + TimestampSize + StringLengthPrefixSize;

        /// <summary>
        /// 头部里 buildVersion 之后那一段的字节数：seed + configHash + fixedDeltaTime + startTick + 快照长度前缀。
        /// </summary>
        public const int HeaderSuffixSize =
            SeedSize + ConfigHashSize + FixedDeltaTimeSize + StartTickSize + BlobLengthPrefixSize;

        /// <summary>
        /// 头部固定部分的总字节数（45）。**这不是头部总长**——还要加上 buildVersion 的 UTF-8 字节数
        /// 与起始快照的字节数。一份 buildVersion 为空、快照为空的文件，头部恰好就是这么长。
        /// </summary>
        public const int HeaderFixedSize = HeaderPrefixSize + HeaderSuffixSize;

        /// <summary>chunk 类型标记字节数（u8）。</summary>
        public const int ChunkTypeSize = 1;

        /// <summary>chunk 所属 tick 字节数（u32）。</summary>
        public const int ChunkTickSize = 4;

        /// <summary>chunk payload 长度前缀字节数（u16）。</summary>
        public const int ChunkLengthSize = 2;

        /// <summary>chunk 头的总字节数（7）：type + tick + length。跳过一个不认识的 chunk = 跳过 7 + length 字节。</summary>
        public const int ChunkHeaderSize = ChunkTypeSize + ChunkTickSize + ChunkLengthSize;

        /// <summary>
        /// 单个 chunk 的 payload 上限（65535 字节），由 <see cref="ChunkLengthSize"/> 是 u16 决定。
        /// <para>
        /// 这对输入命令（31 字节）、状态哈希（8 字节）、QA 打点（一行字）都绰绰有余；
        /// 唯一可能顶到上限的是周期性完整快照。真顶到了，<see cref="ReplayWriter"/> 会**明确报错**
        /// 而不是截断——静默截断出来的快照恢复后是个错误的世界，比录不上糟得多。
        /// 那时的正解是升格式版本把这个前缀改宽，或者把快照拆成多条自定义 chunk。
        /// </para>
        /// </summary>
        public const int MaxChunkPayloadLength = ushort.MaxValue;

        /// <summary>buildVersion 的 UTF-8 字节数上限（65535），由 <see cref="StringLengthPrefixSize"/> 是 u16 决定。</summary>
        public const int MaxBuildVersionByteCount = ushort.MaxValue;

        /// <summary>
        /// chunk 的记录类型。**取值一旦定下就不许改**（老回放里已经写着这些数字了），
        /// 新类型一律往后加——加新类型不需要升 <see cref="CurrentFormatVersion"/>，
        /// 老版本的 <see cref="ReplayReader"/> 会按 length 跳过它不认识的那些。
        /// </summary>
        public enum ChunkType : byte
        {
            /// <summary>
            /// 输入命令。payload 是一条 <see cref="Game.Core.Simulation.InputCommand"/>
            /// （定长 31 字节，布局由 InputCommand 自己定义）。每 tick 一条，回放文件里数量最多的就是它。
            /// </summary>
            Input = 0,

            /// <summary>
            /// 状态哈希。payload 是一个小端 u64（<see cref="StateHasher"/> 算出来的 FNV-1a 64）。
            /// 重放时同 tick 重算一遍对比，不等即判定漂移。
            /// </summary>
            StateHash = 1,

            /// <summary>
            /// 完整快照。payload 是 <see cref="IReplayStateProvider.SerializeAll"/> 写出的那一段字节，
            /// 用来跳转到中途某个 tick 而不用从头重放。受 <see cref="MaxChunkPayloadLength"/> 限制。
            /// </summary>
            Snapshot = 2,

            /// <summary>
            /// QA 打点。payload 是一段 UTF-8 文本（可以为空），内容是打点时的说明
            /// （「就是这一帧卡住的」）。由人按热键产生，数量很少。
            /// </summary>
            QaMarker = 3,
        }

        /// <summary>
        /// 当前版本认不认得这个 chunk 类型。认不得的由 <see cref="ReplayReader"/> 按 length 跳过，
        /// **不报错**——这是格式对未来的承诺，见类文档。
        /// <para>
        /// 故意写成逐个 case 而不是 <c>rawType &lt;= 3</c>：将来类型号出现空洞（某个类型废弃了）时，
        /// 范围判断会把废弃号当成「认得」，然后拿错误的 payload 布局去解读。
        /// </para>
        /// </summary>
        /// <param name="rawType">文件里那一个字节的原值。</param>
        public static bool IsKnownChunkType(byte rawType)
        {
            switch (rawType)
            {
                case (byte)ChunkType.Input:
                case (byte)ChunkType.StateHash:
                case (byte)ChunkType.Snapshot:
                case (byte)ChunkType.QaMarker:
                    return true;
                default:
                    return false;
            }
        }
    }
}
