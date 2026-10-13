// 职责：用现有 LitMotion 播放标题拆件时间轴，覆盖、主题切换或销毁时停止并复位。
// 为什么新建：UIView 管整面板开关，不能承担数十个美术部件与遮罩；复用它的生命周期，不另建 UI 服务。
using System;
using LitMotion;
using LitMotion.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Core.UI
{
    [DisallowMultipleComponent]
    public sealed class TitleEntranceMotion : MonoBehaviour
    {
        [SerializeField] private TitleEntranceConfig config;
        [SerializeField] private RectTransform[] targets = Array.Empty<RectTransform>();
        [SerializeField] private GameObject sceneArtwork;
        [SerializeField] private GameObject[] buttonArtwork = Array.Empty<GameObject>();
        [SerializeField] private Image background;
        [SerializeField] private Image[] buttonImages = Array.Empty<Image>();
        [SerializeField] private GameObject startArtwork;
        [SerializeField] private Image startImage;
        [SerializeField] private RectTransform sceneFrame;
        [SerializeField] private RectTransform sceneContent;
        [SerializeField] private Vector2 referenceSize;

        private TrackState[] states;
        private MotionHandle entrance;
        private MotionHandle[] breathing;
        private CanvasGroup interaction;
        private bool started;
        private bool alternate;
        private bool running;

        public bool IsRunning => running;

        private sealed class TrackState
        {
            internal RectTransform Rect;
            internal CanvasGroup Group;
            internal RectMask2D Mask;
            internal TitleEntranceConfig.TrackSettings Settings;
            internal Vector2 Position;
            internal Vector3 Scale;
            internal Quaternion Rotation;

            internal void Apply(float progress)
            {
                Rect.anchoredPosition = Position + Settings.Offset * (1f - progress);
                Rect.localScale = Scale * Mathf.LerpUnclamped(Settings.Scale, 1f, progress);
                Rect.localRotation = Rotation * Quaternion.Euler(0f, 0f, Settings.Rotation * (1f - progress));
                if (Mask != null)
                {
                    float hidden = Rect.rect.width * (1f - Mathf.Clamp01(progress));
                    Mask.padding = Settings.RevealDirection < 0
                        ? new Vector4(hidden, 0f, 0f, 0f) : new Vector4(0f, 0f, hidden, 0f);
                }
            }

            internal void Breathe(float progress)
            {
                Rect.anchoredPosition = Position + Settings.BreathOffset * progress;
                Rect.localScale = Scale * (1f + Settings.BreathScale * progress);
            }
        }

        private void Capture()
        {
            if (states != null) return;
            if (config == null || config.Tracks.Length != targets.Length || targets.Length == 0)
                throw new InvalidOperationException("标题入场配置与部件数量不匹配");
            interaction = GetComponent<CanvasGroup>();
            states = new TrackState[targets.Length];
            breathing = new MotionHandle[targets.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                RectTransform rect = targets[i];
                if (rect == null || rect.GetComponent<CanvasGroup>() == null || config.Tracks[i].Duration <= 0f)
                    throw new InvalidOperationException("标题入场缺少部件、透明组或有效时长");
                states[i] = new TrackState
                {
                    Rect = rect, Group = rect.GetComponent<CanvasGroup>(), Mask = rect.GetComponent<RectMask2D>(),
                    Settings = config.Tracks[i], Position = rect.anchoredPosition,
                    Scale = rect.localScale, Rotation = rect.localRotation
                };
            }
        }

        public void Begin()
        {
            if (started || alternate) return;
            Capture();
            started = true;
            running = true;
            SetGraphics(false, true);
            if (interaction != null) interaction.interactable = false;
            var sequence = LSequence.Create();
            for (int i = 0; i < states.Length; i++)
            {
                TrackState state = states[i];
                state.Apply(0f);
                state.Group.alpha = 0f;
                sequence.Insert(state.Settings.Delay, LMotion.Create(0f, 1f, state.Settings.Duration)
                    .WithEase(state.Settings.Curve).Bind(state, (value, target) => target.Apply(value)));
                sequence.Insert(state.Settings.Delay, LMotion.Create(0f, 1f, state.Settings.FadeSeconds)
                    .WithEase(Ease.InOutSine).BindToAlpha(state.Group));
            }
            entrance = sequence.Run(motion => motion.WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .WithOnComplete(Finish)).AddTo(gameObject);
        }

        public void SetAlternate(bool value)
        {
            if (alternate == value) return;
            alternate = value;
            Stop();
            Restore();
            SetGraphics(value, false);
            if (started && !alternate && isActiveAndEnabled) StartBreathing();
        }

        /// <summary>开始转场前停止呼吸并回到落定姿态，不改变入场配置。</summary>
        public void SuspendIdle()
        {
            Stop();
            Restore();
            SetGraphics(alternate, false);
        }

        /// <summary>开局取消或失败后恢复标题呼吸。</summary>
        public void ResumeIdle()
        {
            if (started && !alternate && isActiveAndEnabled && !running)
            {
                Stop();
                StartBreathing();
            }
        }

        private void Finish()
        {
            running = false;
            Restore();
            // 落定后交还完整按钮的原生 Sprite Swap，悬停、选中与点击路由沿用原实现。
            SetGraphics(alternate, false);
            if (!alternate && isActiveAndEnabled) StartBreathing();
        }

        private void SetGraphics(bool useAlternate, bool entering)
        {
            if (background != null) background.enabled = useAlternate;
            if (sceneArtwork != null) sceneArtwork.SetActive(!useAlternate);
            if (startArtwork != null)
            {
                startArtwork.SetActive(true);
                // 拆件只用于入场；完整按钮 Image 留在同一个呼吸节点上，Sprite Swap 才不会被子图盖住。
                for (int i = 0; i < startArtwork.transform.childCount; i++)
                    startArtwork.transform.GetChild(i).gameObject.SetActive(entering && !useAlternate);
            }
            if (startImage != null) startImage.enabled = !entering || useAlternate;
            for (int i = 0; i < buttonArtwork.Length; i++)
                if (buttonArtwork[i] != null) buttonArtwork[i].SetActive(entering && !useAlternate);
            for (int i = 0; i < buttonImages.Length; i++)
                if (buttonImages[i] != null) buttonImages[i].enabled = !entering || useAlternate;
        }

        private void Restore()
        {
            if (states != null)
                for (int i = 0; i < states.Length; i++)
                    if (states[i].Rect != null)
                    {
                        states[i].Apply(1f);
                        states[i].Group.alpha = 1f;
                    }
            if (interaction != null) interaction.interactable = true;
        }

        private void StartBreathing()
        {
            if (states == null) return;
            for (int i = 0; i < states.Length; i++)
            {
                TrackState state = states[i];
                if (!state.Rect.gameObject.activeInHierarchy ||
                    (state.Settings.BreathOffset == Vector2.zero && state.Settings.BreathScale == 0f)) continue;
                breathing[i] = LMotion.Create(0f, 1f, state.Settings.BreathSeconds)
                    .WithEase(Ease.InOutSine).WithLoops(-1, LoopType.Yoyo)
                    .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                    .Bind(state, (value, target) => target.Breathe(value)).AddTo(gameObject);
            }
        }

        private void Stop()
        {
            if (entrance.IsActive()) entrance.Cancel();
            if (breathing != null)
                for (int i = 0; i < breathing.Length; i++)
                    if (breathing[i].IsActive()) breathing[i].Cancel();
            running = false;
        }

        private void OnEnable()
        {
            if (started && !alternate && !running) StartBreathing();
        }

        private void OnDisable()
        {
            Stop();
            Restore();
            if (started) SetGraphics(alternate, false);
        }

        private void OnDestroy() => Stop();

        private void OnRectTransformDimensionsChange()
        {
            if (sceneFrame == null || sceneContent == null || referenceSize.x <= 0f || referenceSize.y <= 0f) return;
            float scale = Mathf.Max(sceneFrame.rect.width / referenceSize.x, sceneFrame.rect.height / referenceSize.y);
            sceneContent.localScale = Vector3.one * scale;
        }
    }
}
