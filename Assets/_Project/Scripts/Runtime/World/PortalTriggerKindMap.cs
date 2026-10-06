// 职责：把表里的触发方式枚举翻成运行期枚举，取值不认识时当场报出来。
// 为什么新建：一个文件一个类 / 一个枚举（csharp-code.md「一个文件一个类，文件名等于类名」）；
//   映射要有单一落点——散在各调用点上，加一个触发方式就会有地方忘记处理。
using System;

namespace Game.World
{
    /// <summary>表侧枚举与运行期枚举的取值映射。</summary>
    public static class PortalTriggerKindMap
    {
        /// <summary>把表里那一行的触发方式翻成运行期枚举；取值不认识时抛 <see cref="ArgumentOutOfRangeException"/>。</summary>
        public static PortalTriggerKind FromTable(global::cfg.world.PortalTriggerKind kind)
        {
            switch (kind)
            {
                case global::cfg.world.PortalTriggerKind.EnterRange:
                    return PortalTriggerKind.EnterRange;
                case global::cfg.world.PortalTriggerKind.Interact:
                    return PortalTriggerKind.Interact;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind), kind, "TbPortal.trigger_kind 的取值不在白名单里（见 Tables/Defines/world.xml）。");
            }
        }
    }
}
