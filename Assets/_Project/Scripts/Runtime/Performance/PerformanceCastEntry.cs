// 职责：演出演员名单的一行——字幕说话者显示名 → 对白面板头像（及头像显示在面板哪一侧）。
// 为什么新建：头像按字幕说话者查找，旁白与画外音没有演员物体也能配置；独立数据类型供舞台名单使用。

using System;
using UnityEngine;

namespace Game.Performance
{
    /// <summary>
    /// 演员名单条目。挂在 <see cref="PerformanceStage"/> 的 <c>cast</c> 列表里，
    /// 字幕轨道显示一句字幕时按 <see cref="Speaker"/> 与字幕片段的说话者**严格相等**匹配，取 <see cref="Avatar"/> 显示在面板 <see cref="Side"/> 那一侧的头像位。
    /// </summary>
    [Serializable]
    public sealed class PerformanceCastEntry
    {
        [Tooltip("说话者显示名，必须与字幕片段的「说话者」一字不差（区分大小写、不去空白）。")]
        [SerializeField] private string speaker = string.Empty;

        [Tooltip("对白面板上的方形头像；留空 = 这个说话者不显示头像。")]
        [SerializeField] private Sprite avatar;

        [Tooltip("头像显示在面板左侧还是右侧，按演员站位配（站在画面左半边配 Left，右半边配 Right）。")]
        [SerializeField] private PerformanceAvatarSide side = PerformanceAvatarSide.Left;

        public PerformanceCastEntry()
        {
        }

        public PerformanceCastEntry(string speaker, Sprite avatar)
        {
            this.speaker = speaker ?? string.Empty;
            this.avatar = avatar;
        }

        public PerformanceCastEntry(string speaker, Sprite avatar, PerformanceAvatarSide side)
            : this(speaker, avatar)
        {
            this.side = side;
        }

        public string Speaker => speaker;
        public Sprite Avatar => avatar;
        public PerformanceAvatarSide Side => side;
    }
}
