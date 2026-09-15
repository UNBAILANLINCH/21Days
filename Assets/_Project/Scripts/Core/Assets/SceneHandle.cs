// 职责：把 Addressables 加载出来的场景包成 IDisposable，Dispose 即卸载。
// 为什么新建：architecture.md 5.3 的契约里它是独立类型。场景句柄的释放语义和资源不同
// （要 UnloadSceneAsync 而不是 Release），塞进 AssetHandle<T> 会让那个类同时管两套规则，职责说不通。

using System;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Game.Core.Assets
{
    /// <summary>
    /// 已加载场景的句柄。<see cref="Dispose"/> 即卸载，**幂等**。
    /// 卸载本身是异步的，Dispose 只负责发起，不等待完成。
    /// </summary>
    public sealed class SceneHandle : IDisposable
    {
        private readonly Action<IDisposable> onDisposed;
        private AsyncOperationHandle<SceneInstance> operation;
        private bool disposed;

        /// <summary>由 <see cref="AddressablesAssetService"/> 构造。</summary>
        /// <param name="key">加载时用的 Addressables key，出问题时日志里能认出是哪一个场景。</param>
        /// <param name="operation">已完成的场景加载句柄。</param>
        /// <param name="onDisposed">释放时回调，服务用它划掉登记；可空。</param>
        public SceneHandle(string key, AsyncOperationHandle<SceneInstance> operation, Action<IDisposable> onDisposed = null)
        {
            Key = key;
            this.operation = operation;
            this.onDisposed = onDisposed;
        }

        /// <summary>加载时用的 Addressables key。</summary>
        public string Key { get; }

        /// <summary>场景对象。已卸载或句柄无效时是 default(Scene)（<c>IsValid()</c> 为 false）。</summary>
        public Scene Scene => operation.IsValid() ? operation.Result.Scene : default;

        /// <summary>是否已经卸载过。</summary>
        public bool IsDisposed => disposed;

        /// <summary>卸载场景。幂等：第二次及以后调用什么都不做。</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            if (operation.IsValid())
            {
                // autoReleaseHandle 默认 true：卸完顺带把句柄也释放掉，不需要再 Release 一次。
                Addressables.UnloadSceneAsync(operation);
            }

            operation = default;
            onDisposed?.Invoke(this);
        }
    }
}
