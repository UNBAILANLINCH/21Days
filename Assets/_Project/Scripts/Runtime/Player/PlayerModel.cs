// 职责：保存玩家运行状态与回放快照；SO 配置与 JSON 存档都不适合存逐 tick 状态。
using Game.Core.Replay;
using UnityEngine;

namespace Game.Player
{
    public sealed class PlayerModel : IReplayState
    {
        public Vector2 Position { get; internal set; }
        public Vector2 Facing { get; internal set; } = Vector2.right;
        public bool IsSneaking { get; internal set; }
        public bool IsDisguised { get; internal set; }
        public int Health { get; internal set; }
        public float AttackCooldownLeft { get; internal set; }
        public bool PreviousDisguise { get; internal set; }
        public bool PreviousAttack { get; internal set; }

        public PlayerSnapshot Snapshot => new PlayerSnapshot(Position, Facing, IsSneaking, IsDisguised, Health);

        public PlayerSaveData Capture() => new PlayerSaveData
        {
            PositionX = Position.x,
            PositionY = Position.y,
            FacingX = Facing.x,
            FacingY = Facing.y,
            IsSneaking = IsSneaking,
            IsDisguised = IsDisguised,
            Health = Health,
            AttackCooldownLeft = AttackCooldownLeft,
            PreviousDisguise = PreviousDisguise,
            PreviousAttack = PreviousAttack,
        };

        public void Restore(PlayerSaveData saved)
        {
            if (saved == null) throw new System.ArgumentNullException(nameof(saved));
            saved.Validate();
            Position = new Vector2(saved.PositionX, saved.PositionY);
            Facing = new Vector2(saved.FacingX, saved.FacingY);
            IsSneaking = saved.IsSneaking;
            IsDisguised = saved.IsDisguised;
            Health = saved.Health;
            AttackCooldownLeft = saved.AttackCooldownLeft;
            PreviousDisguise = saved.PreviousDisguise;
            PreviousAttack = saved.PreviousAttack;
        }

        public void Serialize(IStateWriter writer)
        {
            writer.WriteVector2(Position);
            writer.WriteVector2(Facing);
            writer.WriteBool(IsSneaking);
            writer.WriteBool(IsDisguised);
            writer.WriteInt(Health);
            writer.WriteFloat(AttackCooldownLeft);
            writer.WriteBool(PreviousDisguise);
            writer.WriteBool(PreviousAttack);
        }

        public void Deserialize(IStateReader reader)
        {
            Position = reader.ReadVector2();
            Facing = reader.ReadVector2();
            IsSneaking = reader.ReadBool();
            IsDisguised = reader.ReadBool();
            Health = reader.ReadInt();
            AttackCooldownLeft = reader.ReadFloat();
            PreviousDisguise = reader.ReadBool();
            PreviousAttack = reader.ReadBool();
        }
    }
}
