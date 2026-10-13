// 职责：验证标题乘色不累积、能够恢复原色，且包含隐藏按钮而不改变文字。
// 为什么新建：现有 UI 测试没有标题表现用例，扩展无关面板测试会混淆被测职责。
using Game.Core.UI.Views;
using Game.Core.UI;
using System;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests.EditMode.Core
{
    public sealed class TitleViewTests
    {
        private GameObject root;
        private TitleView view;
        private Image image;
        private TMP_Text label;
        private Color original;
        private Texture2D texture;
        private Sprite normalSprite;
        private Sprite alternateSprite;

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
            texture = new Texture2D(2, 2);
            normalSprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
            alternateSprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(normalSprite);
            Object.DestroyImmediate(alternateSprite);
            Object.DestroyImmediate(texture);
        }

        [Test]
        public void DefaultSelection_OnlyNavigationFocusesStart()
        {
            var host = new GameObject("Start", typeof(RectTransform), typeof(Button));
            host.transform.SetParent(root.transform, false);
            typeof(UIView).GetField("defaultSelected", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(view, host.GetComponent<Button>());
            Assert.That(UIService.ResolveDefaultSelection(view), Is.Null, "未操作的标题不能自动显示选中高亮");
            Assert.That(UIService.ResolveDefaultSelection(view, true), Is.SameAs(host), "键盘 / 手柄仍有导航起点");
        }

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

        [Test]
        public void SetAlternateAppearance_WhenUnconfigured_ReturnsFalseWithoutChangingImages()
        {
            image.sprite = normalSprite;
            Assert.That(view.SetAlternateAppearance(true), Is.False);
            Assert.That(image.sprite, Is.SameAs(normalSprite));
            Assert.That(image.color, Is.EqualTo(original));
        }

        [Test]
        public void SetAlternateAppearance_WhenConfigured_SwitchesBackgroundAndSelectedSpriteThenRestores()
        {
            image.sprite = normalSprite;
            SetField(view, "backgroundImage", image);
            SetField(view, "alternateBackground", alternateSprite);
            var host = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            host.transform.SetParent(root.transform, false);
            Button button = host.GetComponent<Button>();
            button.image.sprite = normalSprite;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState { selectedSprite = normalSprite };

            Type type = typeof(TitleView).GetNestedType("ButtonAppearance", BindingFlags.NonPublic);
            object entry = Activator.CreateInstance(type, true);
            SetField(entry, "button", button);
            SetField(entry, "alternateNormal", alternateSprite);
            SetField(entry, "alternateSelected", alternateSprite);
            Array entries = Array.CreateInstance(type, 1);
            entries.SetValue(entry, 0);
            SetField(view, "alternateButtons", entries);

            Assert.That(view.SetAlternateAppearance(true), Is.True);
            Assert.That(view.SetAlternateAppearance(true), Is.True);
            Assert.That(image.sprite, Is.SameAs(alternateSprite));
            Assert.That(button.image.sprite, Is.SameAs(alternateSprite));
            Assert.That(button.spriteState.selectedSprite, Is.SameAs(alternateSprite));
            Assert.That(image.color, Is.EqualTo(original), "正式素材不能再被预览色调加深");

            Assert.That(view.SetAlternateAppearance(false), Is.True);
            Assert.That(image.sprite, Is.SameAs(normalSprite));
            Assert.That(button.image.sprite, Is.SameAs(normalSprite));
            Assert.That(button.spriteState.selectedSprite, Is.SameAs(normalSprite));
        }

        [Test]
        public void SetAlternateAppearance_WhenButtonBindingMissing_DoesNotPartiallyChangeBackground()
        {
            image.sprite = normalSprite;
            SetField(view, "backgroundImage", image);
            SetField(view, "alternateBackground", alternateSprite);
            Type type = typeof(TitleView).GetNestedType("ButtonAppearance", BindingFlags.NonPublic);
            Array entries = Array.CreateInstance(type, 1);
            entries.SetValue(Activator.CreateInstance(type, true), 0);
            SetField(view, "alternateButtons", entries);
            Assert.That(view.SetAlternateAppearance(true), Is.False);
            Assert.That(image.sprite, Is.SameAs(normalSprite));
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
