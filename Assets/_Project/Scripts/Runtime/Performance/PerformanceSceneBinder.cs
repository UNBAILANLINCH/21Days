// 职责：把演出服务注入场景里的每个 PerformanceTrigger——启动时扫已加载场景，之后每加载一个场景扫一次；
//   启动完成后触发 OnSceneStart 模式的触发器（启动完成之后才加载的场景，在加载时触发）。
// 为什么新建（复用 → 扩展 → 新建）：PerformanceTrigger 是场景物体、不在根容器里，只能扫场景注入；
//   DialogueSceneBinder / LootSceneBinder 各扫自己模块的组件，塞进去会让它们认识演出（方向是 Dialogue → Performance）。
using System;
using System.Collections.Generic;
using Game.Core.Events;
using Game.Core.Logging;
using Game.Core.Telemetry;
using MessagePipe;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace Game.Performance
{
    /// <summary>
    /// 场景绑定入口点。只处理场景里摆好的触发器（含未激活的）；运行时 Instantiate 的要自行调 <see cref="PerformanceTrigger.Bind"/>。
    /// DontDestroyOnLoad 场景不在扫描范围内。OnSceneStart 只对激活且启用的触发器调用。
    /// </summary>
    public sealed class PerformanceSceneBinder : IStartable, IDisposable
    {
        private readonly IPerformanceService service;
        private readonly ISubscriber<BootCompletedEvent> bootCompleted;
        private readonly ITelemetryScope telemetry;
        private readonly List<PerformanceTrigger> triggers = new List<PerformanceTrigger>();
        private readonly List<PerformanceTrigger> sceneBuffer = new List<PerformanceTrigger>();
        private IDisposable subscription;
        private bool sceneSubscribed;
        private bool booted;

        public PerformanceSceneBinder(IPerformanceService service, ISubscriber<BootCompletedEvent> bootCompleted,
            ITelemetryScope telemetry)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            this.bootCompleted = bootCompleted ?? throw new ArgumentNullException(nameof(bootCompleted));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>已登记的全部触发器。场景卸载时移除已销毁项。</summary>
        public IReadOnlyList<PerformanceTrigger> Triggers => triggers;

        public void Start()
        {
            // 订阅句柄必须托管（EventConventions.cs 第 5 条）。
            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            bootCompleted.Subscribe(_ => HandleBootCompleted()).AddTo(bag);
            subscription = bag.Build();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) BindScene(scene, sceneBuffer);
            }
            sceneBuffer.Clear();
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
            triggers.Clear();
            sceneBuffer.Clear();
        }

        // 启动完成只发一次：此前已加载场景里的 OnSceneStart 触发器在这里统一触发（入口点 Start 早于 UI / 存档服务初始化）。
        private void HandleBootCompleted()
        {
            if (booted) return;
            booted = true;
            FireSceneStart(triggers);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            BindScene(scene, sceneBuffer);
            // 启动完成之后才加载的场景：只触发本场景新登记的触发器；启动未完成时留给 HandleBootCompleted 统一触发。
            if (booted) FireSceneStart(sceneBuffer);
            sceneBuffer.Clear();
        }

        // 卸载回调时场景物体已销毁，UnityEngine.Object 的 == null 能认出来。
        private void OnSceneUnloaded(Scene scene)
        {
            for (int i = triggers.Count - 1; i >= 0; i--)
            {
                if (triggers[i] == null) triggers.RemoveAt(i);
            }
        }

        // 只在启动与场景加载时跑，不在每帧路径上。新登记的触发器同时放进 added，供调用方决定是否触发。
        private void BindScene(Scene scene, List<PerformanceTrigger> added)
        {
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (PerformanceTrigger trigger in root.GetComponentsInChildren<PerformanceTrigger>(true))
                {
                    if (triggers.Contains(trigger)) continue;
                    trigger.Bind(service, telemetry);
                    triggers.Add(trigger);
                    added.Add(trigger);
                    count++;
                }
            }
            if (count > 0) Log.Debug($"PerformanceSceneBinder：场景 {scene.name} 登记了 {count} 个演出触发器");
        }

        private static void FireSceneStart(List<PerformanceTrigger> candidates)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                PerformanceTrigger trigger = candidates[i];
                if (trigger == null || !trigger.isActiveAndEnabled) continue;
                if (trigger.Mode != PerformanceTriggerMode.OnSceneStart) continue;
                // 同一时刻只能播一段：后面的触发器会以 busy 被跳过（埋 trigger_skipped）。
                trigger.TryFire();
            }
        }
    }
}
