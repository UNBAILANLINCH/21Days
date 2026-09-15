// 职责：全工程唯一的 MonoBehaviour 入口——常驻、串行初始化服务、进标题状态。
// 为什么新建：architecture.md 第 6 节借鉴手法 1「单一 MonoBehaviour 入口驱动」；
// 工程里没有任何入口脚本，LifetimeScope 只负责建容器、不负责驱动启动流程。

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Events;
using Game.Core.Flow;
using Game.Core.Logging;
using MessagePipe;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Game.Core.Boot
{
    /// <summary>
    /// 启动器。挂在 Boot 场景里与 GameLifetimeScope 同一个物体上。
    /// 流程：Awake 常驻 → Start（此时作用域已构建完）→ 进 BootState →
    /// 按注册顺序串行 await 每个 IGameService.InitializeAsync → 发 BootCompletedEvent → 进 TitleState。
    /// 任何一步抛异常都记 Log.Error 并停止，不继续往下走。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GameLifetimeScope))]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("调试台（可空）")]
        [Tooltip("IngameDebugConsole 的预制体。只在编辑器与开发包里实例化；留空则跳过。")]
        [SerializeField] private GameObject debugConsolePrefab;

        private LifetimeScope scope;
        private CancellationTokenSource cts;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            scope = GetComponent<LifetimeScope>();
            cts = new CancellationTokenSource();
        }

        // 用 Start 而不是 Awake 启动流程：LifetimeScope 在自己的 Awake 里建容器，
        // 两个 Awake 的先后顺序不保证，Start 一定在所有 Awake 之后。
        private void Start()
        {
            BootAsync(cts.Token).Forget();
        }

        private void OnDestroy()
        {
            if (cts == null)
            {
                return;
            }

            cts.Cancel();
            cts.Dispose();
            cts = null;
        }

        private async UniTaskVoid BootAsync(CancellationToken ct)
        {
            try
            {
                SpawnDebugConsole();

                if (scope == null)
                {
                    Log.Error("GameBootstrap 找不到 LifetimeScope，启动中止", this);
                    return;
                }

                IObjectResolver resolver = scope.Container;
                var flow = resolver.Resolve<IGameFlow>();
                await flow.GoToAsync<BootState>(ct);

                var services = resolver.Resolve<IReadOnlyList<IGameService>>();
                for (int i = 0; i < services.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    IGameService service = services[i];
                    Log.Info($"初始化服务 {i + 1}/{services.Count}：{service.GetType().Name}");
                    await service.InitializeAsync(ct);
                }

                resolver.Resolve<IPublisher<BootCompletedEvent>>()
                    .Publish(new BootCompletedEvent(services.Count));
                Log.Info($"全部 {services.Count} 个服务初始化完成");

                await flow.GoToAsync<TitleState>(ct);
            }
            catch (OperationCanceledException)
            {
                // 退出播放模式 / 销毁启动器属正常路径，不当错误处理
            }
            catch (Exception e)
            {
                Log.Error($"启动失败，流程已停止：{e}", this);
            }
        }

        private void SpawnDebugConsole()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (debugConsolePrefab == null)
            {
                Log.Warn("Boot 场景没有配置 IngameDebugConsole 预制体，跳过调试台", this);
                return;
            }

            GameObject console = Instantiate(debugConsolePrefab);
            console.name = debugConsolePrefab.name;
            DontDestroyOnLoad(console);
#endif
        }
    }
}
