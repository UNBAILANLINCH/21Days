// 职责：IAssetService 的 Addressables 实现，兼登记所有未释放句柄、在自己销毁时兜底清理并报数。
// 为什么新建：IAssetService 是契约，实现必须分开放（将来换 YooAsset 只换这一个文件）；
// 波 1 没有任何资源相关文件可扩展。

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Boot;
using Game.Core.Logging;
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

        private bool disposed;

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
                throw new ArgumentException("资源 key 不能为空", nameof(key));
            }

            AsyncOperationHandle<T> operation = Addressables.LoadAssetAsync<T>(key);
            try
            {
                await operation.ToUniTask(cancellationToken: ct);
            }
            catch
            {
                // 失败或取消都要把这个 Addressables 句柄还回去，否则 ResourceManager 里永远挂着一条。
                if (operation.IsValid())
                {
                    Addressables.Release(operation);
                }

                throw;
            }

            return Track(new AssetHandle<T>(operation, Untrack));
        }

        public async UniTask<IReadOnlyList<AssetHandle<T>>> LoadAllAsync<T>(string label, CancellationToken ct = default)
            where T : UnityEngine.Object
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(label))
            {
                throw new ArgumentException("资源标签不能为空", nameof(label));
            }

            // 先查地址再逐个加载，而不是用 LoadAssetsAsync：后者只给一个合并句柄，
            // 释放粒度是「整组一起」，与本服务「一个资源一个句柄」的契约对不上。
            AsyncOperationHandle<IList<IResourceLocation>> locationsOperation =
                Addressables.LoadResourceLocationsAsync(label, typeof(T));

            IList<IResourceLocation> locations;
            try
            {
                locations = await locationsOperation.ToUniTask(cancellationToken: ct);
            }
            catch
            {
                if (locationsOperation.IsValid())
                {
                    Addressables.Release(locationsOperation);
                }

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
            catch
            {
                // 中途失败就把已经拿到的全部还回去，不给调用方半份结果。
                for (int i = 0; i < handles.Count; i++)
                {
                    handles[i].Dispose();
                }

                throw;
            }
            finally
            {
                Addressables.Release(locationsOperation);
            }

            return handles;
        }

        public async UniTask<GameObject> InstantiateAsync(string key, Transform parent = null, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("资源 key 不能为空", nameof(key));
            }

            AsyncOperationHandle<GameObject> operation = Addressables.InstantiateAsync(key, parent);
            GameObject instance;
            try
            {
                instance = await operation.ToUniTask(cancellationToken: ct);
            }
            catch
            {
                if (operation.IsValid())
                {
                    Addressables.Release(operation);
                }

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
                return;
            }

            if (!liveInstances.Remove(instance))
            {
                Log.Warn($"ReleaseInstance 收到不是 InstantiateAsync 出来的对象：{instance.name}。"
                         + "池化对象请走 GameObjectPool，自己 Instantiate 的请自己 Destroy。", instance);
                return;
            }

            if (!Addressables.ReleaseInstance(instance))
            {
                Log.Warn($"Addressables 不认识实例 {instance.name}，已从登记里划掉但没能释放。", instance);
            }
        }

        public async UniTask<SceneHandle> LoadSceneAsync(string key, LoadSceneMode mode, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("场景 key 不能为空", nameof(key));
            }

            AsyncOperationHandle<SceneInstance> operation = Addressables.LoadSceneAsync(key, mode);
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
