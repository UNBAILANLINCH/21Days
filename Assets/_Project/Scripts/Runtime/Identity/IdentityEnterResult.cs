// 职责：借用身份这一步的结果——成功、id 非法、身份表里没有、已经在借同一个、还在冷却。
// 为什么新建：「无效身份 id 的明确行为」要在返回值里看得见，不能让调用方去猜状态有没有变。

namespace Game.Identity
{
    /// <summary>
    /// <see cref="IdentityRules.TryEnter"/> 的结果。除 <see cref="Entered"/> 外的四种都表示
    /// <b>状态一点没动</b>（当前身份、时限、冷却全部保持原样），调用方据此决定提示或忽略。
    /// <para>
    /// <see cref="UnknownIdentity"/> 是「格式合法但身份表里没有」——多半是内容没配或 id 拼错；
    /// <see cref="InvalidId"/> 是格式本身不合法（见 <see cref="IdentityId.From"/>）。
    /// 两者分开，是为了让日志能区分「内容缺失」和「调用方传了垃圾」。
    /// </para>
    /// </summary>
    public enum IdentityEnterResult
    {
        /// <summary>已进入该身份。</summary>
        Entered = 0,

        /// <summary>id 格式不合法（大写、点号、空格、中文、超长等），状态未变。</summary>
        InvalidId = 1,

        /// <summary>id 格式合法，但身份定义表里没有这条，状态未变。</summary>
        UnknownIdentity = 2,

        /// <summary>当前已经在借这个身份，状态未变（不刷新时限）。</summary>
        AlreadyBorrowing = 3,

        /// <summary>还在冷却里，状态未变。</summary>
        OnCooldown = 4,
    }
}
