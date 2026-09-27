// 职责：演出模块的存档分区——已完整播过或被跳过的演出 id（用于「只播一次」）。
// 为什么新建（复用 → 扩展 → 新建）：既有分区（对白已读、任务、物资箱）各管各的模块，塞进去会让它们认识演出；
//   按 ISaveData 约定一个模块一个分区。
using System;
using System.Collections.Generic;
using Game.Core.Save;

namespace Game.Performance
{
    public sealed class PerformanceSaveData : ISaveData
    {
        public int Version => 1;

        /// <summary>已播过的演出 id（Completed 或 Skipped），按首次播完顺序，不重复。</summary>
        public List<string> PlayedIds { get; set; } = new();

        public void Migrate(int fromVersion)
        {
            // 第 1 版无迁移。
        }

        /// <summary>记为已播；已存在返回 false 不改数据；id 为空返回 false。</summary>
        public bool MarkPlayed(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (HasPlayed(id)) return false;
            PlayedIds ??= new List<string>();
            PlayedIds.Add(id);
            return true;
        }

        /// <summary>是否已播过；id 为空返回 false。</summary>
        public bool HasPlayed(string id)
        {
            if (string.IsNullOrEmpty(id) || PlayedIds == null) return false;
            for (int i = 0; i < PlayedIds.Count; i++)
            {
                if (string.Equals(PlayedIds[i], id, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
