// 职责：把「编辑器镜像」那四条约束变成可执行断言——镜像行与 Unity 日志行逐字节相同、
//   滚动清理只动自己的 .log、写失败不抛异常。
// 为什么新建：TelemetryServiceTests 断的是「行格式与策略」，全程不碰文件系统，
//   把临时目录的建/删与文件断言塞进去会让那份测试每条用例都背上 IO 的 SetUp；
//   而镜像的价值恰恰全压在「与 Unity 日志那份一模一样」上，值得单独一份测试盯着。

#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Telemetry;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.EditMode.Telemetry
{
    /// <summary>
    /// <see cref="EditorMirrorTelemetrySink"/> 的 EditMode 测试。
    /// <para>
    /// 文件 IO 全部落在系统临时目录里的一个随机子目录，**不碰工程的 <c>Logs/</c>**：
    /// 那里是真实运行留下的证据，测试往里写就分不清哪份是跑出来的、哪份是测出来的。
    /// </para>
    /// <para>
    /// 镜像只在 <c>Application.isPlaying</c> 时才会由 <see cref="TelemetryService"/> 挂上，
    /// 所以这里一律自己 new 出来、从构造函数注进服务——走的还是生产那条分发路径（服务的 sink 循环），
    /// 只是终点的目录换成了临时目录。
    /// </para>
    /// </summary>
    public sealed class EditorMirrorTelemetrySinkTests
    {
        private const string SessionId = "1a2b3c4d";

        /// <summary>与 sink 里一致：UTF-8 无 BOM。逐字节比对时编码必须对齐，否则差在文件头。</summary>
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private string rootDirectory;
        private string mirrorDirectory;
        private FakeTelemetryClock clock;
        private RecordingTelemetrySink recording;
        private EditorMirrorTelemetrySink mirror;
        private TelemetryService telemetry;

        [SetUp]
        public void SetUp()
        {
            rootDirectory = Path.Combine(Path.GetTempPath(), "21days-telemetry-" + Guid.NewGuid().ToString("N"));
            mirrorDirectory = Path.Combine(rootDirectory, "telemetry");
            Directory.CreateDirectory(mirrorDirectory);

            clock = new FakeTelemetryClock();
            recording = new RecordingTelemetrySink();
            mirror = new EditorMirrorTelemetrySink(mirrorDirectory, SessionId, 20);

            // 两个终点一起注进去：服务格式化一次，按顺序分发给它们俩——这正是要验的那条路
            telemetry = new TelemetryService(TelemetryOptions.Default, clock, recording, mirror);
        }

        [TearDown]
        public void TearDown()
        {
            telemetry.Dispose();
            mirror.Dispose();

            try
            {
                if (Directory.Exists(rootDirectory))
                {
                    Directory.Delete(rootDirectory, true);
                }
            }
            catch (Exception)
            {
                // 临时目录删不掉不该让测试变红，系统自己会回收
            }
        }

        /// <summary>
        /// 契约约束 2 的可执行版本：镜像里的每一行与送进 Unity 日志的那一行**逐字节相同**。
        /// 这条一红，就说明镜像那边又长出了第二套格式化。
        /// </summary>
        [UnityTest]
        public IEnumerator Write_ThroughService_MirrorsUnityLogLinesByteForByte() => UniTask.ToCoroutine(async () =>
        {
            await telemetry.InitializeAsync(CancellationToken.None);

            telemetry.Track(TelemetryKeys.Flow, TelemetryKeys.FlowEvents.StateEnter,
                (TelemetryKeys.Props.From, "BootState"),
                (TelemetryKeys.Props.To, "TitleState"),
                (TelemetryKeys.Props.Ms, 312));
            clock.Advance(16);
            telemetry.TrackWarn(TelemetryKeys.Audio, TelemetryKeys.AudioEvents.SfxDenied);
            telemetry.TrackError(TelemetryKeys.Asset, TelemetryKeys.AssetEvents.LoadFailed,
                new InvalidOperationException("键不存在\n第二行"),
                TelemetryProps.Of((TelemetryKeys.Props.Key, "SampleScene_Game")));

            // 会话尾也要进镜像；Dispose 顺带 flush 并关文件
            telemetry.Dispose();

            Assert.That(recording.Count, Is.GreaterThanOrEqualTo(5), "前置条件：至少写出会话头 + 三条事件 + 会话尾");

            string text = ReadMirrorText(mirror.FilePath);
            string[] mirrored = text.Split('\n');

            // 每行都以 \n 结尾，所以 Split 之后末尾会多出一个空串
            Assert.That(mirrored.Length, Is.EqualTo(recording.Count + 1), "镜像行数与 Unity 日志行数对不上");
            Assert.That(mirrored[mirrored.Length - 1], Is.Empty, "最后一行之后不该还有内容");

            for (int i = 0; i < recording.Count; i++)
            {
                Assert.That(mirrored[i], Is.EqualTo(recording.Lines[i]), $"第 {i} 行与 Unity 日志那份不一致");
            }

            // 再过一遍字节：编码、BOM、换行符任何一处不同都在这里现形
            byte[] expected = Utf8NoBom.GetBytes(string.Join("\n", recording.Lines) + "\n");
            byte[] actual = ReadMirrorBytes(mirror.FilePath);
            Assert.That(actual, Is.EqualTo(expected), "镜像文件与 Unity 日志行的字节序列不一致");
        });

        /// <summary>
        /// 读镜像文件。**不能用 <c>File.ReadAllText</c>**：那个的共享模式是 <c>FileShare.Read</c>，
        /// 而 sink 这会儿还开着写句柄，Windows 上会直接判成 Sharing violation。
        /// 要读一份正在被写的文件，自己这边也得声明 <c>FileShare.ReadWrite</c>。
        /// </summary>
        private static string ReadMirrorText(string path)
        {
            return Utf8NoBom.GetString(ReadMirrorBytes(path));
        }

        private static byte[] ReadMirrorBytes(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (MemoryStream copy = new MemoryStream())
            {
                stream.CopyTo(copy);
                return copy.ToArray();
            }
        }

        /// <summary>一段会话一个文件，文件名就是 sid——分析脚本靠它把多次运行分开。</summary>
        [Test]
        public void FilePath_IsSessionIdUnderMirrorDirectory()
        {
            mirror.Write(TelemetryLevel.Info, "[Game][T] I core/session_start | {\"t\":0,\"s\":0,\"f\":0}");
            mirror.Flush();

            Assert.That(Path.GetFileName(mirror.FilePath), Is.EqualTo(SessionId + ".log"));
            Assert.That(Path.GetDirectoryName(mirror.FilePath), Is.EqualTo(mirrorDirectory));
            Assert.That(File.Exists(mirror.FilePath), Is.True);
        }

        /// <summary>flush 之后不必等 Dispose 就能读到内容（文件是以共享读方式开的）。</summary>
        [Test]
        public void Flush_BeforeDispose_MakesLinesReadable()
        {
            const string Line = "[Game][T] I core.ui/open | {\"t\":1,\"s\":1,\"f\":1,\"p\":{\"panel\":\"Title\"}}";
            mirror.Write(TelemetryLevel.Info, Line);
            mirror.Flush();

            Assert.That(ReadMirrorText(mirror.FilePath), Is.EqualTo(Line + "\n"));
        }

        /// <summary>E 级不等攒够就落盘：崩溃现场最值钱的就是错误那几行。</summary>
        [Test]
        public void Write_WithErrorLevel_FlushesImmediately()
        {
            const string Line = "[Game][T] E core.log/unity_error | {\"t\":2,\"s\":2,\"f\":2,\"err\":\"boom\"}";
            mirror.Write(TelemetryLevel.Error, Line);

            Assert.That(ReadMirrorText(mirror.FilePath), Is.EqualTo(Line + "\n"));
        }

        /// <summary>清理按修改时间保留最近 N 个会话文件，别人的东西一个都不碰。</summary>
        [Test]
        public void CleanupOldSessions_KeepsNewestAndLeavesOtherFilesAlone()
        {
            DateTime baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < 5; i++)
            {
                string path = Path.Combine(mirrorDirectory, $"sid{i}.log");
                File.WriteAllText(path, "第 " + i + " 段会话");
                File.SetLastWriteTimeUtc(path, baseTime.AddMinutes(i));
            }

            // 同目录下不是 .log 的文件，以及子目录里的 .log——都不归清理管
            string report = Path.Combine(mirrorDirectory, "2026-01-01.md");
            File.WriteAllText(report, "分析报告");
            string subDirectory = Path.Combine(mirrorDirectory, "keep");
            Directory.CreateDirectory(subDirectory);
            string nested = Path.Combine(subDirectory, "old.log");
            File.WriteAllText(nested, "别人的日志");

            EditorMirrorTelemetrySink.CleanupOldSessions(mirrorDirectory, 2);

            Assert.That(File.Exists(Path.Combine(mirrorDirectory, "sid4.log")), Is.True, "最新的应该留着");
            Assert.That(File.Exists(Path.Combine(mirrorDirectory, "sid3.log")), Is.True, "次新的应该留着");
            Assert.That(File.Exists(Path.Combine(mirrorDirectory, "sid2.log")), Is.False, "更老的应该删掉");
            Assert.That(File.Exists(Path.Combine(mirrorDirectory, "sid1.log")), Is.False, "更老的应该删掉");
            Assert.That(File.Exists(Path.Combine(mirrorDirectory, "sid0.log")), Is.False, "更老的应该删掉");
            Assert.That(File.Exists(report), Is.True, "不是 .log 的文件不许碰");
            Assert.That(File.Exists(nested), Is.True, "子目录不许递归进去");
        }

        /// <summary>保留数设 0 = 不清理（docs/telemetry.md 那张表的口径）。</summary>
        [Test]
        public void CleanupOldSessions_WithZeroKeep_DeletesNothing()
        {
            for (int i = 0; i < 3; i++)
            {
                File.WriteAllText(Path.Combine(mirrorDirectory, $"sid{i}.log"), "第 " + i + " 段会话");
            }

            EditorMirrorTelemetrySink.CleanupOldSessions(mirrorDirectory, 0);

            Assert.That(Directory.GetFiles(mirrorDirectory, "*.log").Length, Is.EqualTo(3));
        }

        /// <summary>
        /// 写失败（这里用「目录名被一个同名文件占着」制造）只停写 + 一条 Warn，绝不往外抛：
        /// 镜像是便利设施，不能因为它让游戏挂掉。
        /// </summary>
        [Test]
        public void Write_WhenDirectoryIsBlocked_WarnsOnceAndNeverThrows()
        {
            string blocked = Path.Combine(rootDirectory, "blocked");
            File.WriteAllText(blocked, "这是个文件，不是目录");

            EditorMirrorTelemetrySink failing = new EditorMirrorTelemetrySink(blocked, "deadbeef", 20);
            LogAssert.Expect(LogType.Warning, new Regex("埋点编辑器镜像停写"));

            Assert.DoesNotThrow(() => failing.Write(TelemetryLevel.Info, "[Game][T] I core/session_start | {}"));
            Assert.That(failing.Available, Is.False, "写失败之后应该停写");

            // 停写之后再怎么用都不能抛，也不该再刷第二条 Warn（LogAssert 只认了一条）
            Assert.DoesNotThrow(() => failing.Write(TelemetryLevel.Error, "[Game][T] E core/x | {}"));
            Assert.DoesNotThrow(() => failing.Flush());
            Assert.DoesNotThrow(() => failing.Dispose());
        }
    }
}
#endif
