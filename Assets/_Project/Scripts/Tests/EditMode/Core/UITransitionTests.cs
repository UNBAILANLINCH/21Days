// 职责：钉住 UIView 四种过渡预设在 seconds = 0 时「不等帧、直接落到终值」这条规则——
//   这条规则原来只对 Fade 成立（UIServiceTests 靠它同步跑完整条开关链路），加了 Slide / Scale 之后
//   同样不能出现「这一帧还在半路」的状态，否则 UIService 的同步测试全得改成异步等帧。
//   另外钉住 UIButtonFeedback 对 Selectable.interactable == false 的容错。
// 为什么新建：UIServiceTests 关注的是 UIService 的三条粘合规则，不覆盖 UIView 自己的过渡分派逻辑；
//   这里只测 UIView / UIButtonFeedback，不需要 UIService、不需要建 Canvas，塞进 UIServiceTests
//   会让那个类背两套前提。

using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests.EditMode.Core
{
    /// <summary>UIView 过渡预设分派（<see cref="UITransition"/>）与 UIButtonFeedback 按压反馈的 EditMode 测试。</summary>
    public sealed class UITransitionTests
    {
        // UIView.transition 是私有 SerializeField，测试用反射设值，不给生产代码开测试专用的公开后门。
        private static readonly FieldInfo TransitionField =
            typeof(UIView).GetField("transition", BindingFlags.NonPublic | BindingFlags.Instance);

        // 排查 MissingReferenceException 时发现：EditMode 测试里「同一次调用内创建又销毁」这种时序，
        // DestroyImmediate 不保证会同步补发 OnDestroy——UIView / UIButtonFeedback 里 OnDestroy 里的
        // Cancel 因此可能根本没跑，残留的 LitMotion 句柄在下一次编辑器 Update 时还想更新一个已经销毁的
        // RectTransform，报 MissingReferenceException。不指望生产代码的 OnDestroy，销毁前自己显式收尾。
        private static readonly FieldInfo TransitionHandleField =
            typeof(UIView).GetField("transitionHandle", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo ScaleHandleField =
            typeof(UIButtonFeedback).GetField("scaleHandle", BindingFlags.NonPublic | BindingFlags.Instance);

        // Game.Tests.EditMode 的 asmdef 没引用 LitMotion（迄今没有测试直接摸过它的类型，不想为了一处
        // 收尾专门加一条程序集引用）。IsActive / Cancel 都是 LitMotion.MotionHandleExtensions 上的静态
        // 扩展方法，纯反射调用即可，不需要编译期类型名。
        private static readonly System.Type MotionHandleExtensionsType =
            System.Type.GetType("LitMotion.MotionHandleExtensions, LitMotion");

        private static readonly MethodInfo IsActiveMethod = MotionHandleExtensionsType?.GetMethod("IsActive");
        private static readonly MethodInfo CancelMethod = MotionHandleExtensionsType?.GetMethod("Cancel");

        private GameObject subject;
        private TestView view;

        [SetUp]
        public void SetUp()
        {
            subject = new GameObject("TestView", typeof(RectTransform), typeof(TestView));
            view = subject.GetComponent<TestView>();
        }

        [TearDown]
        public void TearDown()
        {
            if (subject != null)
            {
                CancelIfActive(view, TransitionHandleField);
                Object.DestroyImmediate(subject);
            }
        }

        /// <summary>销毁前显式收尾：见上面 <see cref="TransitionHandleField"/> 的注释。</summary>
        private static void CancelIfActive(object owner, FieldInfo handleField)
        {
            if (owner == null || IsActiveMethod == null || CancelMethod == null)
            {
                return;
            }

            object handle = handleField.GetValue(owner);
            bool isActive = (bool)IsActiveMethod.Invoke(null, new[] { handle });
            if (isActive)
            {
                CancelMethod.Invoke(null, new[] { handle });
            }
        }

        [TestCase(UITransition.Fade)]
        [TestCase(UITransition.SlideUp)]
        [TestCase(UITransition.SlideDown)]
        [TestCase(UITransition.Scale)]
        public void PlayOpenTransitionAsync_WhenSecondsIsZero_ReachesFinalStateWithoutWaitingAFrame(UITransition preset)
        {
            Vector2 restPosition = view.AnchoredPositionValue;
            TransitionField.SetValue(view, preset);

            view.InvokeOpenAsync(0f).GetAwaiter().GetResult();

            Assert.That(view.Alpha, Is.EqualTo(1f), $"{preset}：seconds = 0 时开面板要直接可见，不等帧");

            switch (preset)
            {
                case UITransition.SlideUp:
                case UITransition.SlideDown:
                    Assert.That(view.AnchoredPositionValue, Is.EqualTo(restPosition),
                        $"{preset}：seconds = 0 时要直接落在起点，不该停在偏移量上");
                    break;
                case UITransition.Scale:
                    Assert.That(view.LocalScaleValue, Is.EqualTo(Vector3.one),
                        "Scale：seconds = 0 时要直接落在 1 倍缩放");
                    break;
            }
        }

        [Test]
        public void OnPointerDown_WhenSelectableIsNotInteractable_DoesNotChangeScale()
        {
            var buttonObject = new GameObject("Button", typeof(RectTransform), typeof(Selectable),
                typeof(UIButtonFeedback));
            try
            {
                Selectable selectable = buttonObject.GetComponent<Selectable>();
                selectable.interactable = false;
                var feedback = buttonObject.GetComponent<UIButtonFeedback>();
                var rect = (RectTransform)buttonObject.transform;

                feedback.OnPointerDown(null);

                Assert.That(rect.localScale, Is.EqualTo(Vector3.one),
                    "按钮 interactable == false 时按下不该缩放");

                CancelIfActive(feedback, ScaleHandleField);
            }
            finally
            {
                Object.DestroyImmediate(buttonObject);
            }
        }

        /// <summary>暴露 UIView 受保护成员给测试用的壳子。</summary>
        private sealed class TestView : UIView
        {
            public override UILayer Layer => UILayer.Panel;

            public float Alpha => Group.alpha;

            public Vector2 AnchoredPositionValue => Rect.anchoredPosition;

            public Vector3 LocalScaleValue => Rect.localScale;

            public UniTask InvokeOpenAsync(float seconds) => PlayOpenTransitionAsync(seconds, CancellationToken.None);
        }
    }
}
