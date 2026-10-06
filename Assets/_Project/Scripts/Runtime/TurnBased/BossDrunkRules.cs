// 职责：BOSS 醉酒值本身的状态机——继承、累加、封顶、以及每个怪物回合开始时的四档结算。
//
// 为什么与 BossBattleRules 分开：醉酒值既能独立测（阈值边界、概率、酩酊的 -50 与持续 2 回合），
// 也是「战斗外灌酒 → 战斗内继承」这条链路的唯一接口（07:66），单独一层更清楚。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:66`（进战斗继承战斗外醉酒值）、:70-73（四档）、
// :80（BOSS 招式 2 饮酒 +30醉酒值）。

using Game.Core.Simulation;

namespace Game.TurnBased
{
    /// <summary>BOSS 醉酒值状态机。只被 `BattleSession` 驱动。</summary>
    public sealed class BossDrunkRules
    {
        private readonly DrunkSettings settings;
        private int value;
        private int deadDrunkRoundsRemaining;

        /// <summary>
        /// 用「战斗外的醉酒值」构造（07:66 继承）。数值会被钳制到 0..上限。
        /// </summary>
        public BossDrunkRules(int carriedDrunkValue, in DrunkSettings settings)
        {
            this.settings = settings;
            value = GameMath.Clamp(carriedDrunkValue, 0, settings.MaxDrunkValue);
        }

        /// <summary>当前醉酒值。</summary>
        public int Value => value;

        /// <summary>当前档位（07:70-73）。</summary>
        public DrunkTier Tier => DrunkTierRules.Resolve(value, settings);

        /// <summary>酩酊状态还剩几个怪物回合（0 = 不在酩酊持续期）。</summary>
        public int DeadDrunkRoundsRemaining => deadDrunkRoundsRemaining;

        /// <summary>本档位跳过回合的概率（百分比）。</summary>
        public int SkipChancePercent => DrunkTierRules.SkipChancePercent(Tier, settings);

        /// <summary>饮酒等外部来源增加醉酒值，封顶在上限（07:80「增加30醉酒值」）。</summary>
        /// <returns>实际增加的量（封顶后可能少于传入值）。</returns>
        public int Add(int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            int before = value;
            value = GameMath.Clamp(value + amount, 0, settings.MaxDrunkValue);
            return value - before;
        }

        /// <summary>直接设置醉酒值（战斗外灌酒 / 调试用），会被钳制。</summary>
        public void Set(int newValue) => value = GameMath.Clamp(newValue, 0, settings.MaxDrunkValue);

        /// <summary>
        /// 怪物回合开始时的醉酒结算（07:70-73）。
        /// <para>
        /// 顺序：先看是不是在酩酊持续期（持续期内一律 100% 跳过，不再重掷骰子）；
        /// 再看本回合是否刚进酩酊（进则 -50、把持续期设为配置的 2 回合、本回合算第一回合）；
        /// 否则按档位概率掷骰子（微醺 20% / 薄醉 40% / 正常必定不跳过）。
        /// </para>
        /// </summary>
        public DrunkTurnOutcome BeginMonsterTurn(IRandomStream random)
        {
            DrunkTier tierAtTurnStart = Tier;

            if (deadDrunkRoundsRemaining > 0)
            {
                deadDrunkRoundsRemaining--;
                bool dropped = false;
                if (!settings.DeadDrunkLowersOncePerEntry && settings.DeadDrunkDropValue > 0)
                {
                    value = GameMath.Clamp(value - settings.DeadDrunkDropValue, 0, settings.MaxDrunkValue);
                    dropped = true;
                }

                return DrunkTurnOutcome.Skip(
                    DrunkSkipReason.DeadDrunk,
                    tierAtTurnStart,
                    value,
                    dropped,
                    deadDrunkRoundsRemaining,
                    BattleHintTexts.DeadDrunkSkip);
            }

            if (tierAtTurnStart == DrunkTier.DeadDrunk)
            {
                if (settings.DeadDrunkDropValue > 0)
                {
                    value = GameMath.Clamp(value - settings.DeadDrunkDropValue, 0, settings.MaxDrunkValue);
                }

                // 配置的「持续 2 回合」包含本回合，所以这里先立好总数再扣掉本回合。
                deadDrunkRoundsRemaining = settings.DeadDrunkDurationRounds - 1;
                if (deadDrunkRoundsRemaining < 0)
                {
                    deadDrunkRoundsRemaining = 0;
                }

                return DrunkTurnOutcome.Skip(
                    DrunkSkipReason.DeadDrunk,
                    tierAtTurnStart,
                    value,
                    settings.DeadDrunkDropValue > 0,
                    deadDrunkRoundsRemaining,
                    BattleHintTexts.DeadDrunkSkip);
            }

            if (DrunkTierRules.ShouldSkipTurn(tierAtTurnStart, settings, random))
            {
                DrunkSkipReason reason = DrunkTierRules.ReasonOf(tierAtTurnStart);
                return DrunkTurnOutcome.Skip(
                    reason,
                    tierAtTurnStart,
                    value,
                    false,
                    0,
                    BattleHintTexts.ForReason(reason));
            }

            return DrunkTurnOutcome.Act(tierAtTurnStart, value);
        }
    }
}
