// 职责：镜转后截取一次标题画面，用 GPU 水波模糊收进暗幕；覆盖、取消和销毁时释放截图并复位。
// 新建原因：入场只驱动拆件，LoadingCurtain 只管加载黑幕；复用 LitMotion 与原开局路由，不改全局渲染管线。
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Logging;
using LitMotion;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Core.UI
{
    [DisallowMultipleComponent]
    public sealed class TitleStartTransition : MonoBehaviour
    {
        [SerializeField] private TitleStartConfig config;
        [SerializeField] private RectTransform mirror;
        [SerializeField] private TitleEntranceMotion entrance;
        [SerializeField] private RawImage screenEffect;
        [SerializeField] private Material effectMaterial;

        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int CenterId = Shader.PropertyToID("_Center");
        private static readonly int StrengthId = Shader.PropertyToID("_Strength");
        private static readonly int FlipYId = Shader.PropertyToID("_FlipY");
        private static readonly Vector3[] CanvasCorners = new Vector3[4];
        private CanvasGroup interaction;
        private Material material;
        private RenderTexture capture;
        private CancellationTokenSource playback;
        private MotionHandle motion;
        private Quaternion originalRotation;
        private Vector3 originalScale;
        private bool capturedPose;
        private bool previousInteractable;

        public bool IsRunning => playback != null;
        public float Progress { get; private set; }
        public bool HasCapture => capture != null;
        public bool IsCovered => screenEffect != null && screenEffect.gameObject.activeSelf && Progress >= 1f;

        public async UniTask PlayAsync(CancellationToken ct, bool loading = false)
        {
            if (IsRunning || (screenEffect != null && screenEffect.gameObject.activeSelf)) return;
            if (config == null || screenEffect == null || effectMaterial == null || !effectMaterial.shader.isSupported)
            {
                Log.Warn("标题开始转场缺少接线或 Shader 不受支持，沿用原开局流程");
                return;
            }
            interaction = GetComponent<CanvasGroup>();
            previousInteractable = interaction == null || interaction.interactable;
            if (entrance != null) entrance.SuspendIdle();
            if (mirror != null)
            {
                originalRotation = mirror.localRotation;
                originalScale = mirror.localScale;
                capturedPose = true;
            }
            if (interaction != null) interaction.interactable = false;
            playback = CancellationTokenSource.CreateLinkedTokenSource(ct);
            CancellationToken token = playback.Token;
            try
            {
                motion = LMotion.Create(0f, 1f, loading ? config.LoadMirrorSeconds : config.MirrorSeconds).WithEase(Ease.InOutSine)
                    .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale).Bind(value =>
                    {
                        if (mirror == null || !mirror.gameObject.activeInHierarchy) return;
                        mirror.localRotation = originalRotation * Quaternion.Euler(0f, 0f, config.MirrorDegrees * value);
                        mirror.localScale = originalScale * Mathf.Lerp(1f, 1.045f, value);
                    });
                await motion.ToUniTask(CancelBehavior.Cancel, false, token);
                // 帧末才截取：包含 Overlay Canvas，且效果层尚未启用，不会把自己递归采进截图。
                await UniTask.WaitForEndOfFrame(this, token);
                token.ThrowIfCancellationRequested();
                capture = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32)
                { name = "TitleStartCapture", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                capture.Create();
                ScreenCapture.CaptureScreenshotIntoRenderTexture(capture);
                material = new Material(effectMaterial);
                // 截图 RenderTexture 在 UV 从顶部起算的后端需翻转采样，屏幕上的水波中心不翻转。
                material.SetFloat(FlipYId, SystemInfo.graphicsUVStartsAtTop ? 1f : 0f);
                material.SetVector(StrengthId, new Vector4(config.WaveStrength, config.BlurStrength, config.LineStrength, 0f));
                Canvas canvas = screenEffect.canvas;
                Vector2 center = mirror != null && mirror.gameObject.activeInHierarchy
                    ? RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, mirror.position)
                    : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                material.SetVector(CenterId, new Vector4(center.x / Screen.width, center.y / Screen.height, (float)Screen.width / Screen.height, 0f));
                FitToCanvas(canvas);
                screenEffect.texture = capture;
                screenEffect.material = material;
                screenEffect.gameObject.SetActive(true);
                ApplyProgress(0f);
                motion = LMotion.Create(0f, 1f, loading ? config.LoadWaveSeconds : config.WaveSeconds).WithEase(Ease.Linear)
                    .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale).Bind(ApplyProgress);
                await motion.ToUniTask(CancelBehavior.Cancel, false, token);
                token.ThrowIfCancellationRequested();
                ApplyProgress(1f);
                // 操作说明在 Top 层，不会停用标题；改用静态黑底等待它，立即归还截图和材质。
                screenEffect.texture = Texture2D.blackTexture;
                screenEffect.material = null;
                ReleaseFrame();
            }
            catch (System.OperationCanceledException)
            {
                ResetVisuals();
                throw;
            }
            catch (System.Exception e)
            {
                ResetVisuals();
                Log.Warn($"标题开始转场失败，沿用原开局流程：{e.Message}");
            }
            finally
            {
                if (playback != null) { playback.Dispose(); playback = null; }
            }
        }

        public void ResetVisuals()
        {
            if (playback != null) playback.Cancel();
            if (motion.IsActive()) motion.Cancel();
            if (screenEffect != null)
            {
                screenEffect.gameObject.SetActive(false);
                screenEffect.texture = null;
                screenEffect.material = null;
            }
            ReleaseFrame();
            if (capturedPose && mirror != null)
            {
                mirror.localRotation = originalRotation;
                mirror.localScale = originalScale;
            }
            if (interaction != null) interaction.interactable = previousInteractable;
            capturedPose = false;
            Progress = 0f;
            if (entrance != null && isActiveAndEnabled) entrance.ResumeIdle();
        }

        private void ApplyProgress(float value)
        {
            Progress = value;
            if (material != null) material.SetFloat(ProgressId, value);
        }

        private void ReleaseFrame()
        {
            if (capture != null) { capture.Release(); Destroy(capture); capture = null; }
            if (material != null) { Destroy(material); material = null; }
        }

        private void FitToCanvas(Canvas canvas)
        {
            RectTransform rect = screenEffect.rectTransform;
            var parent = rect.parent as RectTransform;
            var root = canvas.rootCanvas.transform as RectTransform;
            if (parent == null || root == null) return;
            root.GetWorldCorners(CanvasCorners);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = (Vector2)parent.InverseTransformPoint(CanvasCorners[0]) - parent.rect.min;
            rect.offsetMax = (Vector2)parent.InverseTransformPoint(CanvasCorners[2]) - parent.rect.max;
        }

        private void OnDisable() => ResetVisuals();
    }
}
