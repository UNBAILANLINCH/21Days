// 职责：Player 的具名持久字段；回放字节流不作为长期存档格式。
using System;

namespace Game.Player
{
    public sealed class PlayerSaveData
    {
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float FacingX { get; set; }
        public float FacingY { get; set; }
        public bool IsSneaking { get; set; }
        public bool IsDisguised { get; set; }
        public int Health { get; set; }
        public float AttackCooldownLeft { get; set; }
        public bool PreviousDisguise { get; set; }
        public bool PreviousAttack { get; set; }

        // 走 / 跑切换：2026-09-26 追加；老数据缺这两项时按默认 false（步行）读入。
        public bool IsRunning { get; set; }
        public bool PreviousRun { get; set; }

        public void Validate()
        {
            if (float.IsNaN(PositionX) || float.IsInfinity(PositionX)) throw new ArgumentException("PositionX 非有限值");
            if (float.IsNaN(PositionY) || float.IsInfinity(PositionY)) throw new ArgumentException("PositionY 非有限值");
            if (float.IsNaN(FacingX) || float.IsInfinity(FacingX)) throw new ArgumentException("FacingX 非有限值");
            if (float.IsNaN(FacingY) || float.IsInfinity(FacingY)) throw new ArgumentException("FacingY 非有限值");
            if (float.IsNaN(AttackCooldownLeft) || float.IsInfinity(AttackCooldownLeft)) throw new ArgumentException("AttackCooldownLeft 非有限值");
            if (Health < 0 || AttackCooldownLeft < 0f) throw new ArgumentException("生命或冷却非法");
        }
    }
}

