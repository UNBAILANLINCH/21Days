// 职责：标记「挡住相机看玩家时要变半透明」的场景几何体，负责在原材质与半透明材质之间切换并做短暂的颜色过渡。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：场景里没有按遮挡切换材质的组件（CameraBillboard 只管朝向与碰撞体校准）；
//   2. 扩展不行：遮挡判定在 OccluderFadePresenter（入口点，每帧一次粗射线扫掠），这里只是被判定的一方，放进呈现器会变成按名字找物体。
using UnityEngine;

namespace Game.IsometricExploration
{
    /// <summary>
    /// 挂在带 Renderer 的灰盒几何体上（SampleScene 里会挡视线的高物体：桥、上层甲板、栏杆、塔、坡道、楼梯、墙）。
    /// 只切 <c>sharedMaterial</c> 引用、用 <see cref="MaterialPropertyBlock"/> 覆盖 <c>_BaseColor</c>，不改任何材质资产的内容。
    /// 过渡（波 10）：变淡时立刻换上半透明材质，颜色从「原色、alpha 1」插值到半透明材质自身的颜色；
    /// 恢复时反向插值到 alpha 1 后换回原材质。过渡结束即清掉属性块，静止时不占 SRP Batcher 以外的开销。
    /// 插值由 <see cref="OccluderFadePresenter"/> 只对「正在过渡」的对象逐帧调用 <see cref="Advance"/> 驱动。
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public sealed class SceneOccluder : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("遮挡玩家时换上的半透明材质（灰盒用 Art/Materials/Graybox/M_Graybox_Faded.mat）；为空时不切换")]
        [SerializeField] private Material fadedMaterial;
        [SerializeField, Min(0f), Tooltip("淡出 / 淡入的过渡秒数；0 = 直接切换材质")]
        private float fadeSeconds = 0.15f;

        private Renderer targetRenderer;
        private Material originalMaterial;
        private MaterialPropertyBlock block;
        private Color opaqueColor;
        private Color fadedColor;
        private float progress; // 0 = 原样，1 = 完全半透明

        /// <summary>目标状态：true 表示应处于半透明（过渡中也算）。</summary>
        public bool IsFaded { get; private set; }

        /// <summary>是否仍在过渡中（需要继续调用 <see cref="Advance"/>）。</summary>
        public bool IsTransitioning => fadedMaterial != null && targetRenderer != null && progress != (IsFaded ? 1f : 0f);

        public Material FadedMaterial => fadedMaterial;

        private void Awake()
        {
            targetRenderer = GetComponent<Renderer>();
            originalMaterial = targetRenderer.sharedMaterial;
            if (fadedMaterial != null && fadedMaterial.HasProperty(BaseColorId))
            {
                fadedColor = fadedMaterial.GetColor(BaseColorId);
                opaqueColor = originalMaterial != null && originalMaterial.HasProperty(BaseColorId)
                    ? originalMaterial.GetColor(BaseColorId)
                    : fadedColor;
                opaqueColor.a = 1f;
                block = new MaterialPropertyBlock();
            }
        }

        /// <summary>设目标状态。变淡时立刻换半透明材质；无过渡（fadeSeconds = 0 或材质没有 _BaseColor）时直接到终态。</summary>
        public void SetFaded(bool faded)
        {
            if (faded == IsFaded || fadedMaterial == null || targetRenderer == null)
            {
                return;
            }

            IsFaded = faded;
            if (block == null || fadeSeconds <= 0f)
            {
                progress = faded ? 1f : 0f;
                targetRenderer.sharedMaterial = faded ? fadedMaterial : originalMaterial;
                return;
            }

            if (faded)
            {
                targetRenderer.sharedMaterial = fadedMaterial;
                ApplyColor();
            }
        }

        /// <summary>推进过渡一帧；返回 true 表示还没到终态，下一帧继续调用。</summary>
        public bool Advance(float deltaTime)
        {
            if (!IsTransitioning)
            {
                return false;
            }

            float step = fadeSeconds > 0f ? deltaTime / fadeSeconds : 1f;
            float target = IsFaded ? 1f : 0f;
            progress = Mathf.MoveTowards(progress, target, step); // lint-ok: 纯表现颜色过渡，不参与判定
            if (progress != target)
            {
                ApplyColor();
                return true;
            }

            // 到终态：清属性块（材质自身颜色就是终态颜色），恢复时换回原材质。
            targetRenderer.SetPropertyBlock(null);
            if (!IsFaded)
            {
                targetRenderer.sharedMaterial = originalMaterial;
            }

            return false;
        }

        private void ApplyColor()
        {
            targetRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, Color.Lerp(opaqueColor, fadedColor, progress));
            targetRenderer.SetPropertyBlock(block);
        }

        // 被禁用 / 场景卸载时直接回原样，不留过渡残态。
        private void OnDisable()
        {
            if (targetRenderer == null || fadedMaterial == null)
            {
                return;
            }

            IsFaded = false;
            progress = 0f;
            targetRenderer.SetPropertyBlock(null);
            targetRenderer.sharedMaterial = originalMaterial;
        }
    }
}
