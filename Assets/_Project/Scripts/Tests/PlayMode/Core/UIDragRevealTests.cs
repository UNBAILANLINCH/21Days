// 停用与焦点恢复依赖 MonoBehaviour 的运行时生命周期，EditMode 不执行这些回调。
using System.Collections;
using System.Reflection;
using Game.Core.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode.Core
{
    public sealed class UIDragRevealTests
    {
        [UnityTest]
        public IEnumerator DisableWhileHeld_HidesDeleteAndReleasesPointer()
        {
            var root = new GameObject("DragTest", typeof(RectTransform));
            var events = new GameObject("Events", typeof(EventSystem));
            try
            {
                var paper = new GameObject("Paper", typeof(RectTransform)).GetComponent<RectTransform>();
                paper.SetParent(root.transform, false);
                var reveal = new GameObject("Delete", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<CanvasGroup>();
                reveal.transform.SetParent(root.transform, false);
                var handle = paper.gameObject.AddComponent<UIDragReveal>();
                typeof(UIDragReveal).GetField("content", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(handle, paper);
                typeof(UIDragReveal).GetField("reveal", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(handle, reveal);
                handle.SetAvailable(true);
                var pointer = new PointerEventData(events.GetComponent<EventSystem>()) { pointerId = -1, position = Vector2.zero };
                handle.OnPointerDown(pointer);
                pointer.position = Vector2.down * 110f;
                handle.OnDrag(pointer);
                Assert.That(handle.IsHeld, Is.True);
                Assert.That(reveal.alpha, Is.EqualTo(1f));
                root.SetActive(false);
                yield return null;
                Assert.That(handle.IsHeld, Is.False);
                Assert.That(reveal.alpha, Is.Zero);
                Assert.That(reveal.blocksRaycasts, Is.False);
                Assert.That(paper.anchoredPosition, Is.EqualTo(Vector2.zero));
            }
            finally { Object.Destroy(root); Object.Destroy(events); }
        }
    }
}
