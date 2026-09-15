// 职责：IAssetService 的 Addressables 实现，兼登记所有未释放句柄、在自己销毁时兜底清理并报数。
// 为什么新建：IAssetService 是契约，实现必须分开放（将来换 YooAsset 只换这一个文件）；
// 波 1 没有任何资源相关文件可扩展。

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Boot;
using Game.Core.Logging;
using Game.Core.Telemetry;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Game.Core.Assets
{
    /// <summary>
    /// Addressables 实现。启动时初始化 Addressables，运行期把发出去的句柄与实例都登记下来，
    /// 自己 Dispose 时把没还回来的强行释放并 Warn 出数量——泄漏要在开发期就看得见，而不是真机上 OOM 才发现。
    /// </summary>
    public sealed class AddressablesAssetService : IAssetService, IGameService, IDisposable
    {
        /// <summary>已发出、尚未 Dispose 的资源/场景句柄。用引用相等，同一个句柄只登记一次。</summary>
        private readonly HashSet<IDisposable> liveHandles = new HashSet<IDisposable>();

        /// <summary>已实例化、尚未 ReleaseInstance 的对象。</summary>
        private readonly HashSet<GameObject> liveInstances = new HashSet<GameObject>();

        private readonly ITelemetryScope telemetry;
        private readonly ITelemetryClock clock;

        private bool disposed;

        /// <summary>
        /// 两个埋点参数允许为 null：这个服务在 EditMode 测试里是直接 new 出来的，没有容器。
        /// 拿不到就整条埋点链路变空操作（<see cref="NullTelemetryScope"/>），业务路径一个字节都不变。
        /// </summary>
        public AddressablesAssetService(ITelemetryService telemetry, ITelemetryClock clock)
        {
            this.clock = clock;
            this.telemetry = telemetry == null
                ? (ITelemetryScope)NullTelemetryScope.Instance
                : telemetry.Scope(TelemetryKeys.Asset);
        }

        public async UniTask InitializeAsync(CancellationToken ct)
        {
            // Addressables 自己会保证只真正初始化一次，重复调用拿到的是同一个已完成的句柄。
            await Addressables.InitializeAsync().ToUniTask(cancellationToken: ct);
            Log.Info("Addressables 初始化完成");
        }

        public async UniTask<AssetHandle<T>> LoadAsync<T>(string key, CancellationToken ct = default)
            where T : UnityEngine.Object
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(key))
            {
                TrackLoadFailed(string.Empty, "资源 key 为空");
                throw new ArgumentException("资源 key 不能为空", nameof(key));
            }

            long startMs = NowMs;
            AsyncOperationHandle<T> operation = Addressables.LoadAssetAsync<T>(key);
            try
            {
                await operation.ToUniTask(cancellationToken: ct);
            }
            catch (Exception e)
            {
                // 失败或取消都要把这个 Addressables 句柄还回去，否则 ResourceManager 里永远挂着一条。
                if (operation.IsValid())
                {
                    Addressables.Release(operation);
                }

                TrackLoadFailed(key, e);
                throw;
            }

            telemetry.Track(
                TelemetryKeys.AssetEvents.Load,
                (TelemetryKeys.Props.Key, key),
                (TelemetryKeys.Props.Ms, NowMs - startMs));
            return Track(new AssetHandle<T>(operation, Untrack));
        }

        public async UniTask<IReadOnlyList<AssetHandle<T>>> LoadAllAsync<T>(string label, CancellationToken ct = default)
            where T : UnityEngine.Object
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(label))
            {
                TrackLoadFailed(string.Empty, "资源标签为空");
                throw new ArgumentException("资源标签不能为空", nameof(label));
            }

            long startMs = NowMs;

            // 先查地址再逐个加载，而不是用 LoadAssetsAsync：后者只给一个合并句柄，
            // 释放粒度是「整组一起」，与本服务「一个资源一个句柄」的契约对不上。
            AsyncOperationHandle<IList<IResourceLocation>> locationsOperation =
                Addressables.LoadResourceLocationsAsync(label, typeof(T));

            IList<IResourceLocation> locations;
            try
            {
                locations = await locationsOperation.ToUniTask(cancellationToken: ct);
            }
            catch (Exception e)
            {
                if (locationsOperation.IsValid())
                {
                    Addressables.Release(locationsOperation);
                }

                TrackLoadFailed(label, e);
                throw;
            }

            List<AssetHandle<T>> handles = new List<AssetHandle<T>>(locations.Count);
            try
            {
                for (int i = 0; i < locations.Count; i++)
                {
                    AsyncOperationHandle<T> operation = Addressables.LoadAssetAsync<T>(locations[i]);
                    try
                    {
                        await operation.ToUniTask(cancellationToken: ct);
                    }
                    catch
                    {
                        if (operation.IsValid())
                        {
                            Addressables.Release(operation);
                        }

                        throw;
                    }

                    handles.Add(Track(new AssetHandle<T>(operation, Untrack)));
                }
            }
            catch (Exception e)
            {
                // 中途失败就把已经拿到的全部还回去，不给调用方半份结果。
                for (int i = 0; i < handles.Count; i++)
                {
                    handles[i].Dispose();
                }

                // n 是已经拿到几个才炸的——「第几个资源出的问题」在查坏包时很关键。
                TrackLoadFailed(label, e, handles.Count);
                throw;
            }
            finally
            {
                Addressables.Release(locationsOperation);
            }

            // 整批只埋一条：批里的每个资源都是直接走 Addressables 加载的（没有再过一次 LoadAsync），
            // 所以不会和上面那条 load 重复计数。
            telemetry.Track(
                TelemetryKeys.AssetEvents.Load,
                (TelemetryKeys.Props.Key, label),
                (TelemetryKeys.Props.N, handles.Count),
                (TelemetryKeys.Props.Ms, NowMs - startMs));
            return handles;
        }

        public async UniTask<GameObject> InstantiateAsync(string key, Transform parent = null, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(key))
            {
                TrackLoadFailed(string.Empty, "实例化的资源 key 为空");
                throw new ArgumentException("资源 key 不能为空", nameof(key));
            }

            AsyncOperationHandle<GameObject> operation = Addressables.InstantiateAsync(key, parent);
            GameObject instance;
            try
            {
                instance = await operation.ToUniTask(cancellationToken: ct);
            }
            catch (Exception e)
            {
                if (operation.IsValid())
                {
                    Addressables.Release(operation);
                }

                // 只埋失败不埋成功：实例化在对象池 / 弹幕这类场合会很密集，成功路径上层已经有对应事件
                // （面板走 core.ui/open），再埋一条只会把日志刷厚。失败则是「地址写错了」的首要线索。
                TrackLoadFailed(key, e);
                throw;
            }

            liveInstances.Add(instance);
            return instance;
        }

        public void ReleaseInstance(GameObject instance)
        {
            // UnityEngine.Object 判空只用 == / !=（Unity 重载了 ==，?. 和 is null 会把已销毁对象当成非空）。
            if (instance == null)
            {
                Log.Warn("ReleaseInstance 收到空实例，忽略");
                TrackReleaseDenied(string.Empty, "null_instance");
                return;
            }

            if (!liveInstances.Remove(instance))
            {
                Log.Warn($"ReleaseInstance 收到不是 InstantiateAsync 出来的对象：{instance.name}。"
                         + "池化对象请走 GameObjectPool，自己 Instantiate 的请自己 Destroy。", instance);
                TrackReleaseDenied(instance.name, "not_tracked");
                return;
            }

            if (!Addressables.ReleaseInstance(instance))
            {
                Log.Warn($"Addressables 不认识实例 {instance.name}，已从登记里划掉但没能释放。", instance);
                TrackReleaseDenied(instance.name, "unknown_to_addressables");
            }
        }

        public async UniTask<SceneHandle> LoadSceneAsync(string key, LoadSceneMode mode, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(key))
            {
                TrackLoadFailed(string.Empty, "场景 key 为空");
                throw new ArgumentException("场景 key 不能为空", nameof(key));
            }

            long startMs = NowMs;
            AsyncOperationHandle<SceneInstance> operation = Addressables.LoadSceneAsync(key, mode);
            try
            {
                await operation.ToUniTask(cancellationToken: ct);
            }
            catch (Exception e)
            {
                if (operation.IsValid())
                {
                    Addressables.Release(operation);
                }

                TrackLoadFailed(key, e);
                throw;
            }

            telemetry.Track(
                TelemetryKeys.AssetEvents.SceneLoad,
                (TelemetryKeys.Props.Key, key),
                (TelemetryKeys.Props.Ms, NowMs - startMs));
            return Track(new SceneHandle(key, operation, Untrack));
        }

        /// <summary>
        /// 兜底清理。正常情况下这里一个都不该剩——剩了就是有人拿了句柄不 Dispose，
        /// 所以要把数量 Warn 出来，而不是默默补救。
        /// </summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            if (liveHandles.Count > 0)
            {
                Log.Warn($"资源服务销毁时还有 {liveHandles.Count} 个句柄没释放，已强制释放。"
                         + "句柄要跟着持有者的生命周期 Dispose，别只拿 Asset 引用。");
                TrackLeak("leaked_handles", liveHandles.Count);

                // 句柄 Dispose 会回调 Untrack 改这个集合，先拷一份再遍历。
                IDisposable[] leaked = new IDisposable[liveHandles.Count];
                liveHandles.CopyTo(leaked);
                for (int i = 0; i < leaked.Length; i++)
                {
                    leaked[i].Dispose();
                }

                liveHandles.Clear();
            }

            if (liveInstances.Count > 0)
            {
                Log.Warn($"资源服务销毁时还有 {liveInstances.Count} 个实例没归还，已强制释放。"
                         + "InstantiateAsync 出来的对象要用 ReleaseInstance 还回来，不要 Destroy。");
                TrackLeak("leaked_instances", liveInstances.Count);

                GameObject[] leakedInstances = new GameObject[liveInstances.Count];
                liveInstances.CopyTo(leakedInstances);
                for (int i = 0; i < leakedInstances.Length; i++)
                {
                    if (leakedInstances[i] != null)
                    {
                        Addressables.ReleaseInstance(leakedInstances[i]);
                    }
                }

                liveInstances.Clear();
            }
        }

        private THandle Track<THandle>(THandle handle)
            where THandle : class, IDisposable
        {
            liveHandles.Add(handle);
            return handle;
        }

        private void Untrack(IDisposable handle)
        {
            liveHandles.Remove(handle);

            // 只有场景句柄记得自己的 key（AssetHandle 没存），资源句柄这一侧只能留空。
            // 真正有用的是 n：释放之后还剩多少个没还回来的句柄——「加载了多少、还剩多少」
            // 这条曲线是查资源泄漏的主线索，光看某一条 release 看不出问题。
            telemetry.Track(
                TelemetryKeys.AssetEvents.Release,
                (TelemetryKeys.Props.Key, handle is SceneHandle scene ? scene.Key : string.Empty),
                (TelemetryKeys.Props.N, liveHandles.Count));
        }

        /// <summary>埋点层自己的时钟。拿不到时恒为 0（ms 记成 0），不影响任何业务路径。</summary>
        private long NowMs => clock == null ? 0L : clock.MillisecondsNow;

        /// <summary>加载失败（带异常）。取消是正常路径，不当失败埋。</summary>
        private void TrackLoadFailed(string key, Exception error, int loaded = -1)
        {
            // 退出播放模式、状态切走都会把加载中的请求取消掉。把取消也埋成 E 级的话，
            // 每次退出播放模式 Console 都会红一片，真正的加载失败反而被淹掉。
            if (error is OperationCanceledException)
            {
                return;
            }

            TelemetryProps props = loaded < 0
                ? TelemetryProps.Of((TelemetryKeys.Props.Key, key))
                : TelemetryProps.Of((TelemetryKeys.Props.Key, key), (TelemetryKeys.Props.N, loaded));
            telemetry.TrackError(TelemetryKeys.AssetEvents.LoadFailed, error, props);
        }

        /// <summary>加载失败（只有一句话，没有异常）：参数校验这类「还没真正开始加载就被挡回去」的分支。</summary>
        private void TrackLoadFailed(string key, string message)
        {
            telemetry.TrackError(
                TelemetryKeys.AssetEvents.LoadFailed,
                message,
                TelemetryProps.Of((TelemetryKeys.Props.Key, key)));
        }

        /// <summary>归还实例被拒。reason 区分三条分支，聚合时不用去翻文案。</summary>
        private void TrackReleaseDenied(string key, string reason)
        {
            telemetry.TrackWarn(
                TelemetryKeys.AssetEvents.Release,
                TelemetryProps.Of(
                    (TelemetryKeys.Props.Key, key),
                    (TelemetryKeys.Props.Reason, reason)));
        }

        /// <summary>
        /// 销毁时还有没还回来的东西。这条多半写不出去——VContainer 按注册顺序释放，
        /// 埋点服务排在资源服务前面，轮到这里时它已经 Dispose 了，Track 会直接返回。
        /// 留着是因为「服务先于容器被单独销毁」的场合（测试、热重载）它是有效的，而且调用本身零成本。
        /// </summary>
        private void TrackLeak(string reason, int count)
        {
            telemetry.TrackWarn(
                TelemetryKeys.AssetEvents.Release,
                TelemetryProps.Of(
                    (TelemetryKeys.Props.Reason, reason),
                    (TelemetryKeys.Props.N, count)));
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AddressablesAssetService),
                    "资源服务已销毁，不能再加载资源（多半是作用域已经释放了还有代码在跑）");
            }
        }
    }
}
