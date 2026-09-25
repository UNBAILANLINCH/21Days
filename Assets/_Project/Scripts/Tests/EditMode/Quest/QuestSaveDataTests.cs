// 职责：锁定任务存档——Capture/Restore 往返一致、空分区、未知 id 跳过、追踪失效清零、经 JsonSaveService 落盘往返。
// 为什么新建：任务系统首次落地（PRP/quest-system），任务进度丢失只在读档后才暴露，必须有 EditMode 测试守着。

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using Game.Core.Platform;
using Game.Core.Save;
using Game.Quest;
using NUnit.Framework;
using UnityEngine.TestTools;
using static Game.Tests.EditMode.Quest.QuestTestContent;

namespace Game.Tests.EditMode.Quest
{
    /// <summary>
    /// 落盘用例照 JsonSaveServiceTests：存档根指向临时目录下的随机子目录，TearDown 整个删掉；
    /// 异步用例写成 <c>[UnityTest] + UniTask.ToCoroutine</c>，不在主线程上同步等（会死锁）。
    /// </summary>
    public sealed class QuestSaveDataTests
    {
        private string saveRoot;

        [SetUp]
        public void SetUp()
        {
            saveRoot = Path.Combine(Path.GetTempPath(), "21Days-quest-save-tests", Guid.NewGuid().ToString("N"));
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
        public void CaptureInto_ThenRestore_OnNewRules_ReproducesState()
        {
            QuestRules original = BuildProgressedRules();
            var data = new QuestSaveData();
            original.CaptureInto(data);

            QuestRules restored = NewRules();
            restored.Restore(data);

            Assert.That(restored.Initialized, Is.EqualTo(original.Initialized));
            Assert.That(restored.TrackedId, Is.EqualTo(Side2));
            Assert.That(restored.CurrentMainId, Is.EqualTo(original.CurrentMainId));
            foreach (int id in new[] { Main1, Main2, Main3, Side1, Side2 })
            {
                original.TryGet(id, out QuestProgress expected);
                restored.TryGet(id, out QuestProgress actual);
                Assert.That(actual.State, Is.EqualTo(expected.State), $"任务 {id} 状态");
                Assert.That(actual.ObjectiveIndex, Is.EqualTo(expected.ObjectiveIndex), $"任务 {id} 目标下标");
                Assert.That(actual.Count, Is.EqualTo(expected.Count), $"任务 {id} 计数");
                Assert.That(actual.AcceptOrder, Is.EqualTo(expected.AcceptOrder), $"任务 {id} 激活序号");
            }

            Assert.That(Ids(restored.InProgress), Is.EqualTo(Ids(original.InProgress)));
        }

        [Test]
        public void CaptureInto_ThenRestore_NextActivationContinuesAcceptOrder()
        {
            QuestRules original = BuildProgressedRules();
            var data = new QuestSaveData();
            original.CaptureInto(data);
            QuestRules restored = NewRules();
            restored.Restore(data);

            restored.Report(QuestObjectiveKind.ReachLocation, "camp");

            restored.TryGet(Main2, out QuestProgress main2);
            Assert.That(main2.AcceptOrder, Is.EqualTo(data.NextAcceptOrder + 1), "读档后新激活的序号要接着存档里的计数器往上走");
        }

        [Test]
        public void Restore_EmptyPartition_LeavesEverythingInactiveAndUninitialized()
        {
            QuestRules rules = BuildProgressedRules();

            rules.Restore(new QuestSaveData());

            Assert.That(rules.Initialized, Is.False);
            Assert.That(rules.TrackedId, Is.EqualTo(0));
            Assert.That(rules.CurrentMainId, Is.EqualTo(0));
            Assert.That(rules.InProgress, Is.Empty);
            foreach (int id in new[] { Main1, Main2, Main3, Side1, Side2 })
            {
                rules.TryGet(id, out QuestProgress progress);
                Assert.That(progress.State, Is.EqualTo(QuestState.Inactive), $"任务 {id}");
                Assert.That(progress.AcceptOrder, Is.EqualTo(0), $"任务 {id}");
            }
        }

        [Test]
        public void Restore_UnknownQuestId_IsSkipped()
        {
            var data = new QuestSaveData { Initialized = true, NextAcceptOrder = 2 };
            data.Quests.Add(new QuestProgressData { Id = 424242, State = (int)QuestState.InProgress, AcceptOrder = 1 });
            data.Quests.Add(new QuestProgressData { Id = Side1, State = (int)QuestState.InProgress, AcceptOrder = 2 });
            QuestRules rules = NewRules();

            rules.Restore(data);

            Assert.That(rules.TryGet(424242, out _), Is.False);
            Assert.That(Ids(rules.InProgress), Is.EqualTo(new[] { Side1 }));
        }

        [Test]
        public void Restore_TrackedQuestCompleted_ClearsTracking()
        {
            var data = new QuestSaveData { Initialized = true, TrackedId = Side1, NextAcceptOrder = 1 };
            data.Quests.Add(new QuestProgressData { Id = Side1, State = (int)QuestState.Completed, AcceptOrder = 1 });
            QuestRules rules = NewRules();

            rules.Restore(data);

            Assert.That(rules.TrackedId, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator SaveRoundTrip_ThroughJsonSaveService_PreservesQuestPartition() => UniTask.ToCoroutine(async () =>
        {
            QuestRules original = BuildProgressedRules();
            var saves = new JsonSaveService(new FakePlatformService(saveRoot), null, null);
            QuestSaveData partition = saves.Get<QuestSaveData>();
            original.CaptureInto(partition);

            Assert.That(await saves.SaveAsync(1), Is.True);

            // 换一个服务实例读回来：避免「其实只是读到了内存里那份」的假通过。
            var reloaded = new JsonSaveService(new FakePlatformService(saveRoot), null, null);
            Assert.That(await reloaded.LoadAsync(1), Is.True);

            QuestSaveData loaded = reloaded.Get<QuestSaveData>();
            Assert.That(loaded.Quests.Count, Is.EqualTo(partition.Quests.Count));
            Assert.That(loaded.TrackedId, Is.EqualTo(partition.TrackedId));
            Assert.That(loaded.Initialized, Is.True);
            Assert.That(loaded.NextAcceptOrder, Is.EqualTo(partition.NextAcceptOrder));
        });

        /// <summary>
        /// 激活 → 2001 完成（2002 激活）→ 1001 推进一步 → 追踪 2002。
        /// 结果：1001 进行中（下标 1）、2001 完成、2002 进行中且被追踪。
        /// </summary>
        private static QuestRules BuildProgressedRules()
        {
            QuestRules rules = NewRules();
            rules.ActivateAvailable();
            rules.Report(QuestObjectiveKind.ReachLocation, "lookout");
            rules.Report(QuestObjectiveKind.TalkTo, "1001");
            Assert.That(rules.Track(Side2), Is.True);
            return rules;
        }

        private static int[] Ids(IReadOnlyList<QuestProgress> list)
        {
            var ids = new int[list.Count];
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = list[i].Id;
            }

            return ids;
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
    }
}
