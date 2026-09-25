// 职责：每帧读角色根的位移，经 ChibiPuppetMotionRules 判定走 / 停与朝向，写给同根的 ChibiPuppet。
// 为什么新建（project-root.md「加能力的顺序」）：
//   1. 复用不行：没有现成组件从 Transform 位移反推动画状态；EncounterSceneView 只算 flipX；
//   2. 扩展不行：放进 ChibiPuppet 会让表现门面依赖驱动来源——以后改由输入 / AI 直接驱动时只换这个组件。
using UnityEngine;

namespace Game.CharacterPuppet
{
    // 排在 EncounterSceneView（默认 0）之后，读到的是本帧已投影的位置与已翻好的 flipX。
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(ChibiPuppet))]
    public sealed class ChibiPuppetMotion : MonoBehaviour
    {
        private const string VisualNodeName = "Visual";
        private const float IdlePlaybackRate = 1f;

        [SerializeField] private ChibiPuppet puppet;
        [SerializeField] private ChibiPuppetConfig config;
        [Tooltip("读位移的角色根；为空时沿父级往上取第一个名字不是 Visual 的节点，找不到用自身")]
        [SerializeField] private Transform trackedRoot;
        [Tooltip("朝向来源：读它的 flipX（为真 = 朝左）；为空时按位移在主相机右方向上的投影")]
        [SerializeField] private SpriteRenderer facingSource;

        private Camera cachedCamera;
        private Vector3 lastPosition;
        private Vector3 pendingDelta;
        private float pendingTime;
        private bool moving;
        private bool faceLeft;

        public Transform TrackedRoot => trackedRoot;

        private void Awake()
        {
            if (puppet == null)
            {
                puppet = GetComponent<ChibiPuppet>();
            }

            if (trackedRoot == null)
            {
                trackedRoot = ResolveTrackedRoot(transform);
            }
        }

        private void OnEnable()
        {
            lastPosition = trackedRoot.position;
            pendingDelta = Vector3.zero;
            pendingTime = 0f;
            moving = false;
            faceLeft = puppet.FaceLeft;
            puppet.SetMoving(false, IdlePlaybackRate);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime; // lint-ok: 纯表现层，只反推动画状态，不参与逻辑推进与重放
            Vector3 position = trackedRoot.position;
            if (dt <= 0f)
            {
                // 时停（对话 timeScale = 0）：强制切回待机——Animator 走 unscaled time，
                // 不写 Moving=false 的话会带着 Walk 的最后一帧继续被 unscaled 时间推进，看起来像在原地走。
                // 采样窗口清零、lastPosition 更新为当前，避免恢复后把时停期间的位移一次性计入速度导致误判。
                pendingDelta = Vector3.zero;
                pendingTime = 0f;
                lastPosition = position;
                if (moving)
                {
                    moving = false;
                    puppet.SetMoving(false, IdlePlaybackRate);
                }

                return;
            }

            // 位移与时间攒到采样窗口再判：逻辑 tick 慢于渲染帧时，单帧位移会时有时无。
            pendingDelta += position - lastPosition;
            pendingTime += dt;
            lastPosition = position;
            if (pendingTime < config.SampleWindow)
            {
                return;
            }

            Vector3 delta = pendingDelta;
            float window = pendingTime;
            pendingDelta = Vector3.zero;
            pendingTime = 0f;

            float speed;
            moving = ChibiPuppetMotionRules.Evaluate(delta.magnitude, window, config.MoveStartSpeed,
                config.MoveStopSpeed, moving, out speed);
            float rate = moving
                ? ChibiPuppetMotionRules.WalkPlaybackRate(speed, config.WalkCycleSpeedPerUnit, config.WalkRateMin,
                    config.WalkRateMax)
                : IdlePlaybackRate;
            puppet.SetMoving(moving, rate);

            bool nextFaceLeft = ResolveFacing(delta, window);
            if (nextFaceLeft != faceLeft)
            {
                faceLeft = nextFaceLeft;
                puppet.SetFacing(faceLeft);
            }
        }

        private bool ResolveFacing(Vector3 delta, float dt)
        {
            if (facingSource != null)
            {
                return facingSource.flipX;
            }

            if (cachedCamera == null)
            {
                cachedCamera = Camera.main;
                if (cachedCamera == null)
                {
                    return faceLeft;
                }
            }

            // 用每秒速度而不是单帧位移过死区，帧率高低不改变判定。
            float alongRight = Vector3.Dot(delta, cachedCamera.transform.right) / dt;
            return ChibiPuppetMotionRules.ResolveFacing(alongRight, config.FacingDeadZone, faceLeft);
        }

        private static Transform ResolveTrackedRoot(Transform self)
        {
            Transform current = self.parent;
            while (current != null)
            {
                if (current.name != VisualNodeName)
                {
                    return current;
                }

                current = current.parent;
            }

            return self;
        }
    }
}
