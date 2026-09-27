// 职责：独立驯服原型的移动与控制切换意图；不能复用带攻击和伪装的 PlayerIntent。
using UnityEngine;

namespace Game.Taming
{
    public readonly struct TamingIntent
    {
        /// <param name="run">玩家奔跑状态，原样透传给 PlayerIntent。末尾可选参数：老调用不改，默认步行。</param>
        public TamingIntent(Vector2 movement, bool toggleControl, bool run = false)
        {
            Movement = movement;
            ToggleControl = toggleControl;
            Run = run;
        }

        public Vector2 Movement { get; }
        public bool ToggleControl { get; }
        public bool Run { get; }
    }
}
