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

        /// <summary>战斗关样例必须能整条翻译进表；校验器从「一律抛」改为「可进表 + 有校验」的回归锚点。</summary>
        [Test]
        public void RealTables_BattleStory_TranslatesWithDeclaredResults()
        {
            NarrativeContent story = Find("sample_battle_variants");
            NarrativeContent.Stage battle = story.Get("fight");
            Assert.That(battle.Kind, Is.EqualTo(NarrativeContent.StageKind.Battle));
            Assert.That(battle.BattleResults, Is.EqualTo(new[] { "Downed", "Exposed", "BossPhaseChanged:form2", "Victory" }));
            Assert.That(story.Get("fight").Kind, Is.EqualTo(NarrativeContent.StageKind.Battle));
            Assert.That(story.Get("fight").BattleResults, Is.EqualTo(new[] { "Downed", "Exposed", "BossPhaseChanged:form2", "Victory" }));
            Assert.That(story.Get("fight").Exits["BossPhaseChanged:form2"], Is.EqualTo("fight"));
            Assert.That(story.Get("fight").SetFlags, Does.Contain("combat.phase.1"));
        }

        /// <summary>请求／多部分行为样例同样要能进表；对应的缺出口负对照在下面单独覆盖。</summary>
        [Test]
        public void RealTables_RequestStory_TranslatesWithPartsAndRequestFlag()
        {
            NarrativeContent story = Find("sample_request");
            NarrativeContent.Stage ask = story.Get("ask");
            Assert.That(ask.IssueRequest, Is.True);
            Assert.That(ask.RequiredParts, Is.EqualTo(new[] { "answer", "follow" }));
            Assert.That(ask.Exits.ContainsKey("Success"), Is.True);
        }

        [TestCase(NarrativeContent.StageKind.WaitAction, "", "等待阶段缺少出口")]
        [TestCase(NarrativeContent.StageKind.Battle, "", "战斗阶段必须声明至少一个战斗结果")]
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

        // ------------------------------------------------------------------------------------
        // 三类能力的生产校验分支（各带负对照）
        // ------------------------------------------------------------------------------------

        [Test]
        public void Validate_BattleExitIntoAnotherBattle_Rejects()
        {
            var error = Assert.Throws<ArgumentException>(() => NarrativeCatalog.Validate(
                new[] { BattleStory("Victory", "second") }, Array.Empty<EncounterRules.Rule>(), dialogues));
            Assert.That(error.Message, Does.Contain("不能直接进入另一个战斗阶段"));
        }

        [Test]
        public void Validate_BattleExitBackToSameBattle_Accepts()
        {
            Assert.DoesNotThrow(() => NarrativeCatalog.Validate(
                new[] { BattleStory("BossPhaseChanged:form2", "fight") },
                Array.Empty<EncounterRules.Rule>(), dialogues));
        }

        [Test]
        public void Validate_NonBattleStageDeclaringBattleResults_Rejects()
        {
            var content = new NarrativeContent("req", "wait", new[] {
                new NarrativeContent.Stage { Id = "wait", Kind = NarrativeContent.StageKind.WaitAction,
                    BattleResults = new[] { "Victory" },
                    Exits = new Dictionary<string, string> { ["Victory"] = "end" } },
                new NarrativeContent.Stage { Id = "end", Kind = NarrativeContent.StageKind.End } });
            var error = Assert.Throws<ArgumentException>(() => NarrativeCatalog.Validate(
                new[] { content }, Array.Empty<EncounterRules.Rule>(), dialogues));
            Assert.That(error.Message, Does.Contain("只有战斗阶段可以声明战斗结果"));
        }

        [Test]
        public void Validate_BattleStageDeclaringUnknownResultCode_Rejects()
        {
            var content = new NarrativeContent("req", "fight", new[] {
                new NarrativeContent.Stage { Id = "fight", Kind = NarrativeContent.StageKind.Battle,
                    BattleResults = new[] { "Win" },
                    Exits = new Dictionary<string, string> { ["Win"] = "end" } },
                new NarrativeContent.Stage { Id = "end", Kind = NarrativeContent.StageKind.End } });
            var error = Assert.Throws<ArgumentException>(() => NarrativeCatalog.Validate(
                new[] { content }, Array.Empty<EncounterRules.Rule>(), dialogues));
            Assert.That(error.Message, Does.Contain("战斗结果非法"));
        }

        [Test]
        public void Validate_IssueRequestWithoutAnyExit_Rejects()
        {
            var content = new NarrativeContent("req", "ask", new[] {
                new NarrativeContent.Stage { Id = "ask", Kind = NarrativeContent.StageKind.WaitAction, IssueRequest = true } });
            var error = Assert.Throws<ArgumentException>(() => NarrativeCatalog.Validate(
                new[] { content }, Array.Empty<EncounterRules.Rule>(), dialogues));
            Assert.That(error.Message, Does.Contain("外部请求阶段缺少出口"));
        }

        [Test]
        public void Validate_IssueRequestWithExit_Accepts()
        {
            var content = new NarrativeContent("req", "ask", new[] {
                new NarrativeContent.Stage { Id = "ask", Kind = NarrativeContent.StageKind.WaitAction, IssueRequest = true,
                    Exits = new Dictionary<string, string> { ["Done"] = "end" } },
                new NarrativeContent.Stage { Id = "end", Kind = NarrativeContent.StageKind.End } });
            Assert.DoesNotThrow(() => NarrativeCatalog.Validate(
                new[] { content }, Array.Empty<EncounterRules.Rule>(), dialogues));
        }

        [Test]
        public void Validate_RequiredPartsWithoutSuccessExit_Rejects()
        {
            var content = new NarrativeContent("req", "ask", new[] {
                new NarrativeContent.Stage { Id = "ask", Kind = NarrativeContent.StageKind.WaitAction,
                    RequiredParts = new[] { "answer" },
                    Exits = new Dictionary<string, string> { ["Refused"] = "end" } },
                new NarrativeContent.Stage { Id = "end", Kind = NarrativeContent.StageKind.End } });
            var error = Assert.Throws<ArgumentException>(() => NarrativeCatalog.Validate(
                new[] { content }, Array.Empty<EncounterRules.Rule>(), dialogues));
            Assert.That(error.Message, Does.Contain("缺少 Success 出口"));
        }

        [Test]
        public void Validate_RequiredPartsWithDuplicatePart_Rejects()
        {
            var content = new NarrativeContent("req", "ask", new[] {
                new NarrativeContent.Stage { Id = "ask", Kind = NarrativeContent.StageKind.WaitAction,
                    RequiredParts = new[] { "answer", "answer" },
                    Exits = new Dictionary<string, string> { ["Success"] = "end" } },
                new NarrativeContent.Stage { Id = "end", Kind = NarrativeContent.StageKind.End } });
            var error = Assert.Throws<ArgumentException>(() => NarrativeCatalog.Validate(
                new[] { content }, Array.Empty<EncounterRules.Rule>(), dialogues));
            Assert.That(error.Message, Does.Contain("行为部分 ID 重复"));
        }

        // ------------------------------------------------------------------------------------
        // V1–V3：剧情标记键名（真源 ai-docs/docs/story-facts.md §3.2 / §4）
        // ------------------------------------------------------------------------------------

        [Test]
        public void Validate_StoryFlagKeyInUpperCase_RejectsOnV1()
        {
            var error = Assert.Throws<ArgumentException>(() => NarrativeCatalog.Validate(
                new[] { FlagStory("Identity.Suspicion") }, Array.Empty<EncounterRules.Rule>(), dialogues));
            Assert.That(error.Message, Does.Contain("V1"));
            Assert.That(error.Message, Does.Contain("Identity.Suspicion"));
        }

        [Test]
        public void Validate_StoryFlagKeyWithUnknownNamespace_RejectsOnV2()
        {
            var error = Assert.Throws<ArgumentException>(() => NarrativeCatalog.Validate(
                new[] { FlagStory("foo.bar") }, Array.Empty<EncounterRules.Rule>(), dialogues));
            Assert.That(error.Message, Does.Contain("V2"));
            Assert.That(error.Message, Does.Contain("foo.bar"));
        }

        [Test]
        public void Validate_StoryFlagKeyWithMisspelledTier_RejectsOnV3()
        {
            var error = Assert.Throws<ArgumentException>(() => NarrativeCatalog.Validate(
                new[] { FlagStory("identity.suspision.high") }, Array.Empty<EncounterRules.Rule>(), dialogues));
            Assert.That(error.Message, Does.Contain("V3"));
            Assert.That(error.Message, Does.Contain("identity.suspision.high"));
        }

        [TestCase("identity.suspicion.high")]
        [TestCase("combat.phase.2")]
        [TestCase("item.tooth.owned")]
        [TestCase("stage.p1.passed")]
        [TestCase("quest_completed_1002")]
        public void Validate_RegisteredStoryFlagKey_Accepts(string key)
        {
            Assert.DoesNotThrow(() => NarrativeCatalog.Validate(
                new[] { FlagStory(key) }, Array.Empty<EncounterRules.Rule>(), dialogues));
        }

        private NarrativeContent Find(string storyId)
        {
            foreach (NarrativeContent story in catalog.Stories)
                if (story.Id == storyId) return story;
            Assert.Fail("生成表里没有剧情：" + storyId);
            return null;
        }

        /// <summary>两个不同战斗阶段串联的极简内容：出口自环由调用方传 next == fight 触发。</summary>
        private static NarrativeContent BattleStory(string result, string next)
        {
            return new NarrativeContent("req", "fight", new[] {
                new NarrativeContent.Stage { Id = "fight", Kind = NarrativeContent.StageKind.Battle,
                    BattleResults = new[] { result },
                    Exits = new Dictionary<string, string> { [result] = next } },
                new NarrativeContent.Stage { Id = "second", Kind = NarrativeContent.StageKind.Battle,
                    BattleResults = new[] { "Victory" },
                    Exits = new Dictionary<string, string> { ["Victory"] = "cleared" } },
                new NarrativeContent.Stage { Id = "cleared", Kind = NarrativeContent.StageKind.End },
            });
        }

        private static NarrativeContent FlagStory(string key)
        {
            return new NarrativeContent("flag", "wait", new[] {
                new NarrativeContent.Stage { Id = "wait", Kind = NarrativeContent.StageKind.WaitAction,
                    Conditions = new[] { new[] {
                        new NarrativeCondition { Fact = EncounterContext.Fact.StoryFlag, Key = key } } },
                    Exits = new Dictionary<string, string> { ["Done"] = "end" } },
                new NarrativeContent.Stage { Id = "end", Kind = NarrativeContent.StageKind.End } });
        }

        private sealed class TableConfig : IConfigService
        {
            public global::cfg.Tables Tables { get; } = ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes());
            public ulong ContentHash => 0;
        }
    }
}
