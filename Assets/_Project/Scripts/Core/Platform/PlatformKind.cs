// 职责：框架对外表达「当前跑在什么平台」的枚举。
// 为什么新建：玩法层不许读 Application.platform、也不许用平台宏，需要一个与平台 API 无关的
// 值类型来表达平台；工程内没有同类枚举可扩展。

namespace Game.Core.Platform
{
    /// <summary>
    /// 平台种类。只区分到「玩法可能需要分支」的粒度，不细分到具体机型。
    /// </summary>
    public enum PlatformKind
    {
        /// <summary>未识别的平台。</summary>
        Unknown = 0,

        /// <summary>端游：Windows / macOS / Linux 独立播放器，以及编辑器。</summary>
        Standalone = 1,

        /// <summary>Android 手游。</summary>
        Android = 2,

        /// <summary>iOS 手游（当前未出包，先占位）。</summary>
        IOS = 3,
    }
}
