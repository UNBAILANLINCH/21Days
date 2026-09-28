// 职责：钉住对白历史转成通用记录面板输入的规则——选择项记为说话者「选择」、其余原样；转换后拼出的文字与改用通用面板前逐字相同。
// 为什么新建：DialogueController 依赖 UI / 资源 / 时钟，没有现成的测试类；这里只测它公开的纯静态转换 BuildTranscript，
//   按「被测类 + Tests」单独成文件，其余表现逻辑仍由 Dialogue 回放覆盖。
using System.Collections.Generic;
using System.Text;
using Game.Core.UI;
using Game.Core.UI.Views;
using Game.Dialogue;
using NUnit.Framework;

namespace Game.Tests.EditMode.Dialogue
{
    public sealed class DialogueControllerTests
    {
        private static DialogueSaveData.HistoryEntry Entry(string speaker, string text, bool choice = false) =>
            new DialogueSaveData.HistoryEntry { Speaker = speaker, Text = text, IsChoice = choice };

        [Test]
        public void BuildTranscript_ChoiceEntry_MapsToChoiceSpeaker()
        {
            var entries = new List<DialogueSaveData.HistoryEntry>
            {
                Entry("老者", "人都跑光了，对谁负责去？！"),
                Entry("旅人", "接受", choice: true),
                Entry(string.Empty, "风停了。"),
            };

            List<TranscriptLine> lines = DialogueController.BuildTranscript(entries);

            Assert.That(lines.Count, Is.EqualTo(3));
            Assert.That(lines[0].Speaker, Is.EqualTo("老者"));
            Assert.That(lines[0].Text, Is.EqualTo("人都跑光了，对谁负责去？！"));
            Assert.That(lines[1].Speaker, Is.EqualTo("选择"), "选择项不论谁选，一律记为「选择」");
            Assert.That(lines[1].Text, Is.EqualTo("接受"));
            Assert.That(lines[2].Speaker, Is.Empty, "旁白说话者为空");
            Assert.That(lines[2].Text, Is.EqualTo("风停了。"));
        }

        [Test]
        public void BuildTranscript_ThenFormat_MatchesPreviousHistoryText()
        {
            var entries = new List<DialogueSaveData.HistoryEntry>
            {
                Entry("老者", "人都跑光了，对谁负责去？！"),
                Entry("老者", "我去看看", choice: true),
                Entry(string.Empty, "风停了。"),
            };

            string formatted = TranscriptView.Format(DialogueController.BuildTranscript(entries), true);

            Assert.That(formatted, Is.EqualTo(PreviousHistoryText(entries, true)));
        }

        [Test]
        public void BuildTranscript_NullOrEmpty_ReturnsEmptyList()
        {
            Assert.That(DialogueController.BuildTranscript(null), Is.Empty);
            Assert.That(DialogueController.BuildTranscript(new List<DialogueSaveData.HistoryEntry>()), Is.Empty);
        }

        // 改用通用记录面板之前，对白历史面板 Show 的原实现（逐行照抄），作逐字比对的基准。
        private static string PreviousHistoryText(IReadOnlyList<DialogueSaveData.HistoryEntry> entries, bool truncated)
        {
            var text = new StringBuilder();
            if (truncated) text.AppendLine("更早的记录已省略。\n");
            foreach (DialogueSaveData.HistoryEntry entry in entries)
            {
                if (entry.IsChoice) text.Append("选择：");
                else if (!string.IsNullOrEmpty(entry.Speaker)) text.Append(entry.Speaker).Append("：");
                text.AppendLine(entry.Text).AppendLine();
            }
            return text.ToString();
        }
    }
}
