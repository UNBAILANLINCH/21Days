// 职责：跨存档槽的音游偏移设置。已有通用设置未包含模块参数，独立档案避免修改无关存档契约。
using Game.Core.Save;
namespace Game.Rhythm
{
    public sealed class RhythmCalibrationData : ISaveData
    {
        public int Version => 2;
        public double OffsetMs { get; set; }
        public float VisualOffsetMs { get; set; }
        public void Migrate(int fromVersion) { }
    }
}
