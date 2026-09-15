// 职责：覆盖 InputService 的 Action Map 启停与「名字不存在时警告但不抛异常」的容错。
// 为什么新建：波 1 之前 Input 目录没有任何测试；EnableMap/DisableMap 是波 2/3 各玩法模块
// 读输入前必经的入口，行为一旦改错所有模块都会受影响，需要一份不进 Play 模式的 EditMode 测试守着。

using System.Text.RegularExpressions;
using System.Threading;
using Game.Core.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.EditMode.Core
{
    /// <summary>
    /// InputService 的 EditMode 测试。InitializeAsync 内部同步创建 GameInput 并启用 Gameplay map，
    /// 全程没有 await 真正挂起，因此同步等待即可，不需要进 Play 模式。
    /// </summary>
    public sealed class InputServiceTests
    {
        private InputService service;

        [SetUp]
        public void SetUp()
        {
            service = new InputService();
            service.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            // InputService.Dispose() 在非 Play 模式下改用 DestroyImmediate 释放 asset，
            // 不再触达 GameInput（生成物）里固定写死的 Object.Destroy，这里不应有任何报错日志。
            service.Dispose();
        }

        [Test]
        public void EnableMap_WithUIMapName_SetsMapEnabled()
        {
            service.EnableMap(InputService.UIMap);

            Assert.That(service.Actions.UI.enabled, Is.True);
        }

        [Test]
        public void DisableMap_AfterEnable_SetsMapDisabled()
        {
            Assert.That(service.Actions.Gameplay.enabled, Is.True, "InitializeAsync 应已启用 Gameplay map");

            service.DisableMap(InputService.GameplayMap);

            Assert.That(service.Actions.Gameplay.enabled, Is.False);
        }

        [Test]
        public void EnableMap_WithUnknownName_LogsWarningAndDoesNotThrow()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[Game\] "));

            Assert.DoesNotThrow(() => service.EnableMap("不存在的名字"));
        }

        [Test]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            service.Dispose();

            Assert.DoesNotThrow(() => service.Dispose());
        }
    }
}
