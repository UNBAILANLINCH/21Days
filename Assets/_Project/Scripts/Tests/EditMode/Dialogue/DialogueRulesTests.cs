// 职责：锁定补全文、旧输入隔离、动态选项与恢复；已有 Sample 测试不覆盖阅读状态。
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
            Assert.That(rules.CanSkip, Is.False);
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
        public void Skip_WhenFirstReadCompletes_RemainsDisabledUntilNextVisit()
        {
            var rules = new DialogueRules(new DialogueReadData(), 500, null);
            rules.Start(Content());
            rules.Ready(rules.Generation, rules.Visit, 2, "你好", "角色");
            rules.RevealTo(2);
            Assert.That(rules.CanSkip, Is.False);
            rules.Start(Content());
            rules.Ready(rules.Generation, rules.Visit, 2, "你好", "角色");
            Assert.That(rules.CanSkip, Is.True);
        }
    }
}
