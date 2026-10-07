// 职责：保持初始构图偏移，并平滑跟随目标位置；设了边界时按 CameraConstraintRules 约束镜头位置（roadmap A6）。
// 为什么新建：工程内没有可复用的摄像机跟随组件，CameraBillboard 只负责视觉朝向。
// 执行顺序：必须排在 EncounterSceneView（默认 0，LateUpdate 里把逻辑位置插值写成本帧的角色 Transform 位置）
//   之后、ChibiPuppetMotion（100）之前，保证本组件跟随的是本帧刚投影好的位置，不早不晚。
// A6 接线（2026-10-07，PRP/world-scenes §2.3 方案①）：边界由**作者在 Inspector 上摆**（本组件上的序列化字段），
//   判定在 Game.World 的共享策略里。**不设边界（Use Bounds 没勾）时行为与接线前逐字一致**：
//   目标点原样交给 SmoothDamp，约束一行都不参与——所以没摆边界的场景零变化。
using UnityEngine;

namespace Game.IsometricExploration
{
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(50)]
    public sealed class SmoothCameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private IsometricExplorationConfig config;

        [Header("镜头约束（A6；不设边界 = 接线前的行为）")]
        [Tooltip("勾上才做边界与死区约束。**不勾 = 旧行为**：目标点原样交给 SmoothDamp。")]
        [SerializeField] private bool useBounds;

        [Tooltip("边界矩形中心（XZ，世界坐标）：画面能看到的世界范围的中心。作者在场景里摆。")]
        [SerializeField] private Vector2 boundsCenter;

        [Tooltip("边界矩形尺寸（x = 世界 x 方向的宽度，y = 世界 z 方向的高度）。")]
        [SerializeField] private Vector2 boundsSize = new Vector2(20f, 12f);

        [Tooltip("死区尺寸：跟随点在这个范围内移动时镜头不动（抑制小碎步抖动）。全 0 = 严丝合缝跟随。")]
        [SerializeField] private Vector2 deadZoneSize = new Vector2(2f, 2f);

        [Tooltip("相机视野在世界 XZ 上的尺寸；全 0 = 不做取景修正（边界就是镜头中心的活动范围）。")]
        [SerializeField] private Vector2 viewportSize;

        // 约束策略：默认自己一份（便于单场景独立跑），WorldSceneState 在进入世界场景时会把容器里那份
        // **共享的**推过来（见 BindConstraintPolicy），好让 A6 的另一半（进对话时的构图切换）共用同一个状态。
        private CameraConstraintPolicy policy = new CameraConstraintPolicy();

        private Vector3 offset;
        private Vector3 velocity;

#if UNITY_EDITOR
        // 临时视角对比，仅编辑器显示；确认构图后删除此调试入口。
        private Vector3 comparisonOriginalOffset;
        private Quaternion comparisonOriginalRotation;
        private bool comparisonInitialized;
        private int comparisonAngleIndex;
        private static readonly string[] ComparisonLabels = { "原视角", "30°", "25°", "20°" };
        private static readonly float[] ComparisonPitches = { 0f, 30f, 25f, 20f };

        private void OnGUI()
        {
            if (target == null || gameObject.scene.name != "SampleScene") return;
            int selected = GUI.Toolbar(new Rect(Screen.width - 340f, Screen.height - 72f, 320f, 48f),
                comparisonAngleIndex, ComparisonLabels);
            if (selected != comparisonAngleIndex) SelectComparisonAngle(selected);
        }

        public void SelectComparisonAngle(int index)
        {
            if (target == null || index < 0 || index >= ComparisonPitches.Length) return;
            if (!comparisonInitialized)
            {
                comparisonOriginalOffset = offset;
                comparisonOriginalRotation = transform.rotation;
                comparisonInitialized = true;
            }
            if (index != 0)
            {
                Vector3 originalAngles = comparisonOriginalRotation.eulerAngles;
                Quaternion rotation = Quaternion.Euler(ComparisonPitches[index], originalAngles.y, originalAngles.z);
                // 围绕人物胸口转动，保留人物在画面中的尺寸和位置。
                Vector3 pivot = Vector3.up * 0.8f;
                offset = pivot + rotation * Quaternion.Inverse(comparisonOriginalRotation)
                    * (comparisonOriginalOffset - pivot);
                transform.rotation = rotation;
            }
            else
            {
                offset = comparisonOriginalOffset;
                transform.rotation = comparisonOriginalRotation;
            }
            comparisonAngleIndex = index;
            velocity = Vector3.zero;
            transform.position = target.position + offset;
        }
#endif

        public Transform Target => target;

        /// <summary>当前生效的约束策略（没设边界时 <see cref="CameraConstraintPolicy.HasBounds"/> 为 false）。</summary>
        public CameraConstraintPolicy ConstraintPolicy => policy;

        public void SetTarget(Transform next)
        {
            target = next;
            velocity = Vector3.zero;
        }

        /// <summary>
        /// 由容器侧（<c>Game.World.WorldSceneState</c>）把**共享**策略推进来：推之前本组件用自己的那份，
        /// 推之后用共享的那份，并把本组件 Inspector 上摆的边界灌进去（重复绑定是幂等的）。
        /// </summary>
        public void BindConstraintPolicy(CameraConstraintPolicy shared)
        {
            if (shared == null)
            {
                return;
            }

            policy = shared;
            ApplyBounds();
        }

        /// <summary>
        /// 脚本入口：写的就是 Inspector 上那几列（灰盒布置脚本 / 编辑器工具 / EditMode 测试用；
        /// EditMode 里 <c>Start</c> 不会跑，构造不出「场景里摆好的状态」，所以要有这条能直接写的路）。
        /// </summary>
        public void SetConstraintBounds(Vector2 center, Vector2 size, Vector2 deadZone = default, Vector2 viewport = default)
        {
            useBounds = true;
            boundsCenter = center;
            boundsSize = size;
            deadZoneSize = deadZone;
            viewportSize = viewport;
            ApplyBounds();
        }

        /// <summary>脚本入口：取消约束（回到接线前的行为）。等价于 Inspector 上取消勾选。</summary>
        public void ClearConstraintBounds()
        {
            useBounds = false;
            ApplyBounds();
        }

        /// <summary>
        /// 初始构图偏移（= 相机位置 − 跟随目标位置，在 <c>Start</c> 里取一次）。
        /// </summary>
        public Vector3 Offset => offset;

        /// <summary>设置构图偏移并清掉缓动速度（场景布置脚本按世界坐标摆好相机后再设一次；EditMode 测试也用它）。</summary>
        public void SetOffset(Vector3 value)
        {
            offset = value;
            velocity = Vector3.zero;
        }

        /// <summary>
        /// 这一帧镜头想在哪：跟随目标 + 构图偏移，有边界时再经 <see cref="CameraConstraintPolicy"/> 约束。
        /// <b>public 是为了能在 EditMode 里测</b>：EditMode 没有帧循环，<c>LateUpdate</c> 不会被驱动，
        /// 而「表现层真的在调约束规则」这条判定只能从这个入口验。
        /// </summary>
        public Vector3 ResolveDesiredPosition()
        {
            Vector3 follow = target.position + offset;

            // 不设边界 = 旧行为：follow 原样返回，与接线前的 target.position + offset 逐字一致。
            if (policy != null && policy.TryResolve(transform.position, follow, out Vector3 constrained))
            {
                return constrained;
            }

            return follow;
        }

        private void Awake()
        {
            // 自己跑（没有容器侧推策略）时也要让 Inspector 上摆的边界生效。
            ApplyBounds();
        }

        private void Start()
        {
            if (target != null)
            {
                offset = transform.position - target.position;
            }
        }

        private void LateUpdate()
        {
            if (target == null || config == null)
            {
                return;
            }

            transform.position = Vector3.SmoothDamp(
                transform.position,
                ResolveDesiredPosition(),
                ref velocity,
                config.CameraSmoothTime);
        }

        // 把 Inspector（或 SetConstraintBounds / ClearConstraintBounds 写进去）的边界灌进当前策略。
        // 没勾「有边界」时清掉约束——这一句就是「不设边界 = 旧行为」的落点。
        private void ApplyBounds()
        {
            if (!useBounds)
            {
                policy.ClearBounds();
                return;
            }

            policy.SetBounds(boundsCenter, boundsSize, deadZoneSize, viewportSize);
        }
    }
}
