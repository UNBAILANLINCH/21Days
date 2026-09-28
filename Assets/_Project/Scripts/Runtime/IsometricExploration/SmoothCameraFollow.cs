// 职责：保持初始构图偏移，并平滑跟随目标位置。
// 为什么新建：工程内没有可复用的摄像机跟随组件，CameraBillboard 只负责视觉朝向。
// 执行顺序：必须排在 EncounterSceneView（默认 0，LateUpdate 里把逻辑位置插值写成本帧的角色 Transform 位置）
//   之后、ChibiPuppetMotion（100）之前，保证本组件跟随的是本帧刚投影好的位置，不早不晚。
using UnityEngine;

namespace Game.IsometricExploration
{
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(50)]
    public sealed class SmoothCameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private IsometricExplorationConfig config;

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

        public void SetTarget(Transform next)
        {
            target = next;
            velocity = Vector3.zero;
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
                target.position + offset,
                ref velocity,
                config.CameraSmoothTime);
        }
    }
}
