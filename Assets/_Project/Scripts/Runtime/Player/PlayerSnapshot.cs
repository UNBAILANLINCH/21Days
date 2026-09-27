// 职责：供其他模块读取玩家状态的值快照，不暴露运行数据的写权限。
using UnityEngine;

namespace Game.Player
{
    public readonly struct PlayerSnapshot
    {
        /// <param name="running">末尾可选参数：老调用（Monster 测试等）不改，默认未奔跑。</param>
        public PlayerSnapshot(Vector2 position, Vector2 facing, bool sneaking, bool disguised, int health, bool running = false)
        {
            Position = position;
            Facing = facing;
            IsSneaking = sneaking;
            IsDisguised = disguised;
            Health = health;
            IsRunning = running;
        }

        public Vector2 Position { get; }
        public Vector2 Facing { get; }
        public bool IsSneaking { get; }
        /// <summary>有效奔跑：奔跑模式开启且没按潜行。供表现层与将来的感知规则读，不参与快照序列化。</summary>
        public bool IsRunning { get; }
        public bool IsDisguised { get; }
        public int Health { get; }
        public bool IsAlive => Health > 0;
    }
}
