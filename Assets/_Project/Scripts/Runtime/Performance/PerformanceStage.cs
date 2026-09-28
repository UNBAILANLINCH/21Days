// 职责：演出预制体的根组件（舞台）——持有 PlayableDirector 与舞台相机、给出本段演出的策略开关；
//   播放 / 继续 / 停止时间轴，收到 HoldMarker 就暂停并通知，时间轴停下就通知结束；挂字幕输出端供字幕轨道找到面板；
//   声明演员名单（说话者 → 头像与头像侧）。演出一律是世界舞台：演员站在世界里，舞台相机接管画面。
// 为什么新建（复用 → 扩展 → 新建）：工程里没有「一段按时间轴编排的演出」的场景组件；PlayableDirector 本身不认识
//   停顿标记、策略与字幕输出端，需要一个预制体根把它们装在一起（prp 2.2「一段演出 = 一个预制体」）。
using System;
using System.Collections.Generic;
using Game.Core.Logging;
using Game.Performance.Timeline;
using UnityEngine;
using UnityEngine.Playables;

namespace Game.Performance
{
    /// <summary>
    /// 演出舞台。**接线要求**：挂在演出预制体根上，且 <c>director</c> 必须是同一物体上的 PlayableDirector——
    /// 时间轴 Markers 区的 <see cref="HoldMarker"/> 通知发给 Director 所在物体，字幕轨道也从 Director 所在物体取舞台。
    /// <para>
    /// 舞台相机须为透视 URP Base 相机，服务播放时让它渲染主相机能看到的全部图层 + Performance 层并接管画面，
    /// 预制体子物体就是站在世界里的演员（实例按 <see cref="PerformancePlacement"/> 摆到世界位姿上）。舞台相机不要打 MainCamera 标签。
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerformanceStage : MonoBehaviour, INotificationReceiver
    {
        [Tooltip("本段演出的 PlayableDirector；必须挂在本物体上（预制体根）。")]
        [SerializeField] private PlayableDirector director;

        [Tooltip("舞台相机：透视 URP Base 相机，不打 MainCamera 标签（剔除遮罩、清屏、渲染器等运行时从主相机拷贝）。")]
        [SerializeField] private Camera stageCamera;

        [Tooltip("演员名单：字幕说话者显示名 → 对白面板头像与头像侧（左 / 右）。说话者须与字幕片段一字不差；重名只取第一条。")]
        [SerializeField] private List<PerformanceCastEntry> cast = new List<PerformanceCastEntry>();

        [Tooltip("玩家能否长按跳过。")]
        [SerializeField] private bool skippable = true;

        [Tooltip("演出期间是否暂停世界（timeScale = 0）。")]
        [SerializeField] private bool pauseWorld = true;

        [Tooltip("演出期间是否整层隐藏 HUD 层与弹窗层（对白框在弹窗层，对白里插播时一起藏）。")]
        [SerializeField] private bool hideHud = true;

        private bool stoppedSubscribed;
        private bool warnedDirectorPlacement;

        /// <summary>时间轴走到 <see cref="HoldMarker"/> 并已暂停。</summary>
        public event Action OnHold;

        /// <summary>时间轴停止（自然播完或被 <see cref="Stop"/>）。</summary>
        public event Action OnFinished;

        public PlayableDirector Director => director;
        public Camera StageCamera => stageCamera;

        /// <summary>演员名单（只读视图）；校验器与编辑器读它。</summary>
        public IReadOnlyList<PerformanceCastEntry> Cast => cast;
        public bool Skippable => skippable;
        public bool PauseWorld => pauseWorld;
        public bool HideHud => hideHud;

        /// <summary>时间轴总时长（秒）；没接 Director 时为 0。</summary>
        public double Duration => director == null ? 0d : director.duration;

        /// <summary>字幕输出端，由服务在播放前设置、结束时清空；字幕轨道混合器每帧读它。</summary>
        public IPerformanceSubtitleSink SubtitleSink { get; private set; }

        /// <summary>设置 / 清空字幕输出端（传 null 清空）。</summary>
        public void SetSubtitleSink(IPerformanceSubtitleSink sink)
        {
            SubtitleSink = sink;
        }

        /// <summary>
        /// 按说话者显示名查头像与头像侧：严格相等匹配，重名取第一条。说话者为空串（旁白）、名单里没有、或该条头像为空时返回 false。
        /// <paramref name="side"/> 取命中那一条的 <see cref="PerformanceCastEntry.Side"/>；没命中时为 <see cref="PerformanceAvatarSide.Left"/>。
        /// </summary>
        public bool TryGetAvatar(string speaker, out Sprite avatar, out PerformanceAvatarSide side)
        {
            avatar = null;
            side = PerformanceAvatarSide.Left;
            if (string.IsNullOrEmpty(speaker) || cast == null) return false;
            for (int i = 0; i < cast.Count; i++)
            {
                PerformanceCastEntry entry = cast[i];
                if (entry == null || !string.Equals(entry.Speaker, speaker, StringComparison.Ordinal)) continue;
                avatar = entry.Avatar;
                side = entry.Side;
                // Sprite 是 UnityEngine.Object，判空只用 != null。
                return avatar != null;
            }
            return false;
        }

        /// <summary>用本舞台的开关 + 配置的长按秒数组装策略。</summary>
        public PerformancePolicy BuildPolicy(PerformanceConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            return config.BuildPolicy(skippable, pauseWorld, hideHud);
        }

        /// <summary>从头播放。播放前强制：unscaled 时间（世界时停时照样走）、不外插（播完即停并触发 stopped）、不自动播放。</summary>
        /// <exception cref="InvalidOperationException">没接 Director。</exception>
        public void Play()
        {
            if (director == null) throw new InvalidOperationException($"PerformanceStage {name} 没接 PlayableDirector");
            ConfigureDirector();
            EnsureStoppedSubscribed();
            director.time = 0d;
            director.Play();
        }

        /// <summary>停顿后继续；也用于 <see cref="Pause"/> 之后恢复。</summary>
        public void Resume()
        {
            if (director != null) director.Resume();
        }

        /// <summary>
        /// 暂停时间轴（服务在台词记录 LOG 等覆盖层打开时调）。与 <see cref="HoldMarker"/> 停顿的区别：这里只停导演，
        /// 不发 <see cref="OnHold"/>、不改规则阶段、不出 ▼，恢复由调用方在规则仍是 Playing 时调 <see cref="Resume"/>；
        /// HoldMarker 停顿则由时间轴自己停下并通知，要玩家确认（或「自动」）才继续。停顿中本来就停着，不必再调本方法。
        /// </summary>
        public void Pause()
        {
            if (director != null) director.Pause();
        }

        /// <summary>停止（会触发 <see cref="OnFinished"/>）。</summary>
        public void Stop()
        {
            if (director != null) director.Stop();
        }

        public void OnNotify(Playable origin, INotification notification, object context)
        {
            if (!(notification is HoldMarker)) return;
            if (director != null) director.Pause();
            OnHold?.Invoke();
        }

        private void Awake()
        {
            // 放在 Awake：模板工厂已关 playOnAwake，这里再兜一次底，防止手改预制体后实例化即自动播放。
            ConfigureDirector();
            if (director != null && director.gameObject != gameObject && !warnedDirectorPlacement)
            {
                warnedDirectorPlacement = true;
                Log.Warn($"PerformanceStage {name}：PlayableDirector 不在舞台物体上，HoldMarker 通知与字幕轨道都收不到。", this);
            }
        }

        private void OnEnable() => EnsureStoppedSubscribed();

        private void OnDisable() => UnsubscribeStopped();

        private void OnDestroy() => UnsubscribeStopped();

        private void ConfigureDirector()
        {
            if (director == null) return;
            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
            director.extrapolationMode = DirectorWrapMode.None;
        }

        private void EnsureStoppedSubscribed()
        {
            if (stoppedSubscribed || director == null) return;
            director.stopped += HandleStopped;
            stoppedSubscribed = true;
        }

        private void UnsubscribeStopped()
        {
            if (!stoppedSubscribed) return;
            if (director != null) director.stopped -= HandleStopped;
            stoppedSubscribed = false;
        }

        private void HandleStopped(PlayableDirector stopped) => OnFinished?.Invoke();
    }
}
