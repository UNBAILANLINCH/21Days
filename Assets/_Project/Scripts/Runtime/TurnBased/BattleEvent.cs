// 职责：一条战斗事件。字段按事件种类解释，取值见 `BattleEventKind` 每个成员的说明。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:34-44`、:70-84。

namespace Game.TurnBased
{
    /// <summary>一条战斗事件（值类型，只在本次结算的结果里活着）。</summary>
    public readonly struct BattleEvent
    {
        private BattleEvent(
            BattleEventKind kind,
            int amount,
            int secondaryAmount,
            PlayerSkill playerSkill,
            BossSkill bossSkill,
            DrunkTier drunkTier,
            DrunkSkipReason skipReason,
            string hintText,
            string itemId,
            BattleOutcome outcome)
        {
            Kind = kind;
            Amount = amount;
            SecondaryAmount = secondaryAmount;
            PlayerSkill = playerSkill;
            BossSkill = bossSkill;
            DrunkTier = drunkTier;
            SkipReason = skipReason;
            HintText = hintText;
            ItemId = itemId;
            Outcome = outcome;
        }

        /// <summary>事件种类。</summary>
        public BattleEventKind Kind { get; }

        /// <summary>主数值（伤害 / 扣血 / 加醉酒 / 晕眩回合数，按种类解释）。</summary>
        public int Amount { get; }

        /// <summary>次数值（目前只有 BOSS 饮酒的实际回血用）。</summary>
        public int SecondaryAmount { get; }

        /// <summary>涉及的玩家招式。</summary>
        public PlayerSkill PlayerSkill { get; }

        /// <summary>涉及的 BOSS 招式。</summary>
        public BossSkill BossSkill { get; }

        /// <summary>涉及的醉酒档位（跳过回合时是回合开始那一刻的档位）。</summary>
        public DrunkTier DrunkTier { get; }

        /// <summary>跳过回合的原因。</summary>
        public DrunkSkipReason SkipReason { get; }

        /// <summary>屏幕中央要闪现的文案（只有跳过回合的事件非 null）。</summary>
        public string HintText { get; }

        /// <summary>涉及的道具 id。</summary>
        public string ItemId { get; }

        /// <summary>战斗结果（只有回合上限事件带上判出来的胜负）。</summary>
        public BattleOutcome Outcome { get; }

        /// <summary>偷袭开战扣血（07:18）。</summary>
        public static BattleEvent SneakHealthLoss(int amount) =>
            new BattleEvent(BattleEventKind.SneakHealthLoss, amount, 0, PlayerSkill.None, BossSkill.None,
                DrunkTier.Normal, DrunkSkipReason.None, null, null, BattleOutcome.None);

        /// <summary>玩家施放招式。</summary>
        public static BattleEvent SkillCast(PlayerSkill skill, int damage) =>
            new BattleEvent(BattleEventKind.SkillCast, damage, 0, skill, BossSkill.None,
                DrunkTier.Normal, DrunkSkipReason.None, null, null, BattleOutcome.None);

        /// <summary>玩家用掉一件道具；<paramref name="healed"/> = 道具效果实际回复的生命（没有效果时 0）。</summary>
        public static BattleEvent ItemUsed(string itemId, int healed = 0) =>
            new BattleEvent(BattleEventKind.ItemUsed, healed, 0, PlayerSkill.None, BossSkill.None,
                DrunkTier.Normal, DrunkSkipReason.None, null, itemId, BattleOutcome.None);

        /// <summary>怪物醉得跳过回合（带屏幕中央提示文案）。</summary>
        public static BattleEvent BossTurnSkipped(DrunkTier tier, DrunkSkipReason reason, string hintText) =>
            new BattleEvent(BattleEventKind.BossTurnSkipped, 0, 0, PlayerSkill.None, BossSkill.None,
                tier, reason, hintText, null, BattleOutcome.None);

        /// <summary>BOSS 出招。</summary>
        public static BattleEvent BossSkillUsed(BossSkill skill, int damage) =>
            new BattleEvent(BattleEventKind.BossSkillUsed, damage, 0, PlayerSkill.None, skill,
                DrunkTier.Normal, DrunkSkipReason.None, null, null, BattleOutcome.None);

        /// <summary>BOSS 饮酒（07:80）：加醉酒 + 回血。</summary>
        public static BattleEvent BossDrank(int drunkAdded, int healedAmount) =>
            new BattleEvent(BattleEventKind.BossDrank, drunkAdded, healedAmount, PlayerSkill.None, BossSkill.Skill2,
                DrunkTier.Normal, DrunkSkipReason.None, null, null, BattleOutcome.None);

        /// <summary>玩家被晕眩（07:82）。</summary>
        public static BattleEvent PlayerStunned(int rounds) =>
            new BattleEvent(BattleEventKind.PlayerStunned, rounds, 0, PlayerSkill.None, BossSkill.None,
                DrunkTier.Normal, DrunkSkipReason.None, null, null, BattleOutcome.None);

        /// <summary>玩家因晕眩被跳过一整个回合。</summary>
        public static BattleEvent PlayerStunnedTurnSkipped(int roundsRemaining) =>
            new BattleEvent(BattleEventKind.PlayerStunnedTurnSkipped, roundsRemaining, 0, PlayerSkill.None, BossSkill.None,
                DrunkTier.Normal, DrunkSkipReason.None, null, null, BattleOutcome.None);

        /// <summary>玩家被打倒。</summary>
        public static BattleEvent PlayerDefeated() =>
            new BattleEvent(BattleEventKind.PlayerDefeated, 0, 0, PlayerSkill.None, BossSkill.None,
                DrunkTier.Normal, DrunkSkipReason.None, null, null, BattleOutcome.Defeat);

        /// <summary>BOSS 被打倒。</summary>
        public static BattleEvent BossDefeated() =>
            new BattleEvent(BattleEventKind.BossDefeated, 0, 0, PlayerSkill.None, BossSkill.None,
                DrunkTier.Normal, DrunkSkipReason.None, null, null, BattleOutcome.Victory);

        /// <summary>打到回合上限。</summary>
        public static BattleEvent RoundLimitReached(BattleOutcome outcome) =>
            new BattleEvent(BattleEventKind.RoundLimitReached, 0, 0, PlayerSkill.None, BossSkill.None,
                DrunkTier.Normal, DrunkSkipReason.None, null, null, outcome);

        /// <summary>中文描述，用于日志与测试失败信息。</summary>
        public string Describe()
        {
            switch (Kind)
            {
                case BattleEventKind.SneakHealthLoss:
                    return "偷袭开战：BOSS 生命 -" + Amount;
                case BattleEventKind.SkillCast:
                    return "玩家施放 " + PlayerSkillRules.Describe(PlayerSkill) + "，伤害 " + Amount;
                case BattleEventKind.ItemUsed:
                    return Amount > 0 ? "使用道具：" + ItemId + "，回复生命 " + Amount : "使用道具：" + ItemId;
                case BattleEventKind.BossTurnSkipped:
                    return "怪物跳过回合（" + DrunkTierRules.Name(DrunkTier) + "）：" + HintText;
                case BattleEventKind.BossSkillUsed:
                    return BossSkillRules.Describe(BossSkill) + "，伤害 " + Amount;
                case BattleEventKind.BossDrank:
                    return "BOSS 饮酒：醉酒 +" + Amount + "，回血 " + SecondaryAmount;
                case BattleEventKind.PlayerStunned:
                    return "玩家被晕眩 " + Amount + " 回合";
                case BattleEventKind.PlayerStunnedTurnSkipped:
                    return "玩家因晕眩跳过本回合";
                case BattleEventKind.PlayerDefeated:
                    return "玩家被打倒";
                case BattleEventKind.BossDefeated:
                    return "BOSS 被打倒";
                case BattleEventKind.RoundLimitReached:
                    return "打到回合上限，判定：" + Outcome;
                default:
                    return "无事件";
            }
        }
    }
}
