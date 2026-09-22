// 职责：Monster 的具名持久字段；回放字节流不作为长期存档格式。
using System;

namespace Game.Monster
{
    public sealed class MonsterSaveData
    {
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float FacingX { get; set; }
        public float FacingY { get; set; }
        public float TargetX { get; set; }
        public float TargetY { get; set; }
        public MonsterMode Mode { get; set; }
        public int Health { get; set; }
        public int WaypointIndex { get; set; }
        public float Alert { get; set; }
        public float PatrolWalkElapsed { get; set; }
        public float NextPauseAfter { get; set; }
        public float PatrolPauseLeft { get; set; }
        public float AlertAtLoss { get; set; }
        public float AlertDecayElapsed { get; set; }
        public float HostileLostElapsed { get; set; }
        public float AttackCooldownLeft { get; set; }
        public ulong PatrolRandomState { get; set; }
        public float[] WaypointX { get; set; } = Array.Empty<float>();
        public float[] WaypointY { get; set; } = Array.Empty<float>();
        public void Validate()
        {
            if (float.IsNaN(PositionX) || float.IsInfinity(PositionX)) throw new ArgumentException("PositionX 非有限值");
            if (float.IsNaN(PositionY) || float.IsInfinity(PositionY)) throw new ArgumentException("PositionY 非有限值");
            if (float.IsNaN(FacingX) || float.IsInfinity(FacingX)) throw new ArgumentException("FacingX 非有限值");
            if (float.IsNaN(FacingY) || float.IsInfinity(FacingY)) throw new ArgumentException("FacingY 非有限值");
            if (float.IsNaN(TargetX) || float.IsInfinity(TargetX)) throw new ArgumentException("TargetX 非有限值");
            if (float.IsNaN(TargetY) || float.IsInfinity(TargetY)) throw new ArgumentException("TargetY 非有限值");
            if (float.IsNaN(Alert) || float.IsInfinity(Alert)) throw new ArgumentException("Alert 非有限值");
            if (float.IsNaN(PatrolWalkElapsed) || float.IsInfinity(PatrolWalkElapsed)) throw new ArgumentException("PatrolWalkElapsed 非有限值");
            if (float.IsNaN(NextPauseAfter) || float.IsInfinity(NextPauseAfter)) throw new ArgumentException("NextPauseAfter 非有限值");
            if (float.IsNaN(PatrolPauseLeft) || float.IsInfinity(PatrolPauseLeft)) throw new ArgumentException("PatrolPauseLeft 非有限值");
            if (float.IsNaN(AlertAtLoss) || float.IsInfinity(AlertAtLoss)) throw new ArgumentException("AlertAtLoss 非有限值");
            if (float.IsNaN(AlertDecayElapsed) || float.IsInfinity(AlertDecayElapsed)) throw new ArgumentException("AlertDecayElapsed 非有限值");
            if (float.IsNaN(HostileLostElapsed) || float.IsInfinity(HostileLostElapsed)) throw new ArgumentException("HostileLostElapsed 非有限值");
            if (float.IsNaN(AttackCooldownLeft) || float.IsInfinity(AttackCooldownLeft)) throw new ArgumentException("AttackCooldownLeft 非有限值");
            if (Health < 0 || AttackCooldownLeft < 0f) throw new ArgumentException("生命或冷却非法");
            if (!Enum.IsDefined(typeof(MonsterMode), Mode) || (Health == 0) != (Mode == MonsterMode.Dead) ||
                WaypointX == null || WaypointY == null || WaypointX.Length < 1 || WaypointX.Length > 1024 ||
                WaypointX.Length != WaypointY.Length || WaypointIndex < 0 || WaypointIndex >= WaypointX.Length ||
                Alert < 0f || Alert > 1f || AlertAtLoss < 0f || AlertAtLoss > 1f || PatrolWalkElapsed < 0f ||
                NextPauseAfter <= 0f || AlertDecayElapsed < 0f || HostileLostElapsed < 0f)
                throw new ArgumentException("怪物状态或巡逻点非法");
            for (int i = 0; i < WaypointX.Length; i++)
                if (float.IsNaN(WaypointX[i]) || float.IsInfinity(WaypointX[i]) || float.IsNaN(WaypointY[i]) || float.IsInfinity(WaypointY[i]))
                    throw new ArgumentException("巡逻点非有限值");
        }
    }
}

