// 职责：演出面板——上下黑边、对白面板（左 / 右头像位 + 说话者 + 正文）、停顿提示符、跳过提示与长按进度环、进场黑场淡出；
//   实现字幕输出端供时间轴字幕轨道调用；字幕逐字揭示（打字机）。只显示，不注入服务、不读输入、不持有时间轴进度，全部由 PerformanceService 调方法。
//   逐字进度是文字表现状态（已显示几个字），由服务每帧调 TickTyping 驱动、点击时调 CompleteTyping 补全。
// 为什么新建（复用 → 扩展 → 新建）：DialogueView 是对白主面板（Popup 层、带选项与控件），演出要的是 Panel 层全屏、
//   Esc 关不掉、只有一块全屏透明点击区（停顿时点击继续）的覆盖层；塞进 DialogueView 会让 Performance 依赖 Dialogue（方向禁止）。
using System;
using System.Collections.Generic;
using System.Text;
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
    /// 可选字段（不接也能用，旧预制体兼容）：<c>avatar</c> + <c>avatarFrame</c> 是对白面板左侧头像组，
    /// <c>avatarRight</c> + <c>avatarFrameRight</c> 是右侧头像组；一句字幕只显示说话者那一侧的头像组（侧由演员名单配），
    /// 另一侧隐藏，无头像（旁白 / 未登记）时两侧都隐藏；右侧组没接时右侧说话者退回左侧显示；
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

        [Tooltip("左侧头像（Image，preserveAspect）；可空。说话者配在左侧才显示，否则与左侧底框一起隐藏。")]
        [SerializeField] private Image avatar;

        [Tooltip("左侧头像底框（垫在头像下面的白框物体）；可空。与左侧头像一起显隐。")]
        [SerializeField] private GameObject avatarFrame;

        [Tooltip("右侧头像（Image，preserveAspect）；可空。说话者配在右侧才显示；没接时右侧说话者退回左侧头像位。")]
        [SerializeField] private Image avatarRight;

        [Tooltip("右侧头像底框；可空。与右侧头像一起显隐。")]
        [SerializeField] private GameObject avatarFrameRight;

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

        // TMP maxVisibleCharacters 的默认值，即「不限」。
        private const int AllVisible = 99999;

        // 逐字显示：cadence 在 OnOpenAsync 按参数建一次（速度 ≤ 0 时为 null = 整句直出）。
        private TypingCadence cadence;
        private float charactersPerSecond;
        private readonly StringBuilder visibleBuilder = new StringBuilder(128);
        // 当前句 TMP 解析后的可见字符序列（富文本标签不计），每句只建一次。
        private string visibleText = string.Empty;
        private int visibleCount;
        private bool typing;
        // 服务要求显示 ▼（进入停顿）；实际显隐 = holdRequested && !IsTyping。
        private bool holdRequested;

        public override UILayer Layer => UILayer.Panel;
        public override bool IsFullScreen => true;
        /// <summary>Esc / 手柄 B 不能关演出：关了服务的播放循环会失去面板；跳过走长按。</summary>
        public override bool CloseOnCancel => false;
        /// <summary>演出本身会整层藏 HUD；面板在 Panel 层，这里为 true 只是声明「沉浸模式下也要显示」。</summary>
        public override bool VisibleWhenHudHidden => true;

        /// <summary>全屏点击区被点。服务处理：打字中 = 整句补全；停顿时 = 继续；其余无事，不触发跳过。</summary>
        public event Action OnTap;

        /// <summary>当前句是否还在逐字揭示中。</summary>
        public bool IsTyping => typing;

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
            charactersPerSecond = args.CharactersPerSecond;
            cadence = charactersPerSecond > 0f
                ? new TypingCadence(charactersPerSecond, args.PunctuationPauseSeconds, args.PunctuationChars)
                : null;
            HideSubtitle();
            holdRequested = false;
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
            holdRequested = false;
            if (holdPrompt != null) holdPrompt.gameObject.SetActive(false);
            return UniTask.CompletedTask;
        }

        private void OnDisable()
        {
            // 面板被直接销毁 / 停用时也退订，与 OnOpenAsync 的订阅成对。
            if (tapArea != null) tapArea.onClick.RemoveListener(HandleTap);
        }

        /// <summary>
        /// 显示一句字幕；说话者为空时隐藏名字栏（旁白）。头像只显示在 <paramref name="side"/> 那一侧，另一侧隐藏；
        /// 头像为 null 时两侧都隐藏。逐字速度大于 0 时正文从 0 字开始揭示（由 <see cref="TickTyping"/> 推进），否则整句直出。
        /// </summary>
        public void ShowSubtitle(string speakerName, string text, Sprite avatarSprite, PerformanceAvatarSide side)
        {
            bool hasSpeaker = !string.IsNullOrEmpty(speakerName);
            speaker.gameObject.SetActive(hasSpeaker);
            speaker.text = hasSpeaker ? speakerName : string.Empty;
            body.text = text ?? string.Empty;
            SetAvatar(avatarSprite, side);
            subtitleRoot.SetActive(true);
            BeginTyping();
        }

        /// <summary>
        /// 推进逐字揭示一帧（服务每帧调，unscaled Δt）。打完即停止打字并重算 ▼ 显隐；值没变不重写。
        /// </summary>
        public void TickTyping(float unscaledDelta)
        {
            if (!typing) return;
            int next = cadence.Advance(visibleText, visibleCount, charactersPerSecond * unscaledDelta);
            if (next != visibleCount)
            {
                visibleCount = next;
                body.maxVisibleCharacters = next;
            }
            if (visibleCount >= visibleText.Length) FinishTyping();
        }

        /// <summary>整句补全：立即显示全部正文、停止打字并重算 ▼ 显隐。不在打字时无事。</summary>
        public void CompleteTyping()
        {
            if (!typing) return;
            visibleCount = visibleText.Length;
            body.maxVisibleCharacters = visibleCount;
            FinishTyping();
        }

        // 每句开始：解析可见字符序列（富文本标签不计，照 DialogueView.SetLine），有节奏就从 0 字开始打。
        private void BeginTyping()
        {
            if (cadence == null)
            {
                ResetTyping();
                return;
            }
            body.maxVisibleCharacters = 0;
            body.ForceMeshUpdate();
            TMP_TextInfo info = body.textInfo;
            visibleBuilder.Clear();
            for (int i = 0; i < info.characterCount; i++) visibleBuilder.Append(info.characterInfo[i].character);
            visibleText = visibleBuilder.ToString();
            visibleCount = 0;
            cadence.Reset();
            typing = visibleText.Length > 0;
            if (!typing) body.maxVisibleCharacters = AllVisible;
            ApplyHoldPrompt();
        }

        private void FinishTyping()
        {
            typing = false;
            ApplyHoldPrompt();
        }

        // 清逐字状态并恢复整句可见。
        private void ResetTyping()
        {
            typing = false;
            visibleText = string.Empty;
            visibleCount = 0;
            cadence?.Reset();
            if (body != null) body.maxVisibleCharacters = AllVisible;
        }

        /// <summary>收起字幕。</summary>
        public void HideSubtitle()
        {
            // ▼ 显隐正确依赖调用方在 Hide 前已 SetHoldPromptVisible(false)（现只有 HandleConfirm → stage.Resume 一条路径）。
            if (subtitleRoot == null) return;
            subtitleRoot.SetActive(false);
            if (speaker != null) speaker.text = string.Empty;
            if (body != null) body.text = string.Empty;
            SetAvatar(null, PerformanceAvatarSide.Left);
            ResetTyping();
            if (holdPrompt != null) ApplyHoldPrompt();
        }

        // 头像组显隐：有图才显示说话者那一侧的头像与底框，另一侧整组隐藏（只显示说话者）。
        // 右侧头像没接（旧预制体）时右侧说话者退回左侧；左侧也没接时什么都不显示。
        private void SetAvatar(Sprite sprite, PerformanceAvatarSide side)
        {
            bool right = side == PerformanceAvatarSide.Right && avatarRight != null;
            ApplyAvatarGroup(avatar, avatarFrame, right ? null : sprite);
            ApplyAvatarGroup(avatarRight, avatarFrameRight, right ? sprite : null);
        }

        // 一侧头像组：有图显示头像与底框，无图两者都隐藏；值没变不重写。
        private static void ApplyAvatarGroup(Image image, GameObject frame, Sprite sprite)
        {
            bool visible = sprite != null && image != null;
            if (image != null)
            {
                image.sprite = sprite;
                if (image.gameObject.activeSelf != visible) image.gameObject.SetActive(visible);
            }
            if (frame != null && frame.activeSelf != visible) frame.SetActive(visible);
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

        /// <summary>
        /// 请求显隐停顿提示符（▼）。实际显隐 = 请求显示 且 当前句已打完：打字中先记下请求，打完 / 补全时才显示。
        /// </summary>
        public void SetHoldPromptVisible(bool visible)
        {
            holdRequested = visible;
            ApplyHoldPrompt();
        }

        // ▼ 显隐门控：请求显示且不在打字；activeSelf 没变不重写。
        private void ApplyHoldPrompt()
        {
            bool visible = holdRequested && !typing;
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
