// 职责：传送点的触发方式——运行期用的那一份枚举，取值与 Tables/Defines/world.xml 的 PortalTriggerKind 一一对应。
// 为什么新建：组件不能直接拿生成代码的 cfg.world.PortalTriggerKind 当 Inspector 字段类型——
//   生成物重跑一次就可能改掉枚举的底层类型或命名空间，序列化在场景里的值会跟着漂。
//   这一份是**手写的运行期口径**，与表里的枚举按名字与值逐项对齐，由 EditMode 测试钉住（WorldTableTests）。
// 为什么不用拓展方式复用生成枚举：PortalAnchor 是挂场景的 MonoBehaviour，字段类型一旦引用生成代码，
//   生成物变更就要动所有已挂场景的序列化数据；反过来，表侧的取值变更由测试当场报出来，代价小得多。
namespace Game.World
{
    /// <summary>传送点的触发方式（对应 TbPortal.trigger_kind）。</summary>
    public enum PortalTriggerKind
    {
        /// <summary>进入范围即触发。</summary>
        EnterRange = 0,

        /// <summary>需要交互键：先进入范围，再由外部输入层调 <see cref="PortalAnchor.TryInteract"/>。</summary>
        Interact = 1,
    }
}
