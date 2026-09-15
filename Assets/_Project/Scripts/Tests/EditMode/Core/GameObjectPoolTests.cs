// 职责：覆盖 GameObjectPool 的核心规则——取出激活并回调 OnGet、归还失活并回调 OnRelease、
// Prewarm 后不再新建实例、Release(null) 不抛异常、Clear 后仍可继续取用。
// 为什么新建：波 1 之前 Pooling 目录没有任何测试；池的取还生命周期最容易在改动 ObjectPool
// 回调顺序时被破坏，需要一份不依赖场景、可确定性运行的 EditMode 测试守着。

using System.Text.RegularExpressions;
using Game.Core.Pooling;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.EditMode.Core
{
    /// <summary>
    /// GameObjectPool 的 EditMode 测试。用一个挂了 TestPoolable 的普通 GameObject 当「预制体」，
    /// Instantiate 对非 Prefab 资产的 GameObject 一样生效，因此不需要真正的 Prefab 资产。
    /// </summary>
    public sealed class GameObjectPoolTests
    {
        private GameObject prefab;
        private GameObjectPool pool;

        [SetUp]
        public void SetUp()
        {
            prefab = new GameObject("PoolTestPrefab", typeof(TestPoolable));
            pool = new GameObjectPool(prefab);
        }

        [TearDown]
        public void TearDown()
        {
            pool.Dispose();
            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void Get_WhenCalled_ReturnsActiveInstanceAndInvokesOnGet()
        {
            GameObject instance = pool.Get();

            Assert.That(instance.activeSelf, Is.True);
            Assert.That(instance.GetComponent<TestPoolable>().OnGetCount, Is.EqualTo(1));

            pool.Release(instance);
        }

        [Test]
        public void Release_AfterGet_DeactivatesInstanceAndInvokesOnRelease()
        {
            GameObject instance = pool.Get();
            TestPoolable poolable = instance.GetComponent<TestPoolable>();

            pool.Release(instance);

            Assert.That(instance.activeSelf, Is.False);
            Assert.That(poolable.OnReleaseCount, Is.EqualTo(1));
        }

        [Test]
        public void Prewarm_WithCount_LeavesThatManyInstancesInactiveInPool()
        {
            pool.Prewarm(3);

            Assert.That(pool.CountInactive, Is.EqualTo(3), "Prewarm(3) 后池内闲置实例该有 3 个");
        }

        [Test]
        public void Get_AfterPrewarm_ReusesPrewarmedInstancesWithoutGrowingActiveCount()
        {
            pool.Prewarm(3);

            GameObject a = pool.Get();
            GameObject b = pool.Get();
            GameObject c = pool.Get();

            Assert.That(pool.CountInactive, Is.EqualTo(0), "预热好的 3 个实例该被全部取走复用，闲置数归零");
            Assert.That(pool.CountActive, Is.EqualTo(3), "取出的 3 个都该算作已取出");

            pool.Release(a);
            pool.Release(b);
            pool.Release(c);
        }

        [Test]
        public void Release_WithNullInstance_DoesNotThrow()
        {
            LogAssert.Expect(LogType.Warning, new Regex("空实例"));

            Assert.DoesNotThrow(() => pool.Release(null));
        }

        [Test]
        public void Get_AfterClear_StillReturnsUsableInstance()
        {
            pool.Prewarm(1);
            pool.Clear();

            GameObject instance = pool.Get();

            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.activeSelf, Is.True);

            pool.Release(instance);
        }

        /// <summary>测试用可池化组件：记录取出/归还次数。</summary>
        private sealed class TestPoolable : MonoBehaviour, IPoolable
        {
            public int OnGetCount { get; private set; }

            public int OnReleaseCount { get; private set; }

            public void OnGet() => OnGetCount++;

            public void OnRelease() => OnReleaseCount++;
        }
    }
}
