// 职责：完整同局纪录、旧档资格和版本隔离回归；原进度测试只覆盖最高分整数。
using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Rhythm;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Rhythm
{
    public sealed class RhythmRecordTests
    {
        private RhythmConfig chart;
        private RhythmCatalogConfig catalog;
        private RhythmSongData song;
        private RhythmSongData next;

        [SetUp]
        public void SetUp()
        {
            chart = ScriptableObject.CreateInstance<RhythmConfig>();
            Set(chart, "schemaVersion", 2);
            Set(chart, "chartId", "chart-a");
            Set(chart, "notes", new[] { new RhythmNoteData("a", 0, 1000), new RhythmNoteData("b", 1, 2000) });
            song = Song("song:a", null);
            next = Song("next", song.Id);
            catalog = ScriptableObject.CreateInstance<RhythmCatalogConfig>();
            Set(catalog, "songs", new[] { song, next });
        }
        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(chart);
            UnityEngine.Object.DestroyImmediate(catalog);
        }
        private RhythmSongData Song(string id, string prerequisite)
        {
            var result = new RhythmSongData();
            Set(result, "id", id); Set(result, "chart", chart); Set(result, "prerequisiteId", prerequisite);
            return result;
        }
        private RhythmRunResult Run(string id, int perfect = 2, int good = 0, int miss = 0, int combo = 2,
            RhythmPlayMode mode = RhythmPlayMode.FreePlay, RhythmRunCompletion completion = RhythmRunCompletion.Completed,
            string revision = "1", string scoring = "1", string context = null)
            => new RhythmRunResult(id, context, mode, completion, "test", song.Id, chart.ChartId, revision,
                song.RulesetId, scoring, 2, perfect, good, miss, 0, perfect * 1000 + good * 500, combo);

        [Test]
        public void LegacyMigration_IsIdempotentAndDoesNotInventStatistics()
        {
            var data = new RhythmProgressData();
            string old = RhythmProgressRules.ProgressKey(song.Id, chart.ChartId, "old-revision");
            data.BestScores[old] = 1500; data.ClearedCharts.Add(old);
            data.Migrate(1);
            Assert.That(catalog.MigrateProgress(data), Is.True);
            Assert.That(catalog.MigrateProgress(data), Is.False);
            Assert.That(data.BestScores[old], Is.EqualTo(1500));
            Assert.That(data.ClearedCharts.Contains(old), Is.True);
            Assert.That(data.Records, Is.Empty);
            Assert.That(catalog.IsUnlocked(next, data), Is.True);
            Assert.That(RhythmProgressRules.GetBestScore(data, song), Is.Zero);
            Set(song, "revision", "new-revision");
            Assert.That(catalog.IsUnlocked(next, data), Is.True);
            data.ClearedCharts.Clear();
            Assert.That(catalog.IsUnlocked(next, data), Is.True, "已获得入口资格不能依赖后来仍存在旧clear");
        }
        [Test]
        public void LegacyCurrentScore_RemainsExplicitlySeparateFromCompleteRun()
        {
            var data = new RhythmProgressData(); data.BestScores[song.ProgressKey] = 2000;
            Assert.That(RhythmProgressRules.GetBestScore(data, song), Is.Zero);
            Assert.That(RhythmProgressRules.ReadCurrentRecord(data, song), Is.Null);
            RhythmProgressRules.RecordRun(data, song, Run("new", 1, 0, 1, 1));
            Assert.That(RhythmProgressRules.GetBestScore(data, song), Is.EqualTo(1000));
            Assert.That(RhythmProgressRules.ReadCurrentRecord(data, song).BestScoreRun.Score, Is.EqualTo(1000));
            Assert.That(RhythmProgressRules.GetLegacyBestScore(data, song), Is.EqualTo(2000));
        }
        [Test]
        public void BestScoreAndLastRun_AreWholeDistinctRunsAndTiesKeepFirst()
        {
            var data = new RhythmProgressData(); var best = Run("best", 2, 0, 0, 1);
            RhythmProgressRules.RecordRun(data, song, best);
            RhythmProgressRules.RecordRun(data, song, Run("tie", 2, 0, 0, 2));
            var record = RhythmProgressRules.ReadCurrentRecord(data, song);
            Assert.That(record.BestScoreRun.RunId, Is.EqualTo("best"));
            Assert.That(record.BestScoreRun.MaxCombo, Is.EqualTo(1));
            Assert.That(record.LastCompletedRun.RunId, Is.EqualTo("tie"));
            RhythmProgressRules.RecordRun(data, song, Run("low", 0, 0, 2, 0));
            Assert.That(RhythmProgressRules.ReadCurrentRecord(data, song).Cleared, Is.True);
            catalog.MigrateProgress(data);
            Assert.That(catalog.IsUnlocked(next, data), Is.True);
        }
        [Test]
        public void ExistingCurrentLegacyClear_IsRetainedAfterLowerNewRun()
        {
            var data = new RhythmProgressData(); data.BestScores[song.ProgressKey] = 2000;
            data.ClearedCharts.Add(song.ProgressKey);
            RhythmProgressRules.RecordRun(data, song, Run("low", 0, 0, 2, 0));
            Assert.That(RhythmProgressRules.ReadCurrentRecord(data, song).Cleared, Is.True);
            Assert.That(RhythmProgressRules.GetBestScore(data, song), Is.Zero);
            Assert.That(RhythmProgressRules.GetLegacyBestScore(data, song), Is.EqualTo(2000));
            Assert.That(RhythmProgressRules.ReadCurrentRecord(data, song).BestScoreRun.Score, Is.Zero);
        }
        [Test]
        public void DuplicateResult_DoesNotReplaceLastOrBest()
        {
            var data = new RhythmProgressData();
            Assert.That(RhythmProgressRules.RecordRun(data, song, Run("same")), Is.True);
            Assert.That(RhythmProgressRules.RecordRun(data, song, Run("same", 0, 0, 2, 0)), Is.False);
            Assert.That(RhythmProgressRules.ReadCurrentRecord(data, song).LastCompletedRun.Score, Is.EqualTo(2000));
        }
        [TestCase(RhythmPlayMode.Combat, RhythmRunCompletion.Completed)]
        [TestCase(RhythmPlayMode.Practice, RhythmRunCompletion.Completed)]
        [TestCase(RhythmPlayMode.FreePlay, RhythmRunCompletion.Aborted)]
        [TestCase(RhythmPlayMode.FreePlay, RhythmRunCompletion.TechnicalError)]
        public void OtherModesAndInterruptions_DoNotEnterPersonalRecords(RhythmPlayMode mode, RhythmRunCompletion completion)
        {
            var data = new RhythmProgressData();
            Assert.That(RhythmProgressRules.RecordRun(data, song, Run("foreign", mode: mode, completion: completion, context: "context")), Is.False);
            Assert.That(data.Records, Is.Empty); Assert.That(data.ClearedCharts, Is.Empty);
            Assert.That(data.ProcessedRunIds, Is.Empty);
        }
        [Test]
        public void RevisionAndScoringVersions_IsolateComparableResults()
        {
            var data = new RhythmProgressData(); RhythmProgressRules.RecordRun(data, song, Run("old"));
            Set(song, "scoringVersion", "2");
            Assert.That(RhythmProgressRules.ReadCurrentRecord(data, song), Is.Null);
            Assert.Throws<ArgumentException>(() => RhythmProgressRules.RecordRun(data, song, Run("stale")));
            RhythmProgressRules.RecordRun(data, song, Run("new", scoring: "2"));
            Set(song, "revision", "2");
            Assert.That(RhythmProgressRules.ReadCurrentRecord(data, song), Is.Null);
            Assert.That(data.Records.Count, Is.EqualTo(2));
            catalog.MigrateProgress(data);
            Assert.That(catalog.IsUnlocked(next, data), Is.True);
        }
        [Test]
        public void LegacyScoreWithoutScoringVersion_NeverBecomesCurrentComparableBest()
        {
            var data = new RhythmProgressData();
            string legacyKey = song.ProgressKey;
            data.BestScores[legacyKey] = 3500;
            Assert.That(RhythmProgressRules.GetBestScore(data, song), Is.Zero);
            Assert.That(RhythmProgressRules.GetLegacyBestScore(data, song), Is.EqualTo(3500));
            RhythmProgressRules.RecordRun(data, song, Run("version-one"));
            Assert.That(RhythmProgressRules.GetBestScore(data, song), Is.EqualTo(2000));
            Set(song, "scoringVersion", "2");
            Assert.That(song.ProgressKey, Is.EqualTo(legacyKey));
            Assert.That(RhythmProgressRules.ReadCurrentRecord(data, song), Is.Null);
            Assert.That(RhythmProgressRules.GetBestScore(data, song), Is.Zero);
            Assert.That(RhythmProgressRules.GetLegacyBestScore(data, song), Is.EqualTo(3500));
            Assert.That(data.BestScores[legacyKey], Is.EqualTo(3500));
        }
        [Test]
        public void CopyAndJsonRoundTrip_PreserveImmutableRunsWithoutContainerAliasing()
        {
            var data = new RhythmProgressData(); RhythmProgressRules.RecordRun(data, song, Run("complete"));
            var copy = data.Copy();
            copy.Records[song.RecordKey].Cleared = false; copy.ProcessedRunIds.Clear(); copy.UnlockedSongs.Clear();
            Assert.That(data.Records[song.RecordKey].Cleared, Is.True);
            Assert.That(data.ProcessedRunIds.Contains("complete"), Is.True);
            Assert.That(data.UnlockedSongs.Contains(song.Id), Is.True);
            var json = Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert", true);
            string text = (string)json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { data });
            var restored = (RhythmProgressData)json.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) })
                .Invoke(null, new object[] { text, typeof(RhythmProgressData) });
            RhythmProgressRules.Normalize(restored);
            Assert.That(restored.Records[song.RecordKey].BestScoreRun.RunId, Is.EqualTo("complete"));
            Assert.That(RhythmProgressRules.RecordRun(restored, song, Run("complete")), Is.False);
        }
        [Test]
        public void InvalidStatistics_AndMissingCombatContextAreRejected()
        {
            Assert.Throws<ArgumentException>(() => Run("incomplete", 0, 0, 1, 0));
            Assert.Throws<ArgumentException>(() => Run("combo", combo: 3));
            Assert.Throws<ArgumentException>(() => Run("context", mode: RhythmPlayMode.Combat));
            Assert.Throws<ArgumentException>(() => new RhythmRunResult("score", null, RhythmPlayMode.FreePlay,
                RhythmRunCompletion.Completed, "test", song.Id, chart.ChartId, "1", song.RulesetId, "1", 2, 2, 0, 0, 0, 1, 2));
            Assert.Throws<ArgumentException>(() => new RhythmRunResult("holds", null, RhythmPlayMode.FreePlay,
                RhythmRunCompletion.Completed, "test", song.Id, chart.ChartId, "1", song.RulesetId, "1", 2, 2, 0, 0, 3, 2000, 2));
        }
        [Test]
        public void EncodedIdentityAndNullContainers_MigrateSafely()
        {
            Assert.That(RhythmProgressRules.SongIdFromProgressKey(song.ProgressKey), Is.EqualTo("song:a"));
            Assert.That(RhythmProgressRules.RecordKey("a:b", "c", "d", "e", "f"), Is.Not.EqualTo(RhythmProgressRules.RecordKey("a", "b:c", "d", "e", "f")));
            var data = new RhythmProgressData { BestScores = null, ClearedCharts = null, Records = null, UnlockedSongs = null, ProcessedRunIds = null };
            data.Migrate(1); Assert.That(data.Version, Is.EqualTo(2)); Assert.That(data.Records, Is.Empty);
            Assert.Throws<ArgumentOutOfRangeException>(() => data.Migrate(3));
        }
        private static void Set(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
