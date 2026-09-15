// 职责：需要加载一个场景的状态基类——进入时 Additive 加载场景、退出时卸载，子类只管场景就绪后的逻辑。
// 为什么新建：GameState 只有 Enter/Exit 两个空钩子，不该知道资源服务的存在（BootState、TitleState
//   这类不加载场景的状态用不到它）；把加载写进 GameState 会让每个不需要场景的状态都背上 IAssetService 依赖。
//   扩展现有文件也不行：GameState.cs 一个文件一个类是本工程的硬约定（csharp-code.md #命名与结构）。

using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Logging;
using UnityEngine.SceneManagement;

namespace Game.Core.Flow
{
    /// <summary>
    /// 带场景的状态。<see cref="EnterAsync"/> 按 <see cref="SceneKey"/> Additive 加载场景并持有句柄，
    /// <see cref="ExitAsync"/> 卸载；两个方法都是 sealed，子类改不了这条生命周期——
    /// 漏卸载的场景会一直挂在内存里，而且第二次进同一个状态会叠出两份。
    /// <para>
    /// 子类要做的事：给出 <see cref="SceneKey"/>（Addressables 地址），
    /// 在 <see cref="OnSceneReadyAsync"/> 里做进入逻辑，在 <see cref="OnSceneUnloadingAsync"/> 里做清理。
    /// </para>
    /// <para>
    /// Boot 场景全程常驻，所以这里恒用 <see cref="LoadSceneMode.Additive"/>：
    /// Single 模式会把 Boot 场景连同 GameBootstrap 一起卸掉，整个框架就没了。
    /// </para>
    /// </summary>
    public abstract class SceneGameState : GameState
    {
        private SceneHandle sceneHandle;

        /// <summary>子类经由构造函数把 <see cref="IAssetService"/> 传上来（状态由容器解析，能构造注入）。</summary>
        protected SceneGameState(IAssetService assets)
        {
            Assets = assets;
        }

        /// <summary>资源服务。子类加载自己的资源也用这一个，不要另外注入。</summary>
        protected IAssetService Assets { get; }

        /// <summary>本状态的场景在 Addressables 里的地址。</summary>
        protected abstract string SceneKey { get; }

        /// <summary>已加载的场景。还没加载或已卸载时是 <c>default(Scene)</c>（<c>IsValid()</c> 为 false）。</summary>
        protected Scene Scene => sceneHandle == null ? default : sceneHandle.Scene;

        public sealed override async UniTask EnterAsync(CancellationToken ct)
        {
            // SceneHandle 是普通 C# 类不是 UnityEngine.Object，用 == null 判断没有伪空问题。
            if (sceneHandle != null)
            {
                Log.Warn($"{GetType().Name} 重复进入：上一次的场景 {SceneKey} 还没卸载，先卸掉再加载");
                sceneHandle.Dispose();
                sceneHandle = null;
            }

            sceneHandle = await Assets.LoadSceneAsync(SceneKey, LoadSceneMode.Additive, ct);
            await OnSceneReadyAsync(ct);
        }

        public sealed override async UniTask ExitAsync(CancellationToken ct)
        {
            try
            {
                await OnSceneUnloadingAsync(ct);
            }
            finally
            {
                // 子类清理抛异常也要把场景卸掉，否则下一次进来会叠第二份。
                if (sceneHandle != null)
                {
                    sceneHandle.Dispose();
                    sceneHandle = null;
                }
            }
        }

        /// <summary>场景加载完成之后调用。开 UI、找场景里的入口对象、启动玩法都放这儿。</summary>
        protected virtual UniTask OnSceneReadyAsync(CancellationToken ct) => UniTask.CompletedTask;

        /// <summary>场景卸载之前调用。关 UI、退订事件、释放本状态自己加载的资源句柄。</summary>
        protected virtual UniTask OnSceneUnloadingAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
