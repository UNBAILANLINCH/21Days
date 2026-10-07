using System.Collections.Generic;
using UnityEngine;

namespace Game.LailaFace
{
    // 职责：缓存 Head-topo 的 BlendShape 名称，并把成对的 Up/Down 权重映射成一个可拖拽的有符号值。
    // 新建原因：工程内没有可复用的面部 BlendShape 控制器；控制区和材质表现不应各自直接写 Renderer。
    // 编辑态载体：启用组件订阅 EditorApplication.update；5秒检查：调嘴/眉Key后其Transform与抓点同步。
    // 退场：禁用/销毁时取消订阅；不再需要编辑态形变预览时删除该订阅与预览分支。
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class FaceBlendShapeController : MonoBehaviour
    {
        [SerializeField]
        private SkinnedMeshRenderer faceRenderer;

        private readonly Dictionary<string, int> indices = new();
        private readonly List<Vector3> surfaceVertices = new();
        private readonly List<Vector3> surfaceNormals = new();
        private readonly List<Vector3> referenceVertices = new();
        private Mesh surfaceMesh;
        private int surfaceFrame = -1;
#if UNITY_EDITOR
        private int previewFrame;
        private FaceDragHandle[] previewHandles;
#endif

        public int BlendShapeCount => indices.Count;

        private void Awake()
        {
            if (Application.isPlaying || faceRenderer != null) RebuildCache();
        }

        private void OnEnable()
        {
            if (faceRenderer != null) RebuildCache();
#if UNITY_EDITOR
            previewHandles = GetComponentsInChildren<FaceDragHandle>();
            UnityEditor.EditorApplication.update -= RefreshEditorPoints;
            UnityEditor.EditorApplication.update += RefreshEditorPoints;
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= RefreshEditorPoints;
#endif
        }

#if UNITY_EDITOR
        private void OnTransformChildrenChanged()
        {
            previewHandles = GetComponentsInChildren<FaceDragHandle>();
        }

        private void RefreshEditorPoints()
        {
            if (this == null || Application.isPlaying || !isActiveAndEnabled || !gameObject.scene.IsValid() || previewHandles == null) return;
            previewFrame++;
            foreach (FaceDragHandle handle in previewHandles)
                if (handle != null && handle.isActiveAndEnabled) handle.RefreshGrabPoint();
        }
#endif

        [ContextMenu("Rebuild BlendShape Cache")]
        public void RebuildCache()
        {
            indices.Clear();
            referenceVertices.Clear();
            surfaceFrame = -1;

            if (faceRenderer == null || faceRenderer.sharedMesh == null)
            {
                Debug.LogError("FaceBlendShapeController：没有指定 Head-topo 的 Face Renderer。");
                return;
            }

            Mesh mesh = faceRenderer.sharedMesh;

            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string shapeName = mesh.GetBlendShapeName(i);
                indices[shapeName] = i;
            }

            Debug.Log($"Head-topo BlendShape 缓存完成，共 {indices.Count} 个。");
        }

        public bool HasShape(string shapeName)
        {
            return !string.IsNullOrEmpty(shapeName) && indices.ContainsKey(shapeName);
        }

        public bool TrySetWeight(string shapeName, float value)
        {
            if (!indices.TryGetValue(shapeName, out int index))
            {
                return false;
            }

            faceRenderer.SetBlendShapeWeight(index, Mathf.Clamp(value, 0f, 100f)); // lint-ok: 仅表现层 BlendShape 权重映射，不参与逻辑回放
            surfaceFrame = -1;
            return true;
        }

        public void SetWeight(string shapeName, float value)
        {
            if (!TrySetWeight(shapeName, value))
            {
                Debug.LogError($"Head-topo 找不到 BlendShape：{shapeName}");
            }
        }

        public float GetWeight(string shapeName)
        {
            if (!indices.TryGetValue(shapeName, out int index))
            {
                return 0f;
            }

            return faceRenderer.GetBlendShapeWeight(index);
        }

        /// <summary>
        /// -1 = Down 100, 0 = Basis, +1 = Up 100。
        /// </summary>
        public bool TrySetSignedPair(string upShape, string downShape, float value)
        {
            value = Mathf.Clamp(value, -1f, 1f); // lint-ok: 仅表现层 BlendShape 权重映射，不参与逻辑回放

            float upWeight = Mathf.Max(value, 0f) * 100f; // lint-ok: 仅表现层 BlendShape 权重映射，不参与逻辑回放
            float downWeight = Mathf.Max(-value, 0f) * 100f; // lint-ok: 仅表现层 BlendShape 权重映射，不参与逻辑回放

            return TrySetWeight(upShape, upWeight) && TrySetWeight(downShape, downWeight);
        }

        public void SetSignedPair(string upShape, string downShape, float value)
        {
            if (!TrySetSignedPair(upShape, downShape, value))
            {
                Debug.LogError($"Head-topo 的 BlendShape 成对控制无效：{upShape} / {downShape}");
            }
        }

        public float GetSignedPair(string upShape, string downShape)
        {
            float up = GetWeight(upShape) / 100f;
            float down = GetWeight(downShape) / 100f;

            return up - down;
        }

        [ContextMenu("Reset Face")]
        public void ResetFace()
        {
            if (faceRenderer == null || faceRenderer.sharedMesh == null)
            {
                return;
            }

            for (int i = 0; i < faceRenderer.sharedMesh.blendShapeCount; i++)
            {
                faceRenderer.SetBlendShapeWeight(i, 0f);
            }
            surfaceFrame = -1;
        }

        public bool TryGetSurfacePoint(Vector3 anchor, ref int vertex, out Vector3 point, out Vector3 normal,
            Vector3Int triangle = default, Vector3 barycentric = default)
        {
            point = normal = Vector3.zero;
            if (faceRenderer == null || faceRenderer.sharedMesh == null) return false;
            if (surfaceMesh == null)
            {
                surfaceMesh = new Mesh { name = "Face controller surface" };
                surfaceFrame = -1;
            }
            if (referenceVertices.Count == 0) BakeReferenceVertices();
            if (vertex < 0 || vertex >= referenceVertices.Count)
            {
                float best = float.PositiveInfinity;
                for (int i = 0; i < referenceVertices.Count; i++)
                {
                    float distance = (referenceVertices[i] - anchor).sqrMagnitude;
                    if (distance < best) { vertex = i; best = distance; }
                }
            }
            int frame = Time.frameCount;
#if UNITY_EDITOR
            if (!Application.isPlaying) frame = previewFrame;
#endif
            if (surfaceFrame != frame)
            {
                faceRenderer.BakeMesh(surfaceMesh);
                surfaceMesh.GetVertices(surfaceVertices);
                surfaceMesh.GetNormals(surfaceNormals);
                surfaceFrame = frame;
            }
            if (vertex < 0 || vertex >= surfaceVertices.Count) return false;
            point = faceRenderer.transform.TransformPoint(anchor + surfaceVertices[vertex] - referenceVertices[vertex]);
            normal = faceRenderer.transform.TransformDirection(surfaceNormals[vertex]).normalized;
            if (triangle.x >= 0 && triangle.y >= 0 && triangle.z >= 0
                && triangle.x < surfaceVertices.Count && triangle.y < surfaceVertices.Count && triangle.z < surfaceVertices.Count
                && barycentric.x >= 0 && barycentric.y >= 0 && barycentric.z >= 0
                && Mathf.Approximately(barycentric.x + barycentric.y + barycentric.z, 1f)) // lint-ok: 渲染表面插值权重校验，不属于确定性玩法状态
            {
                Vector3 delta = (surfaceVertices[triangle.x] - referenceVertices[triangle.x]) * barycentric.x
                    + (surfaceVertices[triangle.y] - referenceVertices[triangle.y]) * barycentric.y
                    + (surfaceVertices[triangle.z] - referenceVertices[triangle.z]) * barycentric.z;
                Vector3 localNormal = surfaceNormals[triangle.x] * barycentric.x
                    + surfaceNormals[triangle.y] * barycentric.y + surfaceNormals[triangle.z] * barycentric.z;
                point = faceRenderer.transform.TransformPoint(anchor + delta);
                normal = faceRenderer.transform.TransformDirection(localNormal).normalized;
            }
            return true;
        }

        private void BakeReferenceVertices()
        {
            int count = faceRenderer.sharedMesh.blendShapeCount;
            float[] weights = new float[count];
            try
            {
                for (int i = 0; i < count; i++)
                {
                    weights[i] = faceRenderer.GetBlendShapeWeight(i);
                    faceRenderer.SetBlendShapeWeight(i, 0f);
                }
                faceRenderer.BakeMesh(surfaceMesh);
                surfaceMesh.GetVertices(referenceVertices);
            }
            finally
            {
                for (int i = 0; i < count; i++) faceRenderer.SetBlendShapeWeight(i, weights[i]);
                surfaceFrame = -1;
            }
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= RefreshEditorPoints;
#endif
            if (surfaceMesh != null)
            {
                if (Application.isPlaying) Destroy(surfaceMesh);
                else DestroyImmediate(surfaceMesh);
            }
        }
    }
}
