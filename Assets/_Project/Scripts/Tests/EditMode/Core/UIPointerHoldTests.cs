// 职责：钉住 UIPointerHold 的按住判定——左键按下为按着、抬起 / 移出 / 停用复位、右键不算；实现了 EventSystem 派发要的三个指针接口。
// 为什么新建：UIPointerHold 是 Core 新增的独立组件，按「被测类 + Tests」单独成文件。
// EditMode 下 Unity 不给普通 MonoBehaviour 发生命周期消息，OnDisable 经反射直接调，验的是复位逻辑本身。
using System.Reflection;
using Game.Core.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Tests.EditMode.Core
{
    public sealed class UIPointerHoldTests
    {
        private GameObject go;
        private UIPointerHold hold;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("UIPointerHoldTests", typeof(RectTransform));
            hold = go.AddComponent<UIPointerHold>();
        }

        [TearDown]
        public void TearDown()
        {
            if (go != null) Object.DestroyImmediate(go);
        }

        private static PointerEventData Pointer(PointerEventData.InputButton button) =>
            new PointerEventData(EventSystem.current) { button = button };

        [Test]
        public void New_IsNotHeld()
        {
            Assert.That(hold.IsHeld, Is.False);
        }

        [Test]
        public void PointerDown_Left_IsHeld_UntilPointerUp()
        {
            hold.OnPointerDown(Pointer(PointerEventData.InputButton.Left));
            Assert.That(hold.IsHeld, Is.True);

            hold.OnPointerUp(Pointer(PointerEventData.InputButton.Left));
            Assert.That(hold.IsHeld, Is.False);
        }

        [Test]
        public void PointerExit_WhileHeld_Releases()
        {
            hold.OnPointerDown(Pointer(PointerEventData.InputButton.Left));

            hold.OnPointerExit(Pointer(PointerEventData.InputButton.Left));

            Assert.That(hold.IsHeld, Is.False, "按着移出即算松手");
        }

        [Test]
        public void RightButton_DoesNotHoldOrRelease()
        {
            hold.OnPointerDown(Pointer(PointerEventData.InputButton.Right));
            Assert.That(hold.IsHeld, Is.False, "右键按下不算按住");

            hold.OnPointerDown(Pointer(PointerEventData.InputButton.Left));
            hold.OnPointerUp(Pointer(PointerEventData.InputButton.Right));
            Assert.That(hold.IsHeld, Is.True, "右键抬起不影响左键按着");
        }

        [Test]
        public void OnDisable_WhileHeld_Releases()
        {
            hold.OnPointerDown(Pointer(PointerEventData.InputButton.Left));
            MethodInfo onDisable = typeof(UIPointerHold).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(onDisable, Is.Not.Null, "UIPointerHold 应有 OnDisable 复位");

            onDisable.Invoke(hold, null);

            Assert.That(hold.IsHeld, Is.False, "停用后收不到抬起，必须自己复位");
        }

        [Test]
        public void ImplementsPointerHandlers_ForEventSystemDispatch()
        {
            // EventSystem 按接口派发按下 / 抬起 / 移出；少实现一个，运行时就收不到对应事件。
            Assert.That(hold, Is.InstanceOf<IPointerDownHandler>());
            Assert.That(hold, Is.InstanceOf<IPointerUpHandler>());
            Assert.That(hold, Is.InstanceOf<IPointerExitHandler>());
        }
    }
}
