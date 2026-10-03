using UnityEngine;

namespace Game.LailaFace
{
    // 职责：只控制 Head-topo 的聊斋材质参数，不创建材质副本。
    // 新建原因：阴影方向与强度需要在 Inspector 中快速调参，不能散落在 Shader 或场景脚本里。
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class MuralFaceController : MonoBehaviour
    {
        [SerializeField]
        private SkinnedMeshRenderer faceRenderer;

        [Header("基础色")]
        [SerializeField]
        private Color baseColor = new(0.82f, 0.64f, 0.48f, 1f);

        [Header("阴影")]
        [SerializeField]
        private Color shadowColor = new(0.34f, 0.20f, 0.18f, 1f);

        [SerializeField]
        private Vector3 shadowLightDirection = new(0.35f, 0.65f, -1f);

        [Range(0f, 1f)]
        [SerializeField]
        private float shadowStrength = 0.55f;

        [Range(0f, 1f)]
        [SerializeField]
        private float shadowThreshold = 0.48f;

        [Range(0.02f, 0.5f)]
        [SerializeField]
        private float shadowSoftness = 0.12f;

        [Tooltip("仅 laila 场景的阴影过渡下限；不降低拖动幅度或整体加亮。设为 0 使用原 Shadow Softness。")]
        [Range(0f, 0.5f)]
        [SerializeField]
        private float lailaMinimumShadowSoftness = 0.28f;

        [Tooltip("仅在 laila Play 中创建抓点提示与右手光标，不保存场景。")]
        [SerializeField]
        private bool lailaPointerFeedback = true;

        [Header("边缘与纸张")]
        [SerializeField]
        private Color rimColor = new(1f, 0.76f, 0.47f, 1f);

        [Range(0.5f, 8f)]
        [SerializeField]
        private float rimPower = 3f;

        [Range(0f, 1f)]
        [SerializeField]
        private float rimStrength = 0.16f;

        [Range(0f, 0.3f)]
        [SerializeField]
        private float grainStrength = 0.08f;

        private MaterialPropertyBlock propertyBlock;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ShadowColorId = Shader.PropertyToID("_ShadowColor");
        private static readonly int ShadowDirectionId = Shader.PropertyToID("_ShadowDirection");
        private static readonly int ShadowStrengthId = Shader.PropertyToID("_ShadowStrength");
        private static readonly int ShadowThresholdId = Shader.PropertyToID("_ShadowThreshold");
        private static readonly int ShadowSoftnessId = Shader.PropertyToID("_ShadowSoftness");
        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        private static readonly int RimPowerId = Shader.PropertyToID("_RimPower");
        private static readonly int RimStrengthId = Shader.PropertyToID("_RimStrength");
        private static readonly int GrainStrengthId = Shader.PropertyToID("_GrainStrength");

        private void Reset()
        {
            faceRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
        }

        private void OnEnable()
        {
            Apply();
        }

        private void OnValidate()
        {
            Apply();
        }

        private void Start()
        {
            if (Application.isPlaying && lailaPointerFeedback
                && gameObject.scene.path == "Assets/_Project/Scenes/laila.unity"
                && GetComponent<Game.Gameplay.FacePointerFeedback>() == null)
            {
                gameObject.AddComponent<Game.Gameplay.FacePointerFeedback>();
            }
        }

        [ContextMenu("Apply Mural Style")]
        public void Apply()
        {
            if (faceRenderer == null)
            {
                faceRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
            }

            if (faceRenderer == null)
            {
                return;
            }

            propertyBlock ??= new MaterialPropertyBlock();
            faceRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, baseColor);
            propertyBlock.SetColor(ShadowColorId, shadowColor);
            propertyBlock.SetVector(ShadowDirectionId, shadowLightDirection);
            propertyBlock.SetFloat(ShadowStrengthId, shadowStrength);
            propertyBlock.SetFloat(ShadowThresholdId, shadowThreshold);
            float softness = gameObject.scene.path == "Assets/_Project/Scenes/laila.unity"
                ? Mathf.Max(shadowSoftness, lailaMinimumShadowSoftness) : shadowSoftness; // lint-ok: 材质阴影过渡参数，不参与玩法重放数值
            propertyBlock.SetFloat(ShadowSoftnessId, softness);
            propertyBlock.SetColor(RimColorId, rimColor);
            propertyBlock.SetFloat(RimPowerId, rimPower);
            propertyBlock.SetFloat(RimStrengthId, rimStrength);
            propertyBlock.SetFloat(GrainStrengthId, grainStrength);
            faceRenderer.SetPropertyBlock(propertyBlock);
        }
    }
}
