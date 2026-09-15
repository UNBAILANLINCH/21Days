// 职责：持有并管理生成的 GameInput 动作集，负责启用/禁用 Action Map 与释放。
// 为什么新建：GameInput.cs 是 Input System 从 GameInput.inputactions 生成的，不能手改、
// 也不该由玩法直接 new；需要一个手写的服务承担它的生命周期。
// 为什么 Dispose 要分 Play/非 Play 两条路：生成物 GameInput.Dispose() 内部固定写死
// UnityEngine.Object.Destroy(asset)，在非 Play 模式（如 EditMode 测试）下调用 Destroy
// 会打 Error；这里不能改生成物，只能在服务层按 Application.isPlaying 分流，
// 非 Play 模式改用 DestroyImmediate 销毁同一个 asset。

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Boot;
using Game.Core.Logging;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Core.Input
{
    /// <summary>
    /// 输入服务。启动时创建 GameInput 并启用 Gameplay map；作用域销毁时禁用并释放。
    /// UI map 由波 3 的 IUIService 按需启用，这里不预先打开，免得标题界面还没出来就吃按键。
    /// </summary>
    public sealed class InputService : IInputService, IGameService, IDisposable
    {
        /// <summary>玩法动作图的名字。</summary>
        public const string GameplayMap = "Gameplay";

        /// <summary>界面动作图的名字。</summary>
        public const string UIMap = "UI";

        private GameInput actions;
        private bool disposed;

        public GameInput Actions => actions;

        public UniTask InitializeAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (actions != null)
            {
                return UniTask.CompletedTask;
            }

            actions = new GameInput();
            EnableMap(GameplayMap);
            Log.Info($"InputService 就绪，已启用 {GameplayMap} 动作图");
            return UniTask.CompletedTask;
        }

        public void EnableMap(string map)
        {
            InputActionMap actionMap = FindMap(map);
            if (actionMap == null)
            {
                return;
            }

            actionMap.Enable();
        }

        public void DisableMap(string map)
        {
            InputActionMap actionMap = FindMap(map);
            if (actionMap == null)
            {
                return;
            }

            actionMap.Disable();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (actions == null)
            {
                return;
            }

            actions.Disable();

            if (Application.isPlaying)
            {
                actions.Dispose();
            }
            else if (actions.asset != null)
            {
                UnityEngine.Object.DestroyImmediate(actions.asset);
            }

            actions = null;
        }

        private InputActionMap FindMap(string map)
        {
            if (actions == null)
            {
                Log.Warn($"InputService 还没初始化，忽略对动作图 {map} 的操作");
                return null;
            }

            InputActionMap actionMap = actions.asset.FindActionMap(map, false);
            if (actionMap == null)
            {
                Log.Warn($"GameInput 里没有名为 {map} 的动作图");
            }

            return actionMap;
        }
    }
}
