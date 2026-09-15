// 职责：覆盖 JsonSaveService 的核心规则——往返、Exists/Delete、原子写不留残片、损坏文件不抛、版本迁移。
// 为什么新建：波 2 之前没有存档相关测试；存档出错的代价是玩家进度丢失且只在真机上复现，
// 这几条规则必须有一份不依赖场景、不依赖真实 persistentDataPath 的 EditMode 测试守着。

using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using Game.Core.Platform;
using Game.Core.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.EditMode.Core
{
    /// <summary>
    /// JsonSaveService 的 EditMode 测试。存档根目录指向临时目录下的随机子目录，
    /// TearDown 整个删掉，所以不会碰到本机真实存档、也不会互相干扰。
    /// <para>
    /// 异步用例一律写成 <c>[UnityTest] + UniTask.ToCoroutine</c>，**不要**用
    /// <c>AsTask().GetAwaiter().GetResult()</c> 同步等：存档的 IO 在线程池上跑完后要切回主线程，
    /// 而编辑器下 UniTask 的 PlayerLoop 是靠 <c>EditorApplication.update</c> 推的——
    /// 在主线程上阻塞等就等于把推它的那只手按住了，必死锁。
    /// </para>
    /// </summary>
    public sealed class JsonSaveServiceTests
    {
        private string saveRoot;
        private JsonSaveService saves;

        [SetUp]
        public void SetUp()
        {
            saveRoot = Path.Combine(Path.GetTempPath(), "21Days-save-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(saveRoot);
            saves = new JsonSaveService(new FakePlatformService(saveRoot));
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            if (Directory.Exists(saveRoot))
            {
                Directory.Delete(saveRoot, true);
            }
        }

        [Test]
        public void Get_WhenCalledTwice_ReturnsSameInstanceWithDefaults()
        {
            SettingsSaveData first = saves.Get<SettingsSaveData>();
            SettingsSaveData second = saves.Get<SettingsSaveData>();

            Assert.That(second, Is.SameAs(first), "同一分区每次要拿到同一个实例");
            Assert.That(first.MasterVolume, Is.EqualTo(1f));
            Assert.That(first.BgmVolume, Is.EqualTo(1f));
            Assert.That(first.SfxVolume, Is.EqualTo(1f));
            Assert.That(first.Language, Is.EqualTo("zh-CN"));
            Assert.That(first.Version, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SaveThenLoad_WhenRoundTripped_RestoresPartitionValues() => UniTask.ToCoroutine(async () =>
        {
            SettingsSaveData settings = saves.Get<SettingsSaveData>();
            settings.MasterVolume = 0.4f;
            settings.BgmVolume = 0.25f;
            settings.SfxVolume = 0.75f;
            settings.Language = "en-US";

            Assert.That(await saves.SaveAsync(1), Is.True);

            // 换一个服务实例读回来：避免「其实只是读到了内存里那份」的假通过。
            JsonSaveService reloaded = new JsonSaveService(new FakePlatformService(saveRoot));
            Assert.That(await reloaded.LoadAsync(1), Is.True);

            SettingsSaveData restored = reloaded.Get<SettingsSaveData>();
            Assert.That(restored.MasterVolume, Is.EqualTo(0.4f).Within(1e-4f));
            Assert.That(restored.BgmVolume, Is.EqualTo(0.25f).Within(1e-4f));
            Assert.That(restored.SfxVolume, Is.EqualTo(0.75f).Within(1e-4f));
            Assert.That(restored.Language, Is.EqualTo("en-US"));
        });

        [UnityTest]
        public IEnumerator ExistsAndDelete_FollowTheFileOnDisk() => UniTask.ToCoroutine(async () =>
        {
            Assert.That(saves.Exists(2), Is.False, "没存过就不该存在");

            saves.Get<SettingsSaveData>().MasterVolume = 0.1f;
            Assert.That(await saves.SaveAsync(2), Is.True);
            Assert.That(saves.Exists(2), Is.True);

            saves.Delete(2);
            Assert.That(saves.Exists(2), Is.False);
            Assert.That(File.Exists(Path.Combine(saveRoot, "slot2.json")), Is.False);
        });

        [UnityTest]
        public IEnumerator SaveAsync_WhenSucceeded_LeavesNoTempFileBehind() => UniTask.ToCoroutine(async () =>
        {
            saves.Get<SettingsSaveData>().MasterVolume = 0.6f;

            Assert.That(await saves.SaveAsync(3), Is.True);
            // 再存一次：第二次走的是 File.Replace 分支（正档已存在），残片最容易留在这条路径上。
            Assert.That(await saves.SaveAsync(3), Is.True);

            string[] temps = Directory.GetFiles(saveRoot, "*.tmp", SearchOption.AllDirectories);
            Assert.That(temps, Is.Empty, "原子写完不该留下 .tmp：" + string.Join("、", temps));
            Assert.That(File.Exists(Path.Combine(saveRoot, "slot3.json")), Is.True);
        });

        [UnityTest]
        public IEnumerator LoadAsync_WhenFileIsCorrupted_ReturnsFalseWithoutThrowing() => UniTask.ToCoroutine(async () =>
        {
            File.WriteAllText(Path.Combine(saveRoot, "slot4.json"), "{ 这不是合法 JSON ", new UTF8Encoding(false));

            // 损坏存档按设计会记一条 Error；这里断言的是「不抛、返回 false」，所以放行日志。
            LogAssert.ignoreFailingMessages = true;

            Assert.That(await saves.LoadAsync(4), Is.False, "损坏的存档要返回 false，不能抛出去把游戏带崩");
        });

        [UnityTest]
        public IEnumerator LoadAsync_WhenSlotMissing_ReturnsFalse() => UniTask.ToCoroutine(async () =>
        {
            LogAssert.ignoreFailingMessages = true;

            Assert.That(await saves.LoadAsync(9), Is.False);
        });

        [UnityTest]
        public IEnumerator LoadAsync_WhenStoredVersionIsOlder_CallsMigrateOnceWithStoredVersion() =>
            UniTask.ToCoroutine(async () =>
            {
                // 手写一份 version=1 的存档：MigratingSaveData 当前版本是 2，读回来必须触发一次迁移。
                WriteRawSave(5, typeof(MigratingSaveData), 1, "{ \"Score\": 7 }");

                Assert.That(await saves.LoadAsync(5), Is.True);

                MigratingSaveData migrated = saves.Get<MigratingSaveData>();
                Assert.That(migrated.Score, Is.EqualTo(7), "迁移前的字段值要先读进来");
                Assert.That(migrated.MigrateCalls, Is.EqualTo(1), "Migrate 只该被调用一次");
                Assert.That(migrated.MigratedFrom, Is.EqualTo(1), "Migrate 的参数是存档里的版本号");
            });

        [UnityTest]
        public IEnumerator LoadAsync_WhenStoredVersionMatches_DoesNotCallMigrate() => UniTask.ToCoroutine(async () =>
        {
            WriteRawSave(6, typeof(MigratingSaveData), 2, "{ \"Score\": 3 }");

            Assert.That(await saves.LoadAsync(6), Is.True);

            MigratingSaveData loaded = saves.Get<MigratingSaveData>();
            Assert.That(loaded.Score, Is.EqualTo(3));
            Assert.That(loaded.MigrateCalls, Is.EqualTo(0), "版本一致时不该迁移");
        });

        [UnityTest]
        public IEnumerator LoadAsync_WhenMigrateThrows_PropagatesException() => UniTask.ToCoroutine(async () =>
        {
            // MigrateThrowsSaveData 当前版本是 2，写一份 version=1 的存档触发迁移路径。
            WriteRawSave(7, typeof(MigrateThrowsSaveData), 1, "{ \"Score\": 1 }");

            // Migrate 实现里的 bug 要被记一条 Error，但不能被吞掉——异常得继续往上抛。
            LogAssert.Expect(LogType.Error, new Regex("Migrate"));

            InvalidOperationException thrown = null;
            try
            {
                await saves.LoadAsync(7);
            }
            catch (InvalidOperationException e)
            {
                thrown = e;
            }

            Assert.That(thrown, Is.Not.Null, "Migrate 抛出的异常要继续往上抛，不能被当成存档损坏吞掉");
            Assert.That(thrown.Message, Is.EqualTo("迁移测试用异常"));
        });

        [UnityTest]
        public IEnumerator LoadAsync_WhenPartitionCorrupt_ReturnsFalseAndKeepsOtherPartitions() =>
            UniTask.ToCoroutine(async () =>
            {
                // 两个分区：Settings 合法，MigratingSaveData 的 Score 类型对不上（数组存进 int 字段），
                // ToObject 会因此抛 JsonSerializationException（JsonException 的子类），判定为分区损坏。
                string json =
                    "{\n" +
                    "  \"formatVersion\": " + JsonSaveService.CurrentFormatVersion + ",\n" +
                    "  \"partitions\": {\n" +
                    "    \"" + typeof(SettingsSaveData).FullName + "\": {\n" +
                    "      \"version\": 1,\n" +
                    "      \"data\": { \"MasterVolume\": 0.5 }\n" +
                    "    },\n" +
                    "    \"" + typeof(MigratingSaveData).FullName + "\": {\n" +
                    "      \"version\": 2,\n" +
                    "      \"data\": { \"Score\": [1, 2, 3] }\n" +
                    "    }\n" +
                    "  }\n" +
                    "}";
                File.WriteAllText(Path.Combine(saveRoot, "slot8.json"), json, new UTF8Encoding(false));

                LogAssert.ignoreFailingMessages = true;

                Assert.That(await saves.LoadAsync(8), Is.False, "只要有分区损坏，整体就该返回 false");

                // Load 失败时内存里的存档要保持不变——这里是全新实例，保持的就是默认值，
                // 不该被本次没读完的半份数据污染。
                Assert.That(saves.Get<SettingsSaveData>().MasterVolume, Is.EqualTo(1f),
                    "Load 失败时不该用部分读到的数据覆盖内存里的旧值");
            });

        /// <summary>手写一份只含单个分区的存档文件，用来构造「存档里的版本」这种测不出来的边界。</summary>
        private void WriteRawSave(int slot, Type partitionType, int storedVersion, string dataJson)
        {
            string json =
                "{\n" +
                "  \"formatVersion\": " + JsonSaveService.CurrentFormatVersion + ",\n" +
                "  \"partitions\": {\n" +
                "    \"" + partitionType.FullName + "\": {\n" +
                "      \"version\": " + storedVersion + ",\n" +
                "      \"data\": " + dataJson + "\n" +
                "    }\n" +
                "  }\n" +
                "}";
            File.WriteAllText(Path.Combine(saveRoot, $"slot{slot}.json"), json, new UTF8Encoding(false));
        }

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

        /// <summary>当前版本是 2 的分区，用来验证迁移被正确触发。</summary>
        private sealed class MigratingSaveData : ISaveData
        {
            public int Version => 2;

            public int Score { get; set; }

            /// <summary>Migrate 被调用的次数。运行期计数，只给断言用。</summary>
            public int MigrateCalls { get; private set; }

            /// <summary>最后一次 Migrate 收到的 fromVersion。</summary>
            public int MigratedFrom { get; private set; } = -1;

            public void Migrate(int fromVersion)
            {
                MigrateCalls++;
                MigratedFrom = fromVersion;
            }
        }

        /// <summary>当前版本是 2 的分区，Migrate 实现本身有 bug 会抛异常——用来验证这种异常不会被吞掉。</summary>
        private sealed class MigrateThrowsSaveData : ISaveData
        {
            public int Version => 2;

            public int Score { get; set; }

            public void Migrate(int fromVersion)
            {
                throw new InvalidOperationException("迁移测试用异常");
            }
        }
    }
}
