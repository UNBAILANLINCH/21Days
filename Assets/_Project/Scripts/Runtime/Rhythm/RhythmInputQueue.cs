// 职责：按歌曲时间排序输入并先处理积压事件。固定 tick 指令不保留格内时刻，队列与规则分离便于验证送达顺序。
using System;
using System.Collections.Generic;
namespace Game.Rhythm
{
    public sealed class RhythmInputQueue
    {
        private readonly List<RhythmHitIntent> pending = new List<RhythmHitIntent>(32);
        private readonly bool[] pressed = new bool[4];
        private int session;
        private bool active;
        private double watermark;
        public int LateInputs { get; private set; }
        public void Begin(int session)
        {
            Clear(); this.session = session; active = true; watermark = double.NegativeInfinity; LateInputs = 0;
        }
        public void Clear() { active = false; pending.Clear(); Array.Clear(pressed, 0, pressed.Length); }
        public void Enqueue(in RhythmHitIntent intent)
        {
            if (!active || intent.Session != session) return;
            if (intent.Lane < 0 || intent.Lane > 3 || !RhythmRules.Finite(intent.SongSeconds) ||
                (intent.Edge != RhythmInputEdge.Press && intent.Edge != RhythmInputEdge.Release)) throw new ArgumentException("输入边沿无效");
            int index = pending.Count;
            pending.Add(intent);
            while (index > 0 && pending[index - 1].SongSeconds > intent.SongSeconds)
            { pending[index] = pending[index - 1]; index--; }
            pending[index] = intent;
        }
        public void Drain(RhythmRules rules, double now, Action<RhythmHitResult> onResult)
        {
            if (!active) return;
            if (!RhythmRules.Finite(now) || now < watermark) throw new ArgumentOutOfRangeException(nameof(now));
            int consumed = 0;
            for (; consumed < pending.Count && pending[consumed].SongSeconds <= now; consumed++)
            {
                var intent = pending[consumed];
                if (intent.SongSeconds < watermark) { LateInputs++; continue; }
                rules.Advance(intent.SongSeconds); watermark = intent.SongSeconds;
                bool down = intent.Edge == RhythmInputEdge.Press;
                if (pressed[intent.Lane] == down) continue;
                pressed[intent.Lane] = down;
                var result = rules.Hit(in intent);
                onResult?.Invoke(result);
            }
            if (consumed > 0) pending.RemoveRange(0, consumed);
            rules.Advance(now); watermark = now;
        }
    }
}
