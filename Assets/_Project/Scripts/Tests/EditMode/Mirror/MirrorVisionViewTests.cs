// 职责：钉住视野遮罩的缩放换算——可见范围越小暗角越重（缩放越接近最小值）、结果恒 ≥ 1（画面四周不露边）、越界夹取；
//   以及对外只读的 ShownRadius 跟着 SetRadius 走、≥ 1 时暗角隐藏（回放据此判暗角，不找子物体）。
// 为什么新建：一个被测类一个测试类。
using Game.Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests.EditMode.Mirror
{
    /// <summary><see cref="MirrorVisionView.ScaleFor"/> 的 EditMode 测试。</summary>
    public sealed class MirrorVisionViewTests
    {
        [Test]
        public void ScaleFor_Endpoints()
        {
            Assert.That(MirrorVisionView.ScaleFor(0f, 1f, 2.5f), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(MirrorVisionView.ScaleFor(1f, 1f, 2.5f), Is.EqualTo(2.5f).Within(1e-5f));
        }

        [Test]
        public void ScaleFor_SmallerRadius_SmallerScale()
        {
            float threeCracks = MirrorVisionView.ScaleFor(0.46f, 1f, 2.5f);
            float oneCrack = MirrorVisionView.ScaleFor(0.82f, 1f, 2.5f);

            Assert.That(threeCracks, Is.LessThan(oneCrack));
        }

        [Test]
        public void ScaleFor_OutOfRange_ClampedAndNeverBelowOne()
        {
            Assert.That(MirrorVisionView.ScaleFor(-3f, 1f, 2.5f), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(MirrorVisionView.ScaleFor(9f, 1f, 2.5f), Is.EqualTo(2.5f).Within(1e-5f));
            Assert.That(MirrorVisionView.ScaleFor(0f, 0.5f, 2.5f), Is.GreaterThanOrEqualTo(1f));
        }

        [Test]
        public void ShownRadius_OneBeforeOpen_FollowsSetRadius_HidesVignetteAtOne()
        {
            var host = new GameObject("mirror_vision_test", typeof(RectTransform));
            try
            {
                MirrorVisionView view = host.AddComponent<MirrorVisionView>();
                var vignetteHost = new GameObject("Vignette", typeof(RectTransform));
                vignetteHost.transform.SetParent(host.transform, false);
                Image vignette = vignetteHost.AddComponent<Image>();
                using (var so = new SerializedObject(view))
                {
                    so.FindProperty("vignette").objectReferenceValue = vignette;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                Assert.That(view.ShownRadius, Is.EqualTo(1f), "还没打开过：按不遮算");

                view.SetRadius(0.64f);
                Assert.That(view.ShownRadius, Is.EqualTo(0.64f).Within(1e-5f));
                Assert.That(vignette.enabled, Is.True, "可见范围 < 1 时显示暗角");

                view.SetRadius(1f);
                Assert.That(view.ShownRadius, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(vignette.enabled, Is.False, "可见范围 ≥ 1 时隐藏暗角");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
