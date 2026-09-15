// 职责：震动强度的语义枚举。
// 为什么新建：玩法要表达的是「轻一下 / 重一下」，不是毫秒数与振幅；把强度做成枚举，
// 各平台自己翻译成本平台 API。与 PlatformKind 是两件事，不合并进同一个文件。

namespace Game.Core.Platform
{
    /// <summary>
    /// 震动强度。玩法只表达语义强度，具体震多久由各平台实现决定。
    /// </summary>
    public enum VibrationKind
    {
        /// <summary>轻微反馈：按钮、选中。</summary>
        Light = 0,

        /// <summary>中等反馈：确认、命中。</summary>
        Medium = 1,

        /// <summary>强反馈：失败、爆炸。</summary>
        Heavy = 2,
    }
}
