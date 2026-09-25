// 职责：拼接小人驱动参数（起步 / 停步速度阈值、朝向死区、走路播放速率换算），运行时只读。
// 为什么新建：CharacterPuppet 是新模块，现有 IsometricExplorationConfig 管移动与相机，不该承载动画驱动阈值。
using UnityEngine;

namespace Game.CharacterPuppet
{
    [CreateAssetMenu(menuName = "21Days/CharacterPuppet/Config", fileName = "ChibiPuppetConfig")]
    public sealed class ChibiPuppetConfig : ScriptableObject
    {
        [Tooltip("静止时速度（单位/秒）达到此值才切到走路")]
        [SerializeField, Min(0f)] private float moveStartSpeed = 0.15f;
        [Tooltip("走动中速度（单位/秒）降到此值及以下才回到待机；应小于起步阈值形成滞回")]
        [SerializeField, Min(0f)] private float moveStopSpeed = 0.05f;
        [Tooltip("速度采样窗口（秒）：位移攒够这么久再判一次走 / 停。逻辑 tick（60 Hz）低于渲染帧率时，"
                 + "逐帧判定会在没推进的帧读到 0 速度而闪回待机")]
        [SerializeField, Min(0f)] private float sampleWindow = 0.1f;
        [Tooltip("沿相机右方向的速度（单位/秒）绝对值不超过此值时保持原朝向")]
        [SerializeField, Min(0f)] private float facingDeadZone = 0.01f;
        [Tooltip("走路动画播放速率 = 移动速度 × 此值，再夹到 [walkRateMin, walkRateMax]")]
        [SerializeField, Min(0f)] private float walkCycleSpeedPerUnit = 0.8f;
        [Tooltip("走路动画播放速率下限")]
        [SerializeField, Min(0f)] private float walkRateMin = 0.8f;
        [Tooltip("走路动画播放速率上限")]
        [SerializeField, Min(0f)] private float walkRateMax = 1.6f;

        public float MoveStartSpeed => moveStartSpeed;
        public float MoveStopSpeed => moveStopSpeed;
        public float SampleWindow => sampleWindow;
        public float FacingDeadZone => facingDeadZone;
        public float WalkCycleSpeedPerUnit => walkCycleSpeedPerUnit;
        public float WalkRateMin => walkRateMin;
        public float WalkRateMax => walkRateMax;
    }
}
