// 职责：覆盖 AssetHandle / SceneHandle 的 Dispose 幂等与释放回调——不依赖 Addressables 运行时。
// 为什么新建：波 2 之前没有资源相关测试。句柄的幂等性是整个资源生命周期的地基：不幂等的话
// 「作用域兜底释放」和「持有者自己 Dispose」撞在一起就会重复 Release，Addressables 那边直接报错。

using System;
using Game.Core.Assets;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace Game.Tests.EditMode.Core
{
    /// <summary>
    /// 句柄的 EditMode 测试。用 <c>default</c> 的 <see cref="AsyncOperationHandle{TObject}"/>——
    /// 它 <c>IsValid()</c> 为 false，句柄内部会跳过真正的 Addressables 释放，
    /// 于是这里测到的就是纯粹的「幂等 + 回调」逻辑，不需要拉起 Addressables、也不需要任何资源资产。
    /// </summary>
    public sealed class AssetHandleTests
    {
        [Test]
        public void Dispose_WhenCalledTwice_InvokesCallbackOnlyOnce()
        {
            int callbacks = 0;
            AssetHandle<Texture2D> handle = new AssetHandle<Texture2D>(
                default(AsyncOperationHandle<Texture2D>),
                _ => callbacks++);

            Assert.That(handle.IsDisposed, Is.False);

            handle.Dispose();
            handle.Dispose();
            handle.Dispose();

            Assert.That(handle.IsDisposed, Is.True);
            Assert.That(callbacks, Is.EqualTo(1), "重复 Dispose 不该重复回调，否则登记表会被划掉两次");
        }

        [Test]
        public void Dispose_WhenCalled_ClearsAssetReference()
        {
            AssetHandle<Texture2D> handle = new AssetHandle<Texture2D>(default(AsyncOperationHandle<Texture2D>));

            handle.Dispose();

            // UnityEngine.Object 判空只用 == / !=（Unity 重载了 ==）。
            Assert.That(handle.Asset == null, Is.True, "Dispose 之后不该还能从句柄拿到资源");
        }

        [Test]
        public void Dispose_WithoutCallback_DoesNotThrow()
        {
            AssetHandle<Texture2D> handle = new AssetHandle<Texture2D>(default(AsyncOperationHandle<Texture2D>));

            Assert.DoesNotThrow(() => handle.Dispose());
        }

        [Test]
        public void SceneHandle_Dispose_WhenCalledTwice_InvokesCallbackOnlyOnce()
        {
            int callbacks = 0;
            SceneHandle handle = new SceneHandle(
                "Level01",
                default(AsyncOperationHandle<SceneInstance>),
                _ => callbacks++);

            Assert.That(handle.Key, Is.EqualTo("Level01"));
            Assert.That(handle.IsDisposed, Is.False);

            handle.Dispose();
            handle.Dispose();

            Assert.That(handle.IsDisposed, Is.True);
            Assert.That(callbacks, Is.EqualTo(1));
        }

        [Test]
        public void SceneHandle_Scene_WhenHandleInvalid_IsNotValidScene()
        {
            SceneHandle handle = new SceneHandle("Level01", default(AsyncOperationHandle<SceneInstance>));

            Assert.That(handle.Scene.IsValid(), Is.False);
        }

        [Test]
        public void Callback_ReceivesTheHandleItself()
        {
            IDisposable received = null;
            AssetHandle<Texture2D> handle = new AssetHandle<Texture2D>(
                default(AsyncOperationHandle<Texture2D>),
                d => received = d);

            handle.Dispose();

            Assert.That(received, Is.SameAs(handle), "回调要把自己传回去，服务才能从登记表里划掉");
        }
    }
}
