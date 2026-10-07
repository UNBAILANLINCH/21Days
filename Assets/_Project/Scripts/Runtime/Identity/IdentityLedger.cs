// 职责：账簿——记玩家用过的身份、判超量（剧情事实键 identity.ledger.over 的来源）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：工程里没有任何「记一笔并判阈值」的通用计数设施；`NarrativeSaveData.EdgeCounters`
//      是叙事边的重复策略计数（每次进入范围 / 条件由假变真），语义与身份无关，且 Narrative 不许改。
//   2. 扩展不行：不能把计数塞进 `IdentityState`——状态是「现在是谁」，账簿是「用过谁」，
//      两者的清理时机不同（露馅清状态、阶段迁移清露馅次数，但账簿按原文跨阶段保留）。

using System;
using System.Collections.Generic;

namespace Game.Identity
{
    /// <summary>
    /// 身份账簿（阶段3 的机制）。原文：玩家行动使用的身份会被记录，超过一定数量触发固定追逐
    /// ——<c>docs/design/features-spotlight/02_身份暴露与怀疑.md:114</c>（R16，原文红字）。
    /// <para>
    /// <b>两种计量口径都留着</b>，由 <see cref="LedgerCountingMode"/> 在判定时传入，
    /// 因为原文明确没写「数的是不同身份个数还是使用次数」（`02_身份暴露与怀疑.md:115` R17，
    /// 待拍板 <c>00_功能总览.md:387</c> §8.1 #9）。
    /// </para>
    /// <para>
    /// <b>阈值不在这里</b>：上限来自 <see cref="IdentitySettings.LedgerLimit"/>，
    /// <b>≤ 0 表示关闭超量判定</b>（永不触发）。本类只记数与比较。
    /// </para>
    /// <para>
    /// 跨阶段：`01_换皮与附身.md:192` 写明「身份要跨阶段保留（R18），所以『持有过哪些身份』需要进存档」，
    /// 因此本类的内容随槽位存档（见 <see cref="IdentitySaveData"/>），阶段迁移<b>不清</b>。
    /// </para>
    /// </summary>
    public sealed class IdentityLedger
    {
        private readonly List<IdentityId> used = new List<IdentityId>();
        private readonly HashSet<IdentityId> seen = new HashSet<IdentityId>();
        private int totalUses;

        /// <summary>用过的<b>不同</b>身份个数。</summary>
        public int DistinctCount => used.Count;

        /// <summary>以身份行动的<b>总次数</b>（同一身份借用多次就记多次）。</summary>
        public int TotalUses => totalUses;

        /// <summary>用过的不同身份，按第一次出现的顺序（存档写入顺序即此顺序）。</summary>
        public IReadOnlyList<IdentityId> UsedIdentities => used;

        /// <summary>
        /// 记一笔「以该身份行动」。返回 true 表示这是一个<b>新</b>身份（不同身份个数 +1）。
        /// <para>
        /// <b>无效 id 的明确行为</b>：<see cref="IdentityId.None"/>（本体不是身份）与格式不合法的 id
        /// 一律<b>不记</b>，返回 false——本体不该进账簿，否则「以本体行动」也会把账簿撑满。
        /// </para>
        /// </summary>
        public bool Record(IdentityId id)
        {
            if (!id.IsValid)
            {
                return false;
            }

            totalUses++;
            if (seen.Add(id))
            {
                used.Add(id);
                return true;
            }

            return false;
        }

        /// <summary>按口径取当前计数。</summary>
        public int CountUnder(LedgerCountingMode mode)
            => mode == LedgerCountingMode.TotalUses ? totalUses : used.Count;

        /// <summary>
        /// 是否已经超量。<paramref name="limit"/> ≤ 0 表示关闭判定（恒 false）；
        /// 判定是「计数 &gt; 上限」，即「超过一定数量」，不是「达到」——原文用词是「超过」（`02:114` R16）。
        /// </summary>
        public bool IsOverLimit(LedgerCountingMode mode, int limit)
            => limit > 0 && CountUnder(mode) > limit;

        /// <summary>用过的身份里是否包含某个 id（供「跨阶段记得自己手上有哪些身份」这类查询）。</summary>
        public bool HasUsed(IdentityId id) => id.IsValid && seen.Contains(id);

        /// <summary>清空（原文没写能不能清、离开阶段3 是否清零，见 `02_身份暴露与怀疑.md:115` R17，待拍板 §8.1 #9）。</summary>
        public void Clear()
        {
            used.Clear();
            seen.Clear();
            totalUses = 0;
        }

        /// <summary>从存档恢复（由 <see cref="IdentityRules.Restore"/> 调用）。</summary>
        internal void Restore(IEnumerable<IdentityId> identities, int totalUseCount)
        {
            Clear();
            if (identities != null)
            {
                foreach (IdentityId id in identities)
                {
                    if (!id.IsValid)
                    {
                        continue;
                    }

                    if (seen.Add(id))
                    {
                        used.Add(id);
                    }
                }
            }

            totalUses = totalUseCount < used.Count ? used.Count : totalUseCount;
        }
    }
}
