// 职责：回放文件头的**内存表示**——一份录像的元信息（录在什么平台、什么时候、哪个构建、
//   什么种子、配置指纹、步长、起始 tick）外加起始完整快照那一段字节。
//   它只负责「装着这些值并保证它们自洽」，字节怎么摆是 ReplayFormat 定的，谁来读写是 Writer/Reader 的事。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：工程内唯一带「格式版本 + 元信息」的结构是 Core/Save 的 SaveEnvelope，
//      它是 JsonSaveService 的私有嵌套类、按 JSON 字段名序列化、面向人读与逐分区迁移；
//      这里要的是定长二进制头，而且多出 seed / configHash / fixedDeltaTime / startTick / 起始快照
//      这些存档根本没有的概念（它们的共同点只有「文件开头有个版本号」，共用不了一行代码）。
//   2. 扩展不行：不能把这些字段挂到 ReplayFormat 上。ReplayFormat 是纯常量与纯判断、没有实例，
//      而头部是每份文件一份的**数据**；把数据塞进静态类就等于全局单例，同时开两份回放（对比两次录制）
//      就会互相覆盖——而「同时开两份对比」恰恰是这个系统最常用的调试姿势。
//   3. 也不做成 class：它是一份只读的值，构造完就不该再变；readonly struct 顺带让
//      `in ReplayHeader` 传参不拷贝，也杜绝了「读出来之后被谁改了一个字段」这种问题。

using System;
using Game.Core.Platform;

namespace Game.Core.Replay
{
    /// <summary>
    /// 一份回放文件的头部。字节布局见 <see cref="ReplayFormat"/>。
    /// <para>
    /// <b>构造时就校验</b>：固定步长必须是大于 0 的有限值、快照区间必须落在数组内。
    /// 这些值一旦写进文件就没法补救（放录像时才炸，而那时人已经在查一个不存在的 bug 了），
    /// 所以在能报错的最早时刻报错。
    /// </para>
    /// <para>
    /// <b>分配约定</b>：头部一份文件只写一次、只读一次，不在任何高频路径上，
    /// 这里允许持有字符串与快照数组。
    /// </para>
    /// <para>
    /// <b>为什么传参不用 <c>in</c></b>：本结构与 <see cref="ReplayChunk"/> 都是只在
    /// 「一份文件一次」或「按值取出一条记录」的路径上传递，<c>in</c> 省下的那点拷贝可以忽略；
    /// 而带 <c>in</c> 的方法在 Unity MCP 的 <c>execute_code</c>（只有 C# 6 的 CodeDom 后端）里
    /// 根本调不起来——回放是调试子系统，从编辑器里临时敲一段代码验一下是它的日常用法，
    /// 不值得为一次结构体拷贝把这条路堵死。
    /// </para>
    /// </summary>
    public readonly struct ReplayHeader
    {
        private readonly byte[] snapshot;
        private readonly string buildVersion;

        /// <summary>
        /// 完整构造（含格式版本号）。**录制端不要直接用这个**，用 <see cref="Create"/>——
        /// 那个会自动填当前格式版本，省得有人手写一个数字然后忘了跟着升。
        /// 这个重载是给 <see cref="ReplayReader"/> 用的：它必须能原样装下文件里写着的那个版本号。
        /// </summary>
        /// <param name="formatVersion">文件里的格式版本号。</param>
        /// <param name="platform">录制平台。</param>
        /// <param name="unixUtcSeconds">录制时刻，Unix 纪元起的秒数（UTC，不是毫秒）。</param>
        /// <param name="buildVersion">构建版本号，null 按空串处理（缺个版本号不该让录制失败）。</param>
        /// <param name="seed">本局随机种子。</param>
        /// <param name="configHash">配置内容指纹，由调用方从配置服务取来。</param>
        /// <param name="fixedDeltaTime">逻辑固定步长（秒），必须是大于 0 的有限值。</param>
        /// <param name="startTick">起始快照对应的 tick。</param>
        /// <param name="snapshot">起始完整快照所在的数组，可为 null（此时长度必须为 0）。</param>
        /// <param name="snapshotOffset">快照在数组里的起始偏移。</param>
        /// <param name="snapshotLength">快照字节数。</param>
        /// <exception cref="ArgumentOutOfRangeException">步长不是正的有限值，或快照区间越界。</exception>
        public ReplayHeader(
            ushort formatVersion,
            PlatformKind platform,
            long unixUtcSeconds,
            string buildVersion,
            ulong seed,
            ulong configHash,
            float fixedDeltaTime,
            uint startTick,
            byte[] snapshot,
            int snapshotOffset,
            int snapshotLength)
        {
            if (float.IsNaN(fixedDeltaTime) || float.IsInfinity(fixedDeltaTime) || fixedDeltaTime <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fixedDeltaTime),
                    fixedDeltaTime,
                    "固定步长必须是大于 0 的有限值。写进文件的坏步长要等到放录像时才会暴露，那时排查成本高得多");
            }

            if (snapshotOffset < 0 || snapshotLength < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(snapshotLength), snapshotLength, "快照的偏移与长度都不能为负");
            }

            if (snapshot == null)
            {
                if (snapshotLength != 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(snapshotLength), snapshotLength, "快照数组是 null，长度就必须为 0");
                }
            }
            else if (snapshot.Length - snapshotOffset < snapshotLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(snapshotLength), snapshotLength, "快照区间超出了所给数组的范围");
            }

            FormatVersion = formatVersion;
            Platform = platform;
            UnixUtcSeconds = unixUtcSeconds;
            this.buildVersion = buildVersion ?? string.Empty;
            Seed = seed;
            ConfigHash = configHash;
            FixedDeltaTime = fixedDeltaTime;
            StartTick = startTick;
            this.snapshot = snapshot;
            SnapshotOffset = snapshot == null ? 0 : snapshotOffset;
            SnapshotLength = snapshotLength;
        }

        /// <summary>
        /// 按**当前**格式版本造一个头部。录制端走这条路，参数含义同完整构造。
        /// <para>
        /// 快照那三个参数可以直接传 <c>buffer.GetBuffer(), 0, buffer.Length</c>
        /// （<see cref="StateBuffer"/> 刚写完 <see cref="IReplayStateProvider.SerializeAll"/> 的样子），
        /// 不需要先拷一份出来——<see cref="ReplayWriter"/> 写完就不再持有它。
        /// </para>
        /// </summary>
        public static ReplayHeader Create(
            PlatformKind platform,
            long unixUtcSeconds,
            string buildVersion,
            ulong seed,
            ulong configHash,
            float fixedDeltaTime,
            uint startTick,
            byte[] snapshot,
            int snapshotOffset,
            int snapshotLength)
        {
            return new ReplayHeader(
                ReplayFormat.CurrentFormatVersion,
                platform,
                unixUtcSeconds,
                buildVersion,
                seed,
                configHash,
                fixedDeltaTime,
                startTick,
                snapshot,
                snapshotOffset,
                snapshotLength);
        }

        /// <summary>文件里写着的格式版本号。读回来的头部带的是**文件的**版本，不一定等于当前版本。</summary>
        public ushort FormatVersion { get; }

        /// <summary>
        /// 录制平台。读文件时按原样转成枚举：**不认得的平台码不会被拒**，
        /// 它只是元信息，不该让一份能放的回放因为「录它的那个平台当前代码还不认识」而打不开。
        /// </summary>
        public PlatformKind Platform { get; }

        /// <summary>录制时刻：Unix 纪元起的秒数，UTC。**秒不是毫秒**，写的时候别乘 1000。</summary>
        public long UnixUtcSeconds { get; }

        /// <summary>构建版本号（可能是空串）。用来回答「这份录像是哪个包录的」。</summary>
        public string BuildVersion => buildVersion ?? string.Empty;

        /// <summary>本局随机种子。重放时必须用它重建随机流，否则第一帧就分叉。</summary>
        public ulong Seed { get; }

        /// <summary>
        /// 配置内容指纹。放录像前先比一次：对不上就直接告诉人「配置版本不匹配，这份录像录于 X」，
        /// 而不是让人对着一串对不上的数据逐帧比对（那是回放系统里最耗时的一类误判）。
        /// </summary>
        public ulong ConfigHash { get; }

        /// <summary>逻辑固定步长（秒）。恒为大于 0 的有限值（构造时已校验）。</summary>
        public float FixedDeltaTime { get; }

        /// <summary>起始快照对应的 tick。第一条输入 chunk 通常就是这个 tick。</summary>
        public uint StartTick { get; }

        /// <summary>起始快照在 <see cref="GetSnapshotBuffer"/> 返回的数组里的起始偏移。</summary>
        public int SnapshotOffset { get; }

        /// <summary>起始快照的字节数，可能为 0（录制时世界还是空的）。</summary>
        public int SnapshotLength { get; }

        /// <summary>
        /// 录制时刻的 <see cref="DateTime"/>（UTC），给界面显示用。
        /// <para>
        /// 时间戳超出 <see cref="DateTime"/> 能表示的范围（文件坏了、或者根本不是回放文件但魔数碰巧对上）时
        /// 返回 <see cref="DateTime.MinValue"/> 而**不抛异常**：一个坏掉的时间戳不该让整份回放打不开，
        /// 显示成 0001-01-01 已经足够让人看出不对。
        /// </para>
        /// </summary>
        public DateTime RecordedUtc
        {
            get
            {
                const long minUnixSeconds = -62135596800L; // DateTime.MinValue
                const long maxUnixSeconds = 253402300799L; // DateTime.MaxValue（秒精度）
                if (UnixUtcSeconds < minUnixSeconds || UnixUtcSeconds > maxUnixSeconds)
                {
                    return DateTime.MinValue;
                }

                return DateTimeOffset.FromUnixTimeSeconds(UnixUtcSeconds).UtcDateTime;
            }
        }

        /// <summary>
        /// 取起始快照所在的数组，配合 <see cref="SnapshotOffset"/> 与 <see cref="SnapshotLength"/> 使用。
        /// 快照为空时返回 null。
        /// <para>
        /// 返回的是**活的数组**（录制端传进来的那一个，或读取时新分配的那一个），不要改写它的内容。
        /// 要把快照装进 <see cref="StateBuffer"/> 恢复世界，一行就够：
        /// <c>buffer.LoadFrom(header.GetSnapshotBuffer(), header.SnapshotOffset, header.SnapshotLength)</c>。
        /// </para>
        /// </summary>
        public byte[] GetSnapshotBuffer()
        {
            return snapshot;
        }

        /// <summary>一行摘要，给日志和回放列表用。</summary>
        public override string ToString()
        {
            return $"回放 v{FormatVersion} | {Platform} | {RecordedUtc:yyyy-MM-dd HH:mm:ss}Z | 构建 {BuildVersion} | "
                + $"种子 {Seed} | 配置指纹 {ConfigHash:X16} | 步长 {FixedDeltaTime} | 起始 tick {StartTick} | "
                + $"起始快照 {SnapshotLength} 字节";
        }
    }
}
