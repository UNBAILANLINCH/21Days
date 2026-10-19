// 职责：验证独立水墨预览的播放、拖动与停用清理；正式黑幕测试不覆盖该组件。
using System.Collections;
using System.Reflection;
using Game.InkTransition;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests.PlayMode
{
    public sealed class InkTransitionPreviewTests
    {
        [UnityTest]
        public IEnumerator ReplayScrubAndDisable_PreserveBackgroundAndReleaseMaterial()
        {
            var root = new GameObject("InkPreviewTest", typeof(RectTransform));
            root.SetActive(false);
            var shader = Shader.Find("21Days/UI/InkTransition");
            Assert.That(shader, Is.Not.Null);
            var source = new Material(shader);
            try
            {
                var preview = root.AddComponent<InkTransitionPreview>();
                var background = Child<RawImage>(root, "Background");
                var overlay = Child<RawImage>(root, "Overlay");
                var slider = Child<Slider>(root, "Progress");
                var replay = Child<Button>(root, "Replay");
                var pause = Child<Button>(root, "Pause");
                Bind(preview, "background", background);
                Bind(preview, "firstBackground", Texture2D.whiteTexture);
                Bind(preview, "secondBackground", Texture2D.blackTexture);
                Bind(preview, "inkOverlay", overlay);
                Bind(preview, "inkMaterial", source);
                Bind(preview, "progressSlider", slider);
                Bind(preview, "replayButton", replay);
                Bind(preview, "pauseButton", pause);
                Bind(preview, "duration", 30f);
                root.SetActive(true);
                yield return null;
                Assert.That(preview.IsPlaying, Is.True);
                pause.onClick.Invoke();
                Assert.That(preview.IsPlaying, Is.False);
                pause.onClick.Invoke();
                Assert.That(preview.IsPlaying, Is.True);

                preview.SetProgress(-1f);
                Assert.That(preview.Progress, Is.Zero);
                Assert.That(preview.IsPlaying, Is.False);
                Assert.That(background.texture, Is.SameAs(Texture2D.whiteTexture));
                slider.value = .5f;
                Assert.That(background.texture, Is.SameAs(Texture2D.blackTexture));
                preview.SetProgress(2f);
                Assert.That(preview.Progress, Is.EqualTo(1f));
                replay.onClick.Invoke();
                Assert.That(preview.IsPlaying, Is.True);
                Assert.That(background.texture, Is.SameAs(Texture2D.blackTexture));
                preview.SetProgress(.5f);
                Assert.That(background.texture, Is.SameAs(Texture2D.whiteTexture));

                var runtime = overlay.material;
                Assert.That(runtime, Is.Not.SameAs(source));
                root.SetActive(false);
                slider.value = .8f;
                Assert.That(preview.Progress, Is.Zero);
                Assert.That(preview.HasRuntimeMaterial, Is.False);
                Assert.That(preview.IsPlaying, Is.False);
                Assert.That(overlay.enabled, Is.False);
                yield return null;
                Assert.That(runtime == null, Is.True, "停用后须释放运行时材质");
                Assert.That(source != null, Is.True, "不能销毁源材质");
            }
            finally { Object.Destroy(root); Object.Destroy(source); }
        }

        private static T Child<T>(GameObject root, string name) where T : Component
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(T));
            child.transform.SetParent(root.transform, false);
            return child.GetComponent<T>();
        }

        private static void Bind(InkTransitionPreview preview, string field, object value) =>
            typeof(InkTransitionPreview).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(preview, value);
    }
}
