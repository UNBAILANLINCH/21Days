// 职责：钉住效果执行口的注册表——按标识分发、查不到回「没有执行者」（不静默）、重复注册与空标识在装配期就炸。
// 为什么新建：一个被测类一个测试类；这里是「效果标识 → 执行者」的唯一分发点，
//   将来接皮 / 面具效果时新写的执行者能不能被认出来，全靠这几条。
using System;
using Game.Inventory;
using NUnit.Framework;

namespace Game.Tests.EditMode.Inventory
{
    public sealed class ItemEffectRegistryTests
    {
        private const string Heal = "heal.player_hp";
        private const string NotImplemented = "disguise.blank_skin";

        [Test]
        public void Execute_WhenTheEffectIsRegistered_ReturnsWhatTheExecutorSaid()
        {
            var registry = new ItemEffectRegistry();
            registry.Register(new RecordingEffect(Heal, ItemEffectOutcome.Applied(7)));

            ItemEffectOutcome outcome = registry.Execute(Heal, new ItemEffectContext(1004, Heal, null));

            Assert.That(outcome.Status, Is.EqualTo(ItemEffectStatus.Applied));
            Assert.That(outcome.Amount, Is.EqualTo(7));
            Assert.That(registry.Count, Is.EqualTo(1));
            Assert.That(registry.Has(Heal), Is.True);
        }

        // 防静默失败的核心一条：查不到执行者必须回一个**带中文原因**的明确失败。
        [Test]
        public void Execute_WhenNoExecutorIsRegistered_ReturnsNoExecutorWithAReason()
        {
            var registry = new ItemEffectRegistry();
            registry.Register(new RecordingEffect(Heal, ItemEffectOutcome.Applied(1)));

            ItemEffectOutcome outcome = registry.Execute(NotImplemented, new ItemEffectContext(1103, NotImplemented, null));

            Assert.That(outcome.Status, Is.EqualTo(ItemEffectStatus.NoExecutor));
            Assert.That(outcome.IsApplied, Is.False);
            Assert.That(outcome.Reason, Is.Not.Empty, "给玩家看的短句不能是空串");
            Assert.That(registry.Has(NotImplemented), Is.False);
        }

        [TestCase(null)]
        [TestCase("")]
        public void Execute_WithEmptyEffectId_ReturnsNoExecutor(string effectId)
        {
            var registry = new ItemEffectRegistry();

            Assert.That(registry.Execute(effectId, new ItemEffectContext(1, effectId, null)).Status,
                Is.EqualTo(ItemEffectStatus.NoExecutor));
        }

        [Test]
        public void Execute_WithNullContext_ReturnsBadTarget()
        {
            var registry = new ItemEffectRegistry();
            registry.Register(new RecordingEffect(Heal, ItemEffectOutcome.Applied(1)));

            ItemEffectOutcome outcome = registry.Execute(Heal, null);

            Assert.That(outcome.Status, Is.EqualTo(ItemEffectStatus.BadTarget));
            Assert.That(outcome.Reason, Is.Not.Empty);
        }

        [Test]
        public void Execute_PassesTheContextThrough()
        {
            var registry = new ItemEffectRegistry();
            var effect = new RecordingEffect(Heal, ItemEffectOutcome.Applied(1));
            registry.Register(effect);
            var context = new ItemEffectContext(1004, Heal, null);

            registry.Execute(Heal, context);

            Assert.That(effect.LastContext, Is.SameAs(context));
        }

        [Test]
        public void Register_WhenTheSameEffectIdIsRegisteredTwice_Throws()
        {
            var registry = new ItemEffectRegistry();
            registry.Register(new RecordingEffect(Heal, ItemEffectOutcome.Applied(1)));

            Assert.Throws<ArgumentException>(() => registry.Register(new RecordingEffect(Heal, ItemEffectOutcome.Applied(1))));
        }

        [Test]
        public void Register_WithNullOrEmptyEffectId_Throws()
        {
            var registry = new ItemEffectRegistry();

            Assert.Throws<ArgumentNullException>(() => registry.Register(null));
            Assert.Throws<ArgumentException>(() => registry.Register(new RecordingEffect(string.Empty, ItemEffectOutcome.Applied(1))));
        }

        /// <summary>只记下被调用时的上下文，回一个预设结果。</summary>
        private sealed class RecordingEffect : IItemEffect
        {
            private readonly ItemEffectOutcome result;

            public RecordingEffect(string effectId, ItemEffectOutcome result)
            {
                EffectId = effectId;
                this.result = result;
            }

            public string EffectId { get; }

            public ItemEffectContext LastContext { get; private set; }

            public ItemEffectOutcome Apply(ItemEffectContext context)
            {
                LastContext = context;
                return result;
            }
        }
    }
}
