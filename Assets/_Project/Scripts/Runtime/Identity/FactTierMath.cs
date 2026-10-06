// 职责：数值 → 档位的唯一实现（怀疑度、露馅次数共用），保证「三档互斥」只有一处口径。
// 为什么新建：档位既要给剧情事实键用（identity.suspicion.low|mid|high），也要给埋点用
//   （跨档位各记一条），两处各写一遍必然有一天只改一处；阈值全部由调用方传入，本身不含数值。

namespace Game.Identity
{
    /// <summary>
    /// 档位换算。口径（与 <see cref="IdentitySettings"/> 里三组阈值字段的注释一致）：
    /// <list type="number">
    /// <item>从高档往低档判，命中即停——所以非单调的阈值配置不会算出两个档位；</item>
    /// <item>阈值 ≤ 0 视为该档不启用；</item>
    /// <item>未达最低档时返回 <see cref="FactTier.None"/>，此时三个档位键都不写。</item>
    /// </list>
    /// <para>
    /// 「同一时刻只有一个档为真」由此保证：档位是当前值的纯函数，不是累加出来的若干布尔开关。
    /// 写方在状态变化时要<b>把另外两档键清掉</b>（见 <see cref="IdentityFactSnapshot.CollectTrueKeys"/>）。
    /// </para>
    /// </summary>
    public static class FactTierMath
    {
        /// <summary>按三档阈值算出当前档位（≤ 0 的阈值视为不启用）。</summary>
        public static FactTier TierOf(float value, float lowThreshold, float midThreshold, float highThreshold)
        {
            if (highThreshold > 0f && value >= highThreshold)
            {
                return FactTier.High;
            }

            if (midThreshold > 0f && value >= midThreshold)
            {
                return FactTier.Mid;
            }

            if (lowThreshold > 0f && value >= lowThreshold)
            {
                return FactTier.Low;
            }

            return FactTier.None;
        }

        /// <summary>档位对应的键后缀（不含点号）；<see cref="FactTier.None"/> 返回空串。</summary>
        public static string SuffixOf(FactTier tier)
        {
            switch (tier)
            {
                case FactTier.Low: return "low";
                case FactTier.Mid: return "mid";
                case FactTier.High: return "high";
                default: return string.Empty;
            }
        }
    }
}
