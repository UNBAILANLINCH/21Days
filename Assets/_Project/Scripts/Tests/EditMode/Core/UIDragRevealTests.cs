// 职责：下拉距离、删除区默认隐藏、反向收起及停用复位；独立拖拽组件不能由 UIPointerHold 测试覆盖。
using System.Reflection;
using Game.Core.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Tests.EditMode.Core
{
    public sealed class UIDragRevealTests
    {
        private GameObject root;
        private GameObject events;
        private UIDragReveal handle;
        private CanvasGroup reveal;
        private RectTransform paper;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Row", typeof(RectTransform));
            paper = new GameObject("Paper", typeof(RectTransform)).GetComponent<RectTransform>();
            paper.SetParent(root.transform, false);
            reveal = new GameObject("Delete", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<CanvasGroup>();
            reveal.transform.SetParent(root.transform, false);
            var grab = new GameObject("Grab", typeof(RectTransform));
            grab.transform.SetParent(paper, false);
            handle = grab.AddComponent<UIDragReveal>();
            typeof(UIDragReveal).GetField("content", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(handle, paper);
            typeof(UIDragReveal).GetField("reveal", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(handle, reveal);
            events = new GameObject("Events", typeof(EventSystem));
            handle.SetAvailable(true);
        }

        [TearDown]
        public void TearDown() { Object.DestroyImmediate(root); Object.DestroyImmediate(events); }

        private PointerEventData Pointer(float y, int id = -1) => new PointerEventData(events.GetComponent<EventSystem>())
        { pointerId = id, position = new Vector2(0f, y), button = PointerEventData.InputButton.Left };

        [Test]
        public void HandleClick_IsConsumedBeforeParentButton()
        {
            root.AddComponent<UnityEngine.UI.Button>();
            Assert.That(ExecuteEvents.GetEventHandler<IPointerClickHandler>(handle.gameObject), Is.EqualTo(handle.gameObject));
            var pointer = Pointer(0);
            handle.OnPointerDown(pointer);
            handle.OnPointerUp(pointer);
            ExecuteEvents.Execute(handle.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Assert.That(handle.IsRevealed, Is.False);
        }

        [Test]
        public void PullRelease_NotifiesOnce_WithoutAnotherClick()
        {
            int requests = 0;
            handle.OnRevealed += () => requests++;
            handle.OnPointerDown(Pointer(0));
            handle.OnDrag(Pointer(-110));
            Assert.That(requests, Is.Zero);
            handle.OnPointerUp(Pointer(-110));
            handle.OnEndDrag(Pointer(-110));
            Assert.That(requests, Is.EqualTo(1));
            handle.OnPointerDown(Pointer(-110));
            handle.OnPointerUp(Pointer(-110));
            Assert.That(requests, Is.EqualTo(1));
            handle.SetAvailable(true);
            handle.OnPointerDown(Pointer(0));
            handle.OnDrag(Pointer(-20));
            handle.OnPointerUp(Pointer(-20));
            Assert.That(requests, Is.EqualTo(1), "短拖不能请求删除");
        }

        [Test]
        public void DefaultAndClick_DoNotRevealDelete()
        {
            Assert.That(reveal.alpha, Is.Zero);
            Assert.That(reveal.blocksRaycasts, Is.False);
            handle.OnPointerDown(Pointer(0));
            Assert.That(handle.IsHeld, Is.True);
            handle.OnPointerUp(Pointer(0));
            Assert.That(handle.IsRevealed, Is.False);
        }

        [Test]
        public void PullDown_RevealsThenPullUpCloses()
        {
            handle.OnPointerDown(Pointer(0));
            handle.OnDrag(Pointer(-110));
            Assert.That(reveal.blocksRaycasts, Is.False, "拖拽期间禁止点击删除");
            handle.OnPointerUp(Pointer(-110));
            Assert.That(handle.IsRevealed, Is.True);
            Assert.That(reveal.blocksRaycasts, Is.True);
            Assert.That(paper.anchoredPosition.y, Is.EqualTo(-110f));
            handle.OnPointerDown(Pointer(-110));
            handle.OnDrag(Pointer(0));
            handle.OnPointerUp(Pointer(0));
            Assert.That(handle.IsRevealed, Is.False);
            Assert.That(reveal.alpha, Is.Zero);
        }

        [Test]
        public void ShortPullAndOtherPointer_DoNotReveal()
        {
            handle.OnPointerDown(Pointer(0));
            handle.OnDrag(Pointer(-110, 1));
            Assert.That(paper.anchoredPosition.y, Is.Zero);
            handle.OnDrag(Pointer(-20));
            handle.OnPointerUp(Pointer(-20));
            Assert.That(handle.IsRevealed, Is.False);
            Assert.That(paper.anchoredPosition.y, Is.Zero);
        }

        [Test]
        public void UnavailableAndRebind_ResetInteraction()
        {
            handle.SetAvailable(false);
            handle.OnPointerDown(Pointer(0));
            Assert.That(handle.IsHeld, Is.False);
            handle.SetAvailable(true);
            handle.OnPointerDown(Pointer(0));
            handle.OnDrag(Pointer(-110));
            handle.SetAvailable(false);
            Assert.That(handle.IsHeld, Is.False);
            Assert.That(reveal.alpha, Is.Zero);
        }
    }
}
