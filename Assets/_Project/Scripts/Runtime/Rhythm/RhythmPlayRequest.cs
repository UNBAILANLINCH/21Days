// 职责：保存外部进入的一次运行身份；现有 State 不保存调用方场景上下文。
using System;

namespace Game.Rhythm
{
    public sealed class RhythmPlayRequest
    {
        public RhythmPlayRequest(string runId, string contextId, string songId, RhythmPlayMode mode)
        {
            if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException("运行标识不能为空", nameof(runId));
            if (string.IsNullOrWhiteSpace(contextId)) throw new ArgumentException("上下文标识不能为空", nameof(contextId));
            if (string.IsNullOrWhiteSpace(songId)) throw new ArgumentException("曲目标识不能为空", nameof(songId));
            if (!Enum.IsDefined(typeof(RhythmPlayMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            RunId = runId;
            ContextId = contextId;
            SongId = songId;
            Mode = mode;
        }

        public string RunId { get; }
        public string ContextId { get; }
        public string SongId { get; }
        public RhythmPlayMode Mode { get; }
    }
}
