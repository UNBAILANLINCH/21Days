// 职责：锁定任务表文件层——现有 JSON 往返逐字节相等、缺字段 / 枚举大小写错抛 FormatException、输出字段顺序固定。
// 为什么新建：QuestTableFile 是新增的编辑器文件层（PRP/quest-editor），现有测试都针对运行时类型，无处可并。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Game.Editor.Quest;
using Game.Quest;
using NUnit.Framework;

namespace Game.Tests.EditMode.Quest
{
    public sealed class QuestTableFileTests
    {
        private const string ValidJson =
            "{\n" +
            "    \"id\": 1003,\n" +
            "    \"kind\": \"Main\",\n" +
            "    \"title\": \"标题\",\n" +
            "    \"description\": \"描述\",\n" +
            "    \"prerequisites\": [1001, 1002],\n" +
            "    \"objectives\": [\n" +
            "        {\n" +
            "            \"text\": \"数箱子\",\n" +
            "            \"kind\": \"Counter\",\n" +
            "            \"key\": \"crate\",\n" +
            "            \"count\": 3,\n" +
            "            \"location\": \"camp\"\n" +
            "        }\n" +
            "    ]\n" +
            "}\n";

        // 读真实工程 Tables/Data/quest/ 下的全部 JSON；新增 / 改动任务表文件后这条仍应绿（保存格式与手写一致）。
        [Test]
        public void SerializeDeserialize_ExistingQuestFiles_ByteIdentical()
        {
            string[] files = Directory.GetFiles(QuestTableFile.DataDirectory, "*.json");
            Assert.That(files, Is.Not.Empty, "Tables/Data/quest/ 下没有任务表文件");

            // 工程里的 NUnit 版本没有 Assert.Multiple：先收集全部不一致的文件名，一次报出。
            var utf8 = new UTF8Encoding(false);
            var mismatched = new List<string>();
            foreach (string file in files)
            {
                byte[] original = File.ReadAllBytes(file);
                QuestDraft draft = QuestTableFile.Deserialize(File.ReadAllText(file, Encoding.UTF8), file);
                byte[] roundTrip = utf8.GetBytes(QuestTableFile.Serialize(draft));
                if (!roundTrip.SequenceEqual(original)) mismatched.Add(Path.GetFileName(file));
            }

            Assert.That(mismatched, Is.Empty, "往返后与原文不一致的文件");
        }

        [Test]
        public void SerializeDeserialize_ValidJson_RoundTrips()
        {
            QuestDraft draft = QuestTableFile.Deserialize(ValidJson, "1003.json");

            Assert.That(draft.Id, Is.EqualTo(1003));
            Assert.That(draft.Prerequisites, Is.EqualTo(new[] { 1001, 1002 }));
            Assert.That(draft.Objectives[0].Kind, Is.EqualTo(QuestObjectiveKind.Counter));
            Assert.That(draft.Objectives[0].Count, Is.EqualTo(3));
            Assert.That(draft.SourcePath, Is.EqualTo("1003.json"));
            Assert.That(QuestTableFile.Serialize(draft), Is.EqualTo(ValidJson));
        }

        [Test]
        public void Deserialize_MissingCount_ThrowsFormatExceptionNamingField()
        {
            string json = ValidJson.Replace("            \"count\": 3,\n", string.Empty);

            Assert.That(() => QuestTableFile.Deserialize(json, "1003.json"),
                Throws.TypeOf<FormatException>().With.Message.Contains("count").And.Message.Contains("1003.json"));
        }

        [Test]
        public void Deserialize_MissingTopLevelField_ThrowsFormatExceptionNamingField()
        {
            string json = ValidJson.Replace("    \"prerequisites\": [1001, 1002],\n", string.Empty);

            Assert.That(() => QuestTableFile.Deserialize(json, "1003.json"),
                Throws.TypeOf<FormatException>().With.Message.Contains("prerequisites"));
        }

        [Test]
        public void Deserialize_KindWrongCase_ThrowsFormatException()
        {
            string json = ValidJson.Replace("\"kind\": \"Main\"", "\"kind\": \"main\"");

            Assert.That(() => QuestTableFile.Deserialize(json, "1003.json"),
                Throws.TypeOf<FormatException>().With.Message.Contains("kind").And.Message.Contains("main"));
        }

        [Test]
        public void Deserialize_ObjectiveKindWrongCase_ThrowsFormatException()
        {
            string json = ValidJson.Replace("\"kind\": \"Counter\"", "\"kind\": \"counter\"");

            Assert.That(() => QuestTableFile.Deserialize(json, "1003.json"),
                Throws.TypeOf<FormatException>().With.Message.Contains("kind"));
        }

        [Test]
        public void Deserialize_CountAsString_ThrowsFormatException()
        {
            string json = ValidJson.Replace("\"count\": 3", "\"count\": \"3\"");

            Assert.That(() => QuestTableFile.Deserialize(json, "1003.json"),
                Throws.TypeOf<FormatException>().With.Message.Contains("count"));
        }

        [Test]
        public void Deserialize_BrokenJson_ThrowsFormatException()
        {
            Assert.That(() => QuestTableFile.Deserialize("{ \"id\": ", "1003.json"),
                Throws.TypeOf<FormatException>().With.Message.Contains("1003.json"));
        }

        [Test]
        public void Serialize_NewDraft_WritesFieldsInOrder()
        {
            var draft = new QuestDraft
            {
                Id = 2003,
                Kind = QuestKind.Side,
                Title = "新任务"
            };
            draft.Objectives.Add(new QuestObjectiveDraft
            {
                Text = "去井边",
                Kind = QuestObjectiveKind.ReachLocation,
                Key = "well"
            });

            string json = QuestTableFile.Serialize(draft);

            Assert.That(json, Does.Contain("\"prerequisites\": [],\n"));
            Assert.That(json, Does.Contain("\"kind\": \"Side\""));
            Assert.That(json, Does.Contain("\"kind\": \"ReachLocation\""));
            Assert.That(json, Does.EndWith("}\n"));
            AssertInOrder(json, "\"id\"", "\"kind\"", "\"title\"", "\"description\"", "\"prerequisites\"", "\"objectives\"");
            AssertInOrder(json, "\"text\"", "\"kind\": \"ReachLocation\"", "\"key\"", "\"count\": 1", "\"location\"");
        }

        [Test]
        public void Serialize_NoObjectives_WritesEmptyArrayOnOneLine()
        {
            var draft = new QuestDraft { Id = 1009, Title = "空" };

            Assert.That(QuestTableFile.Serialize(draft), Does.Contain("    \"objectives\": []\n}"));
        }

        private static void AssertInOrder(string text, params string[] parts)
        {
            int position = -1;
            foreach (string part in parts)
            {
                int next = text.IndexOf(part, position + 1, StringComparison.Ordinal);
                Assert.That(next, Is.GreaterThan(position), $"「{part}」没有按顺序出现");
                position = next;
            }
        }
    }
}
