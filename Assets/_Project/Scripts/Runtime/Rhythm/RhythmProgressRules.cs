// 职责：纯 C# 达标和最佳分规则；RhythmRules 只判断音符，不应读关卡档案或前置解锁。
using System;
using System.Collections.Generic;

namespace Game.Rhythm
{
    public static class RhythmProgressRules
    {
        public static void Normalize(RhythmProgressData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.BestScores == null) data.BestScores = new Dictionary<string, int>();
            if (data.ClearedCharts == null) data.ClearedCharts = new HashSet<string>();
            if (data.UnlockedSongs == null) data.UnlockedSongs = new HashSet<string>();
            if (data.Records == null) data.Records = new Dictionary<string, RhythmRecordData>();
            if (data.ProcessedRunIds == null) data.ProcessedRunIds = new HashSet<string>();
            foreach (var pair in data.Records)
            {
                if (pair.Value == null) throw new ArgumentException("成绩记录为空");
                ValidateStoredRun(pair.Key, pair.Value.BestScoreRun);
                ValidateStoredRun(pair.Key, pair.Value.LastCompletedRun);
            }
        }
        private static void ValidateStoredRun(string key, RhythmRunResult run)
        {
            if (run == null) return;
            run.Validate();
            if (run.Mode != RhythmPlayMode.FreePlay || run.Completion != RhythmRunCompletion.Completed ||
                RecordKey(run.SongId, run.ChartId, run.Revision, run.RulesetId, run.ScoringVersion) != key)
                throw new ArgumentException("档案单局版本或成绩资格无效");
        }
        public static string RecordKey(string id, string chartId, string revision, string rulesetId, string scoringVersion)
        {
            if (string.IsNullOrWhiteSpace(rulesetId) || string.IsNullOrWhiteSpace(scoringVersion))
                throw new ArgumentException("判定规则与评分版本不能为空");
            return ProgressKey(id, chartId, revision) + ":" + Uri.EscapeDataString(rulesetId) + ":" + Uri.EscapeDataString(scoringVersion);
        }
        public static string SongIdFromProgressKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            string[] parts = key.Split(':');
            if (parts.Length != 3 || string.IsNullOrEmpty(parts[0]) || string.IsNullOrEmpty(parts[1]) || string.IsNullOrEmpty(parts[2])) return null;
            try { return Uri.UnescapeDataString(parts[0]); }
            catch (UriFormatException) { return null; }
        }
        public static RhythmRecordData ReadCurrentRecord(RhythmProgressData data, RhythmSongData song)
        {
            Normalize(data);
            if (song == null) throw new ArgumentNullException(nameof(song));
            return data.Records.TryGetValue(song.RecordKey, out var record) ? record.Copy() : null;
        }
        public static int GetLegacyBestScore(RhythmProgressData data, RhythmSongData song)
        {
            Normalize(data);
            if (song == null) throw new ArgumentNullException(nameof(song));
            return data.BestScores.TryGetValue(song.ProgressKey, out int score) ? score : 0;
        }
        public static int GetBestScore(RhythmProgressData data, RhythmSongData song)
        {
            var record = ReadCurrentRecord(data, song);
            int current = record != null && record.BestScoreRun != null ? record.BestScoreRun.Score : 0;
            return current;
        }
        public static bool RecordRun(RhythmProgressData data, RhythmSongData song, RhythmRunResult result)
        {
            Normalize(data);
            if (song == null || result == null) throw new ArgumentNullException(song == null ? nameof(song) : nameof(result));
            result.Validate();
            if (result.Mode != RhythmPlayMode.FreePlay || result.Completion != RhythmRunCompletion.Completed) return false;
            var rules = song.Chart.CreateRules(0, null);
            if (RecordKey(result.SongId, result.ChartId, result.Revision, result.RulesetId, result.ScoringVersion) != song.RecordKey ||
                result.NoteCount != rules.Count)
                throw new ArgumentException("完成成绩与当前曲谱版本不一致");
            int holds = 0;
            for (int i = 0; i < rules.Count; i++) if (rules.NoteType(i) == RhythmNoteType.Hold) holds++;
            if (result.CompletedHolds > holds) throw new ArgumentException("成功 Hold 数量超出当前谱面");
            if (data.ProcessedRunIds.Contains(result.RunId)) return false;
            if (!data.Records.TryGetValue(song.RecordKey, out var record))
            {
                record = new RhythmRecordData { Cleared = data.ClearedCharts.Contains(song.ProgressKey) };
                data.Records.Add(song.RecordKey, record);
            }
            record.LastCompletedRun = result;
            // 同分保留先前完整局，不能取后轮较大连击拼成一份不存在的成绩。
            if (record.BestScoreRun == null || result.Score > record.BestScoreRun.Score) record.BestScoreRun = result;
            bool passed = result.Score >= RequiredScore(result.NoteCount, song.PassScoreRatio);
            record.Cleared |= passed;
            data.ProcessedRunIds.Add(result.RunId);
            data.UnlockedSongs.Add(song.Id);
            if (passed) data.ClearedCharts.Add(song.ProgressKey);
            return true;
        }
        public static int RequiredScore(int count, double ratio)
        {
            if (count < 1 || count > int.MaxValue / 1000 || double.IsNaN(ratio) || double.IsInfinity(ratio) || ratio <= 0 || ratio > 1)
                throw new ArgumentException("音符数量或通关比例无效");
            return (int)Math.Ceiling(count * 1000d * ratio); // lint-ok: 这是档案整数门槛，不参与重放模拟；GameMath.Ceil 仅支持 float，降精度会改变大分数边界。
        }
        public static string ProgressKey(string id, string chartId, string revision)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(chartId) || string.IsNullOrWhiteSpace(revision))
                throw new ArgumentException("曲目、谱面及修订标识不能为空");
            return Uri.EscapeDataString(id) + ":" + Uri.EscapeDataString(chartId) + ":" + Uri.EscapeDataString(revision);
        }
        public static bool IsUnlocked(RhythmProgressData data, string prerequisiteKey)
        {
            Normalize(data);
            return string.IsNullOrEmpty(prerequisiteKey) || data.ClearedCharts.Contains(prerequisiteKey);
        }
        public static void ValidateCatalog(string[] ids, string[] prerequisites)
        {
            if (ids == null || prerequisites == null || ids.Length == 0 || ids.Length != prerequisites.Length)
                throw new ArgumentException("曲库标识与前置关必须非空且一一对应");
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < ids.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(ids[i]) || map.ContainsKey(ids[i])) throw new ArgumentException("曲目 ID 空白或重复");
                map.Add(ids[i], prerequisites[i]);
            }
            for (int i = 0; i < ids.Length; i++)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                string current = ids[i];
                while (!string.IsNullOrEmpty(current))
                {
                    if (!map.TryGetValue(current, out string next)) throw new ArgumentException("前置关不存在");
                    if (!seen.Add(current)) throw new ArgumentException("前置关存在循环");
                    current = next;
                }
            }
        }
        public static bool Record(RhythmProgressData data, string key, int count, double ratio,
            int score, int resolved, bool completed)
        {
            Normalize(data);
            int required = RequiredScore(count, ratio);
            if (string.IsNullOrWhiteSpace(key) || score < 0 || score > count * 1000 || resolved < 0 || resolved > count)
                throw new ArgumentException("成绩记录无效");
            if (!completed || resolved != count) return false;
            if (!data.BestScores.TryGetValue(key, out int best) || score > best) data.BestScores[key] = score;
            bool passed = score >= required;
            if (passed) data.ClearedCharts.Add(key);
            return passed;
        }
    }
}
