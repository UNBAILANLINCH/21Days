// 职责：登记已加载场景里的照镜对象（MirrorSubject）与通灵视区域（SpiritSightZone），并按遭遇场景的投影约定把场景坐标换成逻辑 XY；
//   给服务提供照镜候选、给通灵视提供「玩家在哪个生效区域里」。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：LootSceneBinder / QuestSceneBinder 各扫自己模块的场景组件，不认识镜的标记。
//   2. 扩展不行：把镜的标记塞进它们会让拾取 / 任务模块反向依赖 Mirror。
//   标记是场景物体、不在根容器里，只能扫场景登记；扫场景只该有一处，服务与呈现器只读结果（同 LootSceneBinder）。
using System;
using System.Collections.Generic;
using Game.Core.Logging;
using Game.Core.Telemetry;
using Game.Monster;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace Game.Mirror
{
    /// <summary>
    /// 场景绑定入口点。只登记场景里摆好的标记与区域（含未激活的）；运行时 Instantiate 的不登记。
    /// DontDestroyOnLoad 场景不在扫描范围内（SceneManager 的场景列表不含它）。
    /// 投影：场景里有 <see cref="EncounterSceneView"/> 时用它的 <see cref="EncounterSceneView.ToLogicPosition"/>（XZ / XY 由它决定）；
    /// 没有时按 XY（与 EncounterSceneView 的默认 useXZPlane = false 一致）。
    /// </summary>
    public sealed class MirrorSceneBinder : IStartable, IDisposable
    {
        private readonly MonsterModel monster;
        private readonly ITelemetryScope telemetry;
        private readonly List<MirrorSubject> subjects = new List<MirrorSubject>();
        private readonly List<SpiritSightZone> zones = new List<SpiritSightZone>();
        private EncounterSceneView projectionView;
        private bool sceneSubscribed;

        public MirrorSceneBinder(MonsterModel monster, ITelemetryScope telemetry)
        {
            this.monster = monster ?? throw new ArgumentNullException(nameof(monster));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>已加载场景里登记的全部照镜对象。场景卸载时移除已销毁项。</summary>
        public IReadOnlyList<MirrorSubject> Subjects => subjects;

        /// <summary>已加载场景里登记的全部通灵视区域。场景卸载时移除已销毁项。</summary>
        public IReadOnlyList<SpiritSightZone> Zones => zones;

        public void Start()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) BindScene(scene);
            }
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            sceneSubscribed = true;
        }

        public void Dispose()
        {
            if (sceneSubscribed)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                sceneSubscribed = false;
            }
            subjects.Clear();
            zones.Clear();
            projectionView = null;
        }

        /// <summary>场景坐标 → 逻辑 XY，约定同遭遇场景的 <see cref="EncounterSceneView"/>。</summary>
        public Vector2 ToLogicPosition(Vector3 worldPosition)
        {
            // EncounterSceneView 是 UnityEngine.Object，判空只用 != null（场景卸载后成伪空，自动退回 XY）。
            if (projectionView != null) return projectionView.ToLogicPosition(worldPosition);
            return new Vector2(worldPosition.x, worldPosition.y);
        }

        /// <summary>标记的逻辑位置：勾了 FollowsMonster 取 MonsterModel.Position，否则取场景坐标投影。subject 为空返回零向量。</summary>
        public Vector2 LogicPositionOf(MirrorSubject subject)
        {
            if (subject == null) return Vector2.zero;
            return MirrorRules.CandidatePosition(subject.FollowsMonster, ToLogicPosition(subject.WorldPosition),
                monster.Position);
        }

        /// <summary>
        /// 把当前激活的标记换算成照镜候选，写进调用方复用的两个列表（先清空）：candidates[i] 对应 owners[i]。
        /// owners 可传 null（只要候选）。按键时调用，不在每帧路径上。
        /// </summary>
        public void CollectCandidates(List<MirrorCandidate> candidates, List<MirrorSubject> owners)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            candidates.Clear();
            owners?.Clear();
            for (int i = 0; i < subjects.Count; i++)
            {
                MirrorSubject subject = subjects[i];
                if (subject == null || !subject.isActiveAndEnabled) continue;
                candidates.Add(new MirrorCandidate(LogicPositionOf(subject), subject.Kind, subject.YaoId));
                owners?.Add(subject);
            }
        }

        /// <summary>
        /// 玩家逻辑位置落在哪个「条件满足且激活」的区域里；多个重叠时取登记顺序靠前的；都不在返回 null（用 == null 判）。
        /// </summary>
        public SpiritSightZone FindActiveZone(Vector2 logicPosition)
        {
            for (int i = 0; i < zones.Count; i++)
            {
                SpiritSightZone zone = zones[i];
                if (zone == null || !zone.isActiveAndEnabled || !zone.IsConditionMet) continue;
                if (!zone.TryGetWorldCorners(out Vector3 min, out Vector3 max)) continue;
                if (SpiritSightRules.InZone(logicPosition, ToLogicPosition(min), ToLogicPosition(max))) return zone;
            }
            return null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => BindScene(scene);

        // 卸载回调时场景物体已销毁，UnityEngine.Object 的 == null 能认出来。
        private void OnSceneUnloaded(Scene scene)
        {
            for (int i = subjects.Count - 1; i >= 0; i--)
            {
                if (subjects[i] == null) subjects.RemoveAt(i);
            }
            for (int i = zones.Count - 1; i >= 0; i--)
            {
                if (zones[i] == null) zones.RemoveAt(i);
            }
            // 投影视图随场景销毁时，到仍加载着的场景里重找一次：重进场景可能是「先加载新场景、后卸载旧场景」，
            // 新场景加载时旧视图还活着没被换掉，这里不补找就会一直按 XY 投影。
            if (projectionView == null) projectionView = FindProjectionView();
        }

        private static EncounterSceneView FindProjectionView()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    EncounterSceneView view = root.GetComponentInChildren<EncounterSceneView>(true);
                    if (view != null) return view;
                }
            }
            return null;
        }

        // 只在场景加载时跑一次，不在每帧路径上。
        private void BindScene(Scene scene)
        {
            int subjectCount = 0;
            int zoneCount = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (projectionView == null)
                {
                    EncounterSceneView view = root.GetComponentInChildren<EncounterSceneView>(true);
                    if (view != null) projectionView = view;
                }

                foreach (MirrorSubject subject in root.GetComponentsInChildren<MirrorSubject>(true))
                {
                    if (subjects.Contains(subject)) continue;
                    if (subject.Kind == MirrorSubjectKind.Yao && subject.YaoId <= 0)
                    {
                        Log.Warn($"MirrorSceneBinder：妖标记 {subject.name} 没填 yaoId，照镜只会得到模糊轮廓。", subject);
                    }
                    subjects.Add(subject);
                    subjectCount++;
                }

                foreach (SpiritSightZone zone in root.GetComponentsInChildren<SpiritSightZone>(true))
                {
                    if (zones.Contains(zone)) continue;
                    zones.Add(zone);
                    zoneCount++;
                }
            }

            if (subjectCount > 0 || zoneCount > 0)
            {
                telemetry.Track("mirror_subjects_bound", ("count", subjectCount), ("zones", zoneCount),
                    ("scene", scene.name));
            }
        }
    }
}
