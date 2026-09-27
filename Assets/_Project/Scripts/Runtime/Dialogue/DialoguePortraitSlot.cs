// 职责：对白面板一个头像槽的表现状态机——入场 / 退场滑动 + 淡入淡出、同槽换表情交叉淡化（运行时残影 Image）。
//   只改自己那张 Image、它的残影与（可选的）头像白框，不碰剧情状态；白框跟随头像 Image 的实际显隐（退场滑完才收），由 DialogueView 持有（每槽一个），不是组件。
//   说话者 / 非说话者的区分由 View 决定（非说话者直接收起），本类不做压暗或缩放。
// 新建原因：复用——没有现成的立绘动效组件；扩展——每槽要独立持有 2 条补间句柄、起讫值与残影，
//   平铺进 DialogueView 会变成一组按槽下标的平行数组，且 View 的职责（显示 + 抛事件）被动效细节淹没，所以抽成 View 私用的普通类。
using System;
using LitMotion;
using LitMotion.Extensions;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Dialogue
{
    internal sealed class DialoguePortraitSlot
    {
        private readonly Image image;
        private readonly RectTransform rect;
        // 可选头像白框：跟随 image.enabled 的实际显隐；可为 null。
        private readonly GameObject frame;
        // -1 = 左槽（从屏幕左侧进出），+1 = 右槽。
        private readonly float side;
        // 预制体里的原位：只在构造时取一次，动画中途开关面板也不会越跑越偏。
        private readonly Vector2 rest;
        private readonly Action moveCompleted;
        private readonly Action fadeCompleted;
        private DialogueMotionSettings motion = DialogueMotionSettings.Default;
        // 残影：交叉淡化时显示旧表情，懒建、每槽一份，挂在主图同级、层级在主图之下。
        private Image ghost;
        private MotionHandle move;
        private MotionHandle fade;
        // 逻辑目标（动画终态）：null = 该槽为空。
        private Sprite sprite;
        private Vector2 moveFrom;
        private Vector2 moveTo;
        private float alphaFrom;
        private float alphaTo;
        private bool hideAfterMove;
        private float ghostAlphaFrom;

        public DialoguePortraitSlot(Image image, bool rightSide, GameObject frame = null)
        {
            this.image = image;
            this.frame = frame;
            rect = image.rectTransform;
            side = rightSide ? 1f : -1f;
            rest = rect.anchoredPosition;
            moveCompleted = OnMoveCompleted;
            fadeCompleted = HideGhost;
        }

        /// <summary>当前逻辑上显示的立绘（动画终态）；null = 空槽（含正在退场）。</summary>
        public Sprite Sprite => sprite;

        public void Configure(in DialogueMotionSettings settings) => motion = settings;

        /// <summary>
        /// 设置本槽立绘。空 → 有：滑入 + 淡入；有 → 空：滑出 + 淡出后隐藏；换图：交叉淡化。
        /// <paramref name="instant"/> 或物体未激活时直接置终态，不播动画（存档恢复路径）。
        /// </summary>
        public void Set(Sprite next, bool instant)
        {
            Sprite previous = sprite;
            sprite = next;
            if (instant || !image.gameObject.activeInHierarchy)
            {
                Finish();
                return;
            }
            if (next == null)
            {
                if (previous != null) Exit();
                return;
            }
            if (previous == null)
            {
                Enter(next);
                return;
            }
            if (previous != next) Crossfade(previous, next);
        }

        /// <summary>清空本槽并直接置终态（打开面板时用，免得上一段对白的立绘残留）。</summary>
        public void Clear()
        {
            sprite = null;
            Finish();
        }

        /// <summary>掐断所有补间并把表现直接摆到逻辑终态（面板停用 / 瞬间关闭时用，避免残影留着）。</summary>
        public void Finish()
        {
            CancelAll();
            HideGhost();
            rect.anchoredPosition = rest;
            image.sprite = sprite;
            image.enabled = sprite != null;
            SetFrameVisible(sprite != null);
            SetAlpha(image, 1f);
        }

        private void Enter(Sprite next)
        {
            if (fade.IsActive()) fade.Cancel();
            HideGhost();
            if (move.IsActive()) move.Cancel();
            // 正在退场的又被叫回：从当前位置 / 透明度接着滑回原位，而不是跳到屏幕外重来。
            bool midExit = image.enabled && image.sprite != null;
            image.sprite = next;
            image.enabled = true;
            SetFrameVisible(true);
            moveFrom = midExit ? rect.anchoredPosition : Hidden();
            alphaFrom = midExit ? image.color.a : 0f;
            moveTo = rest;
            alphaTo = 1f;
            hideAfterMove = false;
            StartMove(Ease.OutCubic);
        }

        private void Exit()
        {
            if (fade.IsActive()) fade.Cancel();
            HideGhost();
            if (move.IsActive()) move.Cancel();
            moveFrom = rect.anchoredPosition;
            alphaFrom = image.color.a;
            moveTo = Hidden();
            alphaTo = 0f;
            hideAfterMove = true;
            StartMove(Ease.InCubic);
        }

        private void StartMove(Ease ease)
        {
            float seconds = motion.PortraitSlideSeconds;
            if (seconds <= 0f)
            {
                ApplyMove(1f);
                OnMoveCompleted();
                return;
            }
            ApplyMove(0f);
            // UpdateIgnoreTimeScale：对白期间世界时停（timeScale = 0），scaled 补间会冻住。
            move = LMotion.Create(0f, 1f, seconds)
                .WithEase(ease)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .WithOnComplete(moveCompleted)
                .Bind(this, (t, slot) => slot.ApplyMove(t))
                .AddTo(image.gameObject);
        }

        private void ApplyMove(float t)
        {
            rect.anchoredPosition = Vector2.LerpUnclamped(moveFrom, moveTo, t);
            SetAlpha(image, Mathf.LerpUnclamped(alphaFrom, alphaTo, t)); // lint-ok: 立绘动效是纯表现，不参与判定与快照
        }

        private void OnMoveCompleted()
        {
            if (!hideAfterMove) return;
            hideAfterMove = false;
            image.enabled = false;
            SetFrameVisible(false);
            image.sprite = null;
            rect.anchoredPosition = rest;
        }

        private void Crossfade(Sprite previous, Sprite next)
        {
            // 入场还没播完就换表情：先把入场补到终点，再从满透明度开始交叉淡化。
            if (move.IsActive()) move.Complete();
            if (fade.IsActive()) fade.Cancel();
            EnsureGhost();
            RectTransform ghostRect = ghost.rectTransform;
            ghostRect.anchoredPosition = rect.anchoredPosition;
            ghostRect.localScale = rect.localScale;
            ghost.color = image.color;
            ghost.sprite = previous;
            ghost.enabled = true;
            ghostAlphaFrom = image.color.a;
            image.sprite = next;
            image.enabled = true;
            SetFrameVisible(true);
            float seconds = motion.PortraitCrossfadeSeconds;
            if (seconds <= 0f)
            {
                ApplyFade(1f);
                HideGhost();
                return;
            }
            ApplyFade(0f);
            fade = LMotion.Create(0f, 1f, seconds)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .WithOnComplete(fadeCompleted)
                .Bind(this, (t, slot) => slot.ApplyFade(t))
                .AddTo(image.gameObject);
        }

        private void ApplyFade(float t)
        {
            SetAlpha(image, t);
            SetAlpha(ghost, ghostAlphaFrom * (1f - t));
        }

        private Vector2 Hidden() => rest + new Vector2(side * motion.PortraitSlideDistance, 0f);

        private void EnsureGhost()
        {
            // Image 是 UnityEngine.Object，判空只用 == null。
            if (ghost != null) return;
            // 复制主图物体（同 RectTransform 锚点 / 尺寸 / 保持比例等参数），放在主图同级、紧挨其下先画；不改预制体资产。
            ghost = Object.Instantiate(image, rect.parent, false);
            ghost.name = image.name + "Ghost";
            ghost.raycastTarget = false;
            ghost.transform.SetSiblingIndex(rect.GetSiblingIndex());
            ghost.enabled = false;
        }

        private void HideGhost()
        {
            if (ghost == null) return;
            ghost.enabled = false;
            ghost.sprite = null;
        }

        // 白框与头像 Image 同显同隐；没接不做事，值没变不重写。
        private void SetFrameVisible(bool visible)
        {
            if (frame != null && frame.activeSelf != visible) frame.SetActive(visible);
        }

        private void CancelAll()
        {
            if (move.IsActive()) move.Cancel();
            if (fade.IsActive()) fade.Cancel();
            hideAfterMove = false;
        }

        private static void SetAlpha(Graphic graphic, float alpha)
        {
            Color color = graphic.color;
            color.a = alpha;
            graphic.color = color;
        }
    }
}
