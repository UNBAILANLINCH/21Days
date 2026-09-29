// 职责：验证生产表与跨模块引用；纯规则测试不依赖 Luban 或 DialogueCatalog，因此另设内容边界测试。
using System;
using System.Collections.Generic;
using Game.Core.Config;
using Game.Dialogue;
using Game.Narrative;
using Game.Tests.EditMode.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode.Narrative
{
    public sealed class NarrativeCatalogTests
    {
        private DialogueCatalog dialogues;
        private NarrativeCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            var config = new TableConfig();
            dialogues = new DialogueCatalog(config);
            catalog = new NarrativeCatalog(config, dialogues);
        }

        [Test]
        public void RealTables_ValidReferences_BuildSuccessfully()
        {
            Assert.That(catalog.Stories.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(catalog.Encounters.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(catalog.QuestFlags[1002], Is.EqualTo("quest_completed_1002"));
        }

        [TestCase(NarrativeContent.StageKind.WaitAction, "", "等待阶段缺少出口")]
        [TestCase(NarrativeContent.StageKind.Battle, "", "尚未接入")]
        [TestCase(NarrativeContent.StageKind.Dialogue, "999999", "未知对白")]
        [TestCase(NarrativeContent.StageKind.Dialogue, "1002", "未映射")]
        public void Validate_InvalidStage_Rejects(NarrativeContent.StageKind kind, string payload, string message)
        {
            var content = new NarrativeContent("invalid", "entry", new[] {
                new NarrativeContent.Stage { Id = "entry", Kind = kind, PayloadId = payload } });
            var error = Assert.Throws<ArgumentException>(() => NarrativeCatalog.Validate(
                new[] { content }, Array.Empty<EncounterRules.Rule>(), dialogues));
            Assert.That(error.Message, Does.Contain(message));
        }

        [TestCase("missing", "end", 2, "")]
        [TestCase("story", "missing", 2, "")]
        [TestCase("story", "end", 1, "")]
        public void Validate_InvalidEncounterReferenceOrAmbiguousPriority_Rejects(string story, string entry, int priority, string kind)
        {
            var content = new NarrativeContent("story", "end", new[] {
                new NarrativeContent.Stage { Id = "end", Kind = NarrativeContent.StageKind.End } });
            var first = new EncounterRules.Rule { Id = "a", StoryId = "story", EntryStageId = "end", TriggerKind = "Interact", Priority = 1 };
            var second = new EncounterRules.Rule { Id = "b", StoryId = story, EntryStageId = entry, TriggerKind = "Interact", Priority = priority, TargetKind = kind };
            Assert.Throws<ArgumentException>(() => NarrativeCatalog.Validate(new[] { content }, new[] { first, second }, dialogues));
        }

        [TestCase(EncounterContext.Fact.TargetHostile)]
        [TestCase(EncounterContext.Fact.TargetDetected)]
        public void Validate_UnsupportedFact_Rejects(EncounterContext.Fact fact)
        {
            var content = new NarrativeContent("fact", "wait", new[] {
                new NarrativeContent.Stage { Id = "wait", Kind = NarrativeContent.StageKind.WaitAction,
                    Conditions = new[] { new[] { new NarrativeCondition { Fact = fact } } },
                    Exits = new Dictionary<string, string> { ["Done"] = "end" } },
                new NarrativeContent.Stage { Id = "end", Kind = NarrativeContent.StageKind.End } });
            Assert.Throws<ArgumentException>(() => NarrativeCatalog.Validate(new[] { content }, Array.Empty<EncounterRules.Rule>(), dialogues));
        }

        private sealed class TableConfig : IConfigService
        {
            public global::cfg.Tables Tables { get; } = ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes());
            public ulong ContentHash => 0;
        }
    }
}
