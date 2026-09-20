// 职责：让纸片物体始终与目标摄像机的成像平面平行。
// 为什么新建：工程内没有可复用的 Billboard 组件，现有脚本也不承担物体朝向职责。
using UnityEngine;

namespace Game.IsometricExploration
{
    public sealed class CameraBillboard : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private Renderer visualRenderer;
        [SerializeField, Tooltip("保持竖直，前表面的世界 Z 会对齐纸片底边")]
        private BoxCollider verticalCollider;

        private void Start()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (visualRenderer == null)
            {
                visualRenderer = GetComponent<Renderer>();
            }
        }

        private void LateUpdate()
        {
            if (targetCamera != null)
            {
                transform.rotation = targetCamera.transform.rotation;
                if (visualRenderer != null && verticalCollider != null)
                {
                    Bounds rendererBounds = visualRenderer.localBounds;
                    Vector3 bottomEdgeWorld = visualRenderer.transform.TransformPoint(
                        new Vector3(rendererBounds.center.x, rendererBounds.min.y, rendererBounds.center.z));
                    Vector3 halfDepth = Vector3.forward * (verticalCollider.size.z * 0.5f);
                    Vector3 positiveFace = verticalCollider.transform.TransformPoint(verticalCollider.center + halfDepth);
                    Vector3 negativeFace = verticalCollider.transform.TransformPoint(verticalCollider.center - halfDepth);
                    Vector3 cameraPosition = targetCamera.transform.position;
                    Vector3 frontFace = (positiveFace - cameraPosition).sqrMagnitude
                        <= (negativeFace - cameraPosition).sqrMagnitude ? positiveFace : negativeFace;
                    Vector3 center = verticalCollider.center;
                    center += verticalCollider.transform.InverseTransformVector(
                        Vector3.forward * (bottomEdgeWorld.z - frontFace.z));
                    verticalCollider.center = center;
                }
            }
        }
    }
}
