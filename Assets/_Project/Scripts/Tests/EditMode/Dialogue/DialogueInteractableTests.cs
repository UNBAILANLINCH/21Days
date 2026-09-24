// 职责：钉住 DialogueInteractable 的范围判定（三维距离）、无对话树时的常驻台词轮换与可交互判定，
//   以及交互焦点的「选最近可交互者」纯选择逻辑（DialogueInteractionFocus.SelectNearest）。
// 为什么新建：现有 Dialogue 测试各测一个类（Rules / Catalog / Policy / Service），都不涉及场景组件；
//   按「一个被测类一个测试类」新建。
using System.Collections.Generic;
using Game.Dialogue;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Dialogue
{
    public sealed class DialogueInteractableTests
    {
        private GameObject npc;
        private GameObject actor;
        private DialogueInteractable interactable;

        [SetUp]
        public void SetUp()
        {
            npc = new GameObject("Npc");
            actor = new GameObject("Actor");
            interactable = npc.AddComponent<DialogueInteractable>();
            var so = new SerializedObject(interactable);
            so.FindProperty("actor").objectReferenceValue = actor.transform;
            so.FindProperty("interactRadius").floatValue = 3.5f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(npc);
            Object.DestroyImmediate(actor);
        }

        [Test]
        public void InRange_UsesThreeDimensionalDistance()
        {
            npc.transform.position = new Vector3(0f, 4.9f, 3.4f);

            // XY 相同、只在 Z（等距场景的纵深）上拉开 5：旧的 XY 平面算法会误判为在范围内。
            actor.transform.position = new Vector3(0f, 4.9f, 8.4f);
            Assert.IsFalse(interactable.InRange, "Z 方向超出半径应判为范围外");

            // XZ 平面上相距 3（勾股 3-0-0），在 3.5 内。
            actor.transform.position = new Vector3(0f, 4.9f, 6.4f);
            Assert.IsTrue(interactable.InRange, "三维距离 3 应在半径 3.5 内");

            // 边界：恰好 3.5 视为在范围内。
            actor.transform.position = new Vector3(3.5f, 4.9f, 3.4f);
            Assert.IsTrue(interactable.InRange, "恰在半径上应判为范围内");

            // 未绑定服务时不可交互，即使在范围内。
            Assert.IsFalse(interactable.CanInteract, "未绑定 DialogueService 时 CanInteract 应为 false");
        }

        [Test]
        public void Interact_WithoutTreeButBubbleLines_RaisesLinesInOrderAndLoops()
        {
            actor.transform.position = new Vector3(1f, 0f, 0f);
            SetBubble(interactable, 0, "第一句", "第二句");
            var received = new List<string>();
            interactable.OnBubbleRequested += received.Add;

            Assert.That(interactable.CanInteract, Is.True, "无树有台词、在范围内应可交互（不需要服务）");
            interactable.Interact();
            interactable.Interact();
            interactable.Interact();

            Assert.That(received, Is.EqualTo(new[] { "第一句", "第二句", "第一句" }));
        }

        [Test]
        public void CanInteract_WithTreeButUnbound_IsFalse()
        {
            actor.transform.position = new Vector3(1f, 0f, 0f);
            SetBubble(interactable, 1001, "有树时台词不生效");

            Assert.That(interactable.HasTree, Is.True);
            Assert.That(interactable.CanInteract, Is.False, "有对话树但未绑定服务时不可交互");
        }

        [Test]
        public void CanInteract_WithoutTreeAndWithoutLines_IsFalse()
        {
            actor.transform.position = new Vector3(1f, 0f, 0f);
            SetBubble(interactable, 0);

            Assert.That(interactable.CanInteract, Is.False, "既无树也无台词时不可交互");
        }

        [Test]
        public void SelectNearest_PicksClosestInteractable_SkippingOutOfRange()
        {
            var far = new GameObject("Far");
            var near = new GameObject("Near");
            try
            {
                // 当前 npc 放到半径外：不可交互，即使更近也不能选。
                SetBubble(interactable, 0, "npc");
                npc.transform.position = new Vector3(0f, 0f, 10f);
                actor.transform.position = new Vector3(0f, 0f, 5f);

                DialogueInteractable farOne = CreateBubbleNpc(far, new Vector3(0f, 0f, 3f), actor.transform);
                DialogueInteractable nearOne = CreateBubbleNpc(near, new Vector3(0f, 0f, 4f), actor.transform);
                var candidates = new List<DialogueInteractable> { interactable, farOne, nearOne };

                Assert.That(DialogueInteractionFocus.SelectNearest(actor.transform.position, candidates), Is.SameAs(nearOne));

                actor.transform.position = new Vector3(0f, 0f, 20f);
                Assert.That(DialogueInteractionFocus.SelectNearest(actor.transform.position, candidates), Is.Null,
                    "全部超出半径时无焦点");
            }
            finally
            {
                Object.DestroyImmediate(far);
                Object.DestroyImmediate(near);
            }
        }

        private static DialogueInteractable CreateBubbleNpc(GameObject go, Vector3 position, Transform rangeActor)
        {
            go.transform.position = position;
            var component = go.AddComponent<DialogueInteractable>();
            var so = new SerializedObject(component);
            so.FindProperty("actor").objectReferenceValue = rangeActor;
            so.FindProperty("interactRadius").floatValue = 3.5f;
            so.ApplyModifiedPropertiesWithoutUndo();
            SetBubble(component, 0, go.name);
            return component;
        }

        private static void SetBubble(DialogueInteractable target, int dialogueId, params string[] lines)
        {
            var so = new SerializedObject(target);
            so.FindProperty("dialogueId").intValue = dialogueId;
            SerializedProperty array = so.FindProperty("bubbleLines");
            array.arraySize = lines.Length;
            for (int i = 0; i < lines.Length; i++)
            {
                array.GetArrayElementAtIndex(i).stringValue = lines[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
