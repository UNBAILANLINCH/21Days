// 职责：覆盖 SettingsService——独立档案读写往返、Snapshot / Restore 往返、ApplyDisplay 的应用与「只在变化时切分辨率」。
// 为什么新建：设置服务是新建的，档案读写与回滚错了玩家的设置会丢或回不去，必须有 EditMode 测试守着。
// 怎么不碰真编辑器分辨率：服务的显示后端换成 FakeDisplayBackend（只记录调用），不经过 UnityDisplayBackend，
//   所以测试里 Screen.SetResolution / QualitySettings / Application.targetFrameRate 一次都不会被真的调用。

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using Game.Core.Platform;
using Game.Core.Save;
using Game.Core.Settings;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.EditMode.Core
{
    /// <summary>
    /// 存档根目录指向临时目录下的随机子目录，TearDown 整个删掉。
    /// 异步用例写成 <c>[UnityTest] + UniTask.ToCoroutine</c>，原因见 JsonSaveServiceTests 的类注释（同步等会死锁）。
    /// </summary>
    public sealed class SettingsServiceTests
    {
        private string saveRoot;
        private FakePlatformService platform;
        private JsonSaveService saves;
        private FakeDisplayBackend display;

        [SetUp]
        public void SetUp()
        {
            saveRoot = Path.Combine(Path.GetTempPath(), "21Days-settings-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(saveRoot);
            platform = new FakePlatformService(saveRoot, false);
            saves = new JsonSaveService(platform, null, null);
            display = new FakeDisplayBackend();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(saveRoot))
            {
                Directory.Delete(saveRoot, true);
            }
        }

        [UnityTest]
        public IEnumerator InitializeAsync_WhenNoProfile_UsesDefaultsAndAppliesNative() => UniTask.ToCoroutine(async () =>
        {
            SettingsService service = CreateService();

            await service.InitializeAsync(default);

            Assert.That(service.Current.MasterVolume, Is.EqualTo(1f));
            Assert.That(service.Current.VSync, Is.True);
            Assert.That(display.SetResolutionCalls, Is.EqualTo(1));
            Assert.That(display.LastResolution, Is.EqualTo(display.NativeResolution), "0/0 = 原生分辨率");
            Assert.That(display.LastMode, Is.EqualTo(FullScreenMode.FullScreenWindow));
            Assert.That(display.VSyncCount, Is.EqualTo(1));
            Assert.That(display.TargetFrameRate, Is.EqualTo(-1));
        });

        [UnityTest]
        public IEnumerator SaveAsync_ThenNewServiceInitialize_ReadsBackSameValues() => UniTask.ToCoroutine(async () =>
        {
            SettingsService first = CreateService();
            await first.InitializeAsync(default);
            first.Current.MasterVolume = 0.25f;
            first.Current.BgmVolume = 0.5f;
            first.Current.SfxVolume = 0.75f;
            first.Current.ResolutionWidth = 1920;
            first.Current.ResolutionHeight = 1080;
            first.Current.FullScreenMode = SettingsSaveData.FullScreenModeWindowed;
            first.Current.VSync = false;
            first.Current.TargetFrameRate = 144;
            await first.SaveAsync();

            Assert.That(File.Exists(Path.Combine(saveRoot, "profile-" + SettingsService.ProfileName + ".json")), Is.True,
                "设置要写进独立档案，不进存档槽");
            Assert.That(saves.Exists(0), Is.False);

            FakeDisplayBackend secondDisplay = new FakeDisplayBackend();
            SettingsService second = new SettingsService(new JsonSaveService(platform, null, null), platform, secondDisplay, null);
            await second.InitializeAsync(default);

            Assert.That(second.Current.MasterVolume, Is.EqualTo(0.25f));
            Assert.That(second.Current.BgmVolume, Is.EqualTo(0.5f));
            Assert.That(second.Current.SfxVolume, Is.EqualTo(0.75f));
            Assert.That(second.Current.ResolutionWidth, Is.EqualTo(1920));
            Assert.That(second.Current.ResolutionHeight, Is.EqualTo(1080));
            Assert.That(second.Current.FullScreenMode, Is.EqualTo(SettingsSaveData.FullScreenModeWindowed));
            Assert.That(second.Current.VSync, Is.False);
            Assert.That(second.Current.TargetFrameRate, Is.EqualTo(144));

            Assert.That(secondDisplay.LastResolution, Is.EqualTo(new DisplayResolution(1920, 1080)));
            Assert.That(secondDisplay.LastMode, Is.EqualTo(FullScreenMode.Windowed));
            Assert.That(secondDisplay.VSyncCount, Is.EqualTo(0));
            Assert.That(secondDisplay.TargetFrameRate, Is.EqualTo(144));
        });

        [UnityTest]
        public IEnumerator InitializeAsync_WhenProfileOutOfRange_Sanitizes() => UniTask.ToCoroutine(async () =>
        {
            SettingsSaveData bad = new SettingsSaveData
            {
                MasterVolume = 3f,
                FullScreenMode = 5,
                TargetFrameRate = 75,
                ResolutionWidth = -1,
                ResolutionHeight = 1080,
            };
            await saves.WriteProfileAsync(SettingsService.ProfileName, bad);

            SettingsService service = CreateService();
            await service.InitializeAsync(default);

            Assert.That(service.Current.MasterVolume, Is.EqualTo(1f));
            Assert.That(service.Current.FullScreenMode, Is.EqualTo(SettingsSaveData.FullScreenModeBorderless));
            Assert.That(service.Current.TargetFrameRate, Is.EqualTo(0));
            Assert.That(service.Current.ResolutionWidth, Is.EqualTo(0));
            Assert.That(service.Current.ResolutionHeight, Is.EqualTo(0));
        });

        [Test]
        public void SnapshotRestore_WhenCurrentChanged_RollsBackInPlace()
        {
            SettingsService service = CreateService();
            SettingsSaveData current = service.Current;
            current.MasterVolume = 0.4f;
            current.ResolutionWidth = 1920;
            current.ResolutionHeight = 1080;

            SettingsSaveData snapshot = service.Snapshot();
            Assert.That(snapshot, Is.Not.SameAs(current), "快照必须是深拷贝");

            current.MasterVolume = 0.9f;
            current.ResolutionWidth = 2560;
            current.ResolutionHeight = 1440;
            current.VSync = false;

            service.Restore(snapshot);

            Assert.That(service.Current, Is.SameAs(current), "Restore 原地覆盖，不换实例");
            Assert.That(current.MasterVolume, Is.EqualTo(0.4f));
            Assert.That(current.ResolutionWidth, Is.EqualTo(1920));
            Assert.That(current.VSync, Is.True);
            Assert.That(display.LastResolution, Is.EqualTo(new DisplayResolution(1920, 1080)), "Restore 要顺带 ApplyDisplay");
        }

        [Test]
        public void ApplyDisplay_WhenResolutionUnchanged_DoesNotCallSetResolutionAgain()
        {
            SettingsService service = CreateService();

            service.ApplyDisplay();
            service.Current.VSync = false;
            service.ApplyDisplay();

            Assert.That(display.SetResolutionCalls, Is.EqualTo(1), "只改垂直同步时不该再切分辨率（会把拖过的窗口弹回去）");
            Assert.That(display.VSyncCount, Is.EqualTo(0));

            service.Current.FullScreenMode = SettingsSaveData.FullScreenModeWindowed;
            service.ApplyDisplay();

            Assert.That(display.SetResolutionCalls, Is.EqualTo(2));
        }

        [Test]
        public void ApplyDisplay_WhenTouchPrimary_SkipsSetResolution()
        {
            SettingsService service = new SettingsService(saves, new FakePlatformService(saveRoot, true), display, null);

            service.ApplyDisplay();

            Assert.That(display.SetResolutionCalls, Is.EqualTo(0));
            Assert.That(display.VSyncCount, Is.EqualTo(1), "垂直同步在手机上照常设");
        }

        [Test]
        public void AvailableResolutions_FiltersBackendList()
        {
            SettingsService service = CreateService();

            Assert.That(service.AvailableResolutions, Is.EqualTo(new[]
            {
                new DisplayResolution(1280, 720),
                new DisplayResolution(1920, 1080),
                new DisplayResolution(2560, 1440),
            }));
        }

        private SettingsService CreateService()
        {
            return new SettingsService(saves, platform, display, null);
        }

        /// <summary>只提供 SaveRoot 与触屏开关的假平台服务。</summary>
        private sealed class FakePlatformService : IPlatformService
        {
            public FakePlatformService(string saveRoot, bool isTouchPrimary)
            {
                SaveRoot = saveRoot;
                IsTouchPrimary = isTouchPrimary;
            }

            public PlatformKind Kind => PlatformKind.Standalone;

            public string SaveRoot { get; }

            public bool IsTouchPrimary { get; }

            public void Vibrate(VibrationKind kind)
            {
            }
        }

        /// <summary>记录调用的假显示后端：不碰 Screen / QualitySettings / Application。</summary>
        private sealed class FakeDisplayBackend : IDisplayBackend
        {
            public DisplayResolution NativeResolution => new DisplayResolution(2560, 1440);

            public int VSyncCount { get; set; } = -99;

            public int TargetFrameRate { get; set; } = -99;

            public int SetResolutionCalls { get; private set; }

            public DisplayResolution LastResolution { get; private set; }

            public FullScreenMode LastMode { get; private set; }

            public IEnumerable<DisplayResolution> QueryResolutions()
            {
                return new[]
                {
                    new DisplayResolution(2560, 1440),
                    new DisplayResolution(1920, 1080),
                    new DisplayResolution(1920, 1080),
                    new DisplayResolution(1280, 720),
                    new DisplayResolution(800, 600),
                };
            }

            public void SetResolution(int width, int height, FullScreenMode mode)
            {
                SetResolutionCalls++;
                LastResolution = new DisplayResolution(width, height);
                LastMode = mode;
            }
        }
    }
}
