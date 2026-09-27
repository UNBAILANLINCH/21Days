using System.Collections.Generic;
using UnityEngine;

namespace Game.LailaFace
{
    // 职责：缓存 Head-topo 的 BlendShape 名称，并把成对的 Up/Down 权重映射成一个可拖拽的有符号值。
    // 新建原因：工程内没有可复用的面部 BlendShape 控制器；控制区和材质表现不应各自直接写 Renderer。
    [DisallowMultipleComponent]
    public sealed class FaceBlendShapeController : MonoBehaviour
    {
        [SerializeField]
        private SkinnedMeshRenderer faceRenderer;

        private readonly Dictionary<string, int> indices = new();

        public int BlendShapeCount => indices.Count;

        private void Awake()
        {
            RebuildCache();
        }

        [ContextMenu("Rebuild BlendShape Cache")]
        public void RebuildCache()
        {
            indices.Clear();

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
        }
    }
}
