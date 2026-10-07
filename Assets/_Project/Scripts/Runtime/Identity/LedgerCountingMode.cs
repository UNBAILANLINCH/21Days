// 职责：账簿的计量口径——数「用过的不同身份个数」还是「以身份行动的次数」。
// 为什么新建：这是原文明确没定的一个口径（`02_身份暴露与怀疑.md:115`），
//   做成枚举＋配置才能在不改代码的前提下换口径。

namespace Game.Identity
{
    /// <summary>
    /// 账簿超量判定的计量口径。原文两说、待策划拍板：
    /// <c>docs/design/features-spotlight/02_身份暴露与怀疑.md:114-115</c>（R16/R17：
    /// 「一定数量」是多少、数的是不同身份个数还是行动次数，原文都没写），
    /// 阻塞项见 <c>docs/design/features-spotlight/00_功能总览.md:387</c>（§8.1 #9）。
    /// </summary>
    public enum LedgerCountingMode
    {
        /// <summary>数用过的<b>不同</b>身份个数（同一身份反复借用只算一条）。</summary>
        DistinctIdentities = 0,

        /// <summary>数以身份行动的<b>总次数</b>（同一身份借用十次算十条）。</summary>
        TotalUses = 1,
    }
}
