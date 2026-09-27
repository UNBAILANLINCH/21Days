// 职责：钉住 DialogueInteractable 的范围判定（三维距离）、无对话树时的常驻台词轮换与可交互判定，
//   以及交互焦点的「选最近可交互者」纯选择逻辑（DialogueInteractionFocus.SelectNearest）、交互提示 HUD 的拼字符串（键位回退「E」、「对话 · 名字」），
//   头顶图标接管开关（MarkerOverridden）的往返，以及头顶标记三个显隐结果的纯判定（DialogueInteractableMarker.ResolveVisibility）
//   与图标世界锚点（DialogueInteractableMarker.TryGetIconAnchor）。
// 为什么新建：现有 Dialogue 测试各测一个类（Rules / Catalog / Policy / Service），都不涉及场景组件；
//   按「一个被测类一个测试类」新建。
using System.Collections.Generic;
using System.Reflection;
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
        public void MarkerOverridden_Default_IsFalse()
        {
            Assert.That(interactable.MarkerOverridden, Is.False, "未被接管时应为 false");
        }

        [Test]
        public void SetMarkerOverridden_TrueThenFalse_RoundTrips()
        {
            SetMarkerOverridden(interactable, true);
            Assert.That(interactable.MarkerOverridden, Is.True, "接管后应为 true");

            SetMarkerOverridden(interactable, false);
            Assert.That(interactable.MarkerOverridden, Is.False, "交还后应为 false");
        }

        [Test]
        public void SetMarkerOverridden_ThenDisable_KeepsValue()
        {
            SetMarkerOverridden(interactable, true);
            interactable.enabled = false;

            // 接管方只在引用变化时写一次，组件自己在 OnDisable 里清掉会失同步。
            Assert.That(interactable.MarkerOverridden, Is.True, "禁用组件不应清掉接管状态");
        }

        [Test]
        public void ResolveVisibility_NotOverridden_MatchesThreeStates()
        {
            AssertVisibility(canInteract: false, focused: false, speech: false, hidden: false, overridden: false,
                expectFocused: false, expectIdle: false, expectName: false, "不可交互时全隐");
            AssertVisibility(canInteract: true, focused: false, speech: false, hidden: false, overridden: false,
                expectFocused: false, expectIdle: true, expectName: false, "可交互未聚焦只显灰「…」");
            AssertVisibility(canInteract: true, focused: true, speech: false, hidden: false, overridden: false,
                expectFocused: true, expectIdle: false, expectName: true, "焦点显白「!」+ 名字");
        }

        [Test]
        public void ResolveVisibility_Overridden_HidesIconsButKeepsNameOnFocus()
        {
            AssertVisibility(canInteract: true, focused: false, speech: false, hidden: false, overridden: true,
                expectFocused: false, expectIdle: false, expectName: false, "接管时未聚焦：两张图都隐");
            AssertVisibility(canInteract: true, focused: true, speech: false, hidden: false, overridden: true,
                expectFocused: false, expectIdle: false, expectName: true, "接管时焦点：图隐、名字仍显示");
        }

        [Test]
        public void ResolveVisibility_SpeechShowingOrHiddenByHud_HidesAll()
        {
            AssertVisibility(canInteract: true, focused: true, speech: true, hidden: false, overridden: false,
                expectFocused: false, expectIdle: false, expectName: false, "气泡显示中焦点也全隐");
            AssertVisibility(canInteract: true, focused: false, speech: true, hidden: false, overridden: false,
                expectFocused: false, expectIdle: false, expectName: false, "气泡显示中未聚焦也全隐");
            AssertVisibility(canInteract: true, focused: true, speech: true, hidden: false, overridden: true,
                expectFocused: false, expectIdle: false, expectName: false, "气泡显示中且被接管：全隐");
            AssertVisibility(canInteract: true, focused: true, speech: false, hidden: true, overridden: false,
                expectFocused: false, expectIdle: false, expectName: false, "沉浸模式全隐");
        }

        [Test]
        public void TryGetIconAnchor_WithFocusedIcon_ReturnsFocusedPosition()
        {
            npc.transform.position = new Vector3(1f, 0f, 2f);
            SpriteRenderer idle = CreateIcon("MarkerIdle", new Vector3(0f, 2.15f, 0f));
            SpriteRenderer focused = CreateIcon("MarkerFocus", new Vector3(0.1f, 2.2f, 0f));
            focused.gameObject.SetActive(false);
            DialogueInteractableMarker marker = CreateMarker(idle, focused);

            Assert.That(marker.TryGetIconAnchor(out Vector3 anchor), Is.True);
            AssertNear(anchor, new Vector3(1.1f, 2.2f, 2f), "两张图都配时取焦点图的世界位置（隐藏也照取）");
        }

        [Test]
        public void TryGetIconAnchor_OnlyIdleIcon_ReturnsIdlePosition()
        {
            npc.transform.position = new Vector3(-1f, 0.5f, 0f);
            SpriteRenderer idle = CreateIcon("MarkerIdle", new Vector3(0f, 2.15f, 0f));
            DialogueInteractableMarker marker = CreateMarker(idle, null);

            Assert.That(marker.TryGetIconAnchor(out Vector3 anchor), Is.True);
            AssertNear(anchor, new Vector3(-1f, 2.65f, 0f), "只配灰「…」时取它的世界位置");
        }

        [Test]
        public void TryGetIconAnchor_NoIcons_ReturnsFalse()
        {
            DialogueInteractableMarker marker = CreateMarker(null, null);

            Assert.That(marker.TryGetIconAnchor(out Vector3 anchor), Is.False, "两张图都没配应返回 false");
            Assert.That(anchor, Is.EqualTo(Vector3.zero));
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

        [TestCase(null, "E")]
        [TestCase("", "E")]
        [TestCase("   ", "E")]
        [TestCase("F", "F")]
        [TestCase(" Q ", "Q")]
        public void HudKeyText_FallsBackToE_WhenBindingDisplayEmpty(string display, string expected)
        {
            Assert.That(DialogueInteractHudView.FormatKeyText(display), Is.EqualTo(expected));
        }

        [TestCase("长老", "对话 · 长老")]
        [TestCase(" 旅人 ", "对话 · 旅人")]
        [TestCase("", "对话")]
        [TestCase(null, "对话")]
        public void HudLabel_ShowsNpcName_OrVerbOnly(string npcName, string expected)
        {
            Assert.That(DialogueInteractHudView.FormatLabel(npcName), Is.EqualTo(expected));
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

        private static void AssertVisibility(bool canInteract, bool focused, bool speech, bool hidden, bool overridden,
            bool expectFocused, bool expectIdle, bool expectName, string because)
        {
            DialogueInteractableMarker.ResolveVisibility(canInteract, focused, speech, hidden, overridden,
                out bool iconFocused, out bool iconIdle, out bool nameShown);
            Assert.That(iconFocused, Is.EqualTo(expectFocused), because + "（白「!」）");
            Assert.That(iconIdle, Is.EqualTo(expectIdle), because + "（灰「…」）");
            Assert.That(nameShown, Is.EqualTo(expectName), because + "（名字）");
        }

        // 图标子物体挂在 npc 下，随 TearDown 一起销毁。
        private SpriteRenderer CreateIcon(string name, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(npc.transform, false);
            go.transform.localPosition = localPosition;
            return go.AddComponent<SpriteRenderer>();
        }

        private DialogueInteractableMarker CreateMarker(SpriteRenderer idle, SpriteRenderer focused)
        {
            var marker = npc.AddComponent<DialogueInteractableMarker>();
            var so = new SerializedObject(marker);
            so.FindProperty("target").objectReferenceValue = interactable;
            so.FindProperty("bubbleIdle").objectReferenceValue = idle;
            so.FindProperty("bubbleFocused").objectReferenceValue = focused;
            so.ApplyModifiedPropertiesWithoutUndo();
            return marker;
        }

        // SetMarkerOverridden 是 internal（只供同程序集的接管方调用），Game.Runtime 未对测试程序集开 InternalsVisibleTo，经反射调。
        private static void SetMarkerOverridden(DialogueInteractable target, bool overridden)
        {
            MethodInfo method = typeof(DialogueInteractable).GetMethod(
                "SetMarkerOverridden", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "找不到 DialogueInteractable.SetMarkerOverridden");
            method.Invoke(target, new object[] { overridden });
        }

        // Vector3 的 == 带 1e-5 容差；NUnit Is.EqualTo 走 Equals 逐位比较，浮点加法（如 0.8 + 0.8 + 0.3）会差最后一位。
        private static void AssertNear(Vector3 actual, Vector3 expected, string because)
        {
            Assert.That(actual == expected, Is.True, $"{because}：期望 {expected:F4}，实际 {actual:F4}");
        }
    }
}
