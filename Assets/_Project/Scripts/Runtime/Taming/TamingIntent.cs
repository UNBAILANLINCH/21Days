// 职责：独立驯服原型的移动与控制切换意图；不能复用带攻击和伪装的 PlayerIntent。
using UnityEngine;

namespace Game.Taming
{
    public readonly struct TamingIntent
    {
        public TamingIntent(Vector2 movement, bool toggleControl)
        {
            Movement = movement;
            ToggleControl = toggleControl;
        }

        public Vector2 Movement { get; }
        public bool ToggleControl { get; }
    }
}
