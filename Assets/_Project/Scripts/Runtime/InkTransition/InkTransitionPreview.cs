// 职责：独立水墨预览的播放、暂停与进度定位；运行时材质仅归本组件所有。
// 新建原因：标题驱动绑定镜转与截图，加载黑幕绑定正式流程；复用 LitMotion，不扩展这两条生产入口。
using LitMotion;
using UnityEngine;
using UnityEngine.UI;

namespace Game.InkTransition
{
    [DisallowMultipleComponent]
    public sealed class InkTransitionPreview : MonoBehaviour
    {
        [SerializeField] private RawImage background;
        [SerializeField] private Texture firstBackground;
        [SerializeField] private Texture secondBackground;
        [SerializeField] private RawImage inkOverlay;
        [SerializeField] private Material inkMaterial;
        [SerializeField] private Slider progressSlider;
        [SerializeField] private Button replayButton;
        [SerializeField] private Button pauseButton;
        [SerializeField, Min(0.1f)] private float duration = 1.45f;

        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");
        private Material material;
        private MotionHandle motion;
        private bool sourceIsSecond;

        public float Progress { get; private set; }
        public bool HasRuntimeMaterial => material != null;
        public bool IsPlaying => motion.IsActive() && motion.PlaybackSpeed > 0f;
        public float Duration => duration;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (background == null || firstBackground == null || secondBackground == null ||
                inkOverlay == null || inkMaterial == null || !inkMaterial.shader.isSupported ||
                progressSlider == null || replayButton == null || pauseButton == null)
            {
                Debug.LogError("水墨预览缺少背景、材质或控件引用，或 Shader 不受支持。", this);
                enabled = false;
                return;
            }

            material = new Material(inkMaterial) { name = "InkTransitionPreview_Runtime" };
            inkOverlay.material = material;
            inkOverlay.texture = Texture2D.whiteTexture;
            inkOverlay.enabled = true;
            inkOverlay.raycastTarget = false;
            background.texture = firstBackground;
            replayButton.onClick.AddListener(Replay);
            pauseButton.onClick.AddListener(TogglePause);
            progressSlider.onValueChanged.AddListener(SetProgress);
            Replay();
        }

        public void Replay()
        {
            if (material == null) return;
            StopMotion();
            sourceIsSecond = background.texture == secondBackground;
            ApplyProgress(0f);
            PlayFromCurrent();
        }

        public void TogglePause()
        {
            if (material == null) return;
            if (motion.IsActive()) motion.PlaybackSpeed = IsPlaying ? 0f : 1f;
            else if (Progress < 1f) PlayFromCurrent();
            else Replay();
        }

        public void SetProgress(float progress)
        {
            if (material == null) return;
            StopMotion();
            ApplyProgress(Mathf.Clamp01(progress)); // lint-ok: 独立预览的视觉进度，不进入确定性内核。
        }

        private void PlayFromCurrent()
        {
            motion = LMotion.Create(Progress, 1f, Mathf.Max(0.1f, duration) * (1f - Progress)) // lint-ok: 仅视觉动画的真实秒时长。
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(ApplyProgress);
        }

        private void ApplyProgress(float progress)
        {
            Progress = progress;
            // 只有完全遮住的中段切底图；回拖滑杆也能查看同一段转场。
            bool showSecond = progress < 0.5f ? sourceIsSecond : !sourceIsSecond;
            background.texture = showSecond ? secondBackground : firstBackground;
            material.SetFloat(ProgressId, progress);
            Rect rect = inkOverlay.rectTransform.rect;
            material.SetFloat(AspectId, rect.width / Mathf.Max(1f, rect.height)); // lint-ok: 屏幕长宽比只用于视觉遮罩。
            progressSlider.SetValueWithoutNotify(progress);
        }

        private void OnRectTransformDimensionsChange()
        {
            if (material != null) ApplyProgress(Progress);
        }

        private void StopMotion()
        {
            if (motion.IsActive()) motion.Cancel();
            motion = default;
        }

        private void OnDisable()
        {
            StopMotion();
            if (replayButton != null) replayButton.onClick.RemoveListener(Replay);
            if (pauseButton != null) pauseButton.onClick.RemoveListener(TogglePause);
            if (progressSlider != null) progressSlider.onValueChanged.RemoveListener(SetProgress);
            if (inkOverlay != null)
            {
                inkOverlay.enabled = false;
                inkOverlay.material = null;
            }
            if (material != null) Destroy(material);
            material = null;
            Progress = 0f;
        }
    }
}
