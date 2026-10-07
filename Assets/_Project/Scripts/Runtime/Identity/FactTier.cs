// 职责：数值状态的档位——怀疑度与露馅次数只以 low/mid/high 三档进剧情事实表，数值本身不进表。
// 为什么新建：档位段是字典 §3.2 规定的合法值集合（只允许 low/mid/high 或 none/some/full），
//   写成一个枚举才能保证「三档互斥」这件事只有一个实现。

namespace Game.Identity
{
    /// <summary>
    /// 数值状态的档位。词典口径见 <c>ai-docs/docs/story-facts.md</c> §3.2（档位段只允许
    /// <c>low</c> / <c>mid</c> / <c>high</c> 或 <c>none</c> / <c>some</c> / <c>full</c>，同一键只用一套）。
    /// 本模块的键用第一套。
    /// <para>
    /// <see cref="None"/> 表示还没到最低档：此时三个档位键<b>都不写</b>，
    /// 也就是说「三个都为假」是合法状态，而真的时候只有一个为真。
    /// </para>
    /// </summary>
    public enum FactTier
    {
        /// <summary>未达最低档。</summary>
        None = 0,

        /// <summary>低档，键后缀 <c>.low</c>。</summary>
        Low = 1,

        /// <summary>中档，键后缀 <c>.mid</c>。</summary>
        Mid = 2,

        /// <summary>高档，键后缀 <c>.high</c>。</summary>
        High = 3,
    }
}
