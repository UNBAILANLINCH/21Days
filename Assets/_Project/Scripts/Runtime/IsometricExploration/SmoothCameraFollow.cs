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
