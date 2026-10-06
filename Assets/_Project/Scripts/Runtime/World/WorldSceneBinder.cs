// 职责：登记已加载**世界场景**里的出生点锚点、传送点与世界相机（含它跟随的玩家根物体，以及场景里摆好的构图偏移）——
//   扫场景只此一处，出生点放置策略（WorldSpawnPlacement）与场景驱动（WorldSceneDriver）只读结果。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：LootSceneBinder / DialogueSceneBinder / QuestSceneBinder 各扫自己模块的组件，都不认识出生点锚点；
//      三个都不能扩展成「通用的场景登记器」——那会让对白 / 物资反过来依赖 World（依赖方向反了）。
//   2. 扩展不行：把扫描塞进 WorldSceneState 会让状态依赖具体场景物体，EditMode 里一步都测不了
//      （这正是机制波把「摆人」抽成 ISpawnPlacement 的理由，见 ISpawnPlacement 的类注释）。
//   3. 所以照 LootSceneBinder / DialogueSceneBinder 的形状新建一个本模块的场景登记器。
//
// **怎么认「这是世界场景」**：场景里**至少有一个 SpawnAnchor**。不能用「有没有 SmoothCameraFollow」当判据——
//   SampleScene（遭遇原型场景）也挂了 SmoothCameraFollow，用它当判据会把遭遇场景误认成世界场景，
//   于是 WorldSceneDriver 的逐 tick 推进会与 Monster 的 EncounterStep 同时推同一个玩家（速度翻倍）。
//
// 依赖只到 UnityEngine + VContainer 的 IStartable（登记时机），不认识存档、不认识流程。
using System;
using System.Collections.Generic;
using Game.Core.Logging;
using Game.IsometricExploration;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace Game.World
{
    /// <summary>
    /// 世界场景登记器（根作用域单例）。启动时扫一遍已加载场景，之后每加载 / 卸载一个场景扫一次。
    /// <para>
    /// <b>一次只认一个世界场景</b>：两张世界图同时加载时只登记第一个并打 Warn——流程层（<c>SceneGameState</c>）
    /// 是先卸旧场景、再加载新场景，所以正常路径下不会出现两张同时在的场景。
    /// </para>
    /// </summary>
    public sealed class WorldSceneBinder : IStartable, IDisposable
    {
        private readonly List<SpawnAnchor> spawns = new List<SpawnAnchor>();
        private readonly List<PortalAnchor> portals = new List<PortalAnchor>();
        private bool subscribed;

        /// <summary>本世界场景的跟随相机（挂在场景里那台相机上）；没有时为 null。</summary>
        public SmoothCameraFollow Camera { get; private set; }

        /// <summary>玩家根物体 = 相机的跟随目标（场景里就摆成这个关系）；没有时为 null。</summary>
        public Transform PlayerRoot { get; private set; }

        /// <summary>
        /// 场景里摆好的构图偏移（登记那一刻的 <c>相机位置 − 跟随目标位置</c>）。
        /// 出生点放置时用它把相机**立刻**对准新落点：让 SmoothCameraFollow 自己补间会在黑幕底下横穿整张图。
        /// </summary>
        public Vector3 CameraOffset { get; private set; }

        /// <summary>已登记的出生点锚点（含未激活物体上的）。场景卸载时移除已销毁项。</summary>
        public IReadOnlyList<SpawnAnchor> Spawns => spawns;

        /// <summary>已登记的传送点（含未激活物体上的）。场景卸载时移除已销毁项。</summary>
        public IReadOnlyList<PortalAnchor> Portals => portals;

        /// <summary>
        /// 传送点登记表的版本号：每次登记 / 移除都 +1。
        /// 订阅方（<see cref="WorldSceneDriver"/>）靠它判断「要不要重新订阅」——不必每帧比对列表，也不必每帧 Find。
        /// </summary>
        public int PortalsVersion { get; private set; }

        /// <summary>当前有没有世界场景（判据：登记到了至少一个出生点锚点）。</summary>
        public bool HasWorldScene => spawns.Count > 0;

        public void Start()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                {
                    BindScene(scene);
                }
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            subscribed = true;
        }

        public void Dispose()
        {
            if (subscribed)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                subscribed = false;
            }

            ClearWorldScene();
        }

        /// <summary>按出生点 id 找锚点；没有这个 id 时返回 false（调用方负责把「缺哪个 id」报出来）。</summary>
        public bool TryGetSpawn(string spawnId, out SpawnAnchor anchor)
        {
            anchor = null;
            if (string.IsNullOrEmpty(spawnId))
            {
                return false;
            }

            for (int i = 0; i < spawns.Count; i++)
            {
                SpawnAnchor candidate = spawns[i];
                if (candidate != null && string.Equals(candidate.SpawnId, spawnId, StringComparison.Ordinal))
                {
                    anchor = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>当前登记到的出生点 id 清单（拼报错文案用，只在失败路径上调，不在每帧路径上）。</summary>
        public string DescribeSpawns()
        {
            if (spawns.Count == 0)
            {
                return "一个都没有";
            }

            var text = new System.Text.StringBuilder();
            for (int i = 0; i < spawns.Count; i++)
            {
                SpawnAnchor anchor = spawns[i];
                if (anchor == null)
                {
                    continue;
                }

                if (text.Length > 0)
                {
                    text.Append('、');
                }

                text.Append(string.IsNullOrEmpty(anchor.SpawnId) ? $"({anchor.name} 的 spawnId 是空的)" : anchor.SpawnId);
            }

            return text.Length == 0 ? "一个都没有" : text.ToString();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => BindScene(scene);

        // 卸载回调时场景物体已销毁，UnityEngine.Object 的 == null 能认出来。
        private void OnSceneUnloaded(Scene scene)
        {
            bool removedPortal = false;
            for (int i = spawns.Count - 1; i >= 0; i--)
            {
                if (spawns[i] == null)
                {
                    spawns.RemoveAt(i);
                }
            }

            for (int i = portals.Count - 1; i >= 0; i--)
            {
                if (portals[i] == null)
                {
                    portals.RemoveAt(i);
                    removedPortal = true;
                }
            }

            if (removedPortal)
            {
                PortalsVersion++;
            }

            if (spawns.Count == 0)
            {
                ClearWorldScene();
            }
        }

        // 只在场景加载时跑一次，不在每帧路径上。
        private void BindScene(Scene scene)
        {
            List<SpawnAnchor> foundSpawns = null;
            List<PortalAnchor> foundPortals = null;
            SmoothCameraFollow camera = null;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                GameObject root = roots[i];

                SpawnAnchor[] sceneSpawns = root.GetComponentsInChildren<SpawnAnchor>(true);
                for (int s = 0; s < sceneSpawns.Length; s++)
                {
                    foundSpawns ??= new List<SpawnAnchor>();
                    foundSpawns.Add(sceneSpawns[s]);
                }

                PortalAnchor[] scenePortals = root.GetComponentsInChildren<PortalAnchor>(true);
                for (int p = 0; p < scenePortals.Length; p++)
                {
                    foundPortals ??= new List<PortalAnchor>();
                    foundPortals.Add(scenePortals[p]);
                }

                if (camera == null)
                {
                    camera = root.GetComponentInChildren<SmoothCameraFollow>(true);
                }
            }

            // 没有出生点锚点 = 不是世界场景（遭遇原型场景 SampleScene 走这一支，静默跳过）。
            if (foundSpawns == null)
            {
                return;
            }

            if (spawns.Count > 0)
            {
                Log.Warn($"WorldSceneBinder：场景 {scene.name} 也带出生点锚点，但同时还有另一张世界场景已登记；"
                         + "一次只认一个世界场景，本次跳过。正常路径下流程层是先卸旧场景再加载新场景。");
                return;
            }

            spawns.AddRange(foundSpawns);
            if (foundPortals != null)
            {
                portals.AddRange(foundPortals);
                PortalsVersion++;
            }

            Camera = camera;
            if (camera == null)
            {
                PlayerRoot = null;
                CameraOffset = Vector3.zero;
                Log.Warn($"WorldSceneBinder：世界场景 {scene.name} 里没有挂 SmoothCameraFollow 的相机，"
                         + "出生点放置会失败（缺相机就没法 SetTarget + 对准）。");
                return;
            }

            PlayerRoot = camera.Target;
            CameraOffset = PlayerRoot == null ? Vector3.zero : camera.transform.position - PlayerRoot.position;
            if (PlayerRoot == null)
            {
                Log.Warn($"WorldSceneBinder：世界场景 {scene.name} 的相机 {camera.name} 没设 Target（玩家根物体），"
                         + "出生点放置会失败（摆好玩家也没人跟着）。");
            }

            Log.Debug($"WorldSceneBinder：场景 {scene.name} 登记了 {spawns.Count} 个出生点锚点、"
                      + $"{portals.Count} 个传送点，玩家根物体 {(PlayerRoot == null ? "无" : PlayerRoot.name)}");
        }

        private void ClearWorldScene()
        {
            spawns.Clear();
            if (portals.Count > 0)
            {
                portals.Clear();
                PortalsVersion++;
            }

            Camera = null;
            PlayerRoot = null;
            CameraOffset = Vector3.zero;
        }
    }
}
