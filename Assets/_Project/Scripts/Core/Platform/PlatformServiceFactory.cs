// 职责：按当前构建目标挑选平台实现，把「选哪个实现」的平台宏也关在 Core/Platform/ 里。
// 为什么新建：这句 #if 本来该写在 GameLifetimeScope 的注册处，但 project-root.md 规定平台条件编译
// 只能出现在 Platform 目录；抽成一个工厂方法后 GameLifetimeScope 里就只剩一行普通注册。

namespace Game.Core.Platform
{
    /// <summary>
    /// 平台实现的选择点。GameLifetimeScope 调 Create() 拿实例注册，自己不写平台分支。
    /// 编辑器里恒定用 Standalone 实现，方便在 PC 上开发调试。
    /// </summary>
    public static class PlatformServiceFactory
    {
        /// <summary>按当前平台造一个实现。</summary>
        public static PlatformServiceBase Create()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidPlatformService();
#else
            return new StandalonePlatformService();
#endif
        }
    }
}
