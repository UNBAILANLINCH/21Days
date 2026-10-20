// 职责：验证倾斜任务页签导航滚动的边界；现有 Quest 纯规则测试不覆盖 UGUI 视口，单独建最小内存层级。
using Game.Core.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests.EditMode.Quest
{
    public sealed class QuestListItemFocusTests
    {
        private GameObject root;
        private ScrollRect scroll;
        private RectTransform row;
        private UIScrollFocus focus;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Viewport", typeof(RectTransform), typeof(ScrollRect));
            var viewport = (RectTransform)root.transform;
            viewport.pivot = new Vector2(0f, 1f);
            viewport.sizeDelta = new Vector2(430f, 600f);
            scroll = root.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = Rect("Content", viewport, new Vector2(430f, 1500f));
            row = Rect("Row", scroll.content, new Vector2(458f, 124f));
            focus = row.gameObject.AddComponent<UIScrollFocus>();
            focus.Bind(scroll);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(root);

        [TestCase(-1000f, 0f, 524f)]
        [TestCase(-200f, 500f, 200f)]
        [TestCase(-2000f, 0f, 900f)]
        public void Select_OutsideViewport_ClampsToContent(float position, float offset, float expected)
        {
            row.anchoredPosition = new Vector2(0f, position);
            scroll.content.anchoredPosition = new Vector2(0f, offset);
            scroll.velocity = new Vector2(0f, 200f);
            focus.OnSelect(null);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(expected).Within(.01f));
            Assert.That(scroll.velocity, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void Select_TiltedTopCorner_RevealsWholeHeader()
        {
            scroll.content.sizeDelta = new Vector2(430f, 2000f);
            row.anchoredPosition = new Vector2(0f, -1000f);
            row.localRotation = Quaternion.Euler(0f, 0f, 6f);
            scroll.content.anchoredPosition = new Vector2(0f, 1000f);
            focus.OnSelect(null);
            Assert.That(scroll.content.anchoredPosition.y, Is.LessThan(960f));
            Assert.That(scroll.content.anchoredPosition.y, Is.GreaterThan(940f));
        }

        private static RectTransform Rect(string name, RectTransform parent, Vector2 size)
        {
            var value = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            value.SetParent(parent, false);
            value.anchorMin = value.anchorMax = value.pivot = new Vector2(0f, 1f);
            value.sizeDelta = size;
            value.anchoredPosition = Vector2.zero;
            return value;
        }
    }
}
