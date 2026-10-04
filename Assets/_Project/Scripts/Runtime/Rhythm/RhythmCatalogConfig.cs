// 职责：只读曲库与前置关关系；原谱面配置只代表单曲，不能承担全库目录和解锁校验。
using System;
using UnityEngine;

namespace Game.Rhythm
{
    [CreateAssetMenu(menuName = "21Days/Rhythm/曲库")]
    public sealed class RhythmCatalogConfig : ScriptableObject
    {
        [SerializeField] private RhythmSongData[] songs;
        public int Count => songs == null ? 0 : songs.Length;
        public RhythmSongData Song(int index) => songs[index];
        public RhythmSongData Find(string id)
        {
            for (int i = 0; i < Count; i++) if (songs[i].Id == id) return songs[i];
            return null;
        }
        public bool IsUnlocked(RhythmSongData song, RhythmProgressData progress)
        {
            if (song == null || Find(song.Id) != song) return false;
            RhythmProgressRules.Normalize(progress);
            if (progress.UnlockedSongs.Contains(song.Id)) return true;
            if (string.IsNullOrEmpty(song.PrerequisiteId)) return RhythmProgressRules.IsUnlocked(progress, null);
            var prerequisite = Find(song.PrerequisiteId);
            return prerequisite != null && HasHistoricalClear(progress, prerequisite.Id);
        }
        public bool MigrateProgress(RhythmProgressData progress)
        {
            RhythmProgressRules.Normalize(progress);
            bool changed = false;
            for (int i = 0; i < Count; i++)
            {
                var song = songs[i];
                if (string.IsNullOrEmpty(song.PrerequisiteId) || HasHistoricalClear(progress, song.Id) ||
                    HasHistoricalClear(progress, song.PrerequisiteId))
                    changed |= progress.UnlockedSongs.Add(song.Id);
            }
            return changed;
        }
        private static bool HasHistoricalClear(RhythmProgressData progress, string songId)
        {
            foreach (string key in progress.ClearedCharts)
                if (RhythmProgressRules.SongIdFromProgressKey(key) == songId) return true;
            foreach (var record in progress.Records.Values)
                if (record.Cleared && record.BestScoreRun != null && record.BestScoreRun.SongId == songId) return true;
            return false;
        }
        public void Validate()
        {
            if (Count == 0) throw new ArgumentException("曲库不能为空");
            var ids = new string[Count];
            var prerequisites = new string[Count];
            for (int i = 0; i < Count; i++)
            {
                if (songs[i] == null) throw new ArgumentException("曲库存在空条目");
                songs[i].Validate();
                ids[i] = songs[i].Id;
                prerequisites[i] = songs[i].PrerequisiteId;
            }
            RhythmProgressRules.ValidateCatalog(ids, prerequisites);
        }
    }
}
