// 手动离线测试；载体为已安装 .NET SDK。锚点 RESULT failures=0；非零即退出。
// 直接链接本轮生产源码，依赖 stub 不触发 Unity，不写真实 Profile。
using System;
using Game.Core.Audio;
using Game.Rhythm;
using UnityEngine;
using Data = Game.Rhythm.RhythmDiagnosticData;
internal static class Program
{
    private static int checks, failures;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static void Run(string name, Action action)
    {
        try { action(); Console.WriteLine("PASS " + name); }
        catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
    }
    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-8;
    private static RhythmDiagnosticSession Session() => new RhythmDiagnosticSession(
        new Data.Header(7, "chart", "audio", "1", "offline", "Dynamic", 0, 100, 200, 0),
        new[] { new RhythmNoteData("hold", 0, 1000, RhythmNoteType.Hold, 2000), new RhythmNoteData("tap", 1, 4000) });
    private static void Input(RhythmDiagnosticSession s, int lane, double time, RhythmInputEdge edge = RhythmInputEdge.Press)
    {
        var intent = new RhythmHitIntent(lane, time, edge, 7); s.Enqueue(in intent, 200 + time, 200 + time);
    }
    private static void Drain(RhythmDiagnosticSession s, double time) => s.Drain(time, time, time, 200 + time, 100 + time);
    private static int Main()
    {
        Run("播放中暂停冻结双时钟并只暂停一次", () => {
            var source = new AudioSource { isPlaying = true };
            AudioSettings.dspTime = 101;
            var p = new AudioPlayback(source, 100, 200, 0, _ => { }); p.ScheduleEnd(10); p.Pause(); p.Pause();
            AudioSettings.dspTime = 900;
            Check(p.IsPaused && p.Position == 1 && p.PositionAtInputTime(999) == 1 && source.PauseCalls == 1, "冻结/幂等");
            p.Resume(1000); p.Resume(1001);
            Check(!p.IsPaused && source.UnPauseCalls == 1 && Near(p.DspStart, 899) && Near(p.InputStart, 999), "新原点/幂等");
            Check(Near(source.end, 909) && Near(p.PositionAtInputTime(1000.2), 1.2), "重排尾点/输入域");
        });
        Run("预滚暂停取消预约并恢复剩余倒数", () => {
            AudioSettings.dspTime = 99;
            var source = new AudioSource { timeSamples = 123 };
            var p = new AudioPlayback(source, 100, 200, 0, _ => { }); p.ScheduleEnd(10); p.Pause();
            AudioSettings.dspTime = 300; p.Resume(500);
            Check(source.StopCalls == 1 && source.ScheduleCalls == 1 && source.timeSamples == 123, "取消预约/采样点保留");
            Check(p.Position == -1 && source.start == 301 && source.end == 311, "剩余倒数不跳曲");
        });
        Run("已过音频终点暂停不重播尾音", () => {
            AudioSettings.dspTime = 111; var source = new AudioSource();
            var p = new AudioPlayback(source, 100, 200, 0, _ => { }); p.ScheduleEnd(10); p.Pause();
            AudioSettings.dspTime = 400; p.Resume(600);
            Check(source.UnPauseCalls == 0 && source.ScheduleCalls == 0 && p.Position == 11, "尾窗只续判定");
        });
        Run("无效恢复时钟拒绝并保持暂停", () => {
            AudioSettings.dspTime = 101; var source = new AudioSource { isPlaying = true };
            var p = new AudioPlayback(source, 100, 200, 0, _ => { }); p.Pause();
            bool rejected = false; try { p.Resume(double.NaN); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected && p.IsPaused && source.UnPauseCalls == 0, "先验证再改音频");
        });
        Run("暂停不推进Miss/Hold且积压输入丢弃", () => {
            var s = Session(); Input(s, 0, 1); Drain(s, 1.2); Input(s, 1, 4);
            s.Suspend(); Input(s, 0, 1.3, RhythmInputEdge.Release); Drain(s, 100);
            Check(s.Rules.IsHolding(0) && s.Rules.Score == 0 && s.Rules.Miss == 0, "暂停不判定");
            s.Resume(); Drain(s, 3.2);
            Check(s.Rules.CompletedHolds == 1 && s.Rules.Perfect == 1, "继续保留原头判定");
            Drain(s, 4.2); Check(s.Rules.Miss == 1, "暂停前待处理tap不重放");
            Check(!s.IsRecording && s.EndReason == Data.Reason.AudioPaused && s.Snapshot().Commands[^1].EndReason == Data.Reason.AudioPaused, "诊断封为不完整");
        });
        Run("暂停中松开在恢复边界只失败一次", () => {
            var s = Session(); Input(s, 0, 1); Drain(s, 1.2); s.Suspend(); s.Resume();
            Input(s, 0, 1.21, RhythmInputEdge.Release); Input(s, 0, 1.22, RhythmInputEdge.Release); Drain(s, 1.3);
            Check(s.Rules.Miss == 1 && !s.Rules.IsHolding(0) && s.Rules.CompletedHolds == 0, "恢复握持同步生效");
        });
        Run("重复暂停继续仍保留本局分数与未来tap", () => {
            var s = Session(); Input(s, 0, 1); Drain(s, 3.2);
            for (int i = 0; i < 20; i++) { s.Suspend(); s.Suspend(); s.Resume(); s.Resume(); }
            Input(s, 1, 4); Drain(s, 4.2);
            Check(s.Rules.Score == 2000 && s.Rules.Miss == 0 && s.LateInputs == 0, "同局分数不重置");
        });
        Run("Practice完成拒绝成绩及解锁写入", () => {
            var selection = new RhythmSelectionRules(); selection.TrySelect("tutorial:chart:1", true, out long selected); selection.CompleteSelection(selected);
            selection.TryStart(true, out long run); selection.TryFinish(run, "tutorial:chart:1", true, out bool record);
            Check(!record, "选曲层Practice无成绩资格");
            var data = new RhythmProgressData(); var song = new RhythmSongData();
            var result = new RhythmRunResult("practice", "demo", RhythmPlayMode.Practice, RhythmRunCompletion.Completed, "finished", "tutorial", "chart", "1", "rules", "1", 2, 2, 0, 0, 1, 2000, 2);
            Check(!RhythmProgressRules.RecordRun(data, song, result), "实际档案资格门拒绝Practice");
            Check(data.Records.Count == 0 && data.ClearedCharts.Count == 0 && data.UnlockedSongs.Count == 0 && data.ProcessedRunIds.Count == 0, "满分Practice不解锁");
        });
        Console.WriteLine($"RESULT checks={checks} failures={failures}");
        return failures == 0 ? 0 : 1;
    }
}
