// 职责：钉住槽位元数据分区的版本号与默认值（加字段不升版本、默认值即「从未保存」）。
// 为什么新建：SessionSaveData 是新分区，版本号错了旧档会被拒读，按「测试类 = <被测类>Tests」单独成文件。

using Game.Session;
using NUnit.Framework;

namespace Game.Tests.EditMode.Session
{
    public sealed class SessionSaveDataTests
    {
        [Test]
        public void Version_IsOne()
        {
            Assert.That(new SessionSaveData().Version, Is.EqualTo(1));
        }

        [Test]
        public void Defaults_MeanNeverSaved()
        {
            SessionSaveData data = new SessionSaveData();

            Assert.That(data.SavedAtUtcTicks, Is.Zero);
            Assert.That(data.PlaytimeSeconds, Is.Zero);
            Assert.That(data.ProgressText, Is.Empty, "默认空串而不是 null，界面直接显示不用判空");
            Assert.That(data.SceneKey, Is.Empty);
        }

        [Test]
        public void Migrate_FromAnyVersion_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => new SessionSaveData().Migrate(0));
        }
    }
}
