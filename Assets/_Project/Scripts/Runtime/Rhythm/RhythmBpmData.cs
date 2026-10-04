// 职责：制谱网格的 BPM 段。判定只读秒时间；旧配置无节拍表，独立记录让制作元数据不混入运行状态。
using System;
using UnityEngine;
namespace Game.Rhythm
{
    [Serializable]
    public sealed class RhythmBpmData
    {
        [SerializeField] private double timeMs;
        [SerializeField] private double bpm;
        public double TimeMs => timeMs;
        public double Bpm => bpm;
        public RhythmBpmData(double timeMs, double bpm) { this.timeMs = timeMs; this.bpm = bpm; }
    }
}
