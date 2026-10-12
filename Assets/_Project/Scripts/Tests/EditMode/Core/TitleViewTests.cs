// 职责：验证标题乘色不累积、能够恢复原色，且包含隐藏按钮而不改变文字。
// 为什么新建：现有 UI 测试没有标题表现用例，扩展无关面板测试会混淆被测职责。
using Game.Core.UI.Views;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests.EditMode.Core
{
    public sealed class TitleViewTests
    {
        private GameObject root;
        private TitleView view;
        private Image image;
        private TMP_Text label;
        private Color original;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Title", typeof(RectTransform));
            view = root.AddComponent<TitleView>();
            var child = new GameObject("Image", typeof(RectTransform), typeof(Image));
            child.transform.SetParent(root.transform, false);
            image = child.GetComponent<Image>();
            original = new Color(0.8f, 0.7f, 0.6f, 0.5f);
            image.color = original;
            var text = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            text.transform.SetParent(root.transform, false);
            label = text.GetComponent<TextMeshProUGUI>();
            label.color = Color.white;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(root);

        [Test]
        public void SetAppearanceTint_WhenRepeated_DoesNotAccumulateAndRestoresOriginal()
        {
            var yin = new Color(0.45f, 0.35f, 0.6f, 1f);
            view.SetAppearanceTint(yin);
            view.SetAppearanceTint(yin);
            Assert.That(image.color, Is.EqualTo(original * yin));
            view.SetAppearanceTint(Color.white);
            Assert.That(image.color, Is.EqualTo(original));
        }

        [Test]
        public void SetAppearanceTint_WhenImageHidden_TintsImageAndPreservesText()
        {
            image.gameObject.SetActive(false);
            view.SetAppearanceTint(Color.gray);
            Assert.That(image.color, Is.EqualTo(original * Color.gray));
            Assert.That(label.color, Is.EqualTo(Color.white));
            Assert.That(image.gameObject.activeSelf, Is.False);
        }
    }
}
