// 职责：存档槽的元数据分区——保存时间、累计游玩时长、进度描述、场景键，供主界面选槽面板展示。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：JsonSaveService 的信封只有 formatVersion 与 partitions，没有放槽位元数据的位置；
//   2. 扩展不行：往 Core 的信封里加「进度描述」等于让框架层认识玩法名词（任务标题）；
//   所以作为 Session 模块自己的一个分区随槽写入，ReadCandidateAsync 读候选时 Contains 它才算可用槽。

using Game.Core.Save;

namespace Game.Session
{
    /// <summary>
    /// 槽位元数据分区（纯 DTO）。由 <see cref="GameSession.SaveNowAsync"/> 在每次落盘前更新，
    /// 不要长期持有实例：读档会整体替换分区，每次用 <c>saves.Get&lt;SessionSaveData&gt;()</c> 重取。
    /// </summary>
    public sealed class SessionSaveData : ISaveData
    {
        public int Version => 1;

        /// <summary>最后一次保存的 UTC 时间（<see cref="System.DateTime.Ticks"/>）；0 = 从未保存。</summary>
        public long SavedAtUtcTicks { get; set; }

        /// <summary>累计游玩秒数（只在玩法状态里累加，不受时间缩放影响）。</summary>
        public float PlaytimeSeconds { get; set; }

        /// <summary>进度描述（当前追踪任务或主线标题），选槽面板直接显示。</summary>
        public string ProgressText { get; set; } = string.Empty;

        /// <summary>保存时所在的玩法场景键。</summary>
        public string SceneKey { get; set; } = string.Empty;

        public void Migrate(int fromVersion)
        {
        }
    }
}
