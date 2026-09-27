// 职责：玩家在一个逻辑 tick 的只读意图。InputCommand 是通用格式，不能代替玩法意图。
using UnityEngine;

namespace Game.Player
{
    public readonly struct PlayerIntent
    {
        /// <param name="run">奔跑键这一 tick 是否按着（来自 <c>InputCommand.ButtonRun</c>）。必填：调用点须显式决定是否透传。</param>
        public PlayerIntent(Vector2 movement, bool sneak, bool disguise, bool attack, bool run)
        {
            Movement = movement;
            Sneak = sneak;
            Disguise = disguise;
            Attack = attack;
            Run = run;
        }

        public Vector2 Movement { get; }
        public bool Sneak { get; }
        public bool Disguise { get; }
        public bool Attack { get; }

        /// <summary>奔跑键按住。规则按按下沿切换走 / 跑，长按不反复切；潜行按住时临时压过奔跑，见 <c>PlayerRules.Step</c>。</summary>
        public bool Run { get; }
    }
}
