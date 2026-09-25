// 职责：登记已加载场景里的任务地点（QuestLocation）与场景相机，按地点键 / 对话 NPC 解析任务目标的世界坐标（测距点 + 头顶锚点），供目标判定与指引读取。
// 为什么新建：DialogueSceneBinder 只管对白物体与玩家标记，把任务地点塞进去会让对白模块认识任务；
//   地点是场景物体、不在根容器里，只能扫场景登记，且扫场景只该有一处，判定与指引只读结果。
using System;
using System.Collections.Generic;
using Game.Core.Logging;
using Game.Dialogue;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace Game.Quest
{
    /// <summary>
    /// 场景绑定入口点。只登记场景里摆好的地点（含未激活的）；运行时 Instantiate 的地点不登记。
    /// DontDestroyOnLoad 场景不在扫描范围内。玩家锚点复用 <see cref="DialogueSceneBinder.Actor"/>，不再扫一遍。
    /// </summary>
    public sealed class QuestSceneBinder : IStartable, IDisposable
    {
        private readonly DialogueSceneBinder dialogueBinder;
        private readonly QuestConfig config;
        private readonly List<QuestLocation> locations = new List<QuestLocation>();
        private readonly Dictionary<string, QuestLocation> byKey = new Dictionary<string, QuestLocation>(StringComparer.Ordinal);
        // NPC → 根物体上的 Collider（可为 null，也缓存）：解析头顶锚点每帧要用，GetComponent 只在首次遇到时做一次。
        private readonly Dictionary<DialogueInteractable, Collider> colliders = new Dictionary<DialogueInteractable, Collider>();
        private readonly List<DialogueInteractable> staleColliderKeys = new List<DialogueInteractable>();
        private bool subscribed;

        public QuestSceneBinder(DialogueSceneBinder dialogueBinder, QuestConfig config)
        {
            this.dialogueBinder = dialogueBinder ?? throw new ArgumentNullException(nameof(dialogueBinder));
            // ScriptableObject 是 UnityEngine.Object，判空只用 == null。
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
        }

        /// <summary>已加载场景里登记的全部任务地点。场景卸载时移除已销毁项。</summary>
        public IReadOnlyList<QuestLocation> Locations => locations;

        /// <summary>场景主相机；场景加载 / 卸载后重取，不每帧取。没有时为 null（用 == null 判）。</summary>
        public Camera SceneCamera { get; private set; }

        /// <summary>玩家锚点；场景里没有玩家标记时为 null。</summary>
        public Transform PlayerAnchor => dialogueBinder.Actor == null ? null : dialogueBinder.Actor.Anchor;

        public void Start()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) BindScene(scene);
            }
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            subscribed = true;
            SceneCamera = Camera.main;
        }

        public void Dispose()
        {
            if (!subscribed) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            subscribed = false;
            locations.Clear();
            byKey.Clear();
            colliders.Clear();
            SceneCamera = null;
        }

        /// <summary>按地点键取地点；键为空、未登记或已销毁都返回 false。</summary>
        public bool TryGetLocation(string key, out QuestLocation location)
        {
            if (string.IsNullOrEmpty(key) || !byKey.TryGetValue(key, out location) || location == null)
            {
                location = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// 解析目标，按顺序尝试：① 显式 <see cref="QuestObjectiveDefinition.LocationKey"/> 查地点；
        /// ② <see cref="QuestObjectiveKind.ReachLocation"/> 用自己的 Key（即地点键）查地点；
        /// ③ <see cref="QuestObjectiveKind.TalkTo"/> 按 Key（对话编号）在对白登记表里找 NPC。
        /// 都不命中（含 Counter）返回 false，无指引。
        /// 锚点：地点 → 位置上方 <see cref="QuestConfig.LocationMarkerHeight"/>；NPC → 根物体 Collider 顶部再抬
        /// <see cref="QuestConfig.MarkerLift"/>（x/z 取包围盒中心），没有 Collider 时同地点规则。无分配（首次遇到某 NPC 时查一次 Collider）。
        /// </summary>
        public bool TryResolveTarget(in QuestObjectiveDefinition objective, out QuestTarget target)
        {
            if (!string.IsNullOrEmpty(objective.LocationKey))
            {
                if (TryGetLocation(objective.LocationKey, out QuestLocation location))
                {
                    target = LocationTarget(location.Position);
                    return true;
                }

                target = default;
                return false;
            }

            if (objective.Kind == QuestObjectiveKind.ReachLocation)
            {
                if (TryGetLocation(objective.Key, out QuestLocation location))
                {
                    target = LocationTarget(location.Position);
                    return true;
                }

                target = default;
                return false;
            }

            if (objective.Kind == QuestObjectiveKind.TalkTo && int.TryParse(objective.Key, out int dialogueId))
            {
                IReadOnlyList<DialogueInteractable> bound = dialogueBinder.Bound;
                for (int i = 0; i < bound.Count; i++)
                {
                    DialogueInteractable interactable = bound[i];
                    if (interactable != null && interactable.DialogueId == dialogueId)
                    {
                        target = NpcTarget(interactable);
                        return true;
                    }
                }
            }

            target = default;
            return false;
        }

        private QuestTarget LocationTarget(Vector3 position)
        {
            return new QuestTarget(position, position + Vector3.up * config.LocationMarkerHeight);
        }

        private QuestTarget NpcTarget(DialogueInteractable interactable)
        {
            Vector3 position = interactable.transform.position;
            Collider collider = CachedCollider(interactable);
            if (collider == null) return LocationTarget(position);

            Bounds bounds = collider.bounds;
            Vector3 anchor = new Vector3(bounds.center.x, bounds.max.y + config.MarkerLift, bounds.center.z);
            return new QuestTarget(position, anchor);
        }

        // 首次遇到某 NPC 时 GetComponent 一次，结果（含 null）缓存；已销毁项在场景卸载时清掉。
        private Collider CachedCollider(DialogueInteractable interactable)
        {
            if (!colliders.TryGetValue(interactable, out Collider collider))
            {
                collider = interactable.GetComponent<Collider>();
                colliders.Add(interactable, collider);
            }

            return collider;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            BindScene(scene);
            SceneCamera = Camera.main;
        }

        // 卸载回调时场景物体已销毁，UnityEngine.Object 的 == null 能认出来。
        private void OnSceneUnloaded(Scene scene)
        {
            for (int i = locations.Count - 1; i >= 0; i--)
            {
                if (locations[i] == null) locations.RemoveAt(i);
            }
            RebuildIndex();
            PruneColliders();
            SceneCamera = Camera.main;
        }

        // 只在场景加载时跑一次，不在每帧路径上。
        private void BindScene(Scene scene)
        {
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (QuestLocation location in root.GetComponentsInChildren<QuestLocation>(true))
                {
                    if (locations.Contains(location)) continue;
                    locations.Add(location);
                    Index(location);
                    count++;
                }
            }
            if (count > 0) Log.Debug($"QuestSceneBinder：场景 {scene.name} 登记了 {count} 个任务地点");
        }

        private void PruneColliders()
        {
            staleColliderKeys.Clear();
            foreach (KeyValuePair<DialogueInteractable, Collider> pair in colliders)
            {
                if (pair.Key == null) staleColliderKeys.Add(pair.Key);
            }
            for (int i = 0; i < staleColliderKeys.Count; i++)
            {
                colliders.Remove(staleColliderKeys[i]);
            }
            staleColliderKeys.Clear();
        }

        private void RebuildIndex()
        {
            byKey.Clear();
            for (int i = 0; i < locations.Count; i++)
            {
                Index(locations[i]);
            }
        }

        // 键重复时保留先登记的那个。
        private void Index(QuestLocation location)
        {
            string key = location.Key;
            if (string.IsNullOrEmpty(key))
            {
                Log.Warn($"QuestSceneBinder：任务地点 {location.name} 没填地点键，不参与目标判定与指引。", location);
                return;
            }

            if (byKey.TryGetValue(key, out QuestLocation existing) && existing != null)
            {
                Log.Warn($"QuestSceneBinder：地点键「{key}」重复（{existing.name} 与 {location.name}），保留先登记的 {existing.name}。", location);
                return;
            }

            byKey[key] = location;
        }
    }
}
