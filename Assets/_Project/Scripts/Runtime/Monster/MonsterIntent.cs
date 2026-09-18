// 职责：怪物每个逻辑 tick 的只读感知意图；不让表现层直接更改状态字段。
using Game.Player;

namespace Game.Monster
{
    public readonly struct MonsterIntent
    {
        public MonsterIntent(PlayerSnapshot target, float deltaTime)
        {
            Target = target;
            DeltaTime = deltaTime;
        }

        public PlayerSnapshot Target { get; }
        public float DeltaTime { get; }
    }
}
