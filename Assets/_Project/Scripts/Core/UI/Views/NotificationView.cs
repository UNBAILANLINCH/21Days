// 职责：通知卡片的表现——屏幕顶部居中一张卡片（标题 + 可选正文），进出场做 alpha + 轻微上移；
//   外加右下角小字（不进队列的那一路，进出场只做 alpha）。都只显示，不排队、不计时。
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
    /// 「卡片」与「右下角小字」是同一视图上的两路表现：卡片由队列调度，小字由调用方计时，互不干扰。
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

        [Tooltip("右下角小字的 CanvasGroup，进出场改它的 alpha。")]
        [SerializeField] private CanvasGroup cornerGroup;

        [Tooltip("右下角小字文字。")]
        [SerializeField] private TMP_Text cornerLabel;

        [Tooltip("卡片进出场的秒数（真实时间，暂停时也照走）；角落小字的淡入淡出同样用它。")]
        [Min(0f)]
        [SerializeField] private float cardSeconds = 0.2f;

        [Tooltip("进场从下方多少像素（参考分辨率）滑到原位；出场再往上滑同样距离。")]
        [SerializeField] private float slideDistance = 24f;

        private MotionHandle alphaMotion;
        private MotionHandle moveMotion;
        private MotionHandle cornerMotion;
        private float restY;
        private bool restCaptured;
        private Action deactivateCard;

        public override UILayer Layer => UILayer.Top;
        public override bool IsFullScreen => false;

        /// <summary>卡片当前是否在显示（含进场动画中）。</summary>
        public bool IsCardShown { get; private set; }

        /// <summary>角落小字当前是否在显示（含淡入淡出中）。</summary>
        public bool IsCornerShown { get; private set; }

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
            cornerGroup.blocksRaycasts = false;
            cornerGroup.interactable = false;
            if (!IsCardShown)
            {
                StopMotions();
                cardGroup.alpha = 0f;
                card.gameObject.SetActive(false);
            }

            if (!IsCornerShown)
            {
                StopCornerMotion();
                cornerGroup.alpha = 0f;
                cornerLabel.gameObject.SetActive(false);
            }

            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            StopMotions();
            IsCardShown = false;
            StopCornerMotion();
            IsCornerShown = false;
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

        /// <summary>
        /// 显示右下角小字。停留期间再调一次是换字并从当前 alpha 淡到不透明（不闪）。
        /// 停留时长由调用方计时（<see cref="NotificationService.ShowCornerHint"/>），视图不排期。
        /// </summary>
        public void ShowCornerHint(string text)
        {
            Validate();
            float from = IsCornerShown ? cornerGroup.alpha : 0f;
            cornerLabel.text = text;
            cornerLabel.gameObject.SetActive(true);
            IsCornerShown = true;
            PlayCorner(from, 1f, null);
        }

        /// <summary>淡出右下角小字，结束后隐藏文字物体。没在显示时是空操作。</summary>
        public void HideCornerHint()
        {
            if (!IsCornerShown) return;
            IsCornerShown = false;
            PlayCorner(cornerGroup.alpha, 0f, DeactivateCorner);
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

        // 角落小字只做 alpha：位置固定在右下角，不参与卡片的滑动。写法与 Play 一致（同刻只留一个动画、unscaled）。
        private void PlayCorner(float fromAlpha, float toAlpha, Action onComplete)
        {
            StopCornerMotion();
            if (cardSeconds <= 0f)
            {
                cornerGroup.alpha = toAlpha;
                if (onComplete != null) onComplete();
                return;
            }

            cornerGroup.alpha = fromAlpha;
            cornerMotion = onComplete != null
                ? LMotion.Create(fromAlpha, toAlpha, cardSeconds)
                    .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                    .WithOnComplete(onComplete)
                    .BindToAlpha(cornerGroup)
                    .AddTo(gameObject)
                : LMotion.Create(fromAlpha, toAlpha, cardSeconds)
                    .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                    .BindToAlpha(cornerGroup)
                    .AddTo(gameObject);
        }

        private void DeactivateCard()
        {
            // 淡出途中又来了新通知时 IsCardShown 已被 ShowCard 置回 true，且旧动画已被掐断不会走到这里；这里再判一次兜底。
            if (!IsCardShown && card != null) card.gameObject.SetActive(false);
        }

        private void DeactivateCorner()
        {
            // 同 DeactivateCard：淡出途中又来了新的一条时 IsCornerShown 已被置回 true。
            if (!IsCornerShown && cornerLabel != null) cornerLabel.gameObject.SetActive(false);
        }

        private void StopMotions()
        {
            if (alphaMotion.IsActive()) alphaMotion.Cancel();
            if (moveMotion.IsActive()) moveMotion.Cancel();
        }

        private void StopCornerMotion()
        {
            if (cornerMotion.IsActive()) cornerMotion.Cancel();
        }

        private void Validate()
        {
            if (card == null || cardGroup == null || titleLabel == null || bodyLabel == null
                || cornerGroup == null || cornerLabel == null)
            {
                throw new InvalidOperationException(
                    "NotificationView 预制体缺少 card / cardGroup / titleLabel / bodyLabel / cornerGroup / cornerLabel 引用，"
                    + "检查 Prefabs/UI/NotificationView.prefab 的接线");
            }
        }
    }
}
