// 职责：把 docs/telemetry.md 的契约变成可执行断言——行格式正则、序号与 t、三层过滤、限流补报、转义与压平。
// 为什么新建：波 1 之前没有埋点测试；而埋点的价值全压在「格式没坏」上——格式一旦漂移，
//   analyze.py 不会报错，只会安静地什么都解析不出来，等到真出事故去查日志才发现。
//   放进现有的 Core 测试目录不合适：那些是按服务分的，埋点是一整层（格式 + 策略 + 桥 + 采样）。

using System;
using System.Collections;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Telemetry;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Game.Tests.EditMode.Telemetry
{
    /// <summary>
    /// TelemetryService 的 EditMode 测试。时间与帧号由 <see cref="FakeTelemetryClock"/> 手动推，
    /// 输出收进 <see cref="RecordingTelemetrySink"/>，因此完全确定性：不依赖帧循环、不依赖真实时钟、
    /// 也不会往 Unity Console 里打东西。
    /// </summary>
    public sealed class TelemetryServiceTests
    {
        /// <summary>
        /// 契约正则，**一字不差抄自 docs/telemetry.md 第 1 节**（analyze.py 用的也是这一条）。
        /// 改格式就会在这里红——这就是这份测试存在的理由。
        /// </summary>
        private const string ContractPattern = @"^\[Game\]\[T\] ([DIWE]) ([a-z0-9_.]+)/([a-z0-9_]+) \| (\{.*\})$";

        private static readonly Regex Contract = new Regex(ContractPattern);

        private FakeTelemetryClock clock;
        private RecordingTelemetrySink sink;
        private TelemetryService telemetry;

        [SetUp]
        public void SetUp()
        {
            clock = new FakeTelemetryClock();
            sink = new RecordingTelemetrySink();
            telemetry = new TelemetryService(TelemetryOptions.Default, clock, sink);
        }

        [TearDown]
        public void TearDown()
        {
            telemetry.Dispose();
        }

        /// <summary>换一套取值重建服务（过滤、限流这类要改配置的用例用）。</summary>
        private void Rebuild(TelemetryOptions options)
        {
            telemetry.Dispose();
            sink.Clear();
            telemetry = new TelemetryService(options, clock, sink);
        }

        [Test]
        public void Track_WhenCalled_ProducesLineMatchingContractRegex()
        {
            telemetry.Track(TelemetryKeys.Flow, TelemetryKeys.FlowEvents.StateEnter,
                (TelemetryKeys.Props.From, "BootState"),
                (TelemetryKeys.Props.To, "TitleState"),
                (TelemetryKeys.Props.Ms, 312));
            telemetry.TrackWarn(TelemetryKeys.Audio, TelemetryKeys.AudioEvents.SfxDenied);
            telemetry.TrackError(TelemetryKeys.Asset, TelemetryKeys.AssetEvents.LoadFailed,
                new InvalidOperationException("键不存在"),
                TelemetryProps.Of((TelemetryKeys.Props.Key, "SampleScene_Game")));

            Assert.That(sink.Count, Is.EqualTo(3));
            for (int i = 0; i < sink.Count; i++)
            {
                Assert.That(Contract.IsMatch(sink.Lines[i]), Is.True, $"第 {i} 行不符合契约：{sink.Lines[i]}");
            }

            Match first = Contract.Match(sink.Lines[0]);
            Assert.That(first.Groups[1].Value, Is.EqualTo("I"));
            Assert.That(first.Groups[2].Value, Is.EqualTo("core.flow"));
            Assert.That(first.Groups[3].Value, Is.EqualTo("state_enter"));
            Assert.That(first.Groups[4].Value, Does.Contain("\"from\":\"BootState\""));
            Assert.That(first.Groups[4].Value, Does.Contain("\"ms\":312"));
            Assert.That(Contract.Match(sink.Lines[1]).Groups[1].Value, Is.EqualTo("W"));
            Assert.That(Contract.Match(sink.Lines[2]).Groups[1].Value, Is.EqualTo("E"));
        }

        [Test]
        public void Track_WhenCalledRepeatedly_SequenceIncrementsAndTimeNeverGoesBack()
        {
            for (int i = 0; i < 5; i++)
            {
                telemetry.Track(TelemetryKeys.Ui, TelemetryKeys.UiEvents.Open);
                clock.Advance(16L);
            }

            long previousSequence = -1L;
            long previousTime = -1L;
            for (int i = 0; i < sink.Count; i++)
            {
                long sequence = ReadInt(sink.Lines[i], "s");
                long time = ReadInt(sink.Lines[i], "t");
                Assert.That(sequence, Is.EqualTo(previousSequence + 1), "序号必须严格递增且不留空洞");
                Assert.That(time, Is.GreaterThanOrEqualTo(previousTime), "t 必须单调不减");
                previousSequence = sequence;
                previousTime = time;
            }

            Assert.That(previousSequence, Is.EqualTo(4));
        }

        [Test]
        public void Track_WhenBelowMinLevel_IsDropped()
        {
            Rebuild(new TelemetryOptions(true, TelemetryLevel.Warn, null, 0f, 0f, 0, false));

            telemetry.Track(TelemetryKeys.Ui, TelemetryKeys.UiEvents.Open);
            Assert.That(sink.Count, Is.EqualTo(0), "I 级低于最低级别 W，该整条丢掉");

            telemetry.TrackWarn(TelemetryKeys.Ui, TelemetryKeys.UiEvents.Close);
            Assert.That(sink.Count, Is.EqualTo(1));

            // 被丢掉的事件不占序号，否则日志里会出现空洞，分析脚本会误判成日志被截断
            Assert.That(ReadInt(sink.Lines[0], "s"), Is.EqualTo(0));
        }

        [Test]
        public void Track_WhenModuleFiltered_OnlyMatchingModulesPass()
        {
            Rebuild(new TelemetryOptions(true, TelemetryLevel.Info, new[] { TelemetryKeys.Ui }, 0f, 0f, 0, false));

            telemetry.Track(TelemetryKeys.Ui, TelemetryKeys.UiEvents.Open);
            telemetry.Track(TelemetryKeys.Flow, TelemetryKeys.FlowEvents.StateEnter);

            Assert.That(sink.Count, Is.EqualTo(1));
            Assert.That(Contract.Match(sink.Lines[0]).Groups[2].Value, Is.EqualTo(TelemetryKeys.Ui));
        }

        [Test]
        public void Track_WhenFilterIsPrefix_MatchesOnDotBoundaryOnly()
        {
            Rebuild(new TelemetryOptions(true, TelemetryLevel.Info, new[] { TelemetryKeys.Core }, 0f, 0f, 0, false));

            telemetry.Track(TelemetryKeys.Flow, TelemetryKeys.FlowEvents.StateEnter);
            telemetry.Track("coreplay", "hit");

            Assert.That(sink.Count, Is.EqualTo(1), "core 该捞到 core.flow，但不该捞到 coreplay");
            Assert.That(Contract.Match(sink.Lines[0]).Groups[2].Value, Is.EqualTo(TelemetryKeys.Flow));
        }

        [Test]
        public void Track_WhenOverPerSecondLimit_DropsAndReportsThrottledInNextWindow()
        {
            Rebuild(new TelemetryOptions(true, TelemetryLevel.Info, null, 0f, 0f, 3, false));

            for (int i = 0; i < 5; i++)
            {
                telemetry.Track(TelemetryKeys.Ui, TelemetryKeys.UiEvents.Open);
            }

            Assert.That(sink.Count, Is.EqualTo(3), "每秒上限 3 条，多出来的两条要丢掉");

            clock.Advance(1000L);
            telemetry.Track(TelemetryKeys.Ui, TelemetryKeys.UiEvents.Close);

            Assert.That(sink.Count, Is.EqualTo(5), "换窗口时要补一条 throttled，再写本次这条");
            Match throttled = Contract.Match(sink.Lines[3]);
            Assert.That(throttled.Groups[2].Value, Is.EqualTo(TelemetryKeys.Core));
            Assert.That(throttled.Groups[3].Value, Is.EqualTo(TelemetryKeys.CoreEvents.Throttled));
            Assert.That(throttled.Groups[4].Value, Does.Contain("\"n\":2"), "补报里要写清丢了几条");
            Assert.That(Contract.Match(sink.Lines[4]).Groups[3].Value, Is.EqualTo(TelemetryKeys.UiEvents.Close));
        }

        [Test]
        public void TrackError_WithMultilineStack_FlattensToOneLineAndEscapesJson()
        {
            telemetry.TrackError(
                TelemetryKeys.Asset,
                TelemetryKeys.AssetEvents.LoadFailed,
                "键 \"a\\b\" 不存在",
                "at Game.Core.Assets.Load()\r\n\tat Game.Core.Boot.Run()\n\tat Main()");

            string line = sink.Last;
            Assert.That(line.Contains("\n"), Is.False, "日志行不能跨行，跨行就没法按行解析");
            Assert.That(line.Contains("\r"), Is.False);
            Assert.That(line.Contains("\t"), Is.False, "制表符要一并清理");
            Assert.That(Contract.IsMatch(line), Is.True, line);
            Assert.That(line, Does.Contain(TelemetryFormat.NewlineReplacement), "堆栈换行要压成 ⏎");
            Assert.That(line, Does.Contain("\\\"a\\\\b\\\""), "引号与反斜杠要按 JSON 转义");
            Assert.That(line, Does.Contain("\"st\":"));
        }

        [Test]
        public void Track_WithStringPropContainingControlChars_EscapesInsteadOfBreakingLine()
        {
            telemetry.Track("sample", "note", (TelemetryKeys.Props.Name, "第一行\n第二行\u0001"));

            string line = sink.Last;
            Assert.That(Contract.IsMatch(line), Is.True, line);
            Assert.That(line, Does.Contain("\\n"), "属性里的换行走标准 JSON 转义，不真的断行");
            Assert.That(line, Does.Contain("\\u0001"), "控制字符要转成 \\u 形式");
        }

        [Test]
        public void BeginSpan_WhenDisposed_TracksElapsedMilliseconds()
        {
            using (telemetry.BeginSpan(TelemetryKeys.Asset, TelemetryKeys.AssetEvents.SceneLoad))
            {
                clock.Advance(312L);
            }

            Assert.That(sink.Count, Is.EqualTo(1));
            Assert.That(Contract.Match(sink.Last).Groups[3].Value, Is.EqualTo(TelemetryKeys.AssetEvents.SceneLoad));
            Assert.That(ReadInt(sink.Last, "ms"), Is.EqualTo(312L));
        }

        [Test]
        public void Scope_WithSameModule_ReturnsSameInstanceAndBindsModuleName()
        {
            ITelemetryScope first = telemetry.Scope("inventory");
            ITelemetryScope second = telemetry.Scope("inventory");

            Assert.That(second, Is.SameAs(first), "同一个模块名不该每次都 new 一个门面");
            Assert.That(first.Module, Is.EqualTo("inventory"));

            first.Track("buy_item", (TelemetryKeys.Props.Id, 1001), (TelemetryKeys.Props.N, 3));

            Match match = Contract.Match(sink.Last);
            Assert.That(match.Success, Is.True, sink.Last);
            Assert.That(match.Groups[2].Value, Is.EqualTo("inventory"));
            Assert.That(match.Groups[4].Value, Does.Contain("\"id\":1001"));
        }

        [Test]
        public void Track_WhenDisabled_WritesNothing()
        {
            Rebuild(new TelemetryOptions(false, TelemetryLevel.Debug, null, 0f, 0f, 0, false));

            telemetry.Track(TelemetryKeys.Ui, TelemetryKeys.UiEvents.Open);
            telemetry.TrackError(TelemetryKeys.Ui, TelemetryKeys.UiEvents.Open, "炸了");
            using (telemetry.BeginSpan(TelemetryKeys.Ui, TelemetryKeys.UiEvents.Open))
            {
                clock.Advance(5L);
            }

            Assert.That(sink.Count, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator InitializeAsync_WritesSessionHeaderWithSequenceZero() => UniTask.ToCoroutine(async () =>
        {
            await telemetry.InitializeAsync(CancellationToken.None);

            Assert.That(sink.Count, Is.EqualTo(1));
            string line = sink.Lines[0];
            Match match = Contract.Match(line);
            Assert.That(match.Success, Is.True, line);
            Assert.That(match.Groups[1].Value, Is.EqualTo("I"));
            Assert.That(match.Groups[2].Value, Is.EqualTo(TelemetryKeys.Core));
            Assert.That(match.Groups[3].Value, Is.EqualTo(TelemetryKeys.CoreEvents.SessionStart));
            Assert.That(ReadInt(line, "s"), Is.EqualTo(0L), "会话头的序号固定 0");
            Assert.That(line, Does.Contain("\"sid\":\""));

            // at 是跨会话排序的唯一硬证据（镜像一段会话一个文件，文件 mtime 会被拷贝/同步改掉），
            // 形状必须是带时区偏移的 ISO8601，紧跟在 sid 后面。
            Assert.That(line, Does.Match("\"sid\":\"[0-9a-f]{8}\",\"at\":\"\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}[+-]\\d{2}:\\d{2}\""),
                $"会话头的 at 字段不符合契约：{line}");

            // prod 是分析脚本校验「这份日志是不是本工程写的」的唯一判据，少了它就会拿着别的工程的
            // Editor.log 做分析（docs/telemetry.md「日志到底在哪」）。
            Assert.That(line, Does.Contain("\"prod\":"));
            Assert.That(line, Does.Contain("\"ver\":"));
            Assert.That(line, Does.Contain("\"plat\":"));
            Assert.That(line, Does.Contain("\"unity\":"));
            Assert.That(line, Does.Contain("\"dev\":"));
            Assert.That(line, Does.Contain("\"scr\":\""));
            Assert.That(line, Does.Contain("\"mem\":"));
            Assert.That(telemetry.SessionStarted, Is.True);
        });

        [UnityTest]
        public IEnumerator Dispose_AfterInitialize_WritesSessionEnd() => UniTask.ToCoroutine(async () =>
        {
            await telemetry.InitializeAsync(CancellationToken.None);
            sink.Clear();

            telemetry.Dispose();

            Assert.That(sink.Count, Is.EqualTo(1));
            Assert.That(Contract.Match(sink.Last).Groups[3].Value, Is.EqualTo(TelemetryKeys.CoreEvents.SessionEnd));
        });

        /// <summary>从一行里读出某个整数字段（<c>"key":123</c>）。测试自己解析，等于又验了一遍格式。</summary>
        private static long ReadInt(string line, string key)
        {
            Match match = Regex.Match(line, "\"" + key + "\":(-?[0-9]+)");
            Assert.That(match.Success, Is.True, $"行里没有整数字段 {key}：{line}");
            return long.Parse(match.Groups[1].Value);
        }
    }
}
