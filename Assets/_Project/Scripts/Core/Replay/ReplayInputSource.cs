// 职责：把「从回放文件里读出来的那串输入命令」变成推进器认得的 IInputSource——
//   按 tick 逐条取，取到的那条必须和推进器正要推的 tick 严格对上，对不上就报出来。
//   它只管「这一 tick 该给哪条命令」，文件怎么读是 ReplayReader 的事，推几格是 ReplayPlayer 的事。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：工程内两个 IInputSource 实现都给不出「录好的那一条」。LiveInputSource 读的是
//      此刻的设备状态，InputSourceSwitch 只是个转发壳（它自己没有任何命令来源，切过去的那个源
//      必须另有其人）。ReplayReader 也顶不上：它是顺序读字节的，交出来的是 ReplayChunk 而不是
//      InputCommand，而且 payload 指向会被下一条覆盖的复用缓冲。
//   2. 扩展不行：不能把「按 tick 取命令」加进 InputSourceSwitch。那个类的全部价值在于
//      「上游不知道当前是实时还是回放」，让它自己长出回放数据，等于把它要隔离的东西塞回它体内；
//      也不能加进 ReplayReader——顺序读一遍和按 tick 反复取是两种访问模式，合在一个类里，
//      「游标现在在哪」就有两套语义，错位一格就是从这种地方来的。

using System;
using Game.Core.Logging;
using Game.Core.Simulation;

namespace Game.Core.Replay
{
    /// <summary>
    /// 回放输入源：按 tick 喂出录像里那一条命令。由 <see cref="ReplayPlayer"/> 在载入时灌满，
    /// 再交给 <see cref="InputSourceSwitch.SwitchToReplay"/>。
    /// <para>
    /// <b>对齐断言是本类存在的核心理由</b>：<see cref="Sample"/> 会检查取到的那条命令的 tick
    /// 与推进器请求的 tick 是否相等，不等就计数并报一次。输入整体错位一格是回放类 bug 里最典型、
    /// 也最难用肉眼发现的一种——画面照样在动，只是每个操作都晚一格生效，越滚越偏，
    /// 而这道检查几乎零成本（一次整数比较）。
    /// </para>
    /// <para>
    /// <b>缺了某个 tick 的输入不抛异常</b>：录制环回卷的边界、尾部截断的文件都可能缺条。
    /// 缺条时给 <see cref="InputCommand.Empty"/> 并记一次——中断整场回放的代价远大于
    /// 「这一 tick 当作没按键」，而计数器会让人知道这份录像缺了多少。
    /// </para>
    /// <para>
    /// <b>分配约定</b>：载入期（<see cref="Add"/>）按需扩容；<see cref="Sample"/> 与
    /// <see cref="Current"/> 这条每 tick 路径零堆分配（唯一的例外是两类异常各自第一次出现时打的那条日志）。
    /// </para>
    /// <para>线程约定：只在逻辑线程使用，内部不加锁。</para>
    /// </summary>
    public sealed class ReplayInputSource : IInputSource
    {
        /// <summary>默认初始容量（条）。按「几秒钟的录像」估的，不够会翻倍扩容。</summary>
        public const int DefaultCapacity = 1024;

        private uint[] ticks;
        private InputCommand[] commands;
        private int count;

        // 播放游标：指向下一条还没被取走的命令。录像本身按 tick 升序，播放也按 tick 升序，
        // 所以正常情况下每次 Sample 只前进一格，不需要查找。
        private int cursor;

        private InputCommand current;

        private long missingCount;
        private long firstMissingTick = -1L;
        private bool missingLogged;

        private long skippedCount;
        private long firstSkippedTick = -1L;
        private bool skippedLogged;

        /// <param name="capacity">初始容量（条），必须为正。按录像长度给足能省掉扩容。</param>
        /// <exception cref="ArgumentOutOfRangeException">容量不为正。</exception>
        public ReplayInputSource(int capacity = DefaultCapacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "容量必须为正");
            }

            ticks = new uint[capacity];
            commands = new InputCommand[capacity];
        }

        /// <summary>已装进来的命令条数。</summary>
        public int Count => count;

        /// <summary>第一条命令的 tick。一条都没有时返回 0。</summary>
        public uint FirstTick => count == 0 ? 0u : ticks[0];

        /// <summary>最后一条命令的 tick。一条都没有时返回 0。播放器用它判断「放完了没有」。</summary>
        public uint LastTick => count == 0 ? 0u : ticks[count - 1];

        /// <summary>还没被取走的命令条数。</summary>
        public int Remaining => count - cursor;

        /// <summary>当前 tick 的命令。<see cref="Sample"/> 还没被调过、或那一 tick 缺条时是空命令。</summary>
        public InputCommand Current => current;

        /// <summary>
        /// 录像里缺了输入的 tick 数（喂出去的是空命令）。不为 0 说明这份录像有断口——
        /// 常见于录制环回卷的边界与尾部截断的文件，本身不是错误，但会让那几 tick 的重放对不上。
        /// </summary>
        public long MissingCount => missingCount;

        /// <summary>第一个缺输入的 tick，没缺过是 -1。</summary>
        public long FirstMissingTick => firstMissingTick;

        /// <summary>
        /// 被跳过的命令条数：请求的 tick 已经越过了游标上这条，说明播放位置和录像对不上了。
        /// 正常的逐 tick 播放下恒为 0，不为 0 就是**对齐出了问题**，比缺条严重。
        /// </summary>
        public long SkippedCount => skippedCount;

        /// <summary>第一条被跳过的命令的 tick，没跳过是 -1。</summary>
        public long FirstSkippedTick => firstSkippedTick;

        /// <summary>
        /// 装一条命令。**必须按 tick 严格升序调用**（回放文件里的输入本来就是升序写出去的）。
        /// 只在载入期用，播放开始后不要再调。
        /// </summary>
        /// <param name="tick">这条命令属于哪个 tick。</param>
        /// <param name="command">命令本身。</param>
        /// <exception cref="ArgumentOutOfRangeException">tick 不比上一条大。</exception>
        public void Add(uint tick, InputCommand command)
        {
            if (count > 0 && tick <= ticks[count - 1])
            {
                // 在这里报，是因为乱序的输入表会让 Sample 的游标逻辑（只前进、不回头）默默取错命令，
                // 而那时的表现是「回放莫名其妙地不一样」，离真因已经很远了。
                throw new ArgumentOutOfRangeException(
                    nameof(tick),
                    tick,
                    $"输入必须按 tick 升序装入：上一条是 {ticks[count - 1]}，这一条是 {tick}");
            }

            if (count == ticks.Length)
            {
                Grow();
            }

            ticks[count] = tick;
            commands[count] = command;
            count++;
        }

        /// <summary>清空全部命令与统计。载入一份新回放前调。</summary>
        public void Clear()
        {
            count = 0;
            Rewind();
            missingCount = 0L;
            firstMissingTick = -1L;
            missingLogged = false;
            skippedCount = 0L;
            firstSkippedTick = -1L;
            skippedLogged = false;
        }

        /// <summary>游标回到第一条，命令表与统计不动。从头再放一遍时调。</summary>
        public void Rewind()
        {
            cursor = 0;
            current = InputCommand.Empty;
        }

        /// <summary>
        /// 取 <paramref name="tick"/> 这一 tick 的命令。由推进器在每次推进前调一次。
        /// <para>
        /// <b>对齐检查</b>：游标上那条的 tick 必须正好等于请求的 tick。小于（说明播放跑到了录像前面）
        /// 就跳过并计入 <see cref="SkippedCount"/>；大于或者已经取完（说明这一 tick 录像里没有）
        /// 就给空命令并计入 <see cref="MissingCount"/>。两类都只在**第一次**出现时打日志——
        /// 一旦错位，后面每一 tick 都会再撞一次，逐条打会把控制台淹了，而有用的信息只有「第一次在哪」。
        /// </para>
        /// </summary>
        /// <param name="tick">推进器正要推进的那个 tick 号。</param>
        public void Sample(long tick)
        {
            // 正常播放下这个循环一次都不转：游标本来就停在该取的那条上。
            while (cursor < count && ticks[cursor] < tick)
            {
                uint skipped = ticks[cursor];
                cursor++;
                skippedCount++;
                if (firstSkippedTick < 0L)
                {
                    firstSkippedTick = skipped;
                }
            }

            if (cursor < count && ticks[cursor] == tick)
            {
                current = commands[cursor];
                cursor++;
                ReportSkipped(tick);
                return;
            }

            current = InputCommand.Empty;
            missingCount++;
            if (firstMissingTick < 0L)
            {
                firstMissingTick = tick;
            }

            ReportSkipped(tick);
            ReportMissing(tick);
        }

        /// <summary>第一次跳条时报一次。跳条意味着播放位置和录像已经错开，比缺条严重。</summary>
        private void ReportSkipped(long tick)
        {
            if (skippedLogged || skippedCount == 0L)
            {
                return;
            }

            skippedLogged = true;
            Log.Warn(
                $"回放输入对不齐：推进器要 tick {tick}，录像里还停在 tick {firstSkippedTick}，"
                + "已跳过中间的命令继续放。这说明播放位置跑到了录像前面（多半是有人在 Driven 模式外"
                + "也推了推进器），从这里往后的重放结果都不可信。");
        }

        /// <summary>第一次缺条时报一次。</summary>
        private void ReportMissing(long tick)
        {
            if (missingLogged)
            {
                return;
            }

            missingLogged = true;
            Log.Warn(
                $"回放里没有 tick {tick} 的输入，这一 tick 按「没按键」处理，回放继续。"
                + "录制环回卷的边界与尾部截断的文件都会这样，本身不是错误；"
                + "但缺口这几 tick 的重放结果会和录制时不同。");
        }

        /// <summary>容量翻倍。只在载入期发生，播放路径上不会走到。</summary>
        private void Grow()
        {
            int capacity = ticks.Length * 2;
            uint[] grownTicks = new uint[capacity];
            InputCommand[] grownCommands = new InputCommand[capacity];
            Array.Copy(ticks, grownTicks, count);
            Array.Copy(commands, grownCommands, count);
            ticks = grownTicks;
            commands = grownCommands;
        }
    }
}
