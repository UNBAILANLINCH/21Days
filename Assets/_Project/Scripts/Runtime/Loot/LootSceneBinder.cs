// 职责：登记已加载场景里的物资箱与头顶标记，按存档恢复开合；重置时全部合上；沉浸模式切换时逐个切标记显隐。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：QuestSceneBinder / DialogueSceneBinder 各扫自己模块的场景组件，不认识物资箱。
//   2. 扩展不行：把箱子塞进它们会让任务 / 对白模块反向依赖 Loot。
//   箱子是场景物体、不在根容器里，只能扫场景登记；扫场景只该有一处，焦点与 HUD 只读结果（同 QuestSceneBinder）。
using System;
using System.Collections.Generic;
using Game.Core.Events;
using Game.Core.Logging;
using Game.Core.UI;
using MessagePipe;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace Game.Loot
{
    /// <summary>
    /// 场景绑定入口点。只登记场景里摆好的箱子与标记（含未激活的）；运行时 Instantiate 的不登记。
    /// DontDestroyOnLoad 场景不在扫描范围内（SceneManager 的场景列表不含它）。
    /// </summary>
    public sealed class LootSceneBinder : IStartable, IDisposable
    {
        private readonly LootService service;
        private readonly ISubscriber<LootResetEvent> resetSubscriber;
        private readonly ISubscriber<HudVisibilityChangedEvent> hudSubscriber;
        private readonly IHudVisibility hudVisibility;
        private readonly List<SupplyCrate> crates = new List<SupplyCrate>();
        private readonly List<SupplyCrateMarker> markers = new List<SupplyCrateMarker>();
        private IDisposable subscription;
        private bool sceneSubscribed;

        public LootSceneBinder(LootService service, ISubscriber<LootResetEvent> resetSubscriber,
            ISubscriber<HudVisibilityChangedEvent> hudSubscriber, IHudVisibility hudVisibility)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            this.resetSubscriber = resetSubscriber ?? throw new ArgumentNullException(nameof(resetSubscriber));
            this.hudSubscriber = hudSubscriber ?? throw new ArgumentNullException(nameof(hudSubscriber));
            this.hudVisibility = hudVisibility ?? throw new ArgumentNullException(nameof(hudVisibility));
        }

        /// <summary>已加载场景里登记的全部物资箱。场景卸载时移除已销毁项。</summary>
        public IReadOnlyList<SupplyCrate> Crates => crates;

        public void Start()
        {
            // 订阅句柄必须托管（EventConventions.cs 第 5 条）。
            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            resetSubscriber.Subscribe(_ => CloseAll()).AddTo(bag);
            hudSubscriber.Subscribe(e => ApplyHudHidden(e.Hidden)).AddTo(bag);
            subscription = bag.Build();

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
            if (subscription != null)
            {
                subscription.Dispose();
                subscription = null;
            }
            if (sceneSubscribed)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                sceneSubscribed = false;
            }
            crates.Clear();
            markers.Clear();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => BindScene(scene);

        // 卸载回调时场景物体已销毁，UnityEngine.Object 的 == null 能认出来。
        private void OnSceneUnloaded(Scene scene)
        {
            for (int i = crates.Count - 1; i >= 0; i--)
            {
                if (crates[i] == null) crates.RemoveAt(i);
            }
            for (int i = markers.Count - 1; i >= 0; i--)
            {
                if (markers[i] == null) markers.RemoveAt(i);
            }
        }

        // 只在场景加载时跑一次，不在每帧路径上。
        private void BindScene(Scene scene)
        {
            int count = 0;
            bool hidden = hudVisibility.IsHudHidden;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (SupplyCrate crate in root.GetComponentsInChildren<SupplyCrate>(true))
                {
                    if (crates.Contains(crate)) continue;
                    if (string.IsNullOrEmpty(crate.Key))
                    {
                        Log.Warn($"LootSceneBinder：物资箱 {crate.name} 没填箱子键，开箱不会被记录。", crate);
                    }
                    crates.Add(crate);
                    crate.SetOpened(service.IsCollected(crate.Key));
                    count++;
                }

                foreach (SupplyCrateMarker marker in root.GetComponentsInChildren<SupplyCrateMarker>(true))
                {
                    if (markers.Contains(marker)) continue;
                    markers.Add(marker);
                    marker.SetHudHidden(hidden);
                }
            }
            if (count > 0) Log.Debug($"LootSceneBinder：场景 {scene.name} 登记了 {count} 个物资箱");
        }

        private void CloseAll()
        {
            for (int i = 0; i < crates.Count; i++)
            {
                SupplyCrate crate = crates[i];
                if (crate != null) crate.SetOpened(false);
            }
            RefreshMarkers();
        }

        private void ApplyHudHidden(bool hidden)
        {
            for (int i = 0; i < markers.Count; i++)
            {
                SupplyCrateMarker marker = markers[i];
                if (marker != null) marker.SetHudHidden(hidden);
            }
        }

        // 未激活物体上的标记收不到 OnOpenedChanged（OnEnable 没跑），这里补刷一次。
        private void RefreshMarkers()
        {
            for (int i = 0; i < markers.Count; i++)
            {
                SupplyCrateMarker marker = markers[i];
                if (marker != null) marker.Refresh();
            }
        }
    }
}
