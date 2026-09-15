// 职责：端游（Windows / 编辑器）的平台实现。
// 为什么新建：PlatformServiceBase 只抽了共用部分，"是什么平台 / 怎么震动" 必须按平台各写一份；
// 和 Android 实现放同一个文件会让平台宏互相污染。

namespace Game.Core.Platform
{
    /// <summary>
    /// 端游实现。键鼠为主输入，没有震动硬件——Vibrate 是空操作，调用方不需要判断平台。
    /// 编辑器里也用这一份，方便在 PC 上开发调试。
    /// </summary>
    public sealed class StandalonePlatformService : PlatformServiceBase
    {
        public override PlatformKind Kind => PlatformKind.Standalone;

        public override bool IsTouchPrimary => false;

        public override void Vibrate(VibrationKind kind)
        {
            // 端游没有震动马达；手柄震动等有玩法需求时再接 Input System 的 Gamepad.SetMotorSpeeds。
        }
    }
}
