// 职责：钉住设置编辑会话的回滚规则——返回未应用则回滚（音量一并回滚）、应用则存盘一次且之后返回不撤销已应用部分。
// 为什么新建：SettingsEditSession 是新类（SettingsController 的可测核心），没有现成测试可扩展；
//   回滚规则错了会让玩家「点返回却保留了改动」或「点应用又被撤掉」，必须有 EditMode 测试守着。

using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Save;
using Game.Core.Settings;
using Game.Core.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode.Core
{
    /// <summary><see cref="SettingsEditSession"/> 的 EditMode 测试。设置服务用下面的假实现，只记调用次数。</summary>
    public sealed class SettingsEditSessionTests
    {
        private FakeSettingsService settings;
        private SettingsEditSession session;

        [SetUp]
        public void SetUp()
        {
            settings = new FakeSettingsService();
            settings.Current.MasterVolume = 0.8f;
            settings.Current.VSync = true;
            session = new SettingsEditSession(settings);
        }

        [Test]
        public void SetVolume_AppliesAudioImmediately()
        {
            session.Begin();
            session.SetBgmVolume(0.3f);

            Assert.That(settings.Current.BgmVolume, Is.EqualTo(0.3f));
            Assert.That(settings.ApplyAudioCount, Is.EqualTo(1), "滑条拖动要实时生效");
        }

        [Test]
        public void SetDisplay_DoesNotApplyUntilApply()
        {
            session.Begin();
            session.SetResolution(1280, 720);
            session.SetVSync(false);

            Assert.That(settings.ApplyDisplayCount, Is.EqualTo(0), "显示项只记值，点「应用」才生效");
        }

        [Test]
        public void End_WhenNotApplied_RestoresSnapshotIncludingVolume()
        {
            session.Begin();
            session.SetMasterVolume(0.2f);
            session.SetTargetFrameRate(60);

            bool reverted = session.End();

            Assert.That(reverted, Is.True);
            Assert.That(settings.RestoreCount, Is.EqualTo(1));
            Assert.That(settings.Current.MasterVolume, Is.EqualTo(0.8f), "音量也要回滚");
            Assert.That(settings.Current.TargetFrameRate, Is.EqualTo(0));
            Assert.That(settings.SaveCount, Is.EqualTo(0));
        }

        [Test]
        public void End_WhenNothingChanged_DoesNotRestore()
        {
            session.Begin();

            Assert.That(session.End(), Is.False);
            Assert.That(settings.RestoreCount, Is.EqualTo(0));
        }

        [Test]
        public void Apply_SavesOnceAndLaterEndKeepsAppliedValues()
        {
            session.Begin();
            session.SetResolution(1280, 720);
            session.ApplyAsync().GetAwaiter().GetResult();

            Assert.That(settings.ApplyDisplayCount, Is.EqualTo(1));
            Assert.That(settings.SaveCount, Is.EqualTo(1), "应用存盘一次");

            bool reverted = session.End();

            Assert.That(reverted, Is.False);
            Assert.That(settings.Current.ResolutionWidth, Is.EqualTo(1280), "已应用的不该被返回撤掉");
        }

        [Test]
        public void End_AfterApplyThenMoreChanges_RestoresToAppliedPoint()
        {
            session.Begin();
            session.SetSfxVolume(0.5f);
            session.ApplyAsync().GetAwaiter().GetResult();
            session.SetSfxVolume(0.1f);

            session.End();

            Assert.That(settings.Current.SfxVolume, Is.EqualTo(0.5f));
        }

        [Test]
        public void End_IsIdempotent()
        {
            session.Begin();
            session.SetMasterVolume(0.1f);
            session.End();

            Assert.That(session.End(), Is.False);
            Assert.That(settings.RestoreCount, Is.EqualTo(1));
        }

        /// <summary>假设置服务：Snapshot / Restore 用数据自带的 Clone / CopyFrom，其余只计数。</summary>
        private sealed class FakeSettingsService : ISettingsService
        {
            private readonly List<DisplayResolution> resolutions = new List<DisplayResolution>();

            public SettingsSaveData Current { get; } = new SettingsSaveData();

            public IReadOnlyList<DisplayResolution> AvailableResolutions => resolutions;

            public DisplayResolution NativeResolution => new DisplayResolution(1920, 1080);

            public int ApplyDisplayCount { get; private set; }

            public int ApplyAudioCount { get; private set; }

            public int SaveCount { get; private set; }

            public int RestoreCount { get; private set; }

            public void ApplyDisplay() => ApplyDisplayCount++;

            public void ApplyAudio() => ApplyAudioCount++;

            public UniTask SaveAsync(CancellationToken ct = default)
            {
                SaveCount++;
                return UniTask.CompletedTask;
            }

            public SettingsSaveData Snapshot() => Current.Clone();

            public void Restore(SettingsSaveData snapshot)
            {
                RestoreCount++;
                Current.CopyFrom(snapshot);
            }
        }
    }
}
