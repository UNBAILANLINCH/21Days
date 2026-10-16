// 职责：露馅后果策略的「按配置」实现——把 IdentitySettings.ExposureOutcome 这一口径交给 IdentityRules.ResolveExposure。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：IExposureOutcomePolicy 在工程里一个实现都没有（IdentityInstaller 此前注入 null），接口本身就要求一个实现。
//   2. 扩展不行：IdentitySettings 是可序列化的数据块，只装数值与口径、不承担决策；让它实现策略接口，
//      数据块就多了行为，「存档 / 资产要序列化规则」这类错就会长出来（同 IdentityRules 文件头的理由）。
//      IdentityRules 也不行：策略做成注入点就是为了不让规则层硬编后果（`00_功能总览.md` §5 C1 原文矛盾）。
// 出处：PRP/combat-iteration D1；用户 2026-10-14 拍板「露馅 → 进追逐，且借来的身份当场失效」（口径只写资产）。

using System;

namespace Game.Identity
{
    /// <summary>
    /// 按 <see cref="IdentitySettings.ExposureOutcome"/> 决定露馅后果。<b>每次现读</b>数值块，
    /// 所以策划改资产、测试改数值块都立刻生效，不需要重建策略。
    /// <para>
    /// 代码默认 <see cref="ExposureOutcome.None"/>：与此前「不注入策略」的结果逐字一致（后果恒为 None），
    /// 区别只在后果从此可以靠改资产切换。
    /// </para>
    /// </summary>
    public sealed class ConfiguredExposurePolicy : IExposureOutcomePolicy
    {
        private readonly IdentitySettings settings;

        /// <param name="settings">数值块，不可为空（与 <see cref="IdentityRules"/> 绑的是同一份）。</param>
        public ConfiguredExposurePolicy(IdentitySettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>
        /// 命中任意露馅原因时返回配置的口径；<paramref name="cause"/> 为 None（契约上不会发生）或配置值不认识时返回 None——
        /// 不认识的值不猜一种后果出来（同规则层「不硬编」的纪律）。
        /// </summary>
        public ExposureOutcome Decide(ExposureCause cause)
        {
            if (cause == ExposureCause.None)
            {
                return ExposureOutcome.None;
            }

            switch (settings.ExposureOutcome)
            {
                case ExposureOutcome.Death:
                    return ExposureOutcome.Death;
                case ExposureOutcome.Chase:
                    return ExposureOutcome.Chase;
                default:
                    return ExposureOutcome.None;
            }
        }
    }
}
