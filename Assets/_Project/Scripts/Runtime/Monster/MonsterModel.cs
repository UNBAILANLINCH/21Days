// 职责：承载怪物会影响后续逻辑的状态；SO 配置和场景物体都不能承载回放数据。
using Game.Core.Replay;
using UnityEngine;

namespace Game.Monster
{
    public sealed class MonsterModel
    {
        public Vector2 Position { get; internal set; }
        public Vector2 Facing { get; internal set; } = Vector2.right;
        public Vector2 LastKnownTarget { get; internal set; }
        public MonsterMode Mode { get; internal set; }
        public int Health { get; internal set; }
        public int WaypointIndex { get; internal set; }
        public float Alert { get; internal set; }
        public float PatrolWalkElapsed { get; internal set; }
        public float NextPauseAfter { get; internal set; }
        public float PatrolPauseLeft { get; internal set; }
        public float AlertAtLoss { get; internal set; }
        public float AlertDecayElapsed { get; internal set; }
        public float HostileLostElapsed { get; internal set; }
        public float AttackCooldownLeft { get; internal set; }

        internal void Serialize(IStateWriter writer)
        {
            writer.WriteVector2(Position);
            writer.WriteVector2(Facing);
            writer.WriteVector2(LastKnownTarget);
            writer.WriteByte((byte)Mode);
            writer.WriteInt(Health);
            writer.WriteInt(WaypointIndex);
            writer.WriteFloat(Alert);
            writer.WriteFloat(PatrolWalkElapsed);
            writer.WriteFloat(NextPauseAfter);
            writer.WriteFloat(PatrolPauseLeft);
            writer.WriteFloat(AlertAtLoss);
            writer.WriteFloat(AlertDecayElapsed);
            writer.WriteFloat(HostileLostElapsed);
            writer.WriteFloat(AttackCooldownLeft);
        }

        internal void Deserialize(IStateReader reader)
        {
            Position = reader.ReadVector2();
            Facing = reader.ReadVector2();
            LastKnownTarget = reader.ReadVector2();
            Mode = (MonsterMode)reader.ReadByte();
            Health = reader.ReadInt();
            WaypointIndex = reader.ReadInt();
            Alert = reader.ReadFloat();
            PatrolWalkElapsed = reader.ReadFloat();
            NextPauseAfter = reader.ReadFloat();
            PatrolPauseLeft = reader.ReadFloat();
            AlertAtLoss = reader.ReadFloat();
            AlertDecayElapsed = reader.ReadFloat();
            HostileLostElapsed = reader.ReadFloat();
            AttackCooldownLeft = reader.ReadFloat();
        }
    }
}
