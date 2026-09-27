// 职责：演出面板——上下黑边、对白面板（头像 + 说话者 + 正文）、停顿提示符、跳过提示与长按进度环、进场黑场淡出；
//   实现字幕输出端供时间轴字幕轨道调用。只显示，不注入服务、不读输入、不持有播放进度，全部由 PerformanceService 调方法。
// 为什么新建（复用 → 扩展 → 新建）：DialogueView 是对白主面板（Popup 层、带选项与控件），演出要的是 Panel 层全屏、
//   Esc 关不掉、只有一块全屏透明点击区（停顿时点击继续）的覆盖层；塞进 DialogueView 会让 Performance 依赖 Dialogue（方向禁止）。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
using Game.Performance.Timeline;
using LitMotion;
using LitMotion.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Performance
{
    /// <summary>
    /// 演出面板。预制体 Addressables 地址须为 <c>PerformanceView</c>（UI 组）。
    /// <para>
    /// 接线提示：<c>letterboxTop</c> / <c>letterboxBottom</c> 分别锚在屏幕上 / 下边缘、横向拉伸，高度由本类改 sizeDelta.y；
    /// <c>fade</c> 是全屏黑色 Image（不挡射线）；<c>skipFill</c> 的 Image Type 须为 Filled；
    /// <c>tapArea</c> 是全屏透明 Button，放在层级最后（最上层），其余 Graphic 一律关 raycastTarget。
    /// </para>
    /// <para>
    /// 可选字段（不接也能用，旧预制体兼容）：<c>avatar</c> + <c>avatarFrame</c> 是对白面板左侧头像组，无头像时整组隐藏；
    /// <c>skipHint</c> 接了时键位提示（「长按 Ctrl」）写进它、<c>skipLabel</c> 保留预制体里的固定文案（「跳过 ▶」），
    /// 没接时键位提示仍写进 <c>skipLabel</c>。样式（颜色 / 字号 / 位置）全在预制体里，代码只填文字与显隐。
    /// </para>
    /// </summary>
    public sealed class PerformanceView : UIView, IPerformanceSubtitleSink
    {
        [Tooltip("上黑边（锚在顶边、横向拉伸）。")]
        [SerializeField] private Image letterboxTop;

        [Tooltip("下黑边（锚在底边、横向拉伸）。")]
        [SerializeField] private Image letterboxBottom;

        [Tooltip("全屏黑场，进场时从不透明淡到透明。")]
        [SerializeField] private Image fade;

        [Tooltip("字幕根节点；没有字幕时隐藏。")]
        [SerializeField] private GameObject subtitleRoot;

        [Tooltip("头像（Image，preserveAspect）；可空。无头像时与头像底框一起隐藏。")]
        [SerializeField] private Image avatar;

        [Tooltip("头像底框（垫在头像下面的白框物体）；可空。与头像一起显隐。")]
        [SerializeField] private GameObject avatarFrame;

        [Tooltip("说话者名字；旁白（空名字）时隐藏。")]
        [SerializeField] private TMP_Text speaker;

        [Tooltip("字幕正文。")]
        [SerializeField] private TMP_Text body;

        [Tooltip("停顿提示符（▼），时间轴停在 HoldMarker 时显示。")]
        [SerializeField] private TMP_Text holdPrompt;

        [Tooltip("跳过提示根节点；不可跳过的演出整个隐藏。")]
        [SerializeField] private GameObject skipRoot;

        [Tooltip("跳过标签。接了 skipHint 时保留预制体里的固定文案（「跳过 ▶」）；没接时写入键位提示（旧行为）。")]
        [SerializeField] private TMP_Text skipLabel;

        [Tooltip("跳过键位小字（「长按 Ctrl」）；可空。")]
        [SerializeField] private TMP_Text skipHint;

        [Tooltip("长按进度环（Image Type = Filled）；进度为 0 时隐藏。")]
        [SerializeField] private Image skipFill;

        [Tooltip("全屏透明点击区：停顿时点击等价确认键")]
        [SerializeField] private Button tapArea;

        private MotionHandle topHandle;
        private MotionHandle bottomHandle;
        private MotionHandle fadeHandle;
        private float shownSkipProgress = -1f;

        public override UILayer Layer => UILayer.Panel;
        public override bool IsFullScreen => true;
        /// <summary>Esc / 手柄 B 不能关演出：关了服务的播放循环会失去面板；跳过走长按。</summary>
        public override bool CloseOnCancel => false;
        /// <summary>演出本身会整层藏 HUD；面板在 Panel 层，这里为 true 只是声明「沉浸模式下也要显示」。</summary>
        public override bool VisibleWhenHudHidden => true;

        /// <summary>全屏点击区被点。服务按「确认」处理：只在停顿时生效，不触发跳过。</summary>
        public event Action OnTap;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            Validate();
            if (!(arg is PerformanceViewArgs args))
                throw new ArgumentException("PerformanceView 需要 PerformanceViewArgs 参数", nameof(arg));

            tapArea.onClick.RemoveListener(HandleTap);
            tapArea.onClick.AddListener(HandleTap);
            // 全屏透明点击区不参与手柄 / 方向键导航，免得选中落到看不见的按钮上。
            Navigation none = tapArea.navigation;
            none.mode = Navigation.Mode.None;
            tapArea.navigation = none;

            CancelMotions();
            HideSubtitle();
            holdPrompt.text = args.HoldPrompt;
            holdPrompt.gameObject.SetActive(false);
            skipRoot.SetActive(args.Policy.Skippable);
            // TMP_Text 是 UnityEngine.Object，判空只用 != null。
            if (skipHint != null) skipHint.text = args.SkipHint;
            else skipLabel.text = args.SkipHint;
            shownSkipProgress = -1f;
            SetSkipProgress(0f);

            float seconds = args.FadeSeconds > 0f ? args.FadeSeconds : 0f;
            float height = args.Policy.Letterbox && args.LetterboxHeight > 0f ? args.LetterboxHeight : 0f;
            letterboxTop.gameObject.SetActive(height > 0f);
            letterboxBottom.gameObject.SetActive(height > 0f);
            SetLetterbox(0f);
            if (height > 0f)
            {
                if (seconds > 0f)
                {
                    // UpdateIgnoreTimeScale：演出期间世界时停（timeScale = 0），黑边照样推入。
                    // AddTo(this)：面板被直接销毁时随之掐断；OnCloseAsync 里也手动 Cancel，双保险。
                    topHandle = LMotion.Create(0f, height, seconds)
                        .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                        .BindToSizeDeltaY(letterboxTop.rectTransform)
                        .AddTo(this);
                    bottomHandle = LMotion.Create(0f, height, seconds)
                        .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                        .BindToSizeDeltaY(letterboxBottom.rectTransform)
                        .AddTo(this);
                }
                else
                {
                    SetLetterbox(height);
                }
            }

            // 进场黑场：从全黑淡到透明，舞台相机的画面随之显出来。
            fade.raycastTarget = false;
            if (seconds > 0f)
            {
                SetFadeAlpha(1f);
                fadeHandle = LMotion.Create(1f, 0f, seconds)
                    .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                    .BindToColorA(fade)
                    .AddTo(this);
            }
            else
            {
                SetFadeAlpha(0f);
            }
            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            // 淡出过渡已播完才走到这里：收黑边、清字幕，让下次打开从干净状态开始。
            if (tapArea != null) tapArea.onClick.RemoveListener(HandleTap);
            OnTap = null;
            CancelMotions();
            if (letterboxTop != null && letterboxBottom != null) SetLetterbox(0f);
            if (subtitleRoot != null) HideSubtitle();
            if (holdPrompt != null) holdPrompt.gameObject.SetActive(false);
            return UniTask.CompletedTask;
        }

        private void OnDisable()
        {
            // 面板被直接销毁 / 停用时也退订，与 OnOpenAsync 的订阅成对。
            if (tapArea != null) tapArea.onClick.RemoveListener(HandleTap);
        }

        /// <summary>显示一句字幕；说话者为空时隐藏名字栏（旁白），头像为 null 时隐藏头像组。</summary>
        public void ShowSubtitle(string speakerName, string text, Sprite avatarSprite)
        {
            bool hasSpeaker = !string.IsNullOrEmpty(speakerName);
            speaker.gameObject.SetActive(hasSpeaker);
            speaker.text = hasSpeaker ? speakerName : string.Empty;
            body.text = text ?? string.Empty;
            SetAvatar(avatarSprite);
            subtitleRoot.SetActive(true);
        }

        /// <summary>收起字幕。</summary>
        public void HideSubtitle()
        {
            if (subtitleRoot == null) return;
            subtitleRoot.SetActive(false);
            if (speaker != null) speaker.text = string.Empty;
            if (body != null) body.text = string.Empty;
            SetAvatar(null);
        }

        // 头像组显隐：有图才显示头像与底框；字段没接（旧预制体）时什么都不做。
        private void SetAvatar(Sprite sprite)
        {
            bool visible = sprite != null && avatar != null;
            if (avatar != null)
            {
                avatar.sprite = sprite;
                if (avatar.gameObject.activeSelf != visible) avatar.gameObject.SetActive(visible);
            }
            if (avatarFrame != null && avatarFrame.activeSelf != visible) avatarFrame.SetActive(visible);
        }

        /// <summary>设置长按跳过进度（0–1）；0 时隐藏进度环。值没变不重写。</summary>
        public void SetSkipProgress(float progress)
        {
            float clamped = progress <= 0f ? 0f : (progress >= 1f ? 1f : progress);
            // 进度值来自同一处计算，精确比较足够。
            if (clamped == shownSkipProgress) return;
            shownSkipProgress = clamped;
            skipFill.fillAmount = clamped;
            skipFill.enabled = clamped > 0f;
        }

        /// <summary>显隐停顿提示符（▼）。</summary>
        public void SetHoldPromptVisible(bool visible)
        {
            if (holdPrompt.gameObject.activeSelf != visible) holdPrompt.gameObject.SetActive(visible);
        }

        private void SetLetterbox(float height)
        {
            RectTransform top = letterboxTop.rectTransform;
            RectTransform bottom = letterboxBottom.rectTransform;
            top.sizeDelta = new Vector2(top.sizeDelta.x, height);
            bottom.sizeDelta = new Vector2(bottom.sizeDelta.x, height);
        }

        private void SetFadeAlpha(float alpha)
        {
            Color color = fade.color;
            color.a = alpha;
            fade.color = color;
        }

        // 点完即取消选中：否则 EventSystem 留着选中态，之后按 Enter / 手柄 A 会被 UI Submit 再点一次。
        private void HandleTap()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null && eventSystem.currentSelectedGameObject == tapArea.gameObject)
                eventSystem.SetSelectedGameObject(null);
            OnTap?.Invoke();
        }

        private void CancelMotions()
        {
            if (topHandle.IsActive()) topHandle.Cancel();
            if (bottomHandle.IsActive()) bottomHandle.Cancel();
            if (fadeHandle.IsActive()) fadeHandle.Cancel();
        }

        // 逐个点名缺失字段，预制体按名字接线时一眼看出漏了哪个。
        private void Validate()
        {
            var missing = new List<string>();
            if (letterboxTop == null) missing.Add(nameof(letterboxTop));
            if (letterboxBottom == null) missing.Add(nameof(letterboxBottom));
            if (fade == null) missing.Add(nameof(fade));
            if (subtitleRoot == null) missing.Add(nameof(subtitleRoot));
            if (speaker == null) missing.Add(nameof(speaker));
            if (body == null) missing.Add(nameof(body));
            if (holdPrompt == null) missing.Add(nameof(holdPrompt));
            if (skipRoot == null) missing.Add(nameof(skipRoot));
            if (skipLabel == null) missing.Add(nameof(skipLabel));
            if (skipFill == null) missing.Add(nameof(skipFill));
            if (tapArea == null) missing.Add(nameof(tapArea));
            if (missing.Count > 0)
                throw new InvalidOperationException("PerformanceView 引用未接线：" + string.Join("、", missing));
        }
    }
}
