// 职责：验证提示使用有效绑定、按设备筛选，并且取消绑定时不伪造 E。
// 为什么新建：现有 InputServiceTests 测服务生命周期；本类覆盖独立绑定显示规则。
using Game.Core.Input;
using NUnit.Framework;
using UnityEngine.InputSystem;

namespace Game.Tests.EditMode.Core
{
    public sealed class InputBindingDisplayTests
    {
        [Test]
        public void Read_UsesDeviceAndEffectiveOverrides()
        {
            using (var action = new InputAction("Interact"))
            {
                action.AddBinding("<Keyboard>/e");
                action.AddBinding("<Gamepad>/buttonSouth");
                Assert.That(InputBindingDisplay.Read(action, false), Is.EqualTo(action.GetBindingDisplayString(0)));
                Assert.That(InputBindingDisplay.Read(action, true), Is.EqualTo(action.GetBindingDisplayString(1)));
                action.ApplyBindingOverride(0, "<Keyboard>/q");
                Assert.That(InputBindingDisplay.Read(action, false), Is.EqualTo("Q"));
                action.ApplyBindingOverride(0, "<Mouse>/leftButton");
                Assert.That(InputBindingDisplay.Read(action, false), Is.EqualTo(action.GetBindingDisplayString(0)));
                action.ApplyBindingOverride(0, string.Empty);
                Assert.That(InputBindingDisplay.Read(action, false), Is.Empty);
                Assert.That(InputBindingDisplay.Read(action, true), Is.Not.Empty);
            }
        }

        [Test]
        public void Read_MissingDeviceBinding_DoesNotInventKeyboardPrompt()
        {
            using (var action = new InputAction("Interact", binding: "<Gamepad>/buttonSouth"))
                Assert.That(InputBindingDisplay.Read(action, false), Is.Empty);
            Assert.That(InputBindingDisplay.Read(null, true), Is.Empty);
        }
    }
}
