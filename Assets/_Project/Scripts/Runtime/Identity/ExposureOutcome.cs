// 职责：露馅之后的后果——死亡还是追逐。规则层不选，交给注入的策略。
// 为什么新建：原文自相矛盾（`00_功能总览.md:286` §5 C1：sp00 写死亡、sp03 写追逐），
//   硬编任何一边都会把另一边写死；枚举放在这里、决定放在 IExposureOutcomePolicy。

namespace Game.Identity
{
    /// <summary>
    /// 露馅的结果。取值只有「什么都没发生」「死亡」「追逐」三种，<b>不含各阶段的细节</b>
    /// （追谁来追、死在哪里重来，原文都没写：`02_身份暴露与怀疑.md:230` Q1、`:232` Q3）。
    /// <para>
    /// 规则的出处：`docs/design/features-spotlight/00_功能总览.md:286`（§5 C1「被发现之后：死亡还是追逐」
    /// 是原文矛盾），阻塞项 <c>00_功能总览.md:381</c>（§8.1 #3）。
    /// 本模块按任务要求<b>不硬编</b>：由 <see cref="IExposureOutcomePolicy"/> 注入。
    /// </para>
    /// </summary>
    public enum ExposureOutcome
    {
        /// <summary>没有后果（没露馅，或没有注入策略）。</summary>
        None = 0,

        /// <summary>变回原主后死亡（sp00 总规则，`02_身份暴露与怀疑.md:90` R1）。</summary>
        Death = 1,

        /// <summary>触发追逐（sp03 阶段3 / 阶段六写法，`02:118` R20、`:122` R21）。</summary>
        Chase = 2,
    }
}
