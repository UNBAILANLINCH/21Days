// 职责：保持初始构图偏移，并平滑跟随目标位置。
// 为什么新建：工程内没有可复用的摄像机跟随组件，CameraBillboard 只负责视觉朝向。
using UnityEngine;

namespace Game.IsometricExploration
{
    [RequireComponent(typeof(Camera))]
    public sealed class SmoothCameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private IsometricExplorationConfig config;

        private Vector3 offset;
        private Vector3 velocity;

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
