// 职责：编辑器下把埋点行原样再写一份到 <工程根>/Logs/telemetry/<sid>.log，一段会话一个文件，
//   并在开文件时按修改时间清掉更老的会话文件。真机不参与（整份 #if UNITY_EDITOR）。
// 为什么新建：
//   1. 复用不行——现成的 UnityDebugTelemetrySink 只做「把行交给 UnityEngine.Debug」，它连文件都不碰；
//      TelemetryService 里那段 IO 写的是指针文件（一句「日志在哪」，四行，写完就关），没有流、没有缓冲、没有生命周期。
//   2. 扩展不行——把 FileStream、缓冲与滚动清理塞进 UnityDebugTelemetrySink，真机也得背上一套永远不执行的
//      #if 分支和字段，而那个类的职责名（交给 Debug）也说不通。
//   3. 所以新建一个平级的 ITelemetrySink 实现：输出终点多一个，格式化仍然只有一次（见 TelemetryService.Write）。

#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using Game.Core.Logging;
using UnityEngine;

namespace Game.Core.Telemetry
{
    /// <summary>
    /// 编辑器埋点镜像。把 <see cref="TelemetryService"/> **已经格式化好的那一行**原样再写一份到
    /// <c>Logs/telemetry/&lt;sid&gt;.log</c>（<c>Logs/</c> 已 gitignore），一段会话一个文件。
    /// <para>
    /// **为什么编辑器下要多存一份**：<c>Editor.log</c> 的路径是本机全局的，不是本工程独占——
    /// 本机同时开着另一个 Unity 时，它的每帧日志会把本工程刚写的埋点挤出尾部（实测三次：写完 30 秒，
    /// 尾部 256 MB 里一条不剩；20 分钟后整份文件被换掉，全量扫也是 0 条）。真机没有这个问题
    /// （<c>Player.log</c> / logcat 是进程独占的），也不该往玩家设备上多写文件，
    /// 所以整个类用 <c>#if UNITY_EDITOR</c> 包住，打出去的包里一个字节都不写。
    /// </para>
    /// <para>
    /// **写入策略与它的代价**（docs/telemetry.md 没规定，这里定死并写明代价）：不每条刷盘，
    /// 攒够 <see cref="FlushEveryLines"/> 条、或距上次刷盘超过 <see cref="FlushIntervalMs"/> 毫秒才 flush；
    /// E 级立刻 flush（要查的就是它）；<c>session_end</c> / <c>Application.quitting</c> / <see cref="Dispose"/> 各兜一次底。
    /// 代价是：**进程被强杀或崩掉时，镜像可能缺最后几条**，而 Unity 自己那份由 Unity 负责 flush，通常更完整。
    /// 所以两份日志的定位不一样——**镜像干净但可能缺尾，Unity log 吵但完整**，查崩溃现场时两份对照着看。
    /// </para>
    /// <para>
    /// 写失败（目录只读、磁盘满、文件被别的进程占着）只记一条 Warn 然后停写，绝不往外抛：
    /// 它是便利设施不是业务，不能因为它让游戏挂掉。停写之后埋点照常进 Unity 日志，只是少了镜像这一份。
    /// </para>
    /// <para>
    /// 这个类只管 IO，**不认识「现在是不是在 Play」**：那道闸在唯一的正式构造点
    /// <see cref="TelemetryService.InitializeAsync"/>（EditMode 测试也会跑 InitializeAsync，
    /// 不挡住的话每跑一次测试就多一个垃圾文件）。闸放在那边，这个类才能被 EditMode 测试拿临时目录直接跑。
    /// </para>
    /// <para>线程约定同服务：只在主线程用，没有加锁。</para>
    /// </summary>
    public sealed class EditorMirrorTelemetrySink : ITelemetrySink, IDisposable
    {
        /// <summary>镜像目录名，挂在工程根的 <c>Logs/</c> 下面。</summary>
        public const string DirectoryName = "telemetry";

        /// <summary>会话文件的扩展名。清理时只认它，别的文件一律不碰。</summary>
        public const string FileExtension = ".log";

        /// <summary>攒够这么多条就刷一次盘。</summary>
        private const int FlushEveryLines = 32;

        /// <summary>距上次刷盘超过这么多毫秒就刷一次，免得低频会话的内容一直躺在缓冲里。</summary>
        private const int FlushIntervalMs = 1000;

        /// <summary>文件流缓冲字节数。一行埋点百来字节，4 KB 够攒几十条。</summary>
        private const int StreamBufferBytes = 4096;

        /// <summary>UTF-8 无 BOM。带 BOM 的话文件头会多三个字节，「与 Unity log 逐字节相同」就先输在开头。</summary>
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly string directory;
        private readonly int keepSessions;

        private StreamWriter writer;
        private int pendingLines;
        private int lastFlushTick;
        private bool quittingHooked;
        private bool disposed;

        /// <param name="directory">镜像目录（一般是 <see cref="DefaultDirectory"/>，测试传临时目录）。</param>
        /// <param name="sessionId">本次会话 id，文件名就是它。</param>
        /// <param name="keepSessions">目录里保留最近几个会话文件，小于等于 0 表示不清理。</param>
        public EditorMirrorTelemetrySink(string directory, string sessionId, int keepSessions)
        {
            if (string.IsNullOrEmpty(directory))
            {
                throw new ArgumentNullException(nameof(directory));
            }

            if (string.IsNullOrEmpty(sessionId))
            {
                throw new ArgumentNullException(nameof(sessionId));
            }

            this.directory = directory;
            this.keepSessions = keepSessions;
            FilePath = Path.Combine(directory, sessionId + FileExtension);

            // 编辑器真正退出时兜一次底：那条路不走容器销毁，缓冲里的最后几条否则就没了
            Application.quitting += OnApplicationQuitting;
            quittingHooked = true;
        }

        /// <summary>
        /// 默认镜像目录 <c>&lt;工程根&gt;/Logs/telemetry</c>。
        /// <see cref="Application.dataPath"/> 是 <c>&lt;工程根&gt;/Assets</c>，往上一层才是工程根。
        /// </summary>
        public static string DefaultDirectory
        {
            get
            {
                DirectoryInfo root = Directory.GetParent(Application.dataPath);
                string projectRoot = root == null ? Application.dataPath : root.FullName;
                return Path.Combine(projectRoot, "Logs", DirectoryName);
            }
        }

        /// <summary>本次会话镜像文件的绝对路径。指针文件在编辑器下指向它（docs/telemetry.md 约束 3）。</summary>
        public string FilePath { get; }

        /// <summary>还在正常写吗。出过一次写失败就永久为 false，指针文件那边靠它决定要不要指过来。</summary>
        public bool Available { get; private set; } = true;

        /// <summary>
        /// 写一行。收到的是服务格式化好的**同一个字符串实例**，这里一个字符都不加工——
        /// 加工了就等于契约有第二份实现（docs/telemetry.md 约束 2）。
        /// 换行符固定用 <c>\n</c>，不用 <c>WriteLine</c>：那个在 Windows 上会写 <c>\r\n</c>。
        /// </summary>
        public void Write(TelemetryLevel level, string line)
        {
            if (disposed || !Available || line == null)
            {
                return;
            }

            if (!EnsureOpen())
            {
                return;
            }

            try
            {
                writer.Write(line);
                writer.Write('\n');
                pendingLines++;

                // E 级立刻落盘：镜像唯一的软肋就是缺尾，而缺掉的那几条里最值钱的就是错误行
                if (level == TelemetryLevel.Error)
                {
                    Flush();
                    return;
                }

                int now = Environment.TickCount;
                if (pendingLines >= FlushEveryLines || unchecked(now - lastFlushTick) >= FlushIntervalMs)
                {
                    Flush();
                }
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        /// <summary>把缓冲里的内容刷到磁盘。没开过文件时什么都不做。</summary>
        public void Flush()
        {
            if (writer == null)
            {
                return;
            }

            try
            {
                writer.Flush();
                pendingLines = 0;
                lastFlushTick = Environment.TickCount;
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        /// <summary>刷盘并关掉文件。由 <see cref="TelemetryService.Dispose"/> 在写完 session_end 之后调；重复调无副作用。</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            UnhookQuitting();
            Flush();
            CloseQuietly();
        }

        /// <summary>
        /// 把目录里的会话文件按修改时间保留最近 <paramref name="keep"/> 个，更老的删掉。
        /// <para>
        /// 只认本目录顶层、扩展名是 <c>.log</c> 的文件：<c>Logs/</c> 下还有 <c>verify/</c>、<c>build-*.log</c>
        /// 这些别人的东西，递归或放宽判据就会误删。扩展名不用 <c>GetFiles("*.log")</c> 过滤——
        /// Windows 上那个通配符会连带命中 <c>.log2</c> 这类更长的扩展名（8.3 短名的历史包袱）。
        /// </para>
        /// <para>失败只记 Warn：清不掉最多是磁盘上多几个旧文件，不值得为它中断启动。</para>
        /// </summary>
        public static void CleanupOldSessions(string directory, int keep)
        {
            if (keep <= 0 || string.IsNullOrEmpty(directory))
            {
                return;
            }

            try
            {
                DirectoryInfo info = new DirectoryInfo(directory);
                if (!info.Exists)
                {
                    return;
                }

                FileInfo[] all = info.GetFiles("*", SearchOption.TopDirectoryOnly);
                int count = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    if (string.Equals(all[i].Extension, FileExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        all[count++] = all[i];
                    }
                }

                if (count <= keep)
                {
                    return;
                }

                FileInfo[] logs = new FileInfo[count];
                Array.Copy(all, logs, count);
                Array.Sort(logs, CompareByWriteTimeDescending);

                for (int i = keep; i < logs.Length; i++)
                {
                    try
                    {
                        logs[i].Delete();
                    }
                    catch (Exception e)
                    {
                        Log.Warn($"埋点镜像的旧会话文件删不掉（{logs[i].Name}）：{e.Message}。不影响本次埋点。");
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn($"埋点镜像目录清理失败：{e.Message}。不影响本次埋点，只是旧文件会一直留着。");
            }
        }

        /// <summary>新的排前面。同一毫秒写的两个文件谁先谁后无所谓，只影响删谁。</summary>
        private static int CompareByWriteTimeDescending(FileInfo a, FileInfo b)
        {
            return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc);
        }

        /// <summary>
        /// 第一条要写的时候才建目录、开文件：埋点被总开关关掉、或这次会话一条都没产生时，不留空文件。
        /// 清理放在开完文件之后——新文件此刻是最新的，按修改时间排绝不会把自己删掉。
        /// </summary>
        private bool EnsureOpen()
        {
            if (writer != null)
            {
                return true;
            }

            try
            {
                Directory.CreateDirectory(directory);
                FileStream stream = new FileStream(
                    FilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, StreamBufferBytes);
                writer = new StreamWriter(stream, Utf8NoBom);
                writer.NewLine = "\n";
                lastFlushTick = Environment.TickCount;
            }
            catch (Exception e)
            {
                Fail(e);
                return false;
            }

            CleanupOldSessions(directory, keepSessions);
            return true;
        }

        /// <summary>
        /// 停写并解释一句。先置 <see cref="Available"/> 再打日志：<see cref="Log.Warn"/> 最终是一次
        /// <c>Debug.LogWarning</c>，Unity 会同步回调日志桥，早晚有一天那条路会绕回这里来。
        /// </summary>
        private void Fail(Exception e)
        {
            if (!Available)
            {
                return;
            }

            Available = false;
            CloseQuietly();
            Log.Warn($"埋点编辑器镜像停写（{FilePath}）：{e.Message}。"
                     + "埋点本身不受影响，仍然照常进 Unity 日志；只是这次会话没有干净的那一份可对照。");
        }

        private void CloseQuietly()
        {
            StreamWriter current = writer;
            writer = null;
            if (current == null)
            {
                return;
            }

            try
            {
                current.Dispose();
            }
            catch (Exception)
            {
                // 关文件都失败的话已经没有别的补救手段了，再往外抛只会把调用方拖下水
            }
        }

        private void OnApplicationQuitting()
        {
            Flush();
        }

        private void UnhookQuitting()
        {
            if (!quittingHooked)
            {
                return;
            }

            Application.quitting -= OnApplicationQuitting;
            quittingHooked = false;
        }
    }
}
#endif
