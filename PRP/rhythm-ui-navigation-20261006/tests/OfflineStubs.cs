// 离线宿主只模拟音频 API 调用与时钟；生产规则/队列/播放句柄直接编译源码。
// 不证明 Unity 原生预约停音、声卡或 InputSystem 行为。
using System;
namespace UnityEngine
{
    public sealed class SerializeFieldAttribute : Attribute { }
    public class Object { public static void Destroy(object value) { } }
    public static class AudioSettings { public static double dspTime; }
    public sealed class AudioSource : Object
    {
        public int timeSamples;
        public float volume;
        public object gameObject;
        public bool isPlaying;
        public bool paused;
        public int PauseCalls, UnPauseCalls, StopCalls, ScheduleCalls;
        public double start, end;
        public void Pause() { PauseCalls++; paused = isPlaying; isPlaying = false; }
        public void UnPause() { UnPauseCalls++; isPlaying = paused; paused = false; }
        public void Stop() { StopCalls++; isPlaying = paused = false; }
        public void PlayScheduled(double at) { ScheduleCalls++; start = at; }
        public void SetScheduledEndTime(double at) { end = at; }
    }
}
namespace Game.Core.Telemetry
{
    public interface ITelemetryScope { void Track(string name, params (string, object)[] values); }
    public sealed class NullTelemetryScope : ITelemetryScope
    {
        public static ITelemetryScope Instance { get; } = new NullTelemetryScope();
        public void Track(string name, params (string, object)[] values) { }
    }
}
namespace Game.Core.Simulation
{
    public static class GameMath
    {
        public static double Abs(double value) => Math.Abs(value);
        public static int Max(int a, int b) => Math.Max(a, b);
    }
}
namespace Game.Core.Save { public interface ISaveData { int Version { get; } void Migrate(int fromVersion); } }
namespace Newtonsoft.Json { public sealed class JsonConstructorAttribute : Attribute { } }
namespace Game.Rhythm
{
    // Practice 拒绝必须发生在读取谱面前；若资格门失效，本 fixture 会直接抛错。
    public sealed class RhythmConfig { public RhythmRules CreateRules(double offset, object telemetry) => throw new Exception("Practice 不应读取成绩谱面"); }
    public sealed class RhythmSongData
    {
        public string Id => "tutorial";
        public string RecordKey => "tutorial:chart:1:rules:1";
        public string ProgressKey => "tutorial:chart:1";
        public double PassScoreRatio => .6;
        public RhythmConfig Chart => new RhythmConfig();
    }
}
