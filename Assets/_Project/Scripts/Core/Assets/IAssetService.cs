// 职责：资源加载的唯一入口契约，把 Addressables 收拢成「只有 Load 与 Release 两个动词」的接口。
// 为什么新建：architecture.md 5.3 定义了这个契约，波 1 只落地了 Boot/Log/Events/Timing/Pooling/Input/Platform，
// 工程内没有任何资源相关的文件可复用或扩展；实现与契约必须分开（将来换 YooAsset 只换实现）。

using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Core.Assets
{
    /// <summary>
    /// 资源服务。玩法与其它框架服务要资源只走这里，不直接调 Addressables、不调 Resources.Load。
    /// <para>
    /// 只有 Load 与 Release 两类动词：**不提供** Exists / Check / Download 之类接口。
    /// 参考工程的教训是业务会拿这类接口当「加载判定」用，真机上静默失败还查不出来（architecture.md 第 6 节规避第 2 条）。
    /// </para>
    /// <para>
    /// 生命周期规则：<see cref="LoadAsync{T}"/> / <see cref="LoadAllAsync{T}"/> 拿到的句柄由**调用方**负责 Dispose，
    /// 句柄的存活期跟着持有者走；<see cref="InstantiateAsync"/> 出来的实例必须用 <see cref="ReleaseInstance"/> 还回来，
    /// 不能 Destroy。
    /// </para>
    /// </summary>
    public interface IAssetService
    {
        /// <summary>按 key 加载单个资源。返回的句柄 Dispose 即释放，不 Dispose 就一直占着内存。</summary>
        UniTask<AssetHandle<T>> LoadAsync<T>(string key, CancellationToken ct = default)
            where T : UnityEngine.Object;

        /// <summary>
        /// 按标签批量加载。给配置表这类「一整组资源一起预载」的场景用（ConfigService 用它加载 config 标签下的全部表）。
        /// 返回的每个句柄都要各自 Dispose；标签下一个都没有时返回空列表，不抛异常。
        /// </summary>
        UniTask<IReadOnlyList<AssetHandle<T>>> LoadAllAsync<T>(string label, CancellationToken ct = default)
            where T : UnityEngine.Object;

        /// <summary>按 key 实例化一个 GameObject。用完必须 <see cref="ReleaseInstance"/>，不要 Destroy。</summary>
        UniTask<GameObject> InstantiateAsync(string key, Transform parent = null, CancellationToken ct = default);

        /// <summary>归还 <see cref="InstantiateAsync"/> 出来的实例（销毁对象并释放它占的资源引用）。</summary>
        void ReleaseInstance(GameObject instance);

        /// <summary>按 key 加载场景。返回的句柄 Dispose 即卸载（Single 模式下 Unity 自己会卸旧场景，句柄仍要释放）。</summary>
        UniTask<SceneHandle> LoadSceneAsync(string key, LoadSceneMode mode, CancellationToken ct = default);
    }
}
