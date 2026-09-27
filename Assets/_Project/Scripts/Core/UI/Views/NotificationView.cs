// 职责：通知卡片的表现——屏幕顶部居中一张卡片（标题 + 可选正文），进出场做 alpha + 轻微上移。只显示，不排队、不计时。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：TitleView 是 Panel 层全屏面板；各玩法 HUD 绑死在自己的模块上。未入库的 ToastView 把队列与计时
//      写在视图里、挂 Hud 层、只有单行文字，与 architecture.md 5.6 定下的「NotificationService 持队列、视图只显示、
//      Top 层、标题 + 正文」契约不符。
//   2. 扩展不行：通知是框架级通用能力（任务、拾取、系统提示都要用），塞进任何玩法视图都会反向绑定模块。
//      队列逻辑在 NotificationQueue、调度在 NotificationService，这里只剩表现，所以单独一个 UIView 子类。

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using LitMotion.Extensions;
using TMPro;
using UnityEngine;

namespace Game.Core.UI.Views
{
    /// <summary>
    /// 通知卡片视图。预制体 <c>Assets/_Project/Prefabs/UI/NotificationView.prefab</c>，Addressables 地址 <c>NotificationView</c>。
    /// Top 层、不全屏、不进栈；由 <see cref="NotificationService"/> 首次 Show 时打开并常驻，空闲时卡片 alpha 归零并隐藏。
    /// <para>
    /// 不注入服务、不持队列。卡片不挡点击（CanvasGroup.blocksRaycasts = false），通知期间玩家照常操作。
    /// </para>
    /// </summary>
    public sealed class NotificationView : UIView
    {
        [Tooltip("卡片本体（顶部居中，锚点在上沿）。进出场时移动它的 anchoredPosition.y。")]
        [SerializeField] private RectTransform card;

        [Tooltip("卡片上的 CanvasGroup，进出场改它的 alpha。")]
        [SerializeField] private CanvasGroup cardGroup;

        [Tooltip("标题文字。")]
        [SerializeField] private TMP_Text titleLabel;

        [Tooltip("正文文字；传空时整行隐藏。")]
        [SerializeField] private TMP_Text bodyLabel;

        [Tooltip("卡片进出场的秒数（真实时间，暂停时也照走）。")]
        [Min(0f)]
        [SerializeField] private float cardSeconds = 0.2f;

        [Tooltip("进场从下方多少像素（参考分辨率）滑到原位；出场再往上滑同样距离。")]
        [SerializeField] private float slideDistance = 24f;

        private MotionHandle alphaMotion;
        private MotionHandle moveMotion;
        private float restY;
        private bool restCaptured;
        private Action deactivateCard;

        public override UILayer Layer => UILayer.Top;
        public override bool IsFullScreen => false;

        /// <summary>卡片当前是否在显示（含进场动画中）。</summary>
        public bool IsCardShown { get; private set; }

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            Validate();
            if (!restCaptured)
            {
                // 预制体里摆好的位置就是停靠位；只记一次，免得重开时把动画中途的位置当成停靠位。
                restY = card.anchoredPosition.y;
                restCaptured = true;
            }

            cardGroup.blocksRaycasts = false;
            cardGroup.interactable = false;
            if (!IsCardShown)
            {
                StopMotions();
                cardGroup.alpha = 0f;
                card.gameObject.SetActive(false);
            }

            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            StopMotions();
            IsCardShown = false;
            return UniTask.CompletedTask;
        }

        /// <summary>显示一张卡片（换下一条时也调它：直接换字并重播进场）。<paramref name="body"/> 为空则隐藏正文行。</summary>
        public void ShowCard(string title, string body)
        {
            Validate();
            titleLabel.text = title;
            bool hasBody = !string.IsNullOrEmpty(body);
            bodyLabel.gameObject.SetActive(hasBody);
            if (hasBody) bodyLabel.text = body;

            card.gameObject.SetActive(true);
            IsCardShown = true;
            Play(0f, 1f, restY - slideDistance, restY, null);
        }

        /// <summary>收起卡片：淡出并上移，结束后隐藏卡片物体。没在显示时是空操作。</summary>
        public void HideCard()
        {
            if (!IsCardShown) return;
            IsCardShown = false;
            if (deactivateCard == null) deactivateCard = DeactivateCard;
            Play(cardGroup.alpha, 0f, card.anchoredPosition.y, restY + slideDistance, deactivateCard);
        }

        // 写法同 UIView.FadeAsync：同一时刻只留一组动画，新的先掐断旧的，否则旧动画会在新动画之后把值改回去；
        // UpdateIgnoreTimeScale 让暂停菜单开着（timeScale = 0）时卡片也能进出。
        private void Play(float fromAlpha, float toAlpha, float fromY, float toY, Action onComplete)
        {
            StopMotions();
            if (cardSeconds <= 0f)
            {
                cardGroup.alpha = toAlpha;
                SetY(toY);
                if (onComplete != null) onComplete();
                return;
            }

            cardGroup.alpha = fromAlpha;
            SetY(fromY);
            // AddTo(gameObject)：视图被 UIService 直接销毁（退出时 ReleaseAllViews 不走 OnCloseAsync）时随之掐断动画。
            // 不自己写 OnDestroy——那会遮住基类 UIView 的同名私有消息，基类的过渡动画就没人掐了。
            alphaMotion = onComplete != null
                ? LMotion.Create(fromAlpha, toAlpha, cardSeconds)
                    .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                    .WithOnComplete(onComplete)
                    .BindToAlpha(cardGroup)
                    .AddTo(gameObject)
                : LMotion.Create(fromAlpha, toAlpha, cardSeconds)
                    .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                    .BindToAlpha(cardGroup)
                    .AddTo(gameObject);
            moveMotion = LMotion.Create(fromY, toY, cardSeconds)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .WithEase(Ease.OutQuad)
                .BindToAnchoredPositionY(card)
                .AddTo(gameObject);
        }

        private void SetY(float y)
        {
            Vector2 p = card.anchoredPosition;
            p.y = y;
            card.anchoredPosition = p;
        }

        private void DeactivateCard()
        {
            // 淡出途中又来了新通知时 IsCardShown 已被 ShowCard 置回 true，且旧动画已被掐断不会走到这里；这里再判一次兜底。
            if (!IsCardShown && card != null) card.gameObject.SetActive(false);
        }

        private void StopMotions()
        {
            if (alphaMotion.IsActive()) alphaMotion.Cancel();
            if (moveMotion.IsActive()) moveMotion.Cancel();
        }

        private void Validate()
        {
            if (card == null || cardGroup == null || titleLabel == null || bodyLabel == null)
            {
                throw new InvalidOperationException(
                    "NotificationView 预制体缺少 card / cardGroup / titleLabel / bodyLabel 引用，检查 Prefabs/UI/NotificationView.prefab 的接线");
            }
        }
    }
}
