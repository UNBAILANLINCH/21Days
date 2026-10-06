// 职责：锁定战斗结果词汇表与 CompleteBattle 回写契约；生产内容校验不覆盖纯规则的身份匹配语义。
// 为什么新建：NarrativeRulesTests 覆盖的是等待/多部分行为与条件环路，战场回写是 roadmap C5 的另一条契约。
using System.Collections.Generic;
using Game.Narrative;
using NUnit.Framework;

namespace Game.Tests.EditMode.Narrative
{
    public sealed class NarrativeBattleTests
    {
        /// <summary>单场战斗 + 三个终局；转阶段自环与终局出口都在这一个阶段上。</summary>
        private static NarrativeContent BattleContent()
        {
            var exits = new Dictionary<string, string>
            {
                ["Downed"] = "downed", ["Exposed"] = "exposed",
                ["BossPhaseChanged:form2"] = "fight", ["Victory"] = "cleared",
            };
            return new NarrativeContent("battle", "fight", new[]
            {
                new NarrativeContent.Stage { Id = "fight", Kind = NarrativeContent.StageKind.Battle,
                    BattleResults = new[] { "Downed", "Exposed", "BossPhaseChanged:form2", "Victory" }, Exits = exits },
                new NarrativeContent.Stage { Id = "downed", Kind = NarrativeContent.StageKind.End, Outcome = "Downed" },
                new NarrativeContent.Stage { Id = "exposed", Kind = NarrativeContent.StageKind.End, Outcome = "Exposed" },
                new NarrativeContent.Stage { Id = "cleared", Kind = NarrativeContent.StageKind.End, Outcome = "Success" },
            });
        }

        /// <summary>只声明 Victory 的战斗；用于验证未声明的结果码被安静拒绝。</summary>
        private static NarrativeContent VictoryOnlyContent()
        {
            return new NarrativeContent("battle", "fight", new[]
            {
                new NarrativeContent.Stage { Id = "fight", Kind = NarrativeContent.StageKind.Battle,
                    BattleResults = new[] { "Victory" },
                    Exits = new Dictionary<string, string> { ["Victory"] = "cleared" } },
                new NarrativeContent.Stage { Id = "cleared", Kind = NarrativeContent.StageKind.End, Outcome = "Success" },
            });
        }

        [Test]
        public void BattleStage_WithTripleResults_EntersContentTable()
        {
            NarrativeContent content = BattleContent();
            Assert.That(content.Get("fight").Kind, Is.EqualTo(NarrativeContent.StageKind.Battle));
            Assert.That(content.Get("fight").BattleResults, Is.EqualTo(new[] { "Downed", "Exposed", "BossPhaseChanged:form2", "Victory" }));
            Assert.That(content.Get("fight").Exits["BossPhaseChanged:form2"], Is.EqualTo("fight"));
        }

        [TestCase("Downed", "downed")]
        [TestCase("Exposed", "exposed")]
        [TestCase("BossPhaseChanged:form2", "fight")]
        [TestCase("Victory", "cleared")]
        public void CompleteBattle_DownedExposedBossPhaseOrVictory_AdvancesByItsOwnExit(string resultKey, string expectedNext)
        {
            var rules = new NarrativeRules(new[] { BattleContent() }, null);
            rules.Start("battle", "boss");
            Assert.That(rules.CanCompleteBattle(rules.Generation, rules.Current.ActivationId, "boss"), Is.True);
            Assert.That(rules.CompleteBattle(rules.Generation, rules.Current.ActivationId, "boss", resultKey), Is.True);
            rules.ResolveAutomatic(new EncounterContext("boss", "monster", true, false, false, true, false, false));
            // 三个终局出口都落到 End：ResolveAutomatic 走完后 Current 为空、Outcome 记该 End 阶段自己的 Outcome；
            // 换形态自环则停在同一个战斗阶段。断言同时报出战斗结果键，失败时能一眼看出是哪条。
            if (expectedNext == "fight") Assert.That(rules.Current.StageId, Is.EqualTo("fight"), resultKey);
            else
            {
                Assert.That(rules.Current, Is.Null, resultKey);
                string expectedOutcome = resultKey == "Victory" ? "Success" : resultKey;
                Assert.That(rules.Outcome, Is.EqualTo(expectedOutcome), resultKey);
            }
        }

        [Test]
        public void CompleteBattle_BossPhaseChangedExitsToItsOwnStage_KeepsBattleRunning()
        {
            var rules = new NarrativeRules(new[] { BattleContent() }, null);
            rules.Start("battle", "boss");
            long activation = rules.Current.ActivationId;
            Assert.That(rules.CompleteBattle(rules.Generation, activation, "boss", "BossPhaseChanged:form2"), Is.True);
            Assert.That(rules.Stage.Kind, Is.EqualTo(NarrativeContent.StageKind.Battle));
            Assert.That(rules.Current.StageId, Is.EqualTo("fight"));
            Assert.That(rules.Current.ActivationId, Is.EqualTo(activation + 1));
            Assert.That(rules.CompleteBattle(rules.Generation, rules.Current.ActivationId, "boss", "Victory"), Is.True);
            Assert.That(rules.Current.StageId, Is.EqualTo("cleared"));
        }

        [Test]
        public void CompleteBattle_AfterRestore_OldResultRejected()
        {
            var rules = new NarrativeRules(new[] { VictoryOnlyContent() }, null);
            rules.Start("battle", "boss");
            long generation = rules.Generation;
            long activation = rules.Current.ActivationId;
            rules.Restore(rules.Capture());
            Assert.That(rules.CompleteBattle(generation, activation, "boss", "Victory"), Is.False);
            Assert.That(rules.Current.StageId, Is.EqualTo("fight"));
        }

        [Test]
        public void CompleteBattle_ResultNotDeclaredByStage_RejectsWithoutThrowing()
        {
            var rules = new NarrativeRules(new[] { VictoryOnlyContent() }, null);
            rules.Start("battle", "boss");
            long generation = rules.Generation;
            long activation = rules.Current.ActivationId;
            Assert.That(rules.CompleteBattle(generation, activation, "boss", "Exposed"), Is.False);
            Assert.That(rules.CompleteBattle(generation, activation, "boss", "not-a-result"), Is.False);
            Assert.That(rules.Current.StageId, Is.EqualTo("fight"));
        }

        [Test]
        public void CompleteBattle_WrongGenerationActivationOrTarget_Rejects()
        {
            var rules = new NarrativeRules(new[] { VictoryOnlyContent() }, null);
            rules.Start("battle", "boss");
            long generation = rules.Generation;
            long activation = rules.Current.ActivationId;
            Assert.That(rules.CompleteBattle(generation + 1, activation, "boss", "Victory"), Is.False);
            Assert.That(rules.CompleteBattle(generation, activation + 1, "boss", "Victory"), Is.False);
            Assert.That(rules.CompleteBattle(generation, activation, "other", "Victory"), Is.False);
            Assert.That(rules.Current.StageId, Is.EqualTo("fight"));
        }

        [Test]
        public void CompleteBattle_BossPhaseChangedSelfExit_AppliesPhaseFlag()
        {
            // 换形态靠「自环 + 进入阶段即写标记」表达（字典 §4.5 combat.phase.<n>），不要另起一个战斗阶段。
            var fight = new NarrativeContent.Stage { Id = "fight", Kind = NarrativeContent.StageKind.Battle,
                BattleResults = new[] { "BossPhaseChanged:form2", "Victory" }, SetFlags = new[] { "combat.phase.2" },
                Exits = new Dictionary<string, string> { ["BossPhaseChanged:form2"] = "fight", ["Victory"] = "cleared" } };
            var rules = new NarrativeRules(new[]
            {
                new NarrativeContent("battle", "fight", new[]
                {
                    fight,
                    new NarrativeContent.Stage { Id = "cleared", Kind = NarrativeContent.StageKind.End },
                }),
            }, null);
            rules.Start("battle", "boss");
            Assert.That(rules.CompleteBattle(rules.Generation, rules.Current.ActivationId, "boss", "BossPhaseChanged:form2"), Is.True);
            Assert.That(rules.StoryFlags, Does.Contain("combat.phase.2"));
            Assert.That(rules.Current.StageId, Is.EqualTo("fight"));
        }

        [Test]
        public void CanCompleteBattle_NonBattleStage_ReturnsFalse()
        {
            var rules = new NarrativeRules(new[]
            {
                new NarrativeContent("battle", "wait", new[]
                {
                    new NarrativeContent.Stage { Id = "wait", Kind = NarrativeContent.StageKind.WaitAction,
                        Exits = new Dictionary<string, string> { ["Success"] = "end" } },
                    new NarrativeContent.Stage { Id = "end", Kind = NarrativeContent.StageKind.End },
                }),
            }, null);
            rules.Start("battle", "boss");
            Assert.That(rules.CanCompleteBattle(rules.Generation, rules.Current.ActivationId, "boss"), Is.False);
            Assert.That(rules.CompleteBattle(rules.Generation, rules.Current.ActivationId, "boss", "Victory"), Is.False);
        }

        [TestCase("Downed", "Downed")]
        [TestCase("Exposed", "Exposed")]
        [TestCase("BossPhaseChanged:form2", "BossPhaseChanged:form2")]
        [TestCase("Victory", "Victory")]
        public void BattleResult_Parse_KnownClassRoundTripsThroughExitKey(string key, string exitKey)
        {
            BattleResult result = BattleResult.Parse(key);
            Assert.That(result.ExitKey, Is.EqualTo(exitKey));
            Assert.That(result.ToString(), Is.EqualTo(exitKey));
        }

        [TestCase("downed")]
        [TestCase("Victory:form2")]
        [TestCase("BossPhaseChanged")]
        [TestCase("BossPhaseChanged:")]
        [TestCase("Victory:")]
        [TestCase("")]
        public void BattleResult_Parse_UnknownOrMalformedKey_Throws(string key)
        {
            Assert.Throws<System.ArgumentException>(() => BattleResult.Parse(key));
            Assert.That(BattleResult.TryParse(key, out _), Is.False);
        }

        [Test]
        public void BattleResult_Constructor_KindMismatch_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => new BattleResult("Victory", "form2"));
            Assert.Throws<System.ArgumentException>(() => new BattleResult("BossPhaseChanged"));
            Assert.Throws<System.ArgumentException>(() => new BattleResult("Nope"));
        }

        /// <summary>内容级规则只在 <see cref="NarrativeCatalog.Validate"/> 一处抛，负对照必须走它。</summary>
        private static void ValidateContent(NarrativeContent content) =>
            NarrativeCatalog.Validate(new[] { content }, System.Array.Empty<EncounterRules.Rule>(), null);

        [Test]
        public void BattleStage_WithNoDeclaredResult_RejectsContent()
        {
            var stage = new NarrativeContent.Stage
            {
                Id = "fight", Kind = NarrativeContent.StageKind.Battle,
                Exits = new Dictionary<string, string> { ["Victory"] = "cleared" },
            };
            var error = Assert.Throws<System.ArgumentException>(() => ValidateContent(new NarrativeContent("battle", "fight", new[]
            {
                stage,
                new NarrativeContent.Stage { Id = "cleared", Kind = NarrativeContent.StageKind.End },
            })));
            Assert.That(error.Message, Does.Contain("战斗阶段必须声明至少一个战斗结果"));
        }

        [Test]
        public void BattleStage_WithUnknownResultCode_RejectsContent()
        {
            var stage = new NarrativeContent.Stage
            {
                Id = "fight", Kind = NarrativeContent.StageKind.Battle, BattleResults = new[] { "Win" },
                Exits = new Dictionary<string, string> { ["Win"] = "cleared" },
            };
            var error = Assert.Throws<System.ArgumentException>(() => ValidateContent(new NarrativeContent("battle", "fight", new[]
            {
                stage,
                new NarrativeContent.Stage { Id = "cleared", Kind = NarrativeContent.StageKind.End },
            })));
            Assert.That(error.Message, Does.Contain("战斗结果非法"));
        }

        [Test]
        public void BattleStage_WithDeclaredResultButNoMatchingExit_RejectsContent()
        {
            var stage = new NarrativeContent.Stage
            {
                Id = "fight", Kind = NarrativeContent.StageKind.Battle, BattleResults = new[] { "Exposed" },
                Exits = new Dictionary<string, string> { ["Victory"] = "cleared" },
            };
            var error = Assert.Throws<System.ArgumentException>(() => ValidateContent(new NarrativeContent("battle", "fight", new[]
            {
                stage,
                new NarrativeContent.Stage { Id = "cleared", Kind = NarrativeContent.StageKind.End },
            })));
            Assert.That(error.Message, Does.Contain("战斗结果缺少出口"));
        }

        [Test]
        public void BattleStage_WithDuplicateResult_RejectsContent()
        {
            var stage = new NarrativeContent.Stage
            {
                Id = "fight", Kind = NarrativeContent.StageKind.Battle,
                BattleResults = new[] { "Victory", "Victory" },
                Exits = new Dictionary<string, string> { ["Victory"] = "cleared" },
            };
            var error = Assert.Throws<System.ArgumentException>(() => ValidateContent(new NarrativeContent("battle", "fight", new[]
            {
                stage,
                new NarrativeContent.Stage { Id = "cleared", Kind = NarrativeContent.StageKind.End },
            })));
            Assert.That(error.Message, Does.Contain("战斗结果重复"));
        }

        [Test]
        public void NonBattleStage_DeclaringBattleResults_RejectsContent()
        {
            var stage = new NarrativeContent.Stage
            {
                Id = "wait", Kind = NarrativeContent.StageKind.WaitAction, BattleResults = new[] { "Victory" },
                Exits = new Dictionary<string, string> { ["Victory"] = "cleared" },
            };
            var error = Assert.Throws<System.ArgumentException>(() => ValidateContent(new NarrativeContent("battle", "wait", new[]
            {
                stage,
                new NarrativeContent.Stage { Id = "cleared", Kind = NarrativeContent.StageKind.End },
            })));
            Assert.That(error.Message, Does.Contain("只有战斗阶段可以声明战斗结果"));
        }
    }
}
