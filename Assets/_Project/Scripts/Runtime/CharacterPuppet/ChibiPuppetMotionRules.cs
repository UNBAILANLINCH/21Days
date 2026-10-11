// 职责：拼接小人的运动判定规则——位移换算速度、起步 / 停步滞回、跑 / 走滞回、按剪辑地速标定的播放速率、朝向死区；
//   以及门面程序化表现的纯数学：转身补间（时长折算、插值曲线）、朝向指定点、朝向保持、交互挤压回弹曲线。
//   纯 C#，不依赖 UnityEngine，便于 EditMode 穷举。
// 表现曲线为什么放这里而不另建文件（加能力的顺序第 2 步）：转身与朝向保持和既有的朝向判定是同一件事的两半，
//   交互回弹只是几条插值，规模撑不起独立的类；拆开反而让「朝向口径一致」散落两处。
// 为什么新建（project-root.md「加能力的顺序」）：
//   1. 复用不行：EncounterProjection.ResolveFlipX 只管纸片 flipX 的阈值，没有速度滞回与播放速率；
//   2. 扩展不行：CharacterPuppet 是新模块，塞进 Monster / IsometricExploration 的规则类会让两边职责混杂。
namespace Game.CharacterPuppet
{
    public static class ChibiPuppetMotionRules
    {
        /// <summary>交互回弹下压段结束的进度（0..1）：前 30% 时间压下去。</summary>
        private const float PulseSquashEnd = 0.3f;

        /// <summary>交互回弹弹起段结束的进度：30%～65% 从最低弹到过冲顶点，余下 35% 落回原高。</summary>
        private const float PulseReboundEnd = 0.65f;

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

        /// <summary>
        /// 朝向指定点：<paramref name="targetOffsetAlongRight"/> =（目标 − 自身）在相机右方向上的投影。
        /// 与 <see cref="ResolveFacing"/> 同一口径：死区内保持当前朝向，负为朝左、正为朝右。
        /// </summary>
        public static bool ResolveFacingTowards(float targetOffsetAlongRight, float deadZone, bool currentFaceLeft)
        {
            return ResolveFacing(targetOffsetAlongRight, deadZone, currentFaceLeft);
        }

        /// <summary>
        /// 朝向保持：门面 FaceTowards 之后进入保持，驱动层不再按位移 / 纸片改朝向；小人一旦真正移动（<paramref name="moving"/> 为真）
        /// 就解除，朝向回到按位移决定。返回保持是否继续。
        /// </summary>
        public static bool KeepFacingHold(bool held, bool moving)
        {
            return held && !moving;
        }

        /// <summary>
        /// 转身补间耗时：按剩余行程占「整面翻转」（+幅值 → −幅值，行程 2 × 幅值）的比例折算 <paramref name="turnSeconds"/>，
        /// 补间途中改向时剩多少走多少，翻面速度不变。行程为 0、幅值或时长非正时返回 0（立即到位）。
        /// </summary>
        public static float TurnDuration(float fromScaleX, float toScaleX, float baseMagnitude, float turnSeconds)
        {
            if (turnSeconds <= 0f || baseMagnitude <= 0f)
            {
                return 0f;
            }

            float distance = toScaleX - fromScaleX;
            if (distance < 0f)
            {
                distance = -distance;
            }

            float ratio = distance / (2f * baseMagnitude);
            return turnSeconds * (ratio > 1f ? 1f : ratio);
        }

        /// <summary>
        /// 转身补间进度 <paramref name="progress"/>（0..1，越界夹到两端）处的根 localScale.x：smoothstep 缓入缓出，
        /// 近似纸片绕竖轴匀速转动时宽度的余弦投影；从 +幅值到 −幅值中途经过 0。
        /// </summary>
        public static float TurnScaleX(float fromScaleX, float toScaleX, float progress)
        {
            return fromScaleX + (toScaleX - fromScaleX) * SmoothStep(progress);
        }

        /// <summary>
        /// 交互挤压回弹曲线：进度 <paramref name="progress"/>（0..1）处 Sprite 子物体的 Y 缩放倍数与前倾程度（0..1）。
        /// Y：1 → 1 − squash（前 30%）→ 1 + overshoot（30%～65%）→ 1（余下），三段各自 smoothstep，段间连续；
        /// 前倾随下压升到 1、随弹起回到 0，落回段不倾斜。两端都是（1, 0），播完不留残余。
        /// </summary>
        public static void InteractPulse(float progress, float squash, float overshoot, out float scaleY, out float lean01)
        {
            float t = Clamp01(progress);
            if (t < PulseSquashEnd)
            {
                float s = SmoothStep(t / PulseSquashEnd);
                scaleY = 1f - squash * s;
                lean01 = s;
                return;
            }

            if (t < PulseReboundEnd)
            {
                float s = SmoothStep((t - PulseSquashEnd) / (PulseReboundEnd - PulseSquashEnd));
                scaleY = 1f - squash + (squash + overshoot) * s;
                lean01 = 1f - s;
                return;
            }

            float settle = SmoothStep((t - PulseReboundEnd) / (1f - PulseReboundEnd));
            scaleY = 1f + overshoot - overshoot * settle;
            lean01 = 0f;
        }

        private static float SmoothStep(float progress)
        {
            float t = Clamp01(progress);
            return t * t * (3f - 2f * t);
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }
    }
}
