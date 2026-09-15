// 职责：把 Addressables 的 AsyncOperationHandle<T> 包成一个 IDisposable，让资源生命周期跟着持有者走。
// 为什么新建：architecture.md 5.3 的契约里它是独立类型；AsyncOperationHandle 是 struct 且 Release 语义全靠调用方
// 自觉（漏调就泄漏、重复调就报错），必须有一个「幂等、可追踪」的包装，没有现成文件能承担这个职责。

using System;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Game.Core.Assets
{
    /// <summary>
    /// 单个资源的句柄。<see cref="Dispose"/> 即释放，**幂等**（重复 Dispose 是空操作，不会重复 Release）。
    /// 正常从 <see cref="IAssetService"/> 拿，不自己 new。
    /// </summary>
    /// <typeparam name="T">资源类型。</typeparam>
    public sealed class AssetHandle<T> : IDisposable
        where T : UnityEngine.Object
    {
        private readonly Action<IDisposable> onDisposed;
        private AsyncOperationHandle<T> operation;
        private T asset;
        private bool disposed;

        /// <summary>
        /// 由 <see cref="AddressablesAssetService"/> 构造。测试里也可以塞一个 default 的 operation
        /// 来验证 Dispose 的幂等性——无效句柄不会去调 Addressables.Release。
        /// </summary>
        /// <param name="operation">已完成的 Addressables 句柄。</param>
        /// <param name="onDisposed">释放时回调，服务用它把自己那份「未释放句柄」登记划掉；可空。</param>
        public AssetHandle(AsyncOperationHandle<T> operation, Action<IDisposable> onDisposed = null)
        {
            this.operation = operation;
            this.onDisposed = onDisposed;
            asset = operation.IsValid() ? operation.Result : null;
        }

        /// <summary>资源本体。Dispose 之后变成 null——拿了句柄就别只存 Asset 引用。</summary>
        public T Asset => asset;

        /// <summary>是否已经释放过。</summary>
        public bool IsDisposed => disposed;

        /// <summary>释放资源。幂等：第二次及以后调用什么都不做。</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            asset = null;

            if (operation.IsValid())
            {
                Addressables.Release(operation);
            }

            operation = default;

            // Action 不是 UnityEngine.Object，用 ?. 不会撞上 Unity 的伪空重载。
            onDisposed?.Invoke(this);
        }
    }
}
