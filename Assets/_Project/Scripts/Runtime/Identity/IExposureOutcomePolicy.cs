// 职责：露馅后果的策略契约——规则层把「命中哪几条」交给它，由它（关卡 / 接线方）决定死亡还是追逐。
// 为什么新建：`00_功能总览.md:286` §5 C1 是原文矛盾，不是待补的数值；
//   把它做成注入点，接线时按阶段注入不同策略即可，规则层不需要为它改一行。

namespace Game.Identity
{
    /// <summary>
    /// 露馅后果策略。<b>规则层不认识任何实现</b>：不注入（构造参数为 null）时
    /// <see cref="IdentityRules.ResolveExposure"/> 一律给出 <see cref="ExposureOutcome.None"/>，
    /// 也就是「露馅已被判定、但后果还没接线」——这比默认判死或默认追逐都安全。
    /// <para>
    /// 接线建议：阶段一 / 阶段3 / 阶段六按 sp03 注入「追逐」，若策划最终采用 sp00 的总规则
    /// 则注入「死亡」；两种口径的原文出处见 <c>docs/design/features-spotlight/02_身份暴露与怀疑.md:230</c>（Q1）。
    /// </para>
    /// </summary>
    public interface IExposureOutcomePolicy
    {
        /// <summary>
        /// 给定命中原因返回后果。<paramref name="cause"/> 一定是非 <see cref="ExposureCause.None"/> 的位掩码，
        /// 一条也没命中时策略不会被调用。
        /// </summary>
        ExposureOutcome Decide(ExposureCause cause);
    }
}
