// 职责：六种露馅方式的位标记——一次判定可能同时命中多条，用位掩码把「命中哪几条」完整带出来。
// 为什么新建：用单个枚举值只能报「第一条」，而六条的优先级原文没写（`02_身份暴露与怀疑.md:240` Q11）；
//   位掩码把全部命中都留下，让接线方自己决定先处理哪条，规则层不替策划排序。

using System;

namespace Game.Identity
{
    /// <summary>
    /// 六种露馅方式，逐条对应 <c>docs/design/features-spotlight/02_身份暴露与怀疑.md:147-158</c>
    /// 的「六种露馅方式一览」表（判定的具体实现落点在 <see cref="IdentityRules.EvaluateExposure"/>）：
    /// <list type="bullet">
    /// <item><see cref="PersonaTrait"/>：人物特性（表 `:153`，规则 R4–R11 `:96-103`）；</item>
    /// <item><see cref="Checkpoint"/>：身份核验关口（表 `:154`，规则 R12–R15 `:107-110`）；</item>
    /// <item><see cref="Ledger"/>：账簿（表 `:155`，规则 R16–R20 `:114-118`）；</item>
    /// <item><see cref="Suspicion"/>：怀疑度（表 `:156`，规则 R21–R26 `:122-127`）；</item>
    /// <item><see cref="Reveal"/>：揭露（表 `:157`，规则 R27–R30 `:131-134`）；</item>
    /// <item><see cref="Alertness"/>：警戒值（表 `:158`，规则 R32–R35 `:142-145`）。</item>
    /// </list>
    /// <para>
    /// 前五种是「规则层」的暴露，第六种是「视线层」的暴露；两层怎么叠加原文没写
    /// （`02_身份暴露与怀疑.md:145` R35、`:240` Q11），本模块只把两层都报出来，不给优先级。
    /// </para>
    /// </summary>
    [Flags]
    public enum ExposureCause
    {
        /// <summary>没有命中任何一条。</summary>
        None = 0,

        /// <summary>人物特性：所借身份 + 所在区域 + 有无目击者三条件同时成立（`02:97` R5）。</summary>
        PersonaTrait = 1 << 0,

        /// <summary>身份核验关口：身份或道具对不上（`02:107-110` R12–R15）。</summary>
        Checkpoint = 1 << 1,

        /// <summary>账簿超量（`02:114` R16）。</summary>
        Ledger = 1 << 2,

        /// <summary>怀疑度到上限（`02:126` R25 一类的「到达上限」，上限数值待定 `02:127` R26）。</summary>
        Suspicion = 1 << 3,

        /// <summary>龙族按规律揭露（`02:131` R27）。</summary>
        Reveal = 1 << 4,

        /// <summary>警戒值：红区一律、橙区只在身份不生效时（`02:142-144` R32/R34）。</summary>
        Alertness = 1 << 5,
    }
}
