// 职责：锁定补全文、旧输入隔离、动态选项、恢复与同步跳过；已有 Sample 测试不覆盖阅读状态。
using Game.Dialogue;
using Game.Narrative;
using NUnit.Framework;

namespace Game.Tests.EditMode.Dialogue
{
    public sealed class DialogueRulesTests
    {
        private static DialogueContent Content() => new DialogueContent("test", "line", new[]
        {
            new DialogueContent.Node { Id = "line", Text = "你好", Next = "choice" },
            new DialogueContent.Node { Id = "choice", Kind = DialogueContent.NodeKind.Choice,
                Choices = new[] { new DialogueContent.Choice { Id = "pretend", Text = "同类", Outcome = "Pretend",
                    Conditions = new[] { new[] { new NarrativeCondition { Fact = EncounterContext.Fact.PlayerDisguised } } } } } }
        });
        private static EncounterContext Context(bool disguised) => new EncounterContext("target", "monster", true, false, disguised, true, false, true);

        [Test]
        public void Advance_WhenTyping_RevealsWithoutLeavingAndRestoreDoesNotDuplicateHistory()
        {
            var rules = new DialogueRules(new DialogueReadData(), 500, null);
            DialogueContent content = Content();
            rules.Start(content);
            rules.Ready(rules.Generation, rules.Visit, 2, "你好", "角色");
            var input = new DialogueIntent(DialogueIntent.Action.Advance, rules.Generation, rules.Visit);
            Assert.That(rules.Apply(in input, Context(true)), Is.True);
            Assert.That(rules.Phase, Is.EqualTo(DialogueSaveData.Phase.AwaitAdvance));
            Assert.That(rules.History.Count, Is.EqualTo(1));
            DialogueSaveData saved = rules.Capture();
            rules.Restore(content, saved);
            Assert.That(rules.History.Count, Is.EqualTo(1));
            Assert.That(rules.Apply(in input, Context(true)), Is.False);
        }

        [Test]
        public void Choice_WhenConditionChanges_RejectsAndDoesNotRecord()
        {
            var rules = new DialogueRules(new DialogueReadData(), 500, null);
            rules.Start(Content());
            rules.Ready(rules.Generation, rules.Visit, 2, "你好", "角色");
            rules.RevealTo(2);
            var advance = new DialogueIntent(DialogueIntent.Action.Advance, rules.Generation, rules.Visit);
            rules.Apply(in advance, Context(true));
            var choose = new DialogueIntent(DialogueIntent.Action.Choose, rules.Generation, rules.Visit, "pretend");
            Assert.That(rules.Apply(in choose, Context(false)), Is.False);
            Assert.That(rules.History.Count, Is.EqualTo(1));
            Assert.That(rules.Apply(in choose, Context(true)), Is.True);
            Assert.That(rules.Apply(in choose, Context(true)), Is.False);
            Assert.That(rules.Outcome, Is.EqualTo("Pretend"));
            Assert.That(rules.History.Count, Is.EqualTo(2));
        }

        [Test]
        public void Skip_WhenFirstReadCompletes_WritesReadKey()
        {
            var read = new DialogueReadData();
            var rules = new DialogueRules(read, 500, null);
            rules.Start(Content());
            rules.Ready(rules.Generation, rules.Visit, 2, "你好", "角色");
            Assert.That(read.Keys.Contains(DialogueReadData.Key("test", "line", 1)), Is.False);
            rules.RevealTo(2);
            Assert.That(read.Keys.Contains(DialogueReadData.Key("test", "line", 1)), Is.True);
        }

        private static DialogueContent LongContent() => new DialogueContent("long", "a", new[]
        {
            new DialogueContent.Node { Id = "a", SpeakerId = "hero", SpeakerName = "甲", Text = "第一句", Next = "b" },
            new DialogueContent.Node { Id = "b", SpeakerId = "npc", SpeakerName = "乙", Text = "第二句", Next = "c" },
            new DialogueContent.Node { Id = "c", SpeakerId = "hero", SpeakerName = "甲", Text = "第三句", Next = "choice" },
            new DialogueContent.Node { Id = "choice", Kind = DialogueContent.NodeKind.Choice,
                Choices = new[] { new DialogueContent.Choice { Id = "go", Text = "继续", Next = "d" } } },
            new DialogueContent.Node { Id = "d", Text = "第四句", Next = "end" },
            new DialogueContent.Node { Id = "end", Kind = DialogueContent.NodeKind.End, Outcome = "Done" }
        });
        private static string Resolve(DialogueContent.Node node) => "名:" + node.SpeakerId;

        [Test]
        public void Skip_WhenPreparing_StopsAtChoiceAndReturnsLineCount()
        {
            var rules = new DialogueRules(new DialogueReadData(), 500, null);
            rules.Start(LongContent());
            Assert.That(rules.Phase, Is.EqualTo(DialogueSaveData.Phase.Preparing));
            Assert.That(rules.Skip(rules.Generation, Resolve), Is.EqualTo(3));
            Assert.That(rules.Phase, Is.EqualTo(DialogueSaveData.Phase.AwaitChoice));
            Assert.That(rules.Current.Id, Is.EqualTo("choice"));
        }

        [Test]
        public void Skip_WhenLinesSkipped_RecordsEachLineInHistoryAndReadKeys()
        {
            var read = new DialogueReadData();
            var rules = new DialogueRules(read, 500, null);
            rules.Start(LongContent());
            rules.Ready(rules.Generation, rules.Visit, 3, "第一句", "解析甲");
            rules.Skip(rules.Generation, Resolve);
            Assert.That(rules.History.Count, Is.EqualTo(3));
            Assert.That(rules.History[0].Text, Is.EqualTo("第一句"));
            Assert.That(rules.History[0].Speaker, Is.EqualTo("解析甲"));
            Assert.That(rules.History[1].Text, Is.EqualTo("第二句"));
            Assert.That(rules.History[1].Speaker, Is.EqualTo("名:npc"));
            Assert.That(rules.History[2].Speaker, Is.EqualTo("名:hero"));
            Assert.That(read.Keys.Contains(DialogueReadData.Key("long", "b", 1)), Is.True);
            Assert.That(read.Keys.Contains(DialogueReadData.Key("long", "c", 1)), Is.True);
        }

        [Test]
        public void Skip_AfterChoose_RunsToCompletedAndRaisesChoiceSelected()
        {
            var rules = new DialogueRules(new DialogueReadData(), 500, null);
            DialogueContent.Choice raised = null;
            rules.OnChoiceSelected += choice => raised = choice;
            rules.Start(LongContent());
            rules.Skip(rules.Generation, Resolve);
            var choose = new DialogueIntent(DialogueIntent.Action.Choose, rules.Generation, rules.Visit, "go");
            Assert.That(rules.Apply(in choose, Context(true)), Is.True);
            Assert.That(raised, Is.Not.Null);
            Assert.That(raised.Id, Is.EqualTo("go"));
            Assert.That(rules.Skip(rules.Generation, Resolve), Is.EqualTo(1));
            Assert.That(rules.Phase, Is.EqualTo(DialogueSaveData.Phase.Completed));
            Assert.That(rules.Outcome, Is.EqualTo("Done"));
            Assert.That(rules.Skip(rules.Generation, Resolve), Is.EqualTo(0));
        }

        [Test]
        public void Skip_WhenGenerationMismatch_ReturnsZeroAndKeepsPhase()
        {
            var rules = new DialogueRules(new DialogueReadData(), 500, null);
            rules.Start(LongContent());
            Assert.That(rules.Skip(rules.Generation - 1, Resolve), Is.EqualTo(0));
            Assert.That(rules.Phase, Is.EqualTo(DialogueSaveData.Phase.Preparing));
            Assert.That(rules.History.Count, Is.EqualTo(0));
        }

        [Test]
        public void Skip_WhenContentLoops_ThrowsInvalidOperation()
        {
            var rules = new DialogueRules(new DialogueReadData(), 500, null);
            rules.Start(new DialogueContent("loop", "self", new[]
            {
                new DialogueContent.Node { Id = "self", Text = "又是这句", Next = "self" }
            }));
            Assert.Throws<System.InvalidOperationException>(() => rules.Skip(rules.Generation, Resolve));
        }
    }
}
