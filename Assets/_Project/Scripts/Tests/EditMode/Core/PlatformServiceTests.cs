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

        /// <summary>
        /// TearDown 必须清掉覆盖：它是静态状态，留着会污染同一次 Play/域里跑的其它用例
        /// （包括本类自己后面几条、以及真实走 Boot 的 Showcase 回放）。
        /// </summary>
        [TearDown]
        public void ClearOverride()
        {
            PlatformServiceBase.SaveRootOverride = null;
        }

        [Test]
        public void SaveRoot_WhenOverrideSet_ReturnsOverrideValue()
        {
            IPlatformService service = new StandalonePlatformService();
            string overridePath = System.IO.Path.Combine(Application.temporaryCachePath, "platform-service-tests-override");

            PlatformServiceBase.SaveRootOverride = overridePath;

            Assert.That(service.SaveRoot, Is.EqualTo(overridePath));
        }

        [Test]
        public void SaveRoot_WhenOverrideCleared_RestoresPersistentDataPathValue()
        {
            IPlatformService service = new StandalonePlatformService();
            PlatformServiceBase.SaveRootOverride = System.IO.Path.Combine(Application.temporaryCachePath, "platform-service-tests-override");

            PlatformServiceBase.SaveRootOverride = null;

            Assert.That(service.SaveRoot, Does.StartWith(Application.persistentDataPath));
            Assert.That(service.SaveRoot, Does.EndWith("saves"));
        }
    }
}
