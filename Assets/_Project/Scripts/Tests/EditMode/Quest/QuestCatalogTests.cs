// 职责：内容检查——用真实生成的任务表 / 对话表 .bytes 钉住 QuestCatalog 的翻译结果，并验证「TalkTo 引用的对话缺失 → 抛」。
// 为什么新建：QuestCatalog 是新类；同目录的 QuestContentTests 测的是纯模型构造期校验，不读表，职责不同。
using System;
using System.Collections.Generic;
using Game.Core.Config;
using Game.Core.Telemetry;
using Game.Quest;
using Game.Tests.EditMode.Core;
using Luban;
using NUnit.Framework;

namespace Game.Tests.EditMode.Quest
{
    /// <summary>
    /// <see cref="QuestCatalog"/> 的测试。数据直接读 <c>Assets/_Project/Data/Config/*.bytes</c>（复用 ConfigServiceTests.ReadAllTableBytes），不经 Addressables。
    /// 内容（任务数、目标）改了要同步改这里；源数据在 <c>Tables/Data/quest/</c>。
    /// </summary>
    public sealed class QuestCatalogTests
    {
        private const string QuestTableName = "quest_tbquest";

        /// <summary>对话表里保证不存在的编号，用来造「TalkTo 引用的对话缺失」。</summary>
        private const int MissingDialogueId = 987654;

        private global::cfg.Tables tables;
        private QuestCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            tables = ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes());
            catalog = new QuestCatalog(new FakeConfigService(tables), NullTelemetryScope.Instance);
        }

        [Test]
        public void Content_RealTable_BuildsFourQuests()
        {
            QuestContent content = catalog.Content;
            Assert.That(content.All.Count, Is.EqualTo(4));

            Assert.That(content.TryGet(1001, out QuestDefinition first), Is.True);
            Assert.That(first.Kind, Is.EqualTo(QuestKind.Main));
            Assert.That(first.Prerequisites, Is.Empty);
            Assert.That(first.Objectives.Count, Is.EqualTo(2));
            Assert.That(first.Objectives[0].Kind, Is.EqualTo(QuestObjectiveKind.TalkTo));
            Assert.That(first.Objectives[0].Key, Is.EqualTo("1001"));
            Assert.That(first.Objectives[1].Kind, Is.EqualTo(QuestObjectiveKind.ReachLocation));
            Assert.That(first.Objectives[1].Key, Is.EqualTo("camp"));

            Assert.That(content.TryGet(1002, out QuestDefinition second), Is.True);
            Assert.That(second.Kind, Is.EqualTo(QuestKind.Main));
            Assert.That(second.Prerequisites, Is.EqualTo(new[] { 1001 }));
            Assert.That(second.Objectives.Count, Is.EqualTo(1));
            Assert.That(second.Objectives[0].Kind, Is.EqualTo(QuestObjectiveKind.TalkTo));
            Assert.That(second.Objectives[0].Key, Is.EqualTo("1002"));

            Assert.That(content.TryGet(2001, out QuestDefinition side), Is.True);
            Assert.That(side.Kind, Is.EqualTo(QuestKind.Side));
            Assert.That(side.Prerequisites, Is.Empty);
            Assert.That(side.Objectives.Count, Is.EqualTo(1));
            Assert.That(side.Objectives[0].Kind, Is.EqualTo(QuestObjectiveKind.ReachLocation));
            Assert.That(side.Objectives[0].Key, Is.EqualTo("lookout"));

            Assert.That(content.TryGet(2002, out QuestDefinition crates), Is.True);
            Assert.That(crates.Kind, Is.EqualTo(QuestKind.Side));
            Assert.That(crates.Prerequisites, Is.Empty);
            Assert.That(crates.Objectives.Count, Is.EqualTo(1));
            Assert.That(crates.Objectives[0].Kind, Is.EqualTo(QuestObjectiveKind.Counter));
            Assert.That(crates.Objectives[0].Key, Is.EqualTo("crate"));
            Assert.That(crates.Objectives[0].RequiredCount, Is.EqualTo(3));
        }

        [Test]
        public void Content_RealTable_EveryTalkToDialogueExists()
        {
            int talkToCount = 0;
            foreach (QuestDefinition quest in catalog.Content.All)
            {
                foreach (QuestObjectiveDefinition objective in quest.Objectives)
                {
                    if (objective.Kind != QuestObjectiveKind.TalkTo)
                    {
                        continue;
                    }

                    talkToCount++;
                    Assert.That(tables.TbDialogue.DataMap.ContainsKey(int.Parse(objective.Key)), Is.True,
                        $"任务 {quest.Id} 的 TalkTo 目标引用了不存在的对话 {objective.Key}");
                }
            }

            Assert.That(talkToCount, Is.GreaterThan(0), "真实表里至少该有一个 TalkTo 目标，否则这条检查是空转");
        }

        [Test]
        public void Content_AccessedTwice_ReturnsSameInstance()
        {
            Assert.That(catalog.Content, Is.SameAs(catalog.Content));
        }

        [Test]
        public void TryGet_WhenIdIsUnknown_ReturnsFalse()
        {
            Assert.That(catalog.TryGet(9999, out QuestDefinition definition), Is.False);
            Assert.That(definition, Is.Null);
        }

        [Test]
        public void Content_TalkToDialogueMissing_Throws()
        {
            // TbQuest 只有 ByteBuf 构造，这里按生成代码的读取顺序手写一张只含一条任务的假任务表，
            // 其余表（含对话表）仍用真实生成物，不改生成物也不改 Catalog 的公开接口。
            Dictionary<string, byte[]> bytes = ConfigServiceTests.ReadAllTableBytes();
            global::cfg.Tables realTables = ConfigService.BuildTables(bytes);
            Assume.That(realTables.TbDialogue.DataMap.ContainsKey(MissingDialogueId), Is.False,
                "对话表里恰好有这个编号，换一个不存在的");

            bytes[QuestTableName] = WriteSingleTalkToQuest(3001, MissingDialogueId.ToString());
            var broken = new QuestCatalog(new FakeConfigService(ConfigService.BuildTables(bytes)), NullTelemetryScope.Instance);

            var first = Assert.Throws<ArgumentException>(() => _ = broken.Content);
            Assert.That(first.Message, Does.Contain("3001").And.Contain(MissingDialogueId.ToString()));

            // 失败不落缓存：再次访问重新翻译，报同一个错。
            Assert.Throws<ArgumentException>(() => _ = broken.Content);
        }

        /// <summary>
        /// 按 <c>cfg.quest.TbQuest</c> / <c>Quest</c> / <c>Objective</c> 构造函数的读取顺序写一张一条任务的表。
        /// 生成代码的字段顺序变了这里要跟着改（届时这条测试会先在反序列化处炸，不会静默误判）。
        /// </summary>
        private static byte[] WriteSingleTalkToQuest(int questId, string dialogueKey)
        {
            var buf = new ByteBuf();
            buf.WriteSize(1);

            buf.WriteInt(questId);
            buf.WriteInt((int)global::cfg.quest.QuestKind.Side);
            buf.WriteString("假任务");
            buf.WriteString("用于测试对话缺失");
            buf.WriteSize(0); // 前置

            buf.WriteSize(1); // 目标
            buf.WriteString("和不存在的人谈谈");
            buf.WriteInt((int)global::cfg.quest.ObjectiveKind.TalkTo);
            buf.WriteString(dialogueKey);
            buf.WriteInt(1);
            buf.WriteString(string.Empty);

            return buf.CopyData();
        }

        /// <summary>假的配置服务：只递一份现成的 <c>cfg.Tables</c>；目录不该读指纹，读了就炸。</summary>
        private sealed class FakeConfigService : IConfigService
        {
            public FakeConfigService(global::cfg.Tables tables)
            {
                Tables = tables;
            }

            public global::cfg.Tables Tables { get; }

            public ulong ContentHash => throw new NotSupportedException("假配置服务不提供内容指纹");
        }
    }
}
