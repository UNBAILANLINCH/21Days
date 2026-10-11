// 职责：小人的表现门面——持有 SpriteRenderer 与 Animator，对外暴露朝向（补间翻面 / 朝向指定点并保持）、移动状态（走 / 跑）、
//   整体染色、程序化交互动作几个口子；另记录本预制体剪辑的制作地速与有无 run 剪辑（生成工具写入），供驱动层标定播放速率。
// 为什么新建（project-root.md「加能力的顺序」）：
//   1. 复用不行：EncounterSceneView 只驱动单张纸片（位置、flipX、状态色），没有分件与 Animator 概念；
//   2. 扩展不行：把分件动画塞进 EncounterSceneView 会让遭遇视图依赖具体美术结构，以后换 Spine 要改遭遇代码。
// 转身补间 / FaceTowards / 交互动作是对既有门面的扩展（D6 角色动画补齐）：都只改本预制体内的表现变换，
//   不写 Animator 参数，所以「同一小人只有一个驱动者写参数」的约束不变。
// 时间口径：两个补间都走 LitMotion 的 UpdateIgnoreTimeScale（不受 timeScale 影响），对白时停期间照样能转身、能做动作；
//   句柄在 OnDisable / OnDestroy 掐断，掐断时转身直接落到目标朝向、交互动作复位。
using LitMotion;
using UnityEngine;

namespace Game.CharacterPuppet
{
    [DisallowMultipleComponent]
    public sealed class ChibiPuppet : MonoBehaviour
    {
        private static readonly int MovingHash = Animator.StringToHash("Moving");
        private static readonly int RunningHash = Animator.StringToHash("Running");
        private static readonly int SpeedHash = Animator.StringToHash("Speed");

        /// <summary>
        /// 翻面经过 0 时根缩放的最小幅值（占原幅值的比例）：缩放恰为 0 时变换矩阵奇异，
        /// 依赖逆矩阵的材质运算（法线、深度偏移）可能算出 NaN 闪一帧；夹到 1% 肉眼看不出差别。
        /// </summary>
        private const float MinTurnScaleRatio = 0.01f;

        [SerializeField] private Animator animator;
        [Tooltip("全部分件渲染器（腿、躯干、臂、头、发）；SetTint 以各自预制体里的颜色为基底相乘")]
        [SerializeField] private SpriteRenderer[] parts;
        [Tooltip("走路剪辑按每秒多少单位的地速制作：实际速度等于它时按原速播放（生成工具从 meta.json animations.walk.groundSpeed 写入）")]
        [SerializeField, Min(0.01f)] private float walkClipSpeed = 3f;
        [Tooltip("奔跑剪辑按每秒多少单位的地速制作（生成工具从 meta.json animations.run.groundSpeed 写入）")]
        [SerializeField, Min(0.01f)] private float runClipSpeed = 5f;
        [Tooltip("控制器的 Run 态是否有独立的 run 剪辑；没有时 Run 态复用 walk 剪辑，驱动层永不置 Running")]
        [SerializeField] private bool hasRunClip;
        [Tooltip("共用参数资产（与 ChibiPuppetMotion 同一份）：转身补间时长、交互挤压回弹、FaceTowards 的朝向死区。"
                 + "为空时转身瞬间完成、交互动作不播、死区按 0")]
        [SerializeField] private ChibiPuppetConfig config;

        private Color[] baseColors;
        private float baseScaleX = 1f;
        private bool faceLeft;
        private bool moving;
        private bool running;
        private bool facingHeld;
        private Camera cachedCamera;

        private MotionHandle turnMotion;
        private float turnFromX;
        private float turnToX;

        private Transform pulseTarget;
        private Vector3 pulseBaseScale = Vector3.one;
        private Quaternion pulseBaseRotation = Quaternion.identity;
        private MotionHandle pulseMotion;

        public Animator Animator => animator;

        /// <summary>目标朝向（真 = 朝左）。转身补间进行中返回的是要转到的朝向，不是中途的缩放。</summary>
        public bool FaceLeft => faceLeft;

        public bool IsMoving => moving;
        public bool IsRunning => running;
        public float WalkClipSpeed => walkClipSpeed;
        public float RunClipSpeed => runClipSpeed;
        public bool HasRunClip => hasRunClip;

        /// <summary>转身补间是否在进行（根 localScale.x 还没到 ±原幅值）。</summary>
        public bool IsTurning => turnMotion.IsActive();

        /// <summary>
        /// 朝向保持中：<see cref="FaceTowards"/> 之后、小人真正移动之前为真。驱动层（ChibiPuppetMotion）读它，
        /// 为真时不按位移 / 纸片改朝向；<see cref="SetMoving"/> 收到「在移动」即解除。
        /// </summary>
        public bool FacingHeld => facingHeld;

        /// <summary>程序化交互动作是否在播。</summary>
        public bool IsPulsing => pulseMotion.IsActive();

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            float scaleX = transform.localScale.x;
            baseScaleX = scaleX < 0f ? -scaleX : scaleX;
            faceLeft = scaleX < 0f;

            int count = parts == null ? 0 : parts.Length;
            baseColors = new Color[count];
            for (int i = 0; i < count; i++)
            {
                baseColors[i] = parts[i] == null ? Color.white : parts[i].color;
            }

            pulseTarget = ResolvePulseTarget();
            if (pulseTarget != null)
            {
                pulseBaseScale = pulseTarget.localScale;
                pulseBaseRotation = pulseTarget.localRotation;
            }
        }

        private void OnDisable()
        {
            StopTurn();
            StopPulse();
        }

        private void OnDestroy()
        {
            StopTurn();
            StopPulse();
        }

        /// <summary>转身：带补间翻面（时长 config.turnSeconds，填 0 即瞬间），见 <see cref="SetFacing(bool, bool)"/>。</summary>
        public void SetFacing(bool left)
        {
            SetFacing(left, false);
        }

        /// <summary>
        /// 转身：根节点 localScale.x 从当前值连续过渡到 ±原幅值（保留实例整体缩放），像纸片翻面，中途经过 0。
        /// <paramref name="instant"/> 为真时一帧到位——出生、读档、初始化这类不该看到翻面的时刻用。
        /// <see cref="FaceLeft"/> 立刻返回目标朝向；补间途中再改向从当前缩放接着补，不跳变；往同一朝向重复调用不重启补间。
        /// </summary>
        public void SetFacing(bool left, bool instant)
        {
            faceLeft = left;
            float target = left ? -baseScaleX : baseScaleX;
            float current = transform.localScale.x;
            float seconds = instant || config == null || !isActiveAndEnabled
                ? 0f
                : ChibiPuppetMotionRules.TurnDuration(current, target, baseScaleX, config.TurnSeconds);
            if (seconds <= 0f)
            {
                CancelTurn();
                SetScaleX(target);
                return;
            }

            if (turnMotion.IsActive() && (turnToX < 0f) == left)
            {
                return;
            }

            CancelTurn();
            turnFromX = current;
            turnToX = target;
            turnMotion = LMotion.Create(0f, 1f, seconds)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(this, (progress, self) => self.ApplyTurn(progress));
        }

        /// <summary>
        /// 转向世界坐标 <paramref name="worldPosition"/>：按它在主相机右方向上相对自身的投影判左右（与驱动层朝向判定同一口径、同一死区），
        /// 再走补间转身，并进入朝向保持（<see cref="FacingHeld"/>）：小人真正移动之前，驱动层不会按位移 / 纸片把朝向改回去。
        /// 只是表现层转身，角色的逻辑朝向（隐藏纸片的 flipX、玩家 / 怪物模型的 Facing）不变。取不到主相机时保持当前朝向。
        /// </summary>
        public void FaceTowards(Vector3 worldPosition)
        {
            facingHeld = true;
            if (cachedCamera == null)
            {
                cachedCamera = Camera.main;
                if (cachedCamera == null)
                {
                    return;
                }
            }

            float alongRight = Vector3.Dot(worldPosition - transform.position, cachedCamera.transform.right);
            float deadZone = config == null ? 0f : config.FacingDeadZone;
            SetFacing(ChibiPuppetMotionRules.ResolveFacingTowards(alongRight, deadZone, faceLeft));
        }

        /// <summary>
        /// 写 Animator 参数（唯一写入口）：Moving 切待机 / 移动，Running 在 Walk / Run 间切换（不在移动时一律视为 false），
        /// playbackRate 写入 Speed 作为 Walk / Run 状态的播放速率。收到「在移动」时解除朝向保持。
        /// </summary>
        public void SetMoving(bool isMoving, bool isRunning, float playbackRate)
        {
            moving = isMoving;
            running = isMoving && isRunning;
            facingHeld = ChibiPuppetMotionRules.KeepFacingHold(facingHeld, isMoving);
            if (animator == null)
            {
                return;
            }

            animator.SetBool(MovingHash, moving);
            animator.SetBool(RunningHash, running);
            animator.SetFloat(SpeedHash, playbackRate);
        }

        /// <summary>
        /// 程序化交互动作（占位，等美术出 interact 帧）：对 Sprite 子物体做一次挤压回弹——Y 缩放 1 → 1 − squash → 1 + overshoot → 1，
        /// 下压时绕脚底朝面向一侧前倾。时长与幅度取 config；连续调用从头重播，不叠加。
        /// 只改子物体的 transform：帧动画剪辑只写子物体 SpriteRenderer.m_Sprite，根缩放归转身，三者互不覆盖。
        /// </summary>
        public void PlayInteractPulse()
        {
            StopPulse();
            if (pulseTarget == null || config == null || config.InteractPulseSeconds <= 0f || !isActiveAndEnabled)
            {
                return;
            }

            pulseMotion = LMotion.Create(0f, 1f, config.InteractPulseSeconds)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(this, (progress, self) => self.ApplyPulse(progress));
        }

        /// <summary>整体染色：各分件颜色 = 预制体基底色 × tint；传白色即还原。</summary>
        public void SetTint(Color tint)
        {
            if (parts == null || baseColors == null)
            {
                return;
            }

            for (int i = 0; i < parts.Length && i < baseColors.Length; i++)
            {
                if (parts[i] != null)
                {
                    parts[i].color = baseColors[i] * tint;
                }
            }
        }

        private void ApplyTurn(float progress)
        {
            SetScaleX(ChibiPuppetMotionRules.TurnScaleX(turnFromX, turnToX, progress));
        }

        private void ApplyPulse(float progress)
        {
            ChibiPuppetMotionRules.InteractPulse(progress, config.InteractSquash, config.InteractOvershoot,
                out float scaleY, out float lean01);
            Vector3 scale = pulseBaseScale;
            scale.y *= scaleY;
            pulseTarget.localScale = scale;
            // 局部绕 −Z 转 = 顶部往局部 +X（帧按朝右画，局部 +X 即正面）倾；根 localScale.x 取负翻面时一并镜像，前倾始终朝面向一侧。
            pulseTarget.localRotation = pulseBaseRotation * Quaternion.Euler(0f, 0f, -config.InteractLeanDegrees * lean01);
        }

        private void SetScaleX(float x)
        {
            float min = baseScaleX * MinTurnScaleRatio;
            if (x > -min && x < min)
            {
                x = x < 0f ? -min : min;
            }

            Vector3 scale = transform.localScale;
            scale.x = x;
            transform.localScale = scale;
        }

        private void CancelTurn()
        {
            if (turnMotion.IsActive())
            {
                turnMotion.Cancel();
            }
        }

        /// <summary>掐断转身补间并直接落到目标朝向（停用 / 销毁时用，不留半翻的纸片）。</summary>
        private void StopTurn()
        {
            if (!turnMotion.IsActive())
            {
                return;
            }

            turnMotion.Cancel();
            SetScaleX(faceLeft ? -baseScaleX : baseScaleX);
        }

        /// <summary>掐断交互动作并把 Sprite 子物体复位到 Awake 时记下的缩放与旋转。</summary>
        private void StopPulse()
        {
            if (pulseMotion.IsActive())
            {
                pulseMotion.Cancel();
            }

            if (pulseTarget != null)
            {
                pulseTarget.localScale = pulseBaseScale;
                pulseTarget.localRotation = pulseBaseRotation;
            }
        }

        /// <summary>交互动作作用的子物体：parts 里第一个不在根上的渲染器（序列帧小人即唯一的 Sprite 子物体）。</summary>
        private Transform ResolvePulseTarget()
        {
            if (parts == null)
            {
                return null;
            }

            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] != null && parts[i].transform != transform)
                {
                    return parts[i].transform;
                }
            }

            return null;
        }
    }
}
