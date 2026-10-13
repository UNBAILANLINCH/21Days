// 职责：一次效果执行的结局码——把「执行者没接」「执行者接了但拒绝」「真执行了」三件事分开。
// 为什么新建：一个文件一个类型。用 bool 表达不了前两种（都是 false 却要给玩家完全不同的提示：
//   「这个效果还做不了」vs「你现在用不了」），而静默 false 正是本工程反复强调要避免的失败方式。
namespace Game.Inventory
{
    /// <summary>效果执行结局。</summary>
    public enum ItemEffectStatus
    {
        /// <summary>还没走到执行阶段（默认值，不会由执行者返回）。</summary>
        None = 0,

        /// <summary>真执行了效果。</summary>
        Applied = 1,

        /// <summary>执行者接了但拒绝执行（玩家满血、目标不在场等）。背包与件数都不动。</summary>
        Rejected = 2,

        /// <summary>这条效果要的目标没接进来（例如没注册玩家生命口），或参数非法。</summary>
        BadTarget = 3,

        /// <summary>效果标识在注册表里没有执行者（皮 / 面具类归 S1；见 ItemUseRules.cs 文件头）。</summary>
        NoExecutor = 4,
    }
}
