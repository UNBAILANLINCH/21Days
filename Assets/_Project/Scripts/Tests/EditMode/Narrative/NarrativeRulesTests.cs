// 职责：验证局部遭遇续接、行为完成与读档旧回调；现有战斗测试不能证明剧情请求身份。
using System.Collections.Generic;
using Game.Narrative;
using NUnit.Framework;

namespace Game.Tests.EditMode.Narrative
{
    public sealed class NarrativeRulesTests
    {
        private static NarrativeContent Story() => new NarrativeContent("story", "wait", new[]
        {
            new NarrativeContent.Stage { Id = "wait", Kind = NarrativeContent.StageKind.WaitAction, AllowEncounter = true,
                RequiredParts = new[] { "answer", "follow" }, Exits = new Dictionary<string, string> { ["Success"] = "end" } },
            new NarrativeContent.Stage { Id = "local", Kind = NarrativeContent.StageKind.Dialogue,
                Exits = new Dictionary<string, string> { ["Done"] = "localEnd" } },
            new NarrativeContent.Stage { Id = "localEnd", Kind = NarrativeContent.StageKind.End, Outcome = "Resume" },
            new NarrativeContent.Stage { Id = "end", Kind = NarrativeContent.StageKind.End }
        });
        private static EncounterContext Context() => new EncounterContext("target", "monster", true, false, true, true, false, true);
        private static NarrativeIntent Result(NarrativeRules rules, string result, string part = null) =>
            new NarrativeIntent(rules.Generation, rules.Current.ActivationId, rules.Current.TargetId, rules.Current.ActionRequestId, result, part);

        [Test]
        public void LocalEncounter_WhenCompleted_ResumesOriginalRequestAndPartialProgress()
        {
            var rules = new NarrativeRules(new[] { Story() }, null);
            rules.Start("story", "target");
            string request = rules.Current.ActionRequestId;
            NarrativeIntent part = Result(rules, "Success", "answer");
            Assert.That(rules.Apply(in part), Is.True);
            Assert.That(rules.EnterEncounter("story", "local", "target", "trigger"), Is.True);
            NarrativeIntent done = Result(rules, "Done");
            rules.Apply(in done);
            rules.ResolveAutomatic(Context());
            Assert.That(rules.Current.ActionRequestId, Is.EqualTo(request));
            Assert.That(rules.Current.CompletedParts.Contains("answer"), Is.True);
            Assert.That(rules.EnterEncounter("story", "local", "target", "trigger"), Is.False);
        }

        [Test]
        public void Restore_WhenOldResultArrives_RejectsWithoutAdvancing()
        {
            var rules = new NarrativeRules(new[] { Story() }, null);
            rules.Start("story", "target");
            NarrativeIntent old = Result(rules, "Success", "answer");
            rules.Restore(rules.Capture());
            Assert.That(rules.Apply(in old), Is.False);
            Assert.That(rules.Current.CompletedParts.Count, Is.EqualTo(0));
        }

        [Test]
        public void Condition_WhenAutomaticCycleExists_RejectsContent()
        {
            Assert.Throws<System.ArgumentException>(() => new NarrativeContent("loop", "a", new[]
            {
                new NarrativeContent.Stage { Id = "a", Kind = NarrativeContent.StageKind.Condition,
                    Exits = new Dictionary<string, string> { ["True"] = "a", ["False"] = "a" } }
            }));
        }

        [Test]
        public void EncounterRules_HighestPriorityAndStableTargetWin()
        {
            var rules = new NarrativeRules(new[] { Story() }, null);
            var encounters = new EncounterRules(new[]
            {
                new EncounterRules.Rule { Id = "low", TriggerKind = "seen", TargetKind = "monster", Priority = 1, StoryId = "story", EntryStageId = "local", Repeat = EncounterRules.RepeatPolicy.Reenter },
                new EncounterRules.Rule { Id = "high", TriggerKind = "seen", TargetKind = "monster", Priority = 2, StoryId = "story", EntryStageId = "local", Repeat = EncounterRules.RepeatPolicy.Reenter },
            }, rules);
            Assert.That(encounters.TryActivate(new[]
            {
                new EncounterRules.Candidate { TriggerId = "t2", TriggerKind = "seen", EntryEpoch = 1, Context = Context() },
                new EncounterRules.Candidate { TriggerId = "t1", TriggerKind = "seen", EntryEpoch = 1, Context = new EncounterContext("aaa", "monster", true, false, true, true, false, true) },
            }), Is.True);
            Assert.That(rules.Current.TargetId, Is.EqualTo("aaa"));
        }

    }
}
