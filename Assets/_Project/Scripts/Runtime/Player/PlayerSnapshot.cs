// 职责：供其他模块读取玩家状态的值快照，不暴露运行数据的写权限。
using UnityEngine;

namespace Game.Player
{
    public readonly struct PlayerSnapshot
    {
        public PlayerSnapshot(Vector2 position, Vector2 facing, bool sneaking, bool disguised, int health)
        {
            Position = position;
            Facing = facing;
            IsSneaking = sneaking;
            IsDisguised = disguised;
            Health = health;
        }

        public Vector2 Position { get; }
        public Vector2 Facing { get; }
        public bool IsSneaking { get; }
        public bool IsDisguised { get; }
        public int Health { get; }
        public bool IsAlive => Health > 0;
    }
}
