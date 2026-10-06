// 职责：醉酒档位的纯函数——阈值换算、跳过概率、掷骰子。
//
// 概率**只走注入的 IRandomStream**（`Game.Core.Simulation`，本模块只读引用、不改 Core）：
// 工程的重放系统靠它保证同种子同序列，用 UnityEngine.Random 会让回放对不上
// （`Assets/_Project/Scripts/Core/Simulation/IRandomStream.cs:1-7`）。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:70-73`。

using Game.Core.Simulation;

namespace Game.TurnBased
{
    /// <summary>醉酒档位的纯函数规则。</summary>
    public static class DrunkTierRules
    {
        /// <summary>按醉酒值换算档位（07:70-73 的四段区间）。</summary>
        public static DrunkTier Resolve(int drunkValue, in DrunkSettings settings)
        {
            if (drunkValue >= settings.DeadDrunkThreshold)
            {
                return DrunkTier.DeadDrunk;
            }

            if (drunkValue >= settings.DrunkThreshold)
            {
                return DrunkTier.Drunk;
            }

            return drunkValue >= settings.TipsyThreshold ? DrunkTier.Tipsy : DrunkTier.Normal;
        }

        /// <summary>该档位跳过回合的概率（百分比）：0 / 20 / 40 / 100（07:70-73）。</summary>
        public static int SkipChancePercent(DrunkTier tier, in DrunkSettings settings)
        {
            switch (tier)
            {
                case DrunkTier.Tipsy:
                    return settings.TipsySkipPercent;
                case DrunkTier.Drunk:
                    return settings.DrunkSkipPercent;
                case DrunkTier.DeadDrunk:
                    return settings.DeadDrunkSkipPercent;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// 这一回合怪物要不要跳过。
        /// <para>
        /// 概率为 0 或 100 时**不消耗随机数**：0% 与 100% 是确定事件，抽一次只会让同一场战斗里
        /// 后面所有随机判定的位置跟着漂移（正常态每回合都跳一次骰子尤其明显）。
        /// </para>
        /// </summary>
        public static bool ShouldSkipTurn(DrunkTier tier, in DrunkSettings settings, IRandomStream random)
        {
            int chance = SkipChancePercent(tier, settings);
            if (chance <= 0)
            {
                return false;
            }

            if (chance >= 100)
            {
                return true;
            }

            return random.Range(0, 100) < chance;
        }

        /// <summary>档位对应的跳过原因（给表现层的提示用）。</summary>
        public static DrunkSkipReason ReasonOf(DrunkTier tier)
        {
            switch (tier)
            {
                case DrunkTier.Tipsy:
                    return DrunkSkipReason.Tipsy;
                case DrunkTier.Drunk:
                    return DrunkSkipReason.Drunk;
                case DrunkTier.DeadDrunk:
                    return DrunkSkipReason.DeadDrunk;
                default:
                    return DrunkSkipReason.None;
            }
        }

        /// <summary>档位的中文名（07:70-73 的原文用词）。</summary>
        public static string Name(DrunkTier tier)
        {
            switch (tier)
            {
                case DrunkTier.Tipsy:
                    return "微醺";
                case DrunkTier.Drunk:
                    return "薄醉";
                case DrunkTier.DeadDrunk:
                    return "酩酊";
                default:
                    return "正常";
            }
        }
    }
}
