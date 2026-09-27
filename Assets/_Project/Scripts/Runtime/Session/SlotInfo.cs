// 职责：单个存档槽的只读摘要（槽号、三态、保存时间、游玩时长、进度描述），给选槽面板与标题路由用。
// 为什么新建：SessionSaveData 是可写的存档 DTO，直接交给 UI 等于把分区实例外泄；摘要只读、按值传递。

using System;

namespace Game.Session
{
    /// <summary>存档槽摘要。<see cref="State"/> 不是 <see cref="SlotState.Available"/> 时其余元数据为默认值。</summary>
    public readonly struct SlotInfo
    {
        public SlotInfo(int slot, SlotState state, DateTime savedAtUtc, float playtimeSeconds, string progressText)
        {
            Slot = slot;
            State = state;
            SavedAtUtc = savedAtUtc;
            PlaytimeSeconds = playtimeSeconds;
            ProgressText = progressText ?? string.Empty;
        }

        /// <summary>槽号，从 1 起。</summary>
        public int Slot { get; }

        public SlotState State { get; }

        /// <summary>保存时间（UTC）；显示时由界面转本地时间。</summary>
        public DateTime SavedAtUtc { get; }

        public float PlaytimeSeconds { get; }

        public string ProgressText { get; }

        /// <summary>空槽摘要。</summary>
        public static SlotInfo Empty(int slot) => new SlotInfo(slot, SlotState.Empty, default, 0f, string.Empty);

        /// <summary>不可用槽摘要。</summary>
        public static SlotInfo Unavailable(int slot) => new SlotInfo(slot, SlotState.Unavailable, default, 0f, string.Empty);
    }
}
