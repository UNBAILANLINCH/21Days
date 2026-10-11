// 职责：登记已加载场景里的任务地点（QuestLocation）与场景相机，按地点键 / 对话 NPC 解析任务目标的世界坐标（测距点 + 头顶锚点），供目标判定与指引读取；
//   NPC 头顶锚点优先对齐其对话「…/!」图标（DialogueInteractableMarker.TryGetIconAnchor），其次碰撞体顶部 + 抬升，再次固定高度。
// 为什么新建：DialogueSceneBinder 只管对白物体，把任务地点塞进去会让对白模块认识任务；
//   地点是场景物体、不在根容器里，只能扫场景登记，且扫场景只该有一处，判定与指引只读结果。
using System;
using System.Collections.Generic;
using Game.Core.Logging;
using Game.Dialogue;
using Game.Interaction;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace Game.Quest
{
    /// <summary>
    /// 场景绑定入口点。只登记场景里摆好的地点（含未激活的）；运行时 Instantiate 的地点不登记。
    /// DontDestroyOnLoad 场景不在扫描范围内。玩家锚点取统一交互登记表的 <see cref="IInteractionRegistry.Actor"/>
    /// （扫玩家标记全工程只在 InteractionRegistry 一处，PRP/interaction D7），不再扫一遍。
    /// </summary>
    public sealed class QuestSceneBinder : IStartable, IDisposable
    {
        private readonly DialogueSceneBinder dialogueBinder;
        private readonly IInteractionRegistry interaction;
        private readonly QuestConfig config;
        private readonly List<QuestLocation> locations = new List<QuestLocation>();
        private readonly Dictionary<string, QuestLocation> byKey = new Dictionary<string, QuestLocation>(StringComparer.Ordinal);
        // NPC → 根物体上的 Collider 与对话头顶标记（都可为 null，也缓存）：解析头顶锚点每帧要用，GetComponent 只在首次遇到时做一次。
        private readonly Dictionary<DialogueInteractable, NpcParts> npcParts = new Dictionary<DialogueInteractable, NpcParts>();
        private readonly List<DialogueInteractable> staleNpcKeys = new List<DialogueInteractable>();
        private bool subscribed;

        /// <param name="dialogueBinder">TalkTo 目标的 NPC 从它登记的 <see cref="DialogueSceneBinder.Bound"/> 里找。</param>
        /// <param name="interaction">玩家锚点从统一交互登记表的 <see cref="IInteractionRegistry.Actor"/> 取。</param>
        public QuestSceneBinder(DialogueSceneBinder dialogueBinder, IInteractionRegistry interaction, QuestConfig config)
        {
            this.dialogueBinder = dialogueBinder ?? throw new ArgumentNullException(nameof(dialogueBinder));
            this.interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
            // ScriptableObject 是 UnityEngine.Object，判空只用 == null。
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
        }

        /// <summary>已加载场景里登记的全部任务地点。场景卸载时移除已销毁项。</summary>
        public IReadOnlyList<QuestLocation> Locations => locations;

        /// <summary>场景主相机；场景加载 / 卸载后重取，不每帧取。没有时为 null（用 == null 判）。</summary>
        public Camera SceneCamera { get; private set; }

        /// <summary>玩家锚点；场景里没有玩家标记时为 null。</summary>
        public Transform PlayerAnchor => interaction.Actor == null ? null : interaction.Actor.Anchor;

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
            npcParts.Clear();
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
        /// 锚点：地点 → 位置上方 <see cref="QuestConfig.LocationMarkerHeight"/>；NPC 按三级取（<see cref="ResolveNpcAnchor"/>）：
        /// ① 根物体上的 <see cref="DialogueInteractableMarker"/> 给出图标锚点 → 与对话「…/!」图标同一位置；
        /// ② 否则根物体 Collider 顶部再抬 <see cref="QuestConfig.MarkerLift"/>（x/z 取包围盒中心）；③ 都没有时同地点规则。
        /// 无分配（首次遇到某 NPC 时查一次 Collider 与标记组件）。
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
            return new QuestTarget(position, position + Vector3.up * config.LocationMarkerHeight, null);
        }

        /// <summary>
        /// NPC 头顶锚点的三级规则（纯函数，供 <see cref="NpcTarget"/> 与 EditMode 测试共用）：
        /// ① 有对话图标锚点 → 直接用它（任务标记与被它接管的「…/!」图标重合）；
        /// ② 否则有碰撞体包围盒 → 顶部 + <paramref name="markerLift"/>，x/z 取包围盒中心；
        /// ③ 否则 <paramref name="position"/> 上方 <paramref name="fallbackHeight"/>。
        /// 公开是为了测试程序集可调（Game.Runtime 未对测试开 InternalsVisibleTo）。
        /// </summary>
        public static Vector3 ResolveNpcAnchor(Vector3 position, bool hasIconAnchor, Vector3 iconAnchor,
            bool hasBounds, Bounds bounds, float markerLift, float fallbackHeight)
        {
            if (hasIconAnchor) return iconAnchor;
            if (hasBounds) return new Vector3(bounds.center.x, bounds.max.y + markerLift, bounds.center.z);
            return position + Vector3.up * fallbackHeight;
        }

        private QuestTarget NpcTarget(DialogueInteractable interactable)
        {
            Vector3 position = interactable.transform.position;
            NpcParts parts = CachedParts(interactable);
            Vector3 iconAnchor = default;
            bool hasIconAnchor = parts.Marker != null && parts.Marker.TryGetIconAnchor(out iconAnchor);
            bool hasBounds = parts.Collider != null;
            Bounds bounds = hasBounds ? parts.Collider.bounds : default;
            // 三级都会带上交互组件：标记无论落在哪一级锚点，都摆在这个 NPC 头顶，要接管它的图标。
            Vector3 anchor = ResolveNpcAnchor(position, hasIconAnchor, iconAnchor, hasBounds, bounds,
                config.MarkerLift, config.LocationMarkerHeight);
            return new QuestTarget(position, anchor, interactable);
        }

        // 首次遇到某 NPC 时各 GetComponent 一次，结果（含 null）缓存；已销毁项在场景卸载时清掉。
        private NpcParts CachedParts(DialogueInteractable interactable)
        {
            if (!npcParts.TryGetValue(interactable, out NpcParts parts))
            {
                parts = new NpcParts(interactable.GetComponent<Collider>(), interactable.GetComponent<DialogueInteractableMarker>());
                npcParts.Add(interactable, parts);
            }

            return parts;
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
            PruneNpcParts();
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

        private void PruneNpcParts()
        {
            staleNpcKeys.Clear();
            foreach (KeyValuePair<DialogueInteractable, NpcParts> pair in npcParts)
            {
                if (pair.Key == null) staleNpcKeys.Add(pair.Key);
            }
            for (int i = 0; i < staleNpcKeys.Count; i++)
            {
                npcParts.Remove(staleNpcKeys[i]);
            }
            staleNpcKeys.Clear();
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

        // 某 NPC 根物体上缓存的两个组件；任一可为 null（用 == null 判）。只在本类内部用，不单拆文件。
        private readonly struct NpcParts
        {
            public NpcParts(Collider collider, DialogueInteractableMarker marker)
            {
                Collider = collider;
                Marker = marker;
            }

            public Collider Collider { get; }
            public DialogueInteractableMarker Marker { get; }
        }
    }
}
