// 职责：把平台差异收拢成一个与平台无关的接口，供玩法与其它框架服务使用。
// 为什么新建：architecture.md 第 3 节硬约束「平台条件编译与平台专属 API 只在 Core/Platform/，
// 对外暴露 IPlatformService」；工程内还没有这个接口。

namespace Game.Core.Platform
{
    /// <summary>
    /// 平台服务。玩法要知道「在什么平台上、存档放哪、是不是触屏为主、震一下」都走这里，
    /// 不去读 Application.platform、不写 #if UNITY_ANDROID。
    /// </summary>
    public interface IPlatformService
    {
        /// <summary>当前平台种类。</summary>
        PlatformKind Kind { get; }

        /// <summary>存档根目录（绝对路径，运行时由 Application.persistentDataPath 推出）。</summary>
        string SaveRoot { get; }

        /// <summary>触屏是否是主要输入方式。UI 的点击热区大小、是否显示虚拟摇杆按它决定。</summary>
        bool IsTouchPrimary { get; }

        /// <summary>震动。不支持震动的平台上是空操作，调用方不需要判断平台。</summary>
        void Vibrate(VibrationKind kind);
    }
}
