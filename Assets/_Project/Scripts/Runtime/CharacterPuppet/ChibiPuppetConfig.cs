// 职责：拼接小人驱动参数（起步 / 停步速度阈值、跑 / 走切换阈值、朝向死区、播放速率夹取），运行时只读。
//   剪辑自身的地速（walkClipSpeed / runClipSpeed）随剪辑走，是每个预制体的字段（ChibiPuppet），不在这里。
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
        [Tooltip("走路中速度（单位/秒）达到此值才切到奔跑（仅有 run 剪辑的小人）；落在走 3 与跑 5 之间")]
        [SerializeField, Min(0f)] private float runStartSpeed = 4f;
        [Tooltip("奔跑中速度（单位/秒）降到此值及以下才切回走路；应小于切跑阈值形成滞回")]
        [SerializeField, Min(0f)] private float runStopSpeed = 3.5f;
        [Tooltip("速度采样窗口（秒）：位移攒够这么久再判一次。视图已做逻辑 tick 间插值、位移逐帧连续，"
                 + "窗口只用来抹平单帧噪声；越短起步 / 停步越跟手")]
        [SerializeField, Min(0f)] private float sampleWindow = 0.05f;
        [Tooltip("沿相机右方向的速度（单位/秒）绝对值不超过此值时保持原朝向")]
        [SerializeField, Min(0f)] private float facingDeadZone = 0.01f;
        [Tooltip("播放速率下限：速率 = 速度 / 剪辑地速，再夹到 [rateMin, rateMax]；太低会滑步")]
        [SerializeField, Min(0f)] private float rateMin = 0.8f;
        [Tooltip("播放速率上限：超过就是「快放」，脚步僵硬；需要更快请出 run 剪辑")]
        [SerializeField, Min(0f)] private float rateMax = 1.6f;

        public float MoveStartSpeed => moveStartSpeed;
        public float MoveStopSpeed => moveStopSpeed;
        public float RunStartSpeed => runStartSpeed;
        public float RunStopSpeed => runStopSpeed;
        public float SampleWindow => sampleWindow;
        public float FacingDeadZone => facingDeadZone;
        public float RateMin => rateMin;
        public float RateMax => rateMax;
    }
}
