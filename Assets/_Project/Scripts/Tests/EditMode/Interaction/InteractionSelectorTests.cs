// 职责：钉住统一交互选焦点纯函数 InteractionSelector.Select 的规则（PRP/interaction D2、D5）：
//   最近者得焦点、半径外跳过、半径 ≤ 0 不限距离、CanInteract=false 跳过、未激活 / 未启用 / 已销毁跳过、距离相等时按优先级打平。
// 为什么新建：Interaction 是新模块，按「一个被测类一个测试类」新建；纯函数可在 EditMode 直接覆盖。
using System.Collections.Generic;
using Game.Interaction;
using Game.Loot;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Interaction
{
    public sealed class InteractionSelectorTests
    {
        private readonly List<GameObject> created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < created.Count; i++)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
            created.Clear();
        }

        [Test]
        public void Select_PicksNearestInsideRadius()
        {
            var far = new FakeInteractable(new Vector3(2f, 0f, 0f), 3f);
            var near = new FakeInteractable(new Vector3(0f, 0f, 1f), 3f);

            Assert.That(InteractionSelector.Select(Vector3.zero, List(far, near)), Is.SameAs(near));
        }

        [Test]
        public void Select_UsesThreeDimensionalDistance()
        {
            // XZ 平面更近、但 Y 上拉开：按三维距离反而更远。
            var high = new FakeInteractable(new Vector3(0.5f, 3f, 0f), 0f);
            var flat = new FakeInteractable(new Vector3(1.5f, 0f, 0f), 0f);

            Assert.That(InteractionSelector.Select(Vector3.zero, List(high, flat)), Is.SameAs(flat));
        }

        [Test]
        public void Select_OutsideRadius_ReturnsNull()
        {
            var target = new FakeInteractable(new Vector3(3f, 0f, 0f), 1.5f);

            Assert.That(InteractionSelector.Select(Vector3.zero, List(target)), Is.Null);
        }

        [Test]
        public void Select_ExactlyOnRadius_IsInside()
        {
            var target = new FakeInteractable(new Vector3(1.5f, 0f, 0f), 1.5f);

            Assert.That(InteractionSelector.Select(Vector3.zero, List(target)), Is.SameAs(target));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        public void Select_RadiusZeroOrNegative_IsUnlimited(float radius)
        {
            var farAway = new FakeInteractable(new Vector3(500f, 0f, 0f), radius);

            Assert.That(InteractionSelector.Select(Vector3.zero, List(farAway)), Is.SameAs(farAway), "半径 ≤ 0 不限距离");
        }

        [Test]
        public void Select_CanInteractFalse_IsSkipped()
        {
            var blocked = new FakeInteractable(new Vector3(0.5f, 0f, 0f), 3f) { CanInteract = false };
            var open = new FakeInteractable(new Vector3(2f, 0f, 0f), 3f);

            Assert.That(InteractionSelector.Select(Vector3.zero, List(blocked, open)), Is.SameAs(open), "更近但不可交互的要跳过");
            Assert.That(InteractionSelector.Select(Vector3.zero, List(blocked)), Is.Null);
        }

        [Test]
        public void Select_InactiveDisabledDestroyedAndNull_AreSkipped()
        {
            SupplyCrate inactive = CreateCrate(new Vector3(0.2f, 0f, 0f));
            inactive.gameObject.SetActive(false);
            SupplyCrate disabled = CreateCrate(new Vector3(0.3f, 0f, 0f));
            disabled.enabled = false;
            SupplyCrate destroyed = CreateCrate(new Vector3(0.4f, 0f, 0f));
            Object.DestroyImmediate(destroyed.gameObject);
            SupplyCrate valid = CreateCrate(new Vector3(1f, 0f, 0f));

            IInteractable result = InteractionSelector.Select(Vector3.zero, List(null, inactive, disabled, destroyed, valid));

            Assert.That(result, Is.SameAs(valid));
        }

        [Test]
        public void Select_EqualDistance_HigherPriorityWins()
        {
            var low = new FakeInteractable(new Vector3(1f, 0f, 0f), 3f) { InteractionPriority = 0 };
            var high = new FakeInteractable(new Vector3(-1f, 0f, 0f), 3f) { InteractionPriority = 5 };

            Assert.That(InteractionSelector.Select(Vector3.zero, List(low, high)), Is.SameAs(high));
            Assert.That(InteractionSelector.Select(Vector3.zero, List(high, low)), Is.SameAs(high), "与列表顺序无关");
        }

        [Test]
        public void Select_EqualDistanceAndPriority_FirstInListWins()
        {
            var first = new FakeInteractable(new Vector3(1f, 0f, 0f), 3f);
            var second = new FakeInteractable(new Vector3(-1f, 0f, 0f), 3f);

            Assert.That(InteractionSelector.Select(Vector3.zero, List(first, second)), Is.SameAs(first));
        }

        [Test]
        public void Select_PriorityOnlyBreaksTies_CloserLowerPriorityStillWins()
        {
            var closeLow = new FakeInteractable(new Vector3(1f, 0f, 0f), 3f) { InteractionPriority = 0 };
            var farHigh = new FakeInteractable(new Vector3(2f, 0f, 0f), 3f) { InteractionPriority = 99 };

            Assert.That(InteractionSelector.Select(Vector3.zero, List(farHigh, closeLow)), Is.SameAs(closeLow), "D2：最近者得焦点，优先级只打平");
        }

        [Test]
        public void Select_NullOrEmptyCandidates_ReturnsNull()
        {
            Assert.That(InteractionSelector.Select(Vector3.zero, null), Is.Null);
            Assert.That(InteractionSelector.Select(Vector3.zero, new List<IInteractable>()), Is.Null);
        }

        private static List<IInteractable> List(params IInteractable[] items) => new List<IInteractable>(items);

        private SupplyCrate CreateCrate(Vector3 position)
        {
            var go = new GameObject("TestCrate");
            created.Add(go);
            go.transform.position = position;
            SupplyCrate crate = go.AddComponent<SupplyCrate>();
            crate.BindInteraction(3f, new InteractionPrompt("打开", "物资箱"), _ => true);
            return crate;
        }
    }
}
