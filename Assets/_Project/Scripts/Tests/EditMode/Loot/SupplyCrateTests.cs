// 职责：钉住物资箱作为统一交互对象（IInteractable）的规则——经 InteractionSelector 选焦点时半径内最近、半径外无焦点、
//   跳过已开 / 未激活 / 空项；没下发交互参数的箱子不可交互；提示是「打开 · 物资箱」；Interact 调下发的开箱回调、已开不再调。
// 为什么改（由原物资箱焦点类的测试 git mv 迁移而来）：物资箱焦点类随统一交互删除（PRP/interaction D8），选择改走 InteractionSelector，
//   原来的三条选择用例原样迁过来，补上 SupplyCrate 自己实现契约的部分；按「一个被测类一个测试类」改名。
using System.Collections.Generic;
using Game.Interaction;
using Game.Loot;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Loot
{
    public sealed class SupplyCrateTests
    {
        private const float Radius = 1.5f;

        private readonly List<GameObject> created = new List<GameObject>();
        private int collectCalls;

        [SetUp]
        public void SetUp()
        {
            collectCalls = 0;
        }

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
        public void Select_PicksClosestInsideRadius()
        {
            SupplyCrate far = CreateCrate(new Vector3(1.2f, 0f, 0f));
            SupplyCrate near = CreateCrate(new Vector3(0.5f, 0f, 0f));

            IInteractable result = InteractionSelector.Select(Vector3.zero, new IInteractable[] { far, near });

            Assert.That(result, Is.SameAs(near));
        }

        [Test]
        public void Select_OutsideRadius_ReturnsNull()
        {
            SupplyCrate crate = CreateCrate(new Vector3(3f, 0f, 0f));

            Assert.That(InteractionSelector.Select(Vector3.zero, new IInteractable[] { crate }), Is.Null);
        }

        [Test]
        public void Select_SkipsOpenedInactiveAndNull()
        {
            SupplyCrate opened = CreateCrate(new Vector3(0.2f, 0f, 0f));
            opened.SetOpened(true);
            SupplyCrate inactive = CreateCrate(new Vector3(0.3f, 0f, 0f));
            inactive.gameObject.SetActive(false);
            SupplyCrate valid = CreateCrate(new Vector3(1f, 0f, 0f));

            IInteractable result = InteractionSelector.Select(Vector3.zero, new IInteractable[] { null, opened, inactive, valid });

            Assert.That(result, Is.SameAs(valid));
        }

        [Test]
        public void CanInteract_WithoutBindInteraction_IsFalse()
        {
            var go = new GameObject("UnboundCrate");
            created.Add(go);
            SupplyCrate crate = go.AddComponent<SupplyCrate>();

            Assert.That(crate.CanInteract, Is.False, "没由 LootSceneBinder 下发开箱回调的箱子不参与焦点");
            Assert.That(InteractionSelector.Select(Vector3.zero, new IInteractable[] { crate }), Is.Null);
        }

        [Test]
        public void Prompt_IsOpenSupplyCrate()
        {
            SupplyCrate crate = CreateCrate(Vector3.zero);

            Assert.That(InteractPromptHudView.FormatLabel(crate.Prompt), Is.EqualTo("打开 · 物资箱"));
            Assert.That(crate.InteractionRadius, Is.EqualTo(Radius));
        }

        [Test]
        public void Interact_CallsCollectOnce_AndNotAfterOpened()
        {
            SupplyCrate crate = CreateCrate(Vector3.zero);

            crate.Interact();
            Assert.That(collectCalls, Is.EqualTo(1), "未开时调下发的开箱回调");
            Assert.That(crate.IsOpened, Is.True, "回调里开了箱（同 LootService.TryCollect 成功）");

            crate.Interact();
            Assert.That(collectCalls, Is.EqualTo(1), "已开不再调");
            Assert.That(crate.CanInteract, Is.False, "已开不可交互，焦点会离开它");
        }

        private SupplyCrate CreateCrate(Vector3 position)
        {
            var go = new GameObject("TestCrate");
            created.Add(go);
            go.transform.position = position;
            SupplyCrate crate = go.AddComponent<SupplyCrate>();
            crate.BindInteraction(Radius, new InteractionPrompt("打开", "物资箱"), Collect);
            return crate;
        }

        // 开箱回调替身：计数并把箱子切开（LootService.TryCollect 成功时也会 SetOpened(true)）。
        private bool Collect(SupplyCrate crate)
        {
            collectCalls++;
            crate.SetOpened(true);
            return true;
        }
    }
}
