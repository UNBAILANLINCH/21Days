// 职责：覆盖 DialogueReadStore——空档案读入、写出后新 store 读回一致（原地填充同一实例）、同帧多次对白结束只写一次。
// 为什么新建：DialogueReadStore 是新类，按「一个被测类一个测试类」新建；已读档案写坏或写倒退玩家察觉不到，必须有测试守着。
// 写入次数怎么数：用「计数 ISaveService」——包一层真实的临时目录 JsonSaveService，只给 WriteProfileAsync 计数、其余原样转发。
//   没选「数文件写入次数」：JsonSaveService 先写临时文件再替换，文件时间戳 / FileSystemWatcher 分不清一次还是两次。

using System;
using System.Collections;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Platform;
using Game.Core.Save;
using Game.Dialogue;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Game.Tests.EditMode.Dialogue
{
    /// <summary>
    /// 存档根目录指向临时目录下的随机子目录，TearDown 整个删掉。
    /// 异步用例写成 <c>[UnityTest] + UniTask.ToCoroutine</c>（同步等会死锁，见 JsonSaveServiceTests 类注释）。
    /// 对白结束事件用测试构造接到本类的一个普通 C# 事件上，不必搭整套 DialogueService。
    /// </summary>
    public sealed class DialogueReadStoreTests
    {
        private string saveRoot;
        private CountingSaveService saves;
        private event Action<DialogueEndedEvent> Ended;

        [SetUp]
        public void SetUp()
        {
            saveRoot = Path.Combine(Path.GetTempPath(), "21Days-dialogue-read-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(saveRoot);
            saves = new CountingSaveService(new JsonSaveService(new FakePlatformService(saveRoot), null, null));
            Ended = null;
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
        public IEnumerator InitializeAsync_WhenNoProfile_LeavesKeysEmpty() => UniTask.ToCoroutine(async () =>
        {
            var read = new DialogueReadData();
            DialogueReadStore store = CreateStore(read);

            await store.InitializeAsync(default);

            Assert.That(read.Keys, Is.Empty);
            store.Dispose();
        });

        [UnityTest]
        public IEnumerator DialogueEnded_ThenNewStoreInitialize_ReadsBackSameKeysIntoSameInstance() => UniTask.ToCoroutine(async () =>
        {
            var firstRead = new DialogueReadData();
            DialogueReadStore first = CreateStore(firstRead);
            await first.InitializeAsync(default);
            string[] keys =
            {
                DialogueReadData.Key("1001", "a", 1),
                DialogueReadData.Key("1001", "b", 1),
                DialogueReadData.Key("1002", "a", 2),
            };
            foreach (string key in keys) firstRead.Keys.Add(key);

            RaiseEnded();
            await WaitForWritesAsync(1);
            first.Dispose();

            var secondRead = new DialogueReadData();
            DialogueReadData sameInstance = secondRead;
            DialogueReadStore second = CreateStore(secondRead);
            await second.InitializeAsync(default);

            Assert.That(secondRead, Is.SameAs(sameInstance), "应原地填充容器里的同一个实例");
            Assert.That(secondRead.Keys, Is.EquivalentTo(keys));
            second.Dispose();
        });

        [UnityTest]
        public IEnumerator DialogueEnded_TwiceInSameFrame_WritesOnce() => UniTask.ToCoroutine(async () =>
        {
            var read = new DialogueReadData();
            DialogueReadStore store = CreateStore(read);
            await store.InitializeAsync(default);

            read.Keys.Add(DialogueReadData.Key("1001", "a", 1));
            RaiseEnded();
            read.Keys.Add(DialogueReadData.Key("1001", "b", 1));
            RaiseEnded();
            Assert.That(saves.WriteCount, Is.EqualTo(0), "结束当帧只标记脏，不应立刻写");

            await WaitForWritesAsync(1);
            // 再多等几帧，确认没有第二次写。
            for (int i = 0; i < 5; i++) await UniTask.Yield();

            Assert.That(saves.WriteCount, Is.EqualTo(1), "同帧两次结束应合并成一次写");
            DialogueReadProfile onDisk = await saves.ReadProfileAsync<DialogueReadProfile>(DialogueReadStore.ProfileName);
            Assert.That(onDisk.Keys, Is.EquivalentTo(read.Keys), "合并写应带上两次结束时的全部已读键");
            store.Dispose();
        });

        private DialogueReadStore CreateStore(DialogueReadData read)
        {
            return new DialogueReadStore(read, saves, handler => Ended += handler, handler => Ended -= handler);
        }

        private void RaiseEnded()
        {
            Ended?.Invoke(new DialogueEndedEvent(1001, "done", false));
        }

        /// <summary>写盘在线程池上跑，按帧轮询到计数达标；超时判失败而不是挂死。</summary>
        private async UniTask WaitForWritesAsync(int expected)
        {
            for (int frame = 0; frame < 300 && saves.CompletedWriteCount < expected; frame++)
            {
                await UniTask.Yield();
            }

            Assert.That(saves.CompletedWriteCount, Is.GreaterThanOrEqualTo(expected), "等待档案写完超时");
        }

        /// <summary>计数装饰器：WriteProfileAsync 计数（发起 / 完成各一个），其余原样转发给真实存档服务。</summary>
        private sealed class CountingSaveService : ISaveService
        {
            private readonly ISaveService inner;

            public CountingSaveService(ISaveService inner)
            {
                this.inner = inner;
            }

            public int WriteCount { get; private set; }

            public int CompletedWriteCount { get; private set; }

            public async UniTask WriteProfileAsync<T>(string name, T data, CancellationToken ct = default) where T : class
            {
                WriteCount++;
                await inner.WriteProfileAsync(name, data, ct);
                CompletedWriteCount++;
            }

            public T Get<T>() where T : class, ISaveData, new() => inner.Get<T>();
            public UniTask<bool> SaveAsync(int slot, CancellationToken ct = default) => inner.SaveAsync(slot, ct);
            public UniTask<bool> LoadAsync(int slot, CancellationToken ct = default) => inner.LoadAsync(slot, ct);
            public UniTask<SaveSnapshot> ReadCandidateAsync(int slot, CancellationToken ct = default) => inner.ReadCandidateAsync(slot, ct);
            public SaveSnapshot Capture() => inner.Capture();
            public void Commit(SaveSnapshot snapshot) => inner.Commit(snapshot);
            public void ResetAll() => inner.ResetAll();

            public UniTask<T> ReadProfileAsync<T>(string name, CancellationToken ct = default) where T : class, new() =>
                inner.ReadProfileAsync<T>(name, ct);

            public UniTask<T> ReadProfileAsync<T>(string name, Action<T> validate, CancellationToken ct = default)
                where T : class, new() => inner.ReadProfileAsync(name, validate, ct);

            public bool Exists(int slot) => inner.Exists(slot);
            public void Delete(int slot) => inner.Delete(slot);
        }

        /// <summary>只提供 SaveRoot 的假平台服务。</summary>
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
