// 职责：钉住身份存档分区——版本与默认值、Capture/Restore 往返、无效 / 未知身份的读档行为、
//   经 JsonSaveService 真正落盘再读回来的往返。
// 为什么新建：身份要跨阶段保留（`01_换皮与附身.md:192`），「读档后身份丢了 / 账簿清零」
//   只在读档那一刻才暴露，必须有 EditMode 用例守着。

using System;
using System.Collections;
using System.IO;
using Cysharp.Threading.Tasks;
using Game.Core.Platform;
using Game.Core.Save;
using Game.Identity;
using NUnit.Framework;
using UnityEngine.TestTools;
using static Game.Tests.EditMode.Identity.IdentityTestContent;

namespace Game.Tests.EditMode.Identity
{
    /// <summary>
    /// 落盘用例照 JsonSaveServiceTests / QuestSaveDataTests：存档根指向临时目录下的随机子目录，
    /// TearDown 整个删掉；异步用例写成 <c>[UnityTest] + UniTask.ToCoroutine</c>，不在主线程上同步等。
    /// </summary>
    public sealed class IdentitySaveDataTests
    {
        private string saveRoot;

        [SetUp]
        public void SetUp()
        {
            saveRoot = Path.Combine(Path.GetTempPath(), "21Days-identity-save-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(saveRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(saveRoot))
            {
                Directory.Delete(saveRoot, true);
            }
        }

        [Test]
        public void Version_IsOne()
        {
            Assert.That(new IdentitySaveData().Version, Is.EqualTo(1));
        }

        [Test]
        public void Defaults_MeanBodyWithEmptyLedger()
        {
            var data = new IdentitySaveData();

            Assert.That(data.CurrentIdentityId, Is.Empty, "默认空串而不是 null，读档路径不用判空");
            Assert.That(data.CurrentOrigin, Is.Zero);
            Assert.That(data.HasTimeLimit, Is.False);
            Assert.That(data.ExposureCount, Is.Zero);
            Assert.That(data.Exposed, Is.False);
            Assert.That(data.Dead, Is.False);
            Assert.That(data.MemoryRecorded, Is.False);
            Assert.That(data.UsedIdentityIds, Is.Empty);
            Assert.That(data.UsedIdentityTotalUses, Is.Zero);
            Assert.That(data.Suspicion, Is.Zero);
        }

        [Test]
        public void Migrate_FromAnyVersion_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => new IdentitySaveData().Migrate(0));
        }

        [Test]
        public void CaptureThenRestore_ReproducesStateLedgerAndSuspicion()
        {
            IdentitySettings settings = NewSettings();
            settings.DefaultDurationSeconds = 20f;
            settings.CooldownSeconds = 3f;
            settings.SuspicionLimit = 50f;
            IdentityCatalog catalog = NewCatalog();
            var rules = new IdentityRules(settings, catalog);
            var state = new IdentityState();
            var ledger = new IdentityLedger();
            SuspicionState suspicion = SuspicionState.FromSettings(settings);

            rules.TryEnter(state, ledger, StewardPeltId);
            ledger.Record(IdentityId.From(Musician));
            suspicion.Add(12f);
            rules.AdvanceIdentity(state, 5f);

            var data = new IdentitySaveData();
            rules.Capture(state, ledger, suspicion, data);

            var restoredState = new IdentityState();
            var restoredLedger = new IdentityLedger();
            SuspicionState restoredSuspicion = SuspicionState.FromSettings(settings);
            bool currentKept = rules.Restore(restoredState, restoredLedger, restoredSuspicion, data);

            Assert.That(currentKept, Is.True);
            Assert.That(restoredState.Current.Value, Is.EqualTo(StewardPelt));
            Assert.That(restoredState.CurrentOrigin, Is.EqualTo(IdentityOrigin.Pelt));
            Assert.That(restoredState.HasTimeLimit, Is.True);
            Assert.That(restoredState.RemainingSeconds, Is.EqualTo(15f).Within(0.001f));
            Assert.That(restoredState.MemoryRecorded, Is.True);
            Assert.That(restoredLedger.DistinctCount, Is.EqualTo(2));
            Assert.That(restoredLedger.TotalUses, Is.EqualTo(ledger.TotalUses));
            Assert.That(restoredSuspicion.Value, Is.EqualTo(12f).Within(0.001f));
        }

        /// <summary>无效 / 未知身份的读档行为：当前身份丢弃回本体，其余计数照常恢复。</summary>
        [Test]
        public void Restore_UnknownCurrentIdentity_DropsIdentityButKeepsCounters()
        {
            IdentitySettings settings = NewSettings();
            var rules = new IdentityRules(settings, NewCatalog());
            var data = new IdentitySaveData
            {
                CurrentIdentityId = "removed_identity",
                HasTimeLimit = true,
                RemainingSeconds = 9f,
                ExposureCount = 2,
                Exposed = true,
                Suspicion = 7f,
            };
            data.UsedIdentityIds.Add(Steward);
            var state = new IdentityState();
            var ledger = new IdentityLedger();
            SuspicionState suspicion = SuspicionState.FromSettings(settings);

            bool currentKept = rules.Restore(state, ledger, suspicion, data);

            Assert.That(currentKept, Is.False);
            Assert.That(state.IsBorrowing, Is.False, "未知身份读档后回到本体");
            Assert.That(state.RemainingSeconds, Is.Zero);
            Assert.That(state.ExposureCount, Is.EqualTo(2), "露馅计数照常恢复");
            Assert.That(state.Exposed, Is.True);
            Assert.That(suspicion.Value, Is.EqualTo(7f).Within(0.001f));
            Assert.That(ledger.HasUsed(IdentityId.From(Steward)), Is.True);
        }

        /// <summary>损坏存档里格式不合法的 id：跳过那一条，不抛异常、不影响其余恢复。</summary>
        [Test]
        public void Restore_MalformedUsedIdentity_IsSkipped()
        {
            IdentitySettings settings = NewSettings();
            var rules = new IdentityRules(settings, NewCatalog());
            var data = new IdentitySaveData { UsedIdentityTotalUses = 3 };
            data.UsedIdentityIds.Add("Bad Id");
            data.UsedIdentityIds.Add(Musician);
            var state = new IdentityState();
            var ledger = new IdentityLedger();
            SuspicionState suspicion = SuspicionState.FromSettings(settings);

            Assert.DoesNotThrow(() => rules.Restore(state, ledger, suspicion, data));
            Assert.That(ledger.HasUsed(IdentityId.From(Musician)), Is.True);
            Assert.That(ledger.DistinctCount, Is.EqualTo(1));
        }

        [Test]
        public void Capture_TwiceIntoSamePartition_DoesNotAccumulateDuplicates()
        {
            IdentitySettings settings = NewSettings();
            var rules = new IdentityRules(settings, NewCatalog());
            var state = new IdentityState();
            var ledger = new IdentityLedger();
            SuspicionState suspicion = SuspicionState.FromSettings(settings);
            rules.TryEnter(state, ledger, MusicianId);
            var data = new IdentitySaveData();

            rules.Capture(state, ledger, suspicion, data);
            rules.Capture(state, ledger, suspicion, data);

            Assert.That(data.UsedIdentityIds.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SaveRoundTrip_ThroughJsonSaveService_PreservesIdentityPartition() => UniTask.ToCoroutine(async () =>
        {
            IdentitySettings settings = NewSettings();
            var rules = new IdentityRules(settings, NewCatalog());
            var state = new IdentityState();
            var ledger = new IdentityLedger();
            SuspicionState suspicion = SuspicionState.FromSettings(settings);
            rules.TryEnter(state, ledger, InspectorMaskId);
            suspicion.Add(4f);

            var saves = new JsonSaveService(new FakePlatformService(saveRoot), null, null);
            IdentitySaveData partition = saves.Get<IdentitySaveData>();
            rules.Capture(state, ledger, suspicion, partition);
            Assert.That(await saves.SaveAsync(1), Is.True);

            // 换一个服务实例读回来：避免「其实只是读到了内存里那份」的假通过。
            var reloaded = new JsonSaveService(new FakePlatformService(saveRoot), null, null);
            Assert.That(await reloaded.LoadAsync(1), Is.True);

            IdentitySaveData loaded = reloaded.Get<IdentitySaveData>();
            Assert.That(loaded.CurrentIdentityId, Is.EqualTo(InspectorMask));
            Assert.That(loaded.CurrentOrigin, Is.EqualTo((int)IdentityOrigin.Mask));
            Assert.That(loaded.UsedIdentityIds, Is.EqualTo(partition.UsedIdentityIds));
            Assert.That(loaded.Suspicion, Is.EqualTo(4f).Within(0.001f));
        });

        /// <summary>只提供 SaveRoot 的假平台服务；其余成员测试里用不到。</summary>
        private sealed class FakePlatformService : IPlatformService
        {
            public FakePlatformService(string saveRoot)
            {
                SaveRoot = saveRoot;
            }

            public PlatformKind Kind => PlatformKind.Standalone;

            public string SaveRoot { get; }

            public bool IsTouchPrimary => false;

            public void Vibrate(VibrationKind kind)
            {
            }
        }
    }
}
