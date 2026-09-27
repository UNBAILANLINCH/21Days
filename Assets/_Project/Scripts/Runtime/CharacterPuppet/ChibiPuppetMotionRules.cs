// 职责：拼接小人的运动判定规则——位移换算速度、起步 / 停步滞回、跑 / 走滞回、按剪辑地速标定的播放速率、朝向死区；纯 C#，不依赖 UnityEngine，便于 EditMode 穷举。
// 为什么新建（project-root.md「加能力的顺序」）：
//   1. 复用不行：EncounterProjection.ResolveFlipX 只管纸片 flipX 的阈值，没有速度滞回与播放速率；
//   2. 扩展不行：CharacterPuppet 是新模块，塞进 Monster / IsometricExploration 的规则类会让两边职责混杂。
namespace Game.CharacterPuppet
{
    public static class ChibiPuppetMotionRules
    {
        /// <summary>
        /// 由本帧位移判定是否在走：速度 = 位移 / dt；静止时速度达到 moveStart 才起步，
        /// 走动中速度降到 moveStop 及以下才停（滞回，避免阈值附近来回抖）。dt &lt;= 0 视为静止。
        /// </summary>
        public static bool Evaluate(float deltaMagnitude, float dt, float moveStart, float moveStop, bool wasMoving,
            out float speed)
        {
            if (dt <= 0f)
            {
                speed = 0f;
                return false;
            }

            speed = (deltaMagnitude < 0f ? -deltaMagnitude : deltaMagnitude) / dt;
            return wasMoving ? speed > moveStop : speed >= moveStart;
        }

        /// <summary>
        /// 跑 / 走判定（只在已判定为移动时调用）：走着时速度达到 runStart 才切跑，跑着时速度降到 runStop 及以下才切回走
        /// （滞回，速度在两档之间抖动时不来回切）。没有 run 剪辑时永远不跑——此时奔跑由 walk 剪辑提速表现。
        /// </summary>
        public static bool ResolveRunning(float speed, float runStartSpeed, float runStopSpeed, bool wasRunning,
            bool hasRunClip)
        {
            if (!hasRunClip)
            {
                return false;
            }

            return wasRunning ? speed > runStopSpeed : speed >= runStartSpeed;
        }

        /// <summary>
        /// 朝向：沿「右」方向的分量绝对值不超过死区时保持上一次朝向；否则负为朝左、正为朝右。
        /// 分量与死区单位一致即可（调用方传位移或每秒速度都行）。
        /// </summary>
        public static bool ResolveFacing(float deltaAlongRight, float deadZone, bool previousFaceLeft)
        {
            if (deltaAlongRight <= deadZone && deltaAlongRight >= -deadZone)
            {
                return previousFaceLeft;
            }

            return deltaAlongRight < 0f;
        }

        /// <summary>
        /// 播放速率 = clamp(speed / clipSpeed, min, max)。clipSpeed 是该剪辑制作时对应的地速（单位/秒），
        /// 实际速度等于它时按原速播放，脚步与位移对得上；clipSpeed ≤ 0（数据缺失）时按原速 1 再夹取。
        /// </summary>
        public static float PlaybackRate(float speed, float clipSpeed, float min, float max)
        {
            float rate = clipSpeed > 0f ? speed / clipSpeed : 1f;
            if (rate < min)
            {
                return min;
            }

            return rate > max ? max : rate;
        }
    }
}
