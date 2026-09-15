// 职责：把 UnityEngine.Pool.ObjectPool<GameObject> 包成「按预制体实例化 + IPoolable 回调」的池。
// 为什么新建：UnityEngine.Pool 只管对象的取还，不管 Instantiate / SetActive / 归属父节点 /
// 通知实例重置这些 Unity 侧的事；这层胶水没有现成文件可扩展，只能新建。

using System;
using System.Collections.Generic;
using Game.Core.Logging;
using UnityEngine;
using UnityEngine.Pool;

namespace Game.Core.Pooling
{
    /// <summary>
    /// 按预制体建一个池。一个池只管一种预制体；不同预制体各建各的。
    /// 用完 Dispose（或 Clear）销毁池内闲置实例。
    /// </summary>
    public sealed class GameObjectPool : IDisposable
    {
        private readonly GameObject prefab;
        private readonly Transform parent;
        private readonly ObjectPool<GameObject> pool;
        private readonly List<IPoolable> callbackBuffer = new List<IPoolable>(4);
        private readonly List<GameObject> prewarmBuffer = new List<GameObject>();

        private bool disposed;

        /// <param name="prefab">要池化的预制体，不能为空。</param>
        /// <param name="parent">闲置与取出实例挂到哪个节点下；null 表示放到场景根。</param>
        /// <param name="defaultCapacity">内部栈的初始容量。</param>
        /// <param name="maxSize">池里最多留多少闲置实例，超出的归还会直接销毁。</param>
        public GameObjectPool(GameObject prefab, Transform parent = null, int defaultCapacity = 10, int maxSize = 100)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            this.prefab = prefab;
            this.parent = parent;
            pool = new ObjectPool<GameObject>(
                Create,
                OnTakeFromPool,
                OnReturnToPool,
                OnDestroyInstance,
                true,
                defaultCapacity,
                maxSize);
        }

        /// <summary>池里闲置的实例数量。</summary>
        public int CountInactive => pool.CountInactive;

        /// <summary>已取出、还没归还的实例数量。</summary>
        public int CountActive => pool.CountActive;

        /// <summary>取一个实例：没有闲置的就新建。取出时已 SetActive(true) 并回调过 IPoolable.OnGet。</summary>
        public GameObject Get()
        {
            ThrowIfDisposed();
            return pool.Get();
        }

        /// <summary>归还实例。归还前会回调 IPoolable.OnRelease 并 SetActive(false)。</summary>
        public void Release(GameObject instance)
        {
            ThrowIfDisposed();
            if (instance == null)
            {
                Log.Warn($"往 {prefab.name} 的池里归还了空实例（可能已被 Destroy），已忽略");
                return;
            }

            pool.Release(instance);
        }

        /// <summary>预热：先造好 count 个实例再全部归还，避免玩法过程中集中实例化造成卡顿。</summary>
        public void Prewarm(int count)
        {
            ThrowIfDisposed();
            if (count <= 0)
            {
                return;
            }

            prewarmBuffer.Clear();
            for (int i = 0; i < count; i++)
            {
                prewarmBuffer.Add(pool.Get());
            }

            for (int i = 0; i < prewarmBuffer.Count; i++)
            {
                pool.Release(prewarmBuffer[i]);
            }

            prewarmBuffer.Clear();
        }

        /// <summary>销毁池里所有闲置实例；已经取出去的实例不受影响，归还时会被新建的池管。</summary>
        public void Clear()
        {
            pool.Clear();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            pool.Clear();
        }

        private GameObject Create()
        {
            GameObject instance = parent == null
                ? UnityEngine.Object.Instantiate(prefab)
                : UnityEngine.Object.Instantiate(prefab, parent);
            instance.name = prefab.name;
            instance.SetActive(false);
            return instance;
        }

        private void OnTakeFromPool(GameObject instance)
        {
            instance.SetActive(true);
            instance.GetComponents(callbackBuffer);
            for (int i = 0; i < callbackBuffer.Count; i++)
            {
                callbackBuffer[i].OnGet();
            }

            callbackBuffer.Clear();
        }

        private void OnReturnToPool(GameObject instance)
        {
            instance.GetComponents(callbackBuffer);
            for (int i = 0; i < callbackBuffer.Count; i++)
            {
                callbackBuffer[i].OnRelease();
            }

            callbackBuffer.Clear();
            instance.SetActive(false);
            if (parent != null)
            {
                instance.transform.SetParent(parent, false);
            }
        }

        private void OnDestroyInstance(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(instance);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(GameObjectPool));
            }
        }
    }
}
