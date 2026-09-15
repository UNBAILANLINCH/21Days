// 职责：覆盖 PlatformServiceBase 的存档根目录拼法与只读缓存，以及编辑器下 Kind 该是 Standalone。
// 为什么新建：波 1 之前 Platform 目录没有任何测试；SaveRoot 的拼法是波 2 ISaveService 的前提，
// 拼错或每次返回不同字符串都会在存档功能上炸得很隐蔽，需要一份不依赖场景的 EditMode 测试守着。

using Game.Core.Platform;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Core
{
    /// <summary>
    /// PlatformServiceBase 的 EditMode 测试。编辑器下 PlatformServiceFactory.Create() 恒定
    /// 返回 StandalonePlatformService，直接构造/工厂创建即可，不需要走场景或 Play 模式。
    /// </summary>
    public sealed class PlatformServiceTests
    {
        [Test]
        public void SaveRoot_WhenRead_StartsWithPersistentDataPathAndEndsWithSaves()
        {
            IPlatformService service = new StandalonePlatformService();

            string saveRoot = service.SaveRoot;

            Assert.That(saveRoot, Does.StartWith(Application.persistentDataPath));
            Assert.That(saveRoot, Does.EndWith("saves"));
        }

        [Test]
        public void SaveRoot_WhenReadMultipleTimes_ReturnsSameString()
        {
            IPlatformService service = new StandalonePlatformService();

            string first = service.SaveRoot;
            string second = service.SaveRoot;

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void Kind_WhenCreatedByFactoryInEditor_IsStandalone()
        {
            PlatformServiceBase service = PlatformServiceFactory.Create();

            Assert.That(service.Kind, Is.EqualTo(PlatformKind.Standalone));
        }
    }
}
