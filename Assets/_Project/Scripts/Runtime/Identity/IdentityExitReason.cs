// 职责：身份怎么结束的——主动退出、时限归零、露馅失效、换了下一个身份。
// 为什么新建：四种离开方式在原文里各有出处且后果不同（冷却不同），合成一个 bool 会丢掉这个区别。

namespace Game.Identity
{
    /// <summary>
    /// 身份退出的原因。<see cref="None"/> 表示本来就在本体、没有发生退出。
    /// <para>
    /// 四种来源：<see cref="Voluntary"/> 主动退出（`01_换皮与附身.md:165` R26 明写「主动退出附身的操作原文没写」，
    /// 本波暂按「可主动退出、进冷却」处理 [推断]）；<see cref="Expired"/> 时限归零（`01_换皮与附身.md:166` R27
    /// 执事皮有「生效中」时段，时长待定）；<see cref="Exposed"/> 露馅失效（`02_身份暴露与怀疑.md:90` R1
    /// 「变回原主」）；<see cref="Replaced"/> 直接换成下一个身份（`01_换皮与附身.md:118` R4 链式附身）。
    /// </para>
    /// </summary>
    public enum IdentityExitReason
    {
        /// <summary>没有发生退出（本来就在本体）。</summary>
        None = 0,

        /// <summary>主动退出（键位与条件待定，见 `01_换皮与附身.md:165` R26）。</summary>
        Voluntary = 1,

        /// <summary>时限归零自动退出。</summary>
        Expired = 2,

        /// <summary>露馅导致身份失效。</summary>
        Exposed = 3,

        /// <summary>被下一个借用的身份替换。</summary>
        Replaced = 4,
    }
}
