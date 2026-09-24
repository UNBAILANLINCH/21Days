// 职责：内容检查——用真实生成的对话表 .bytes 钉住 DialogueCatalog 的翻译结果与验证内容（1001 / 1002 / 角色立绘地址）。
// 为什么新建：DialogueCatalog 是新类；同目录的 DialogueRulesTests 测的是对白状态机，职责不同，塞进去说不通。
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Config;
using Game.Dialogue;
using Game.Narrative;
using Game.Tests.EditMode.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode.Dialogue
{
    /// <summary>
    /// <see cref="DialogueCatalog"/> 的测试。数据直接读 <c>Assets/_Project/Data/Config/*.bytes</c>（复用 ConfigServiceTests.ReadAllTableBytes），不经 Addressables。
    /// 内容（节点数、入口立绘）改了要同步改这里；源数据在 <c>Tables/Data/dialogue/</c>。
    /// </summary>
    public sealed class DialogueCatalogTests
    {
        private DialogueCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            catalog = new DialogueCatalog(new FakeConfigService(ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes())));
        }

        [Test]
        public void TryGet_WhenIdIs1001_ReturnsTreeWithNineNodes()
        {
            Assert.That(catalog.TryGet(1001, out DialogueContent content), Is.True);
            Assert.That(content.Nodes.Count(), Is.EqualTo(9));
        }

        [Test]
        public void TryGet_WhenIdIs1002_ReturnsTreeWithThreeNodes()
        {
            Assert.That(catalog.TryGet(1002, out DialogueContent content), Is.True);
            Assert.That(content.Nodes.Count(), Is.EqualTo(3));
        }

        [Test]
        public void TryGet_WhenIdIsUnknown_ReturnsFalse()
        {
            Assert.That(catalog.TryGet(9999, out DialogueContent content), Is.False);
            Assert.That(content, Is.Null);
        }

        [Test]
        public void TryGet_1001Entry_ShowsElderAngryOnLeftAndClearsRight()
        {
            catalog.TryGet(1001, out DialogueContent content);
            DialogueContent.Node entry = content.Get(content.Entry);

            Assert.That(entry.SpeakerId, Is.EqualTo("elder"));
            Assert.That(entry.SpeakerName, Is.EqualTo("老者"), "speakerName 为空时取角色显示名");
            Assert.That(entry.Portraits.Length, Is.EqualTo(2));

            DialogueContent.Portrait show = entry.Portraits[0];
            Assert.That(show.Slot, Is.EqualTo(0));
            Assert.That(show.Action, Is.EqualTo(DialogueContent.PortraitAction.Show));
            Assert.That(show.CharacterId, Is.EqualTo("elder"));
            Assert.That(show.ExpressionId, Is.EqualTo("angry"));

            DialogueContent.Portrait clear = entry.Portraits[1];
            Assert.That(clear.Slot, Is.EqualTo(1));
            Assert.That(clear.Action, Is.EqualTo(DialogueContent.PortraitAction.Clear));
        }

        [Test]
        public void TryGet_1001SecondLine_UsesTravelerDefaultExpressionOnRight()
        {
            catalog.TryGet(1001, out DialogueContent content);
            DialogueContent.Node second = content.Get(content.Get(content.Entry).Next);

            Assert.That(second.Portraits.Length, Is.EqualTo(1), "clearOther=false 不产生 Clear");
            Assert.That(second.Portraits[0].Slot, Is.EqualTo(1));
            Assert.That(second.Portraits[0].CharacterId, Is.EqualTo("traveler"));
            Assert.That(second.Portraits[0].ExpressionId, Is.EqualTo("default"), "表情为空取角色默认表情");
        }

        [Test]
        public void Characters_EveryExpression_HasPortraitAddress()
        {
            IReadOnlyList<DialogueCharacter> characters = catalog.Characters;
            Assert.That(characters.Select(c => c.Id), Is.EquivalentTo(new[] { "elder", "traveler" }));

            foreach (DialogueCharacter character in characters)
            {
                Assert.That(character.SpriteKeys, Is.Not.Empty, character.Id);
                foreach (string key in character.SpriteKeys)
                {
                    Assert.That(key, Does.StartWith("Dialogue/Portrait_" + character.Id + "_"), character.Id);
                }
            }
        }

        [Test]
        public void TryGet_1001ThirdChoice_RequiresKnowsElderFlag()
        {
            catalog.TryGet(1001, out DialogueContent content);
            DialogueContent.Node choiceNode = content.Nodes.Single(n => n.Kind == DialogueContent.NodeKind.Choice);
            Assert.That(choiceNode.Choices.Length, Is.EqualTo(3));

            DialogueContent.Choice third = choiceNode.Choices[2];
            Assert.That(third.HideWhenUnavailable, Is.True);

            var stranger = new EncounterContext("elder", "npc", true, false, false, true, false, false);
            var acquaintance = new EncounterContext("elder", "npc", true, false, false, true, false, false,
                new[] { "knows_elder" });

            Assert.That(NarrativeCondition.Matches(third.Conditions, stranger), Is.False);
            Assert.That(NarrativeCondition.Matches(third.Conditions, acquaintance), Is.True);
        }

        [Test]
        public void TryGet_1001Choices_CarryIconKeysAndRememberHasNone()
        {
            catalog.TryGet(1001, out DialogueContent content);
            DialogueContent.Choice[] choices = content.Nodes.Single(n => n.Kind == DialogueContent.NodeKind.Choice).Choices;

            Assert.That(choices[0].Id, Is.EqualTo("accept"));
            Assert.That(choices[0].IconKey, Is.EqualTo("Dialogue/ChoiceIcon_Go"));
            Assert.That(choices[1].Id, Is.EqualTo("refuse"));
            Assert.That(choices[1].IconKey, Is.EqualTo("Dialogue/ChoiceIcon_Leave"));
            Assert.That(choices[2].Id, Is.EqualTo("remember"));
            Assert.That(choices[2].IconKey, Is.Empty, "表里空串 = 无图标");
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
