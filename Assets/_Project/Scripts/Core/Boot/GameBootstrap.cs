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
using Game.Core.Telemetry;
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
            // 这几个要在 catch 里用，所以声明在 try 外面。埋点默认是空实现：
            // 还没从容器里取到之前出的错，也不该因为埋点本身再炸一次。
            ITelemetryService telemetryService = null;
            ITelemetryClock telemetryClock = null;
            ITelemetryScope telemetry = NullTelemetryScope.Instance;
            string currentServiceName = string.Empty;
            long bootStartMs = 0L;

            try
            {
                SpawnDebugConsole();

                if (scope == null)
                {
                    // 这条路埋不了：连容器都没有，埋点服务也就无从取起。
                    Log.Error("GameBootstrap 找不到 LifetimeScope，启动中止", this);
                    return;
                }

                IObjectResolver resolver = scope.Container;

                // 埋点从容器里**取**而不是构造注入：本类是 MonoBehaviour，由 Unity 实例化，没有构造注入的机会。
                // 用 TryResolve 而不是 Resolve：容器里没注册埋点时（裁剪过的测试作用域）启动照样要跑得起来。
                resolver.TryResolve(out telemetryService);
                resolver.TryResolve(out telemetryClock);
                if (telemetryService != null)
                {
                    telemetry = telemetryService.Scope(TelemetryKeys.Boot);
                }

                var flow = resolver.Resolve<IGameFlow>();
                await flow.GoToAsync<BootState>(ct);

                var services = resolver.Resolve<IReadOnlyList<IGameService>>();
                bootStartMs = NowMs(telemetryClock);

                // 会话头（session_start）是 TelemetryService 自己的 InitializeAsync 写出来的，而它也在这张表里。
                // 排在它前面的服务（Platform）初始化完时会话还没开始，这时候埋出去的事件会被分析脚本
                // 按 session_start 切段时算进**上一段会话**。所以先攒着，等会话头写出来再按原顺序补上。
                List<KeyValuePair<string, long>> pendingSteps = null;

                for (int i = 0; i < services.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    IGameService service = services[i];
                    currentServiceName = service.GetType().Name;
                    Log.Info($"初始化服务 {i + 1}/{services.Count}：{currentServiceName}");

                    long stepStartMs = NowMs(telemetryClock);
                    await service.InitializeAsync(ct);
                    long stepMs = NowMs(telemetryClock) - stepStartMs;

                    if (telemetryService != null && telemetryService.SessionStarted)
                    {
                        if (pendingSteps != null)
                        {
                            for (int pending = 0; pending < pendingSteps.Count; pending++)
                            {
                                TrackStep(telemetry, pendingSteps[pending].Key, pendingSteps[pending].Value);
                            }

                            pendingSteps = null;
                        }

                        TrackStep(telemetry, currentServiceName, stepMs);
                    }
                    else
                    {
                        if (pendingSteps == null)
                        {
                            pendingSteps = new List<KeyValuePair<string, long>>();
                        }

                        pendingSteps.Add(new KeyValuePair<string, long>(currentServiceName, stepMs));
                    }
                }

                resolver.Resolve<IPublisher<BootCompletedEvent>>()
                    .Publish(new BootCompletedEvent(services.Count));
                Log.Info($"全部 {services.Count} 个服务初始化完成");

                // ready 的 ms 是「全部服务初始化完」的总耗时，不含后面切标题状态那一段——
                // 那一段由 core.flow/state_enter 自己记，两边重叠会让总时长看起来比实际长。
                telemetry.Track(
                    TelemetryKeys.BootEvents.Ready,
                    (TelemetryKeys.Props.N, services.Count),
                    (TelemetryKeys.Props.Ms, NowMs(telemetryClock) - bootStartMs));

                await flow.GoToAsync<TitleState>(ct);
            }
            catch (OperationCanceledException)
            {
                // 退出播放模式 / 销毁启动器属正常路径，不当错误处理，也不埋点
            }
            catch (Exception e)
            {
                Log.Error($"启动失败，流程已停止：{e}", this);

                // name 是**炸在哪个服务上**——启动失败最要紧的一条信息；
                // ms 是从开始初始化到炸掉走了多久，用来区分「一上来就炸」和「卡了很久才炸」。
                telemetry.TrackError(
                    TelemetryKeys.BootEvents.Failed,
                    e,
                    TelemetryProps.Of(
                        (TelemetryKeys.Props.Name, currentServiceName),
                        (TelemetryKeys.Props.Ms, NowMs(telemetryClock) - bootStartMs)));
            }
        }

        /// <summary>埋点层自己的时钟。拿不到时恒为 0（ms 记成 0），不影响任何业务路径。</summary>
        private static long NowMs(ITelemetryClock clock)
        {
            return clock == null ? 0L : clock.MillisecondsNow;
        }

        /// <summary>
        /// 埋一条「某个服务初始化完了」。没用 <c>BeginSpan</c>：那个结构体 Dispose 时只带得出 <c>ms</c>，
        /// 而这条事件的价值全在 <c>name</c>（是哪个服务）上，少了它一堆 ms 没法归属。
        /// </summary>
        private static void TrackStep(ITelemetryScope telemetry, string name, long elapsedMs)
        {
            telemetry.Track(
                TelemetryKeys.BootEvents.Step,
                (TelemetryKeys.Props.Name, name),
                (TelemetryKeys.Props.Ms, elapsedMs));
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
