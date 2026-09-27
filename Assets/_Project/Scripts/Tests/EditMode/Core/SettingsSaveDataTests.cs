// 职责：覆盖 SettingsSaveData 版本 2 的迁移默认值与深拷贝。
// 为什么新建：设置从 v1 升到 v2 新增了显示五项，迁移与 Clone 漏字段只会在玩家老档上暴露，没有现成测试覆盖。

using Game.Core.Save;
using NUnit.Framework;

namespace Game.Tests.EditMode.Core
{
    public sealed class SettingsSaveDataTests
    {
        [Test]
        public void Version_WhenCreated_IsTwo()
        {
            Assert.That(new SettingsSaveData().Version, Is.EqualTo(2));
        }

        [Test]
        public void Migrate_FromV1_FillsDisplayDefaultsAndKeepsVolumes()
        {
            SettingsSaveData data = new SettingsSaveData
            {
                MasterVolume = 0.3f,
                ResolutionWidth = 123,
                ResolutionHeight = 45,
                FullScreenMode = 9,
                VSync = false,
                TargetFrameRate = 77,
            };

            data.Migrate(1);

            Assert.That(data.MasterVolume, Is.EqualTo(0.3f), "迁移不该动 v1 就有的字段");
            Assert.That(data.ResolutionWidth, Is.EqualTo(0));
            Assert.That(data.ResolutionHeight, Is.EqualTo(0));
            Assert.That(data.FullScreenMode, Is.EqualTo(SettingsSaveData.FullScreenModeBorderless));
            Assert.That(data.VSync, Is.True);
            Assert.That(data.TargetFrameRate, Is.EqualTo(0));
        }

        [Test]
        public void Constructor_WhenCreated_UsesV2DisplayDefaults()
        {
            SettingsSaveData data = new SettingsSaveData();

            Assert.That(data.ResolutionWidth, Is.EqualTo(0));
            Assert.That(data.ResolutionHeight, Is.EqualTo(0));
            Assert.That(data.FullScreenMode, Is.EqualTo(SettingsSaveData.FullScreenModeBorderless));
            Assert.That(data.VSync, Is.True);
            Assert.That(data.TargetFrameRate, Is.EqualTo(0));
        }

        [Test]
        public void Clone_WhenCalled_CopiesEveryFieldIntoNewInstance()
        {
            SettingsSaveData data = new SettingsSaveData
            {
                MasterVolume = 0.1f,
                BgmVolume = 0.2f,
                SfxVolume = 0.3f,
                Language = "en-US",
                ResolutionWidth = 1920,
                ResolutionHeight = 1080,
                FullScreenMode = SettingsSaveData.FullScreenModeWindowed,
                VSync = false,
                TargetFrameRate = 144,
            };

            SettingsSaveData copy = data.Clone();

            Assert.That(copy, Is.Not.SameAs(data));
            Assert.That(copy.MasterVolume, Is.EqualTo(0.1f));
            Assert.That(copy.BgmVolume, Is.EqualTo(0.2f));
            Assert.That(copy.SfxVolume, Is.EqualTo(0.3f));
            Assert.That(copy.Language, Is.EqualTo("en-US"));
            Assert.That(copy.ResolutionWidth, Is.EqualTo(1920));
            Assert.That(copy.ResolutionHeight, Is.EqualTo(1080));
            Assert.That(copy.FullScreenMode, Is.EqualTo(SettingsSaveData.FullScreenModeWindowed));
            Assert.That(copy.VSync, Is.False);
            Assert.That(copy.TargetFrameRate, Is.EqualTo(144));
        }
    }
}
