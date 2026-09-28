// 职责：场景里的演出挂载点——玩家进入触发区（或场景开始时）按 id 拉起一段演出；支持「只播一次」；
//   可指定演出的摆放锚点，并可在演出期间隐藏触发者（玩家，演出里另有替身）与点名的场景物体（NPC、巡逻怪、任务标记）。
// 为什么新建（复用 → 扩展 → 新建）：DialogueInteractable 是按键交互的对白入口、属于 Dialogue；演出触发是进入即播、
//   不需要焦点与按键，语义不同且 Performance 不得依赖 Dialogue。场景物体不在容器里，服务由 PerformanceSceneBinder 注入。
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.Core.Logging;
using Game.Core.Telemetry;
using UnityEngine;

namespace Game.Performance
{
    /// <summary>
    /// 演出触发器。OnEnter 模式需要同物体上有 isTrigger 的 Collider / Collider2D；只认根上（含父级）带
    /// <see cref="PerformanceTriggerActor"/> 的对象。OnSceneStart 模式由 <see cref="PerformanceSceneBinder"/> 在启动完成后调 <see cref="TryFire"/>。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerformanceTrigger : MonoBehaviour
    {
        [Tooltip("要播放的演出 id（Addressables Performance 组地址）。")]
        [PerformanceId]
        [SerializeField] private string performanceId = string.Empty;

        [Tooltip("触发时机：进入触发区 / 场景开始。")]
        [SerializeField] private PerformanceTriggerMode mode = PerformanceTriggerMode.OnEnter;

        [Tooltip("只播一次：存档里已播过（完整播完或被跳过）就不再触发。")]
        [SerializeField] private bool once = true;

        [Tooltip("演出的摆放锚点：演出实例摆到它的世界位置与朝向。留空 = 不传摆放（实例保持预制体位姿）。")]
        [SerializeField] private Transform anchor;

        [Tooltip("演出期间隐藏触发者（带 PerformanceTriggerActor 的根）下的全部 Renderer 与 Canvas（头顶名牌），结束后恢复原状。演出里另有替身演员时勾上。")]
        [SerializeField] private bool hideActorVisual;

        [Tooltip("演出期间隐藏的场景物体根（NPC、巡逻怪、任务标记等），结束后恢复。藏的是根下全部 Renderer 与 Canvas 的 enabled，不停用物体。")]
        [SerializeField] private GameObject[] hiddenDuringPlay = new GameObject[0];

        private IPerformanceService service;
        private ITelemetryScope telemetry = NullTelemetryScope.Instance;
        private bool warnedUnbound;
        private bool warnedEmptyId;

        public string PerformanceId => performanceId;
        public PerformanceTriggerMode Mode => mode;
        public bool Once => once;
        public Transform Anchor => anchor;
        public bool HideActorVisual => hideActorVisual;
        public IReadOnlyList<GameObject> HiddenDuringPlay => hiddenDuringPlay;

        /// <summary>注入服务与埋点（由 <see cref="PerformanceSceneBinder"/> 调用）。</summary>
        public void Bind(IPerformanceService performanceService, ITelemetryScope telemetryScope)
        {
            service = performanceService;
            telemetry = telemetryScope ?? NullTelemetryScope.Instance;
        }

        /// <summary>
        /// 按判定规则尝试触发；未绑定、id 为空、已播过、服务忙时不触发。
        /// 不知道触发者（场景开始模式）时，<c>hideActorVisual</c> 按场景里的 <see cref="PerformanceTriggerActor"/> 找（一个场景一个）。
        /// </summary>
        public void TryFire()
        {
            TryFire(null);
        }

        /// <summary>同 <see cref="TryFire()"/>，指定触发者（进入触发区时就是进入的那个）；传 null 按场景查找。</summary>
        public void TryFire(PerformanceTriggerActor actor)
        {
            if (service == null)
            {
                if (!warnedUnbound)
                {
                    warnedUnbound = true;
                    Log.Warn($"PerformanceTrigger {name}：未绑定演出服务（Boot 场景没挂 PerformanceInstaller？），不触发。", this);
                }
                return;
            }
            if (string.IsNullOrEmpty(performanceId))
            {
                if (!warnedEmptyId)
                {
                    warnedEmptyId = true;
                    Log.Warn($"PerformanceTrigger {name}：没填 performanceId，不触发。", this);
                }
                return;
            }
            bool hasPlayed = once && service.HasPlayed(performanceId);
            if (!PerformanceTriggerRules.ShouldFire(once, hasPlayed, service.IsRunning, out string reason))
            {
                telemetry.Track("trigger_skipped", ("id", performanceId), ("reason", reason));
                return;
            }
            telemetry.Track("trigger_fired", ("id", performanceId), ("mode", ModeName(mode)));
            PerformanceTriggerRules.HiddenVisuals hidden = null;
            List<GameObject> hideRoots = CollectHideRoots(actor);
            // Renderer 与 Canvas（头顶名牌 / 气泡 / 任务标记）一起藏，只切 enabled、不 SetActive，物体脚本照跑。
            if (hideRoots.Count > 0) hidden = PerformanceTriggerRules.HideVisuals(hideRoots);
            PlayAsync(performanceId, PerformancePlacement.FromTransform(anchor), hidden).Forget();
        }

        private void OnTriggerEnter(Collider other) // lint-ok: 触发区只用来拉起表现层演出，不进逻辑判定、不影响回放
        {
            if (mode != PerformanceTriggerMode.OnEnter || other == null) return;
            PerformanceTriggerActor actor = other.GetComponentInParent<PerformanceTriggerActor>();
            if (actor == null) return;
            TryFire(actor);
        }

        private void OnTriggerEnter2D(Collider2D other) // lint-ok: 触发区只用来拉起表现层演出，不进逻辑判定、不影响回放
        {
            if (mode != PerformanceTriggerMode.OnEnter || other == null) return;
            PerformanceTriggerActor actor = other.GetComponentInParent<PerformanceTriggerActor>();
            if (actor == null) return;
            TryFire(actor);
        }

        // Forget 出去的异步不能让异常悄悄消失：取消是正常结束，其他异常记 Error + 埋点。
        // 不传本物体的销毁令牌：演出挂在 DontDestroyOnLoad 的根上，触发器所在场景卸载不该打断演出。
        // hidden 非空时：演出结束（完成 / 跳过 / 取消 / 异常，同一个 finally）后按原值恢复被藏组件的 enabled。
        private async UniTask PlayAsync(string id, PerformancePlacement placement, PerformanceTriggerRules.HiddenVisuals hidden)
        {
            try
            {
                await service.PlayAsync(id, placement);
            }
            catch (OperationCanceledException)
            {
                // 演出被取消属于正常收尾，服务已埋 ended(cancelled)。
            }
            catch (Exception e)
            {
                telemetry.TrackError("trigger_play_failed", e, TelemetryProps.Of(("id", id)));
                Log.Error($"PerformanceTrigger：演出 {id} 播放失败：{e.Message}");
            }
            finally
            {
                PerformanceTriggerRules.RestoreVisuals(hidden);
            }
        }

        // 只在触发这一刻查一次（不在每帧路径上）。触发者（hideActorVisual）+ 场景里点名的物体根。
        private List<GameObject> CollectHideRoots(PerformanceTriggerActor actor)
        {
            var roots = new List<GameObject>();
            if (hideActorVisual)
            {
                if (actor == null) actor = FindObjectOfType<PerformanceTriggerActor>(); // lint-ok: 仅触发时查一次，场景开始模式拿不到进入者
                if (actor != null) roots.Add(actor.gameObject);
            }
            if (hiddenDuringPlay != null)
            {
                for (int i = 0; i < hiddenDuringPlay.Length; i++)
                {
                    if (hiddenDuringPlay[i] != null) roots.Add(hiddenDuringPlay[i]);
                }
            }
            return roots;
        }

        private static string ModeName(PerformanceTriggerMode value)
        {
            return value == PerformanceTriggerMode.OnSceneStart ? "on_scene_start" : "on_enter";
        }
    }
}
