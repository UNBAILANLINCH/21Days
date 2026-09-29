// 职责：无对话树 NPC 的头顶常驻台词气泡——接住 DialogueInteractable.OnBubbleRequested，逐字打出、打完显示 ▼、
//   停留 holdSeconds 后淡出；显示中再次交互直接换句重来（PRP 8.5）。全程 unscaled 时间，不暂停世界、不开面板。
// 为什么新建：复用——DialogueView 是全屏对白面板（Popup 层、暂停世界），语义完全不同；
//   扩展——塞进 DialogueInteractable 会让逻辑组件依赖 Canvas / TMP / LitMotion；塞进 DialogueInteractableMarker 会把
//   「状态标记」和「台词展示」两种表现绑死，将来单独换气泡样式要动标记。
using System;
using Game.Core.Logging;
using Game.Core.Simulation;
using LitMotion;
using LitMotion.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Dialogue
{
    /// <summary>
    /// 世界空间台词气泡。挂在气泡预制体（<c>Prefabs/World/DialogueSpeechBubble.prefab</c>）根上，
    /// 预制体实例作为 NPC 的子物体摆在头顶标记之上；交互组件取 Inspector 的 target，未配时向父级找。
    /// 朝向相机由预制体实例决定（3D 场景挂 CameraBillboard，2D 场景不旋转）；相机跟随后把气泡约束在视口内。
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class DialogueSpeechBubble : MonoBehaviour
    {
        private const string NamePrefix = "◌ ";

        [Tooltip("台词来源；为空时在 Awake 里向父级找 DialogueInteractable。")]
        [SerializeField] private DialogueInteractable target;
        [Tooltip("气泡内容根（背景框、名字、正文、箭头）；隐藏时整体关掉。")]
        [SerializeField] private GameObject root;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private TMP_Text body;
        [Tooltip("打完后显示的 ▼。")]
        [SerializeField] private GameObject arrow;
        [Tooltip("淡出用。")]
        [SerializeField] private CanvasGroup group;
        [Tooltip("逐字速度（字/秒，unscaled）。")]
        [SerializeField, Min(1f)] private float charactersPerSecond = 30f;
        [Tooltip("打完后停留多久开始淡出（秒，unscaled）。")]
        [SerializeField, Min(0f)] private float holdSeconds = 4f;
        [Tooltip("淡出时长（秒，unscaled）。")]
        [SerializeField, Min(0f)] private float fadeSeconds = 0.3f;
        [Tooltip("气泡与相机画面边缘的最小像素距离。")]
        [SerializeField, Min(0f)] private float screenPadding = 12f;

        private enum Phase
        {
            Hidden,
            Typing,
            Holding,
            Fading,
        }

        private Phase phase = Phase.Hidden;
        private float visible;
        private int totalCharacters;
        private float holdElapsed;
        private MotionHandle fade;
        private Action onFadeCompleted;
        private RectTransform bubbleRect;
        private Canvas canvas;
        private Camera viewCamera;
        private Vector3 anchorPosition;
        private Vector3 authoredScale;
        private readonly Vector3[] corners = new Vector3[4];

        /// <summary>气泡是否在显示（含打字、停留、淡出中）。</summary>
        public bool IsShowing => phase != Phase.Hidden;

        /// <summary>当前（最近一次）显示的台词；从未显示时为空串。</summary>
        public string CurrentText { get; private set; } = string.Empty;

        /// <summary>单句停留时长，供外部（回放）估算超时。</summary>
        public float HoldSeconds => holdSeconds;

        private void Awake()
        {
            bubbleRect = (RectTransform)transform;
            canvas = GetComponent<Canvas>();
            anchorPosition = bubbleRect.localPosition;
            authoredScale = bubbleRect.localScale;
            onFadeCompleted = HideImmediate;
            if (target == null) target = GetComponentInParent<DialogueInteractable>();
            if (target == null)
            {
                Log.Warn($"{name} 的 DialogueSpeechBubble 找不到 DialogueInteractable（Inspector 未配、父级也没有）：气泡不会出现", this);
            }
            else
            {
                target.OnBubbleRequested += Show;
            }
            HideImmediate();
        }

        private void OnDestroy()
        {
            if (target != null) target.OnBubbleRequested -= Show;
            if (fade.IsActive()) fade.Cancel();
        }

        /// <summary>显示一句台词；显示中调用会直接换句、从头打字。</summary>
        public void Show(string line)
        {
            // 沉浸模式下不弹气泡（世界空间提示一律隐藏）。
            if (target != null && target.HiddenByHud) return;
            viewCamera = canvas != null && canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
            if (fade.IsActive()) fade.Cancel();
            CurrentText = line ?? string.Empty;
            if (root != null) root.SetActive(true);
            if (group != null) group.alpha = 1f;
            if (nameLabel != null) nameLabel.text = NamePrefix + (target == null ? string.Empty : target.DisplayName);
            if (arrow != null) arrow.SetActive(false);
            visible = 0f;
            holdElapsed = 0f;
            totalCharacters = CurrentText.Length;
            if (body != null)
            {
                body.text = CurrentText;
                body.maxVisibleCharacters = 0;
                // 以 TMP 解析后的可见字符数为准（富文本标签不计）；只在换句时算一次。
                body.ForceMeshUpdate();
                totalCharacters = body.textInfo.characterCount;
                // 气泡高度由 ContentSizeFitter/VerticalLayoutGroup 按 Body 行数自适应（预制体侧）；
                // 世界空间 Canvas 不会在设完文本的当帧自动重排，这里强制重排一次，避免本帧位置/高度仍是上一句的。
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
            }
            phase = totalCharacters > 0 ? Phase.Typing : Phase.Holding;
            if (phase == Phase.Holding && arrow != null) arrow.SetActive(true);
        }

        private void Update()
        {
            // 显示中进了沉浸模式：立刻收起（只比较布尔，不分配）。
            if (phase != Phase.Hidden && target != null && target.HiddenByHud)
            {
                if (fade.IsActive()) fade.Cancel();
                HideImmediate();
                return;
            }

            switch (phase)
            {
                case Phase.Typing:
                    visible += charactersPerSecond * Time.unscaledDeltaTime;
                    int count = (int)visible;
                    if (count > totalCharacters) count = totalCharacters;
                    if (body != null && body.maxVisibleCharacters != count) body.maxVisibleCharacters = count;
                    if (count >= totalCharacters)
                    {
                        phase = Phase.Holding;
                        if (arrow != null) arrow.SetActive(true);
                    }
                    break;
                case Phase.Holding:
                    holdElapsed += Time.unscaledDeltaTime;
                    if (holdElapsed >= holdSeconds) BeginFade();
                    break;
            }
        }

        private void BeginFade()
        {
            if (group == null || fadeSeconds <= 0f)
            {
                HideImmediate();
                return;
            }
            phase = Phase.Fading;
            // UpdateIgnoreTimeScale：与世界时停无关，对白外 timeScale 被别的系统改了也照常淡出。
            fade = LMotion.Create(1f, 0f, fadeSeconds)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .WithOnComplete(onFadeCompleted)
                .BindToAlpha(group);
        }

        private void LateUpdate()
        {
            if (!IsShowing || viewCamera == null || !viewCamera.isActiveAndEnabled) return;
            // 每帧从作者的头顶锚点重算，不能累加上帧修正，否则移动相机后气泡会漂移。
            bubbleRect.localPosition = anchorPosition;
            bubbleRect.localScale = authoredScale;
            Vector3 anchor = viewCamera.WorldToViewportPoint(bubbleRect.position);
            if (anchor.z <= viewCamera.nearClipPlane) return;
            Vector2 margin = new Vector2(
                GameMath.Min(0.25f, screenPadding / GameMath.Max(1f, viewCamera.pixelWidth)),
                GameMath.Min(0.25f, screenPadding / GameMath.Max(1f, viewCamera.pixelHeight)));
            Rect bounds = ViewportBounds();
            // 极近镜头或窄画面放不下时只缩小显示，不改作者尺寸；离开边缘后自动恢复。
            float fit = GameMath.Min(1f, GameMath.Min(
                (1f - 2f * margin.x) / GameMath.Max(0.0001f, bounds.width),
                (1f - 2f * margin.y) / GameMath.Max(0.0001f, bounds.height)));
            if (fit < 1f)
            {
                bubbleRect.localScale = authoredScale * fit;
                bounds = ViewportBounds();
            }
            Vector3 adjusted = anchor;
            adjusted.x += GameMath.Clamp(0f, margin.x - bounds.xMin, 1f - margin.x - bounds.xMax);
            adjusted.y += GameMath.Clamp(0f, margin.y - bounds.yMin, 1f - margin.y - bounds.yMax);
            bubbleRect.position = viewCamera.ViewportToWorldPoint(adjusted);
        }

        private Rect ViewportBounds()
        {
            bubbleRect.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 point = viewCamera.WorldToViewportPoint(corners[i]);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private void HideImmediate()
        {
            phase = Phase.Hidden;
            if (bubbleRect != null)
            {
                bubbleRect.localPosition = anchorPosition;
                bubbleRect.localScale = authoredScale;
            }
            if (root != null) root.SetActive(false);
            if (group != null) group.alpha = 1f;
        }
    }
}
