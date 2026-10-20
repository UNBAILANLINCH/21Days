// 职责：按提示设备读取动作的有效绑定文字；不采样按键，也不改变动作。
// 为什么新建：输入服务不持有 HUD，两个现有提示入口都需要同一套绑定筛选；生成的 GameInput 不可修改。
using System;
using UnityEngine.InputSystem;

namespace Game.Core.Input
{
    public static class InputBindingDisplay
    {
        public static string Read(InputAction action, bool gamepad)
        {
            if (action == null) return string.Empty;
            string prefix = gamepad ? "<Gamepad>" : "<Keyboard>";
            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding binding = action.bindings[i];
                if (binding.isComposite || binding.isPartOfComposite) continue;
                string path = binding.effectivePath;
                if (!string.IsNullOrEmpty(path) && (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || (!gamepad && path.StartsWith("<Mouse>", StringComparison.OrdinalIgnoreCase))))
                    return action.GetBindingDisplayString(i);
            }
            return string.Empty;
        }

        // 动作通知只用于提示切换，不参与玩法输入判定；忽略无关设备与取消事件。
        public static bool TryGetDevice(object source, InputActionChange change, out bool gamepad)
        {
            gamepad = false;
            if (change != InputActionChange.ActionPerformed || !(source is InputAction action)) return false;
            InputDevice device = action.activeControl == null ? null : action.activeControl.device;
            if (device is Gamepad) { gamepad = true; return true; }
            return device is Keyboard || device is Mouse;
        }
    }
}
