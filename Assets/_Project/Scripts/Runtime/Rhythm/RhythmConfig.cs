// 职责：短曲谱面与判定参数。其它模块配置没有音符时间，扩展它们会产生无关依赖。
using System;
using Game.Core.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Rhythm
{
    [CreateAssetMenu(menuName = "21Days/Rhythm/谱面")]
    public sealed class RhythmConfig : ScriptableObject
    {
        [SerializeField] private AudioClip song;
        [SerializeField] private InputActionAsset input;
        [SerializeField] private float clipStartSeconds = 16f;
        [SerializeField] private float durationSeconds = 40f;
        [SerializeField] private float approachSeconds = 2.5f;
        [SerializeField] private float countdownSeconds = 3f;
        [SerializeField] private float perfectMs = 65f;
        [SerializeField] private float goodMs = 140f;
        [SerializeField] private double[] noteTimes;
        [SerializeField] private int[] noteLanes;
        [SerializeField] private int schemaVersion = 1;
        [SerializeField] private string chartId = "chongerfei-demo";
        [SerializeField] private string audioKey = "ChongErFei";
        [SerializeField] private int laneCount = 4;
        [SerializeField] private RhythmNoteData[] notes;
        [SerializeField] private RhythmBpmData[] bpmSegments;
        [SerializeField] private double chartOffsetMs;
        [SerializeField] private float finishBufferSeconds = 0.1f;
        [SerializeField] private float calibrationBpm = 120;
        [SerializeField] private int calibrationWarmupBeats = 8;
        [SerializeField] private int calibrationSampleBeats = 32;
        [SerializeField] private int calibrationMinimumSamples = 24;
        [SerializeField] private float calibrationWindowMs = 250;
        [SerializeField] private float calibrationMaxMadMs = 30;
        [SerializeField, Range(0.15f, 1f)] private float farWidthRatio = 0.32f;
        [SerializeField] private float practiceDurationSeconds = 9f;
        [SerializeField] private RhythmNoteData[] practiceNotes = {
            new RhythmNoteData("practice-tap-d", 0, 1000), new RhythmNoteData("practice-tap-f", 1, 2000),
            new RhythmNoteData("practice-hold-j", 2, 3000, RhythmNoteType.Hold, 2000),
            new RhythmNoteData("practice-hold-k", 3, 6000, RhythmNoteType.Hold, 2000) };
        public AudioClip Song => song;
        public InputActionAsset Input => input;
        public float ClipStartSeconds => clipStartSeconds;
        public float DurationSeconds => durationSeconds;
        public float ApproachSeconds => approachSeconds;
        public float CountdownSeconds => countdownSeconds;
        public int SchemaVersion => schemaVersion;
        public string ChartId => chartId;
        public string AudioKey => audioKey;
        public double ChartOffsetMs => chartOffsetMs;
        public float FinishBufferSeconds => finishBufferSeconds;
        public double GoodMs => goodMs;
        public float CalibrationBpm => calibrationBpm;
        public int CalibrationWarmupBeats => calibrationWarmupBeats;
        public int CalibrationSampleBeats => calibrationSampleBeats;
        public int CalibrationMinimumSamples => calibrationMinimumSamples;
        public float CalibrationWindowMs => calibrationWindowMs;
        public float CalibrationMaxMadMs => calibrationMaxMadMs;
        public float FarWidthRatio => farWidthRatio;
        public float PracticeDurationSeconds => practiceDurationSeconds;
        public RhythmRules CreatePracticeRules(double offsetMs, Game.Core.Telemetry.ITelemetryScope telemetry)
        {
            var rules = new RhythmRules(practiceNotes, perfectMs, goodMs, offsetMs, 0, telemetry);
            if (!RhythmRules.Finite(practiceDurationSeconds) || practiceDurationSeconds <= 0 || rules.LastEndSeconds > practiceDurationSeconds)
                throw new ArgumentException("练习谱终点或时长无效");
            return rules;
        }
        // 歌曲开头可以有音符；预滚增加提前量，不移动谱面时刻。
        public float LeadInSeconds(float visualOffsetMs) => GameMath.Max(countdownSeconds, approachSeconds + GameMath.Abs(visualOffsetMs) / 1000f);
        public RhythmRules CreateRules(double offsetMs, Game.Core.Telemetry.ITelemetryScope telemetry)
        {
            if (laneCount != 4 || string.IsNullOrWhiteSpace(chartId) || string.IsNullOrWhiteSpace(audioKey) ||
                !RhythmRules.Finite(chartOffsetMs) || !RhythmRules.Finite(finishBufferSeconds) || finishBufferSeconds < 0 ||
                !RhythmRules.Finite(farWidthRatio) || farWidthRatio < 0.15f || farWidthRatio > 1 ||
                !RhythmRules.Finite(calibrationBpm) || calibrationBpm < 60 || calibrationBpm > 180 || calibrationWarmupBeats < 0 ||
                calibrationWarmupBeats > 32 || calibrationSampleBeats < 3 || calibrationSampleBeats > 64 || calibrationMinimumSamples < 3 || calibrationMinimumSamples > calibrationSampleBeats ||
                !RhythmRules.Finite(calibrationWindowMs) || calibrationWindowMs <= 0 || calibrationWindowMs > 30000 / calibrationBpm ||
                !RhythmRules.Finite(calibrationMaxMadMs) || calibrationMaxMadMs <= 0)
                throw new ArgumentException("谱面标识、轨数或偏移配置无效");
            ValidateBpm(bpmSegments);
            RhythmNoteData[] records;
            if (schemaVersion == 1)
            {
                if (notes != null && notes.Length != 0) throw new ArgumentException("旧版和新版音符不能同时编辑；请迁移到版本 2");
                records = RhythmRules.FromLegacy(noteTimes, noteLanes);
            }
            else if (schemaVersion == 2)
            {
                if ((noteTimes != null && noteTimes.Length != 0) || (noteLanes != null && noteLanes.Length != 0)) throw new ArgumentException("版本 2 只编辑毫秒记录，旧数组须清空");
                records = notes;
            }
            else throw new ArgumentException("不支持的谱面版本");
            var rules = new RhythmRules(records, perfectMs, goodMs, offsetMs, chartOffsetMs, telemetry);
            if (rules.LastEndSeconds > durationSeconds) throw new ArgumentException("音符终点超出音频片段");
            return rules;
        }
        public static void ValidateBpm(RhythmBpmData[] segments)
        {
            if (segments == null) return;
            for (int i = 0; i < segments.Length; i++)
                if (segments[i] == null || !RhythmRules.Finite(segments[i].TimeMs) || segments[i].TimeMs < 0 ||
                    !RhythmRules.Finite(segments[i].Bpm) || segments[i].Bpm <= 0 ||
                    (i > 0 && segments[i].TimeMs <= segments[i - 1].TimeMs))
                    throw new ArgumentException("BPM 段必须具有有限正 BPM，时刻非负且严格递增；未知节拍可留空");
        }
    }
}
