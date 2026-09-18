// 职责：玩家在一个逻辑 tick 的只读意图。InputCommand 是通用格式，不能代替玩法意图。
using UnityEngine;

namespace Game.Player
{
    public readonly struct PlayerIntent
    {
        public PlayerIntent(Vector2 movement, bool sneak, bool disguise, bool attack)
        {
            Movement = movement;
            Sneak = sneak;
            Disguise = disguise;
            Attack = attack;
        }

        public Vector2 Movement { get; }
        public bool Sneak { get; }
        public bool Disguise { get; }
        public bool Attack { get; }
    }
}
