// 职责：纯 C# 选曲请求代次、异步完成与成绩资格；RhythmRules 只判音符，UI 不应判断请求是否陈旧。
using System;

namespace Game.Rhythm
{
    public sealed class RhythmSelectionRules
    {
        private long generation;
        private string pendingKey;
        private bool preparing;
        private bool playing;
        private bool practice;
        public string SelectedKey { get; private set; }
        public bool IsPreparing => preparing;
        public bool IsPlaying => playing;
        public long Generation => generation;
        public bool TrySelect(string key, bool unlocked, out long ticket)
        {
            ticket = generation;
            if (!unlocked || preparing) return false;
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("选曲标识不能为空");
            Invalidate();
            pendingKey = key;
            SelectedKey = null;
            preparing = true;
            ticket = generation;
            return true;
        }
        public bool CompleteSelection(long ticket)
        {
            if (!preparing || ticket != generation) return false;
            SelectedKey = pendingKey;
            pendingKey = null;
            preparing = false;
            return true;
        }
        public bool TryStart(bool isPractice, out long ticket)
        {
            ticket = generation;
            if (preparing || SelectedKey == null) return false;
            // 重试替换上一轮；结果判定不允许再使用旧代次。
            Invalidate();
            playing = true; practice = isPractice;
            ticket = generation;
            return true;
        }
        public bool TryFinish(long ticket, string key, bool complete, out bool recordSong)
        {
            recordSong = false;
            if (!playing || ticket != generation || !string.Equals(key, SelectedKey, StringComparison.Ordinal)) return false;
            recordSong = complete && !practice;
            playing = false;
            return true;
        }
        public void Invalidate()
        {
            if (generation == long.MaxValue) throw new InvalidOperationException("会话代次已耗尽");
            generation++;
            preparing = false; playing = false; practice = false; pendingKey = null;
        }
        public void LeaveSelection()
        {
            Invalidate(); SelectedKey = null;
        }
    }
}
