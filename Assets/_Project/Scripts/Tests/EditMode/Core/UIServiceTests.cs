// 职责：钉住 UIService 的三条容易回归的粘合规则——打开失败要清干净、同类型并发打开只开一份、
//   关一个不是自己开的面板要容错。UIStack 只覆盖纯栈规则，这三条都在 UIService 里。
// 为什么新建：UIStackTests 测的是 UIStack（不碰 Unity 对象），把要建 GameObject 的用例塞进去
//   会让那个类同时管两套前提；扩展它说不通，只能新建一个对应 UIService 的测试类。

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Input;
using Game.Core.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests.EditMode.Core
{
    /// <summary>
    /// UIService 的 EditMode 测试。
    /// <para>
    /// **故意不调 InitializeAsync**：那一步要建四层 Canvas 和 EventSystem（还要一个初始化过的
    /// InputService），在编辑器模式下既慢又和本文件要验的三条规则无关。没初始化时 UIService
    /// 会记一条「面板会挂在场景根上」的 Warn 然后照常走完开关流程——正是我们要测的那条路径，
    /// 所以不需要为了测试给生产代码开任何后门。
    /// </para>
    /// <para>
    /// 资源服务用假实现（<see cref="FakeAssetService"/>），过渡时长为 0（UIConfig 传 null），
    /// 所以整条链路同步完成，断言可以直接做，不需要等帧。
    /// </para>
    /// </summary>
    public sealed class UIServiceTests
    {
        private FakeAssetService assets;
        private UIService service;

        [SetUp]
        public void SetUp()
        {
            assets = new FakeAssetService();
            assets.Register<ThrowingView>();
            assets.Register<PlainView>();

            // InputService 只有在 InitializeAsync 之后才会创建 GameInput；这里只是给构造函数一个非空依赖。
            service = new UIService(assets, new InputService(), null);
        }

        [TearDown]
        public void TearDown()
        {
            service.Dispose();
            assets.DestroyRemainingInstances();
        }

        [Test]
        public void OpenAsync_WhenOnOpenAsyncThrows_ReleasesInstanceAndForgetsIt()
        {
            LogAssert.Expect(LogType.Warning, new Regex("还没初始化就要开 ThrowingView"));

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => service.OpenAsync<ThrowingView>().GetAwaiter().GetResult(),
                "OnOpenAsync 抛的异常要原样传给调用方");

            Assert.That(error.Message, Is.EqualTo(ThrowingView.Message));
            Assert.That(service.Get<ThrowingView>(), Is.Null, "打开失败的面板不能留在记账里，否则下次会当成「已开着」复用");
            Assert.That(assets.ReleaseCount, Is.EqualTo(1), "失败路径要把实例还给资源服务，不能留在场景上");
            Assert.That(assets.LiveInstanceCount, Is.EqualTo(0));
        }

        [Test]
        public void OpenAsync_WhenCalledTwiceConcurrently_ReturnsTheSameInstance()
        {
            LogAssert.Expect(LogType.Warning, new Regex("还没初始化就要开 PlainView"));

            // 扣住实例化，制造「第一次还没开完、第二次就进来了」的窗口（连点按钮就是这个形状）。
            assets.HoldNextInstantiate();
            UniTask<PlainView> first = service.OpenAsync<PlainView>();
            UniTask<PlainView> second = service.OpenAsync<PlainView>();

            assets.ReleaseHeldInstantiate();

            PlainView firstView = first.GetAwaiter().GetResult();
            PlainView secondView = second.GetAwaiter().GetResult();

            Assert.That(assets.InstantiateCount, Is.EqualTo(1), "并发打开同类型只该实例化一份");
            Assert.That(secondView, Is.SameAs(firstView), "后来者要拿到第一次打开的那个实例");
            Assert.That(service.Get<PlainView>(), Is.SameAs(firstView));
        }

        [Test]
        public void CloseAsync_WhenViewWasNotOpenedByService_WarnsAndDoesNothing()
        {
            var foreign = new GameObject("Foreign", typeof(PlainView));
            assets.Track(foreign);
            var view = foreign.GetComponent<PlainView>();

            LogAssert.Expect(LogType.Warning, new Regex("不是 UIService 打开的"));

            Assert.DoesNotThrow(() => service.CloseAsync(view).GetAwaiter().GetResult(),
                "关一个别人建的面板是误用，记 Warn 忽略即可，不该把调用方炸掉");
            Assert.That(assets.ReleaseCount, Is.EqualTo(0), "不是自己开的就不能还给资源服务");
        }

        /// <summary>OnOpenAsync 直接抛异常的面板，用来覆盖「打开到一半失败」这条路径。</summary>
        private sealed class ThrowingView : UIView
        {
            /// <summary>抛出的异常消息，测试用它确认拿到的就是这一个异常。</summary>
            public const string Message = "ThrowingView 打不开";

            public override UILayer Layer => UILayer.Panel;

            public override UniTask OnOpenAsync(object arg, CancellationToken ct)
            {
                throw new InvalidOperationException(Message);
            }
        }

        /// <summary>什么都不做的面板，用来覆盖正常路径。</summary>
        private sealed class PlainView : UIView
        {
            public override UILayer Layer => UILayer.Panel;
        }

        /// <summary>
        /// 假资源服务：只实现 UIService 真正会用到的 <see cref="InstantiateAsync"/> 与
        /// <see cref="ReleaseInstance"/>，其余成员一律抛 <see cref="NotSupportedException"/>——
        /// 哪天 UIService 偷偷用上了别的成员，测试会当场炸而不是静默通过。
        /// </summary>
        private sealed class FakeAssetService : IAssetService
        {
            private readonly Dictionary<string, Type> prefabs = new Dictionary<string, Type>(StringComparer.Ordinal);
            private readonly List<GameObject> instances = new List<GameObject>();

            private UniTaskCompletionSource<GameObject> heldCompletion;
            private GameObject heldInstance;
            private bool holdNext;

            /// <summary>实例化过几次（并发去重失效的话这个数会变成 2）。</summary>
            public int InstantiateCount { get; private set; }

            /// <summary>归还过几次。</summary>
            public int ReleaseCount { get; private set; }

            /// <summary>还没归还、也还没销毁的实例数。</summary>
            public int LiveInstanceCount
            {
                get
                {
                    int alive = 0;
                    for (int i = 0; i < instances.Count; i++)
                    {
                        // GameObject 是 UnityEngine.Object，判空只用 != null。
                        if (instances[i] != null)
                        {
                            alive++;
                        }
                    }

                    return alive;
                }
            }

            /// <summary>登记一个「地址 = 类名」的假预制体：实例化时新建一个挂着该组件的 GameObject。</summary>
            public void Register<T>() where T : Component
            {
                prefabs[typeof(T).Name] = typeof(T);
            }

            /// <summary>让下一次实例化挂起，直到 <see cref="ReleaseHeldInstantiate"/>。</summary>
            public void HoldNextInstantiate()
            {
                holdNext = true;
            }

            /// <summary>放行被扣住的那一次实例化。</summary>
            public void ReleaseHeldInstantiate()
            {
                holdNext = false;
                UniTaskCompletionSource<GameObject> completion = heldCompletion;
                GameObject instance = heldInstance;
                heldCompletion = null;
                heldInstance = null;

                if (completion != null)
                {
                    completion.TrySetResult(instance);
                }
            }

            /// <summary>把一个不是本服务造出来的对象也纳入清理范围（测试里手搓的 GameObject）。</summary>
            public void Track(GameObject instance)
            {
                instances.Add(instance);
            }

            /// <summary>TearDown 用：把还活着的实例全部销毁。EditMode 下只能 DestroyImmediate。</summary>
            public void DestroyRemainingInstances()
            {
                for (int i = 0; i < instances.Count; i++)
                {
                    if (instances[i] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(instances[i]);
                    }
                }

                instances.Clear();
            }

            public UniTask<GameObject> InstantiateAsync(string key, Transform parent = null,
                CancellationToken ct = default)
            {
                if (!prefabs.TryGetValue(key, out Type componentType))
                {
                    throw new InvalidOperationException($"假资源服务里没登记地址 {key}，先 Register<T>()");
                }

                InstantiateCount++;
                var instance = new GameObject(key, componentType);
                instances.Add(instance);
                if (parent != null)
                {
                    instance.transform.SetParent(parent, false);
                }

                if (!holdNext)
                {
                    return UniTask.FromResult(instance);
                }

                heldCompletion = new UniTaskCompletionSource<GameObject>();
                heldInstance = instance;
                return heldCompletion.Task;
            }

            public void ReleaseInstance(GameObject instance)
            {
                ReleaseCount++;
                if (instance != null)
                {
                    instances.Remove(instance);
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }

            public UniTask<AssetHandle<T>> LoadAsync<T>(string key, CancellationToken ct = default)
                where T : UnityEngine.Object
            {
                throw new NotSupportedException("UIService 不该用 LoadAsync");
            }

            public UniTask<IReadOnlyList<AssetHandle<T>>> LoadAllAsync<T>(string label,
                CancellationToken ct = default)
                where T : UnityEngine.Object
            {
                throw new NotSupportedException("UIService 不该用 LoadAllAsync");
            }

            public UniTask<SceneHandle> LoadSceneAsync(string key, LoadSceneMode mode,
                CancellationToken ct = default)
            {
                throw new NotSupportedException("UIService 不该用 LoadSceneAsync");
            }
        }
    }
}
