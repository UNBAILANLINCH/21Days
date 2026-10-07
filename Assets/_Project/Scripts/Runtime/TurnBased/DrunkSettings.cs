// 职责：BOSS 醉酒四档的数值参数（纯值，测试不必创建 ScriptableObject）。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:70-73`（四档区间与三条概率都是原文给的）；
// 「上限多少」「-50 是每次还是一进入就降」原文没写，占位并等 C91。

namespace Game.TurnBased
{
    /// <summary>醉酒四档的数值。</summary>
    public readonly struct DrunkSettings
    {
        /// <summary>构造。</summary>
        public DrunkSettings(
            int tipsyThreshold,
            int drunkThreshold,
            int deadDrunkThreshold,
            int maxDrunkValue,
            int tipsySkipPercent,
            int drunkSkipPercent,
            int deadDrunkSkipPercent,
            int deadDrunkDropValue,
            int deadDrunkDurationRounds,
            bool deadDrunkLowersOncePerEntry)
        {
            TipsyThreshold = tipsyThreshold;
            DrunkThreshold = drunkThreshold;
            DeadDrunkThreshold = deadDrunkThreshold;
            MaxDrunkValue = maxDrunkValue;
            TipsySkipPercent = tipsySkipPercent;
            DrunkSkipPercent = drunkSkipPercent;
            DeadDrunkSkipPercent = deadDrunkSkipPercent;
            DeadDrunkDropValue = deadDrunkDropValue;
            DeadDrunkDurationRounds = deadDrunkDurationRounds;
            DeadDrunkLowersOncePerEntry = deadDrunkLowersOncePerEntry;
        }

        /// <summary>微醺档下界（07:71「50-79」→ 50）。</summary>
        public int TipsyThreshold { get; }

        /// <summary>薄醉档下界（07:72「80-99」→ 80）。</summary>
        public int DrunkThreshold { get; }

        /// <summary>酩酊档下界（07:73「100」→ 100）。</summary>
        public int DeadDrunkThreshold { get; }

        /// <summary>醉酒值上限（原文没写，占位 100，等 C91）。</summary>
        public int MaxDrunkValue { get; }

        /// <summary>微醺跳过概率（07:71 → 20）。</summary>
        public int TipsySkipPercent { get; }

        /// <summary>薄醉跳过概率（07:72 → 40）。</summary>
        public int DrunkSkipPercent { get; }

        /// <summary>酩酊跳过概率（07:73 → 100）。</summary>
        public int DeadDrunkSkipPercent { get; }

        /// <summary>进入酩酊时醉酒值下降量（07:73 → 50）。</summary>
        public int DeadDrunkDropValue { get; }

        /// <summary>酩酊持续回合数（07:73 → 2）。</summary>
        public int DeadDrunkDurationRounds { get; }

        /// <summary>「下降50」是进入酩酊时降一次（true）还是每次酩酊回合都降（false）；原文没写，占位 true。</summary>
        public bool DeadDrunkLowersOncePerEntry { get; }

        /// <summary>本工程当前的占位默认值（与 `TurnBasedConfig` 的字段默认一致）。</summary>
        public static DrunkSettings PlaceholderDefault =>
            new DrunkSettings(50, 80, 100, 100, 20, 40, 100, 50, 2, true);
    }
}
