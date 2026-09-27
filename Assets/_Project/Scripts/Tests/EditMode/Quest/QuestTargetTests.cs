// 职责：钉住 QuestTarget 值类型的三个成员——NPC 目标带交互组件（任务标记据此接管其头顶图标）、地点目标交互组件为 null。
// 为什么新建：现有 Quest 测试各测一个类（Rules / Content / GuidanceMath / HudPresenter ……），没有覆盖 QuestTarget 的；
//   按「一个被测类一个测试类」新建。QuestSceneBinder.TryResolveTarget 依赖场景与容器，这里测结构体本身，
//   以及决定 QuestTarget.Anchor 的纯函数 QuestSceneBinder.ResolveNpcAnchor（NPC 头顶锚点三级规则）。
using Game.Dialogue;
using Game.Quest;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Quest
{
    public sealed class QuestTargetTests
    {
        private GameObject npc;

        [SetUp]
        public void SetUp()
        {
            npc = new GameObject("Npc");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(npc);
        }

        [Test]
        public void Constructor_WithInteractable_KeepsAllMembers()
        {
            DialogueInteractable interactable = npc.AddComponent<DialogueInteractable>();
            var position = new Vector3(1f, 0f, 2f);
            var anchor = new Vector3(1f, 2.3f, 2f);

            var target = new QuestTarget(position, anchor, interactable);

            Assert.That(target.Position, Is.EqualTo(position));
            Assert.That(target.Anchor, Is.EqualTo(anchor));
            Assert.That(target.Interactable, Is.SameAs(interactable), "NPC 目标应带上其交互组件");
        }

        [Test]
        public void Constructor_LocationTarget_InteractableIsNull()
        {
            var target = new QuestTarget(Vector3.zero, Vector3.up * 1.5f);

            Assert.That(target.Interactable == null, Is.True, "地点目标不对应任何 NPC，交互组件应为 null");
        }

        [Test]
        public void Default_InteractableIsNull()
        {
            QuestTarget target = default;

            Assert.That(target.Interactable == null, Is.True, "default 值（解析失败的 out 参数）交互组件应为 null");
        }

        private static readonly Vector3 NpcPosition = new Vector3(1f, 0f, 2f);
        private static readonly Bounds NpcBounds = new Bounds(new Vector3(1.2f, 0.8f, 2.1f), new Vector3(1f, 1.6f, 0.5f));

        [Test]
        public void ResolveNpcAnchor_WithIconAnchor_UsesIconEvenIfBoundsPresent()
        {
            var icon = new Vector3(1f, 2.15f, 2f);

            Vector3 anchor = QuestSceneBinder.ResolveNpcAnchor(NpcPosition, true, icon, true, NpcBounds, 0.3f, 1.5f);

            AssertNear(anchor, icon, "有对话图标锚点时应与图标重合，不再按碰撞体顶部算");
        }

        [Test]
        public void ResolveNpcAnchor_NoIconWithBounds_UsesBoundsTopPlusLift()
        {
            Vector3 anchor = QuestSceneBinder.ResolveNpcAnchor(NpcPosition, false, new Vector3(9f, 9f, 9f), true, NpcBounds, 0.3f, 1.5f);

            AssertNear(anchor, new Vector3(1.2f, 1.9f, 2.1f), "无图标时取包围盒顶部 1.6 + 抬升 0.3，x/z 取包围盒中心");
        }

        [Test]
        public void ResolveNpcAnchor_NoIconNoBounds_UsesPositionPlusHeight()
        {
            Vector3 anchor = QuestSceneBinder.ResolveNpcAnchor(NpcPosition, false, default, false, default, 0.3f, 1.5f);

            AssertNear(anchor, new Vector3(1f, 1.5f, 2f), "都没有时退回位置上方固定高度");
        }

        // Vector3 的 == 带 1e-5 容差；NUnit Is.EqualTo 走 Equals 逐位比较，浮点加法（如 0.8 + 0.8 + 0.3）会差最后一位。
        private static void AssertNear(Vector3 actual, Vector3 expected, string because)
        {
            Assert.That(actual == expected, Is.True, $"{because}：期望 {expected:F4}，实际 {actual:F4}");
        }
    }
}
