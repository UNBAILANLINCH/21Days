// 职责：独立水墨预览的播放、暂停与进度定位；运行时材质仅归本组件所有。
// 独立试播保留控件；正式加载通过受控模式等待实际就绪信号。
using LitMotion;
using Game.CharacterPuppet;
using Game.Monster;
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
        [SerializeField] private bool controlledOnly;
        [Header("角色起墨试播（不切换真实地图）")]
        [SerializeField] private EncounterSceneView encounterView;
        [SerializeField] private Transform actorAnchor;
        [SerializeField] private Camera actorCamera;
        [SerializeField] private Shader worldInkShader;
        [SerializeField] private Vector2 fallbackOriginUV = new Vector2(0.5f, 0.5f);

        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");
        private static readonly int OriginId = Shader.PropertyToID("_OriginUV");
        private Material material;
        private MotionHandle motion;
        private bool sourceIsSecond;
        private SpriteRenderer[] actorParts = System.Array.Empty<SpriteRenderer>();
        private ActorInkWorldEffect worldEffect;
        private System.Action fullyCovered;
        private System.Action revealCompleted;
        private System.Action coverCancelled;
        private bool controlledCover;
        private bool controlledReveal;
        private bool coverNotified;
        private int motionGeneration;
        private Vector2 originUV;
        private Bounds actorBounds;
        private Texture seedDensity;
        private Transform capturedTarget;
        private Camera capturedCamera;
        private bool previewInitialized;

        public float Progress { get; private set; }
        public bool HasRuntimeMaterial => material != null;
        public bool IsPlaying => motion.IsActive() && motion.PlaybackSpeed > 0f;
        public float Duration => duration;
        public bool HasActorOrigin { get; private set; }
        public bool HasWorldEffect => worldEffect != null;
        public bool AwaitingReveal => controlledCover && coverNotified;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            bool missingPreview = !controlledOnly && (background == null || firstBackground == null || secondBackground == null ||
                progressSlider == null || replayButton == null || pauseButton == null);
            if (missingPreview || inkOverlay == null || inkMaterial == null || !inkMaterial.shader.isSupported)
            {
                Debug.LogError("水墨预览缺少背景、材质或控件引用，或 Shader 不受支持。", this);
                enabled = false;
                return;
            }

            material = new Material(inkMaterial) { name = "InkTransitionPreview_Runtime" };
            inkOverlay.material = material;
            inkOverlay.texture = Texture2D.whiteTexture;
            inkOverlay.enabled = true;
            inkOverlay.raycastTarget = controlledOnly;
            if (background != null) background.texture = firstBackground;
            if (replayButton != null) replayButton.onClick.AddListener(Replay);
            if (pauseButton != null) pauseButton.onClick.AddListener(TogglePause);
            if (progressSlider != null) progressSlider.onValueChanged.AddListener(SetProgress);
            // 只由按钮或明确的上下文菜单试播，挂入实际场景不会自动遮屏。
            inkOverlay.enabled = false;
            if (background != null) background.enabled = false;
            ApplyProgress(0f);
        }

        public void Replay()
        {
            if (controlledOnly) return;
            if (material == null) return;
            if (controlledCover || controlledReveal) return;
            Texture currentBackground = background == null ? null : background.texture;
            StopPreview();
            if (background != null) background.texture = currentBackground;
            BeginPreview();
            ApplyProgress(0f);
            PlayFromCurrent();
        }

        // 可选接线接口：调用方负责输入锁及加载完成信号；本组件不加载地图。
        public bool BeginCover(System.Action onFullyCovered, System.Action onRevealed = null, System.Action onCancelled = null)
        {
            if (material == null || !isActiveAndEnabled || controlledCover || controlledReveal) return false;
            StopPreview();
            controlledCover = true;
            coverNotified = false;
            fullyCovered = onFullyCovered;
            revealCompleted = onRevealed;
            coverCancelled = onCancelled;
            BeginPreview();
            ApplyProgress(0f);
            PlayFromCurrent();
            return true;
        }

        public bool RevealAfterReady()
        {
            if (!AwaitingReveal || material == null) return false;
            controlledCover = false;
            controlledReveal = true;
            fullyCovered = null;
            // 已全遮，旧角色可随加载卸载；揭幕只操作屏幕层。
            ReleaseActorInk();
            PlayFromCurrent();
            return true;
        }

        private void BeginPreview()
        {
            CaptureActorOrigin();
            inkOverlay.enabled = true;
            if (background != null)
            {
                background.enabled = !controlledCover && !HasActorOrigin;
                sourceIsSecond = background.texture == secondBackground;
            }
            previewInitialized = true;
        }

        [ContextMenu("水墨试播/停止并清除预览")]
        public void StopPreview()
        {
            System.Action cancelled = controlledCover || controlledReveal ? coverCancelled : null;
            controlledCover = false;
            controlledReveal = false;
            coverNotified = false;
            fullyCovered = null;
            revealCompleted = null;
            coverCancelled = null;
            StopMotion();
            ReleaseActorInk();
            if (material != null) ApplyProgress(0f);
            if (inkOverlay != null) inkOverlay.enabled = false;
            if (background != null) background.enabled = false;
            previewInitialized = false;
            cancelled?.Invoke();
        }

        [ContextMenu("水墨试播/绑定当前已加载的控制角色并播放（不切地图）")]
        private void PreviewCurrentActor()
        {
            if (controlledOnly) return;
            if (!Application.isPlaying || material == null) return;
            if (controlledCover || controlledReveal) return;
            // 显式操作时只查一次；多场景多入口拒绝猜测，不在帧循环里扫描。
            EncounterSceneView[] views = FindObjectsOfType<EncounterSceneView>();
            if (views.Length != 1)
            {
                Debug.LogWarning("需要唯一 EncounterSceneView；请手动设置 Encounter View 和 Actor Camera 后点击重播。", this);
                return;
            }
            encounterView = views[0];
            actorCamera = encounterView.ControlCamera == null ? null : encounterView.ControlCamera.GetComponent<Camera>();
            Replay();
        }

        public void BindCurrentActor(EncounterSceneView view)
        {
            if (controlledCover || controlledReveal) throw new System.InvalidOperationException("盖幕运行中不能改绑角色");
            encounterView = view;
            actorAnchor = null;
            actorCamera = view == null || view.ControlCamera == null ? null : view.ControlCamera.GetComponent<Camera>();
        }

        private void CaptureActorOrigin()
        {
            HasActorOrigin = false;
            originUV = fallbackOriginUV;
            seedDensity = inkMaterial.HasProperty("_InkDensityTex") ? inkMaterial.GetTexture("_InkDensityTex") : null;
            if (seedDensity == null) seedDensity = Texture2D.grayTexture;
            Transform target = encounterView != null ? encounterView.CurrentControlObject : actorAnchor;
            Transform effectOwner = target;
            if (encounterView != null && encounterView.ControlCamera != null)
                actorCamera = encounterView.ControlCamera.GetComponent<Camera>();
            // 本预览只支持主显示器直接渲染；RenderTexture/多显示器需专门映射，不能猜坐标。
            if (target != null && actorCamera != null && actorCamera.isActiveAndEnabled && actorCamera.targetTexture == null && actorCamera.targetDisplay == 0
                && (inkOverlay.canvas == null || inkOverlay.canvas.targetDisplay == 0))
            {
                ChibiPuppet puppet = target.GetComponentInChildren<ChibiPuppet>();
                actorParts = (puppet != null ? puppet.transform : target).GetComponentsInChildren<SpriteRenderer>();
                bool found = false;
                foreach (SpriteRenderer part in actorParts)
                {
                    if (!CanInk(part)) continue;
                    if (!found) { actorBounds = part.bounds; effectOwner = part.transform; found = true; }
                    else actorBounds.Encapsulate(part.bounds);
                }
                if (found)
                {
                    Vector3 screen = actorCamera.WorldToScreenPoint(actorBounds.center);
                    Canvas canvas = inkOverlay.canvas;
                    Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                    if (screen.z > 0f && actorCamera.pixelRect.Contains(new Vector2(screen.x, screen.y)) && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        inkOverlay.rectTransform, screen, uiCamera, out Vector2 local))
                    {
                        Rect rect = inkOverlay.rectTransform.rect;
                        if (rect.width > 0f && rect.height > 0f)
                        {
                            originUV = new Vector2((local.x - rect.xMin) / rect.width, (local.y - rect.yMin) / rect.height);
                            HasActorOrigin = originUV.x >= 0f && originUV.x <= 1f && originUV.y >= 0f && originUV.y <= 1f;
                        }
                    }
                }
            }
            if (!HasActorOrigin)
            {
                actorParts = System.Array.Empty<SpriteRenderer>();
                originUV = fallbackOriginUV;
                if (!controlledOnly) Debug.LogWarning("当前仅为手工起点扩散示意：没有有效的屏内控制角色、相机或SpriteRenderer；不包含周身世界墨层。", this);
            }
            capturedTarget = HasActorOrigin ? target : null;
            capturedCamera = HasActorOrigin ? actorCamera : null;
            originUV = new Vector2(Mathf.Clamp01(originUV.x), Mathf.Clamp01(originUV.y)); // lint-ok: 仅视觉UV边界。
            if (HasActorOrigin)
            {
                Shader shader = worldInkShader != null ? worldInkShader : Shader.Find("21Days/Effects/ActorInkWorld");
                worldEffect = ActorInkWorldEffect.Create(effectOwner, actorBounds, capturedCamera, shader, seedDensity);
                if (worldEffect == null) Debug.LogWarning("周身墨层未创建：请显式绑定 ActorInkWorld Shader；当前只有屏幕晕染。", this);
            }
        }

        private void LateUpdate()
        {
            if (!previewInitialized || !HasActorOrigin) return;
            Transform current = encounterView != null ? encounterView.CurrentControlObject : actorAnchor;
            if (capturedTarget == null || !capturedTarget.gameObject.activeInHierarchy || current != capturedTarget || capturedCamera == null || !capturedCamera.isActiveAndEnabled
                || capturedCamera.targetTexture != null || capturedCamera.targetDisplay != 0
                || (encounterView != null && encounterView.ControlCamera != null && encounterView.ControlCamera.transform != capturedCamera.transform))
            {
                // 控制对象切换、离场或销毁时中止试播，绝不能继续把旧角色染黑。
                if (controlledCover && Progress >= 0.44f) ReleaseActorInk(); // lint-ok: 全遮平台后目标卸载不能提前揭幕。
                else StopPreview();
                return;
            }
            bool anyVisible = false;
            foreach (SpriteRenderer part in actorParts) if (CanInk(part)) { anyVisible = true; break; }
            if (!anyVisible)
            {
                if (controlledCover && Progress >= 0.44f) ReleaseActorInk(); // lint-ok: 同上，等待外部加载完成。
                else StopPreview();
            }
        }

        private bool CanInk(SpriteRenderer part) => part != null && part.enabled && part.gameObject.activeInHierarchy
            && part.sprite != null && part.sharedMaterial != null && actorCamera != null
            && (actorCamera.cullingMask & (1 << part.gameObject.layer)) != 0;

        private void ApplyActorInk(float progress)
        {
            // 拆件可随原有动画运动；只重新合并缓存bounds，不重新扫描层级或改角色动作。
            bool found = false;
            foreach (SpriteRenderer part in actorParts)
            {
                if (!CanInk(part)) continue;
                if (!found) { actorBounds = part.bounds; found = true; }
                else actorBounds.Encapsulate(part.bounds);
            }
            if (worldEffect != null)
            {
                if (found) worldEffect.SetActorBounds(actorBounds);
                worldEffect.SetProgress(progress);
            }
        }

        private void ReleaseActorInk()
        {
            if (worldEffect != null)
            {
                worldEffect.Dispose();
                worldEffect = null;
            }
            actorParts = System.Array.Empty<SpriteRenderer>();
            HasActorOrigin = false;
            capturedTarget = null;
            capturedCamera = null;
        }

        public void TogglePause()
        {
            if (controlledOnly) return;
            if (material == null) return;
            if (controlledCover || controlledReveal) return; // 加载等待接口不接受试播按钮改变状态。
            if (!previewInitialized) { Replay(); return; }
            if (motion.IsActive()) motion.PlaybackSpeed = IsPlaying ? 0f : 1f;
            else if (Progress < 1f) PlayFromCurrent();
            else Replay();
        }

        public void SetProgress(float progress)
        {
            if (controlledOnly) return;
            if (material == null) return;
            if (float.IsNaN(progress) || float.IsInfinity(progress)) return;
            if (controlledCover || controlledReveal) return; // 受控盖幕期间禁止UI拖动绕过加载等待。
            StopMotion();
            if (!previewInitialized) BeginPreview();
            ApplyProgress(Mathf.Clamp01(progress)); // lint-ok: 独立预览的视觉进度，不进入确定性内核。
        }

        private void PlayFromCurrent()
        {
            float end = controlledCover ? 0.5f : 1f; // lint-ok: .5位于shader严格全遮平台。
            int generation = motionGeneration;
            motion = LMotion.Create(Progress, end, Mathf.Max(0.1f, duration) * (end - Progress)) // lint-ok: 仅视觉动画的真实秒时长。
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .WithOnComplete(() => CompleteMotion(generation))
                .Bind(ApplyProgress);
        }

        private void CompleteMotion(int generation)
        {
            if (generation != motionGeneration || !isActiveAndEnabled) return;
            if (controlledReveal)
            {
                controlledReveal = false;
                System.Action revealed = revealCompleted;
                revealCompleted = null;
                coverCancelled = null;
                revealed?.Invoke();
                return;
            }
            if (!controlledCover || coverNotified) return;
            ApplyProgress(0.5f); // lint-ok: 回调前明确写入严格全遮平台。
            coverNotified = true;
            System.Action callback = fullyCovered;
            fullyCovered = null;
            callback?.Invoke(); // 普通委托，不是Unity Object。
        }

        private void ApplyProgress(float progress)
        {
            Progress = progress;
            // 只有完全遮住的中段切底图；回拖滑杆也能查看同一段转场。
            bool showSecond = progress < 0.5f ? sourceIsSecond : !sourceIsSecond;
            if (background != null) background.texture = showSecond ? secondBackground : firstBackground;
            material.SetFloat(ProgressId, progress);
            material.SetVector(OriginId, new Vector4(originUV.x, originUV.y, 0f, 0f));
            ApplyActorInk(progress);
            Rect rect = inkOverlay.rectTransform.rect;
            material.SetFloat(AspectId, rect.width / Mathf.Max(1f, rect.height)); // lint-ok: 屏幕长宽比只用于视觉遮罩。
            if (progressSlider != null) progressSlider.SetValueWithoutNotify(progress);
        }

        private void OnRectTransformDimensionsChange()
        {
            if (material != null) ApplyProgress(Progress);
        }

        private void StopMotion()
        {
            motionGeneration++;
            if (motion.IsActive()) motion.Cancel();
            motion = default;
        }

        private void OnDisable()
        {
            System.Action cancelled = controlledCover || controlledReveal ? coverCancelled : null;
            controlledCover = false;
            controlledReveal = false;
            coverNotified = false;
            fullyCovered = null;
            revealCompleted = null;
            coverCancelled = null;
            StopMotion();
            ReleaseActorInk();
            previewInitialized = false;
            if (background != null) background.enabled = false;
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
            cancelled?.Invoke();
        }
    }
}
