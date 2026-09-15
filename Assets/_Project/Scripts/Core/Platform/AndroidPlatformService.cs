// 职责：Android 手游的平台实现，唯一会调用 Handheld.Vibrate 的地方。
// 为什么新建：同 StandalonePlatformService——平台专属 API 必须各自成文件，
// 这样 `#if UNITY_ANDROID` 的作用范围一眼看得清，不会扩散到别的类型上。

namespace Game.Core.Platform
{
    /// <summary>
    /// Android 实现。触屏为主输入；震动走 Handheld.Vibrate。
    /// Handheld 是 Android/iOS 专属 API，因此用平台宏包住——本目录是全工程唯一允许平台宏的地方。
    /// </summary>
    public sealed class AndroidPlatformService : PlatformServiceBase
    {
        public override PlatformKind Kind => PlatformKind.Android;

        public override bool IsTouchPrimary => true;

        public override void Vibrate(VibrationKind kind)
        {
#if UNITY_ANDROID
            // Handheld.Vibrate 只有「震一下」一档，强度差异等接了原生 Vibrator 再细分。
            UnityEngine.Handheld.Vibrate();
#endif
        }
    }
}
