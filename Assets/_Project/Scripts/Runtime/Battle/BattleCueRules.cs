// 职责：把一条 BattleEvent 翻成舞台上要演的那一段（BattleCue）——谁动、加不加重、显示什么字。纯函数。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：BattleEvent.Describe 是给日志看的整句，不区分「加重版」「中央提示 / 头顶字样」，也没有演出种类。
//   2. 扩展不行：塞进 TurnBased 会让纯规则内核认识舞台；塞进表现层（带场景、带 UI）就没法在 EditMode 里逐条钉住
//      「BOSS 跳过回合闪的是 07 原文」「招式 3 是加重版」这类口径。
// 中央提示文案：BOSS 跳过回合一律用事件带出来的 HintText（= BattleHintTexts 的 07 原文，只搬不改），不在这里改写。
using Game.TurnBased;

namespace Game.Battle
{
    /// <summary>事件 → 演出的纯规则。</summary>
    public static class BattleCueRules
    {
        /// <summary>BOSS 饮酒时头顶的字样（07:80「饮酒」）。</summary>
        public const string DrinkText = "饮酒";

        /// <summary>玩家被晕眩时头顶的字样（07:82「晕眩玩家1回合」）。</summary>
        public const string StunText = "晕眩";

        /// <summary>玩家因晕眩被跳过本回合时的中央提示（照 07:71-73 的句式写，07 本身没写这一句）。</summary>
        public const string PlayerStunSkipText = "你处于晕眩状态，本回合无法行动。";

        /// <summary>BOSS 倒下后的中央提示。</summary>
        public const string VictoryText = "胜利";

        /// <summary>玩家倒下后的中央提示。</summary>
        public const string DefeatText = "战败";

        /// <summary>打到回合上限、判胜时的中央提示。</summary>
        public const string RoundLimitVictoryText = "回合用尽，判定获胜";

        /// <summary>打到回合上限、判负时的中央提示。</summary>
        public const string RoundLimitDefeatText = "回合用尽，判定落败";

        /// <summary>翻译一条事件；不认识的事件返回 <see cref="BattleCueKind.None"/>（表现层跳过，不抛）。</summary>
        public static BattleCue Cue(in BattleEvent e)
        {
            switch (e.Kind)
            {
                case BattleEventKind.SneakHealthLoss:
                    return Make(BattleCueKind.SneakHit, false, e.Amount);
                case BattleEventKind.SkillCast:
                    return Make(BattleCueKind.PlayerStrike, e.PlayerSkill == PlayerSkill.Skill3, e.Amount);
                case BattleEventKind.ItemUsed:
                    return new BattleCue(BattleCueKind.ItemUse, false, e.Amount, 0, null, e.ItemId);
                case BattleEventKind.BossTurnSkipped:
                    // 事件里没带文案时按原因补（同一张 07 原文表），仍不改写。
                    return new BattleCue(BattleCueKind.BossSkip, false, 0, 0,
                        e.HintText ?? BattleHintTexts.ForReason(e.SkipReason), null);
                case BattleEventKind.BossSkillUsed:
                    return Make(BattleCueKind.BossStrike, e.BossSkill == BossSkill.Skill3, e.Amount);
                case BattleEventKind.BossDrank:
                    return new BattleCue(BattleCueKind.BossDrink, false, e.Amount, e.SecondaryAmount, DrinkText, null);
                case BattleEventKind.PlayerStunned:
                    return new BattleCue(BattleCueKind.PlayerStun, false, e.Amount, 0, StunText, null);
                case BattleEventKind.PlayerStunnedTurnSkipped:
                    return new BattleCue(BattleCueKind.PlayerStunSkip, false, e.Amount, 0, PlayerStunSkipText, null);
                case BattleEventKind.PlayerDefeated:
                    return new BattleCue(BattleCueKind.PlayerDown, false, 0, 0, DefeatText, null);
                case BattleEventKind.BossDefeated:
                    return new BattleCue(BattleCueKind.BossDown, false, 0, 0, VictoryText, null);
                case BattleEventKind.RoundLimitReached:
                    return new BattleCue(BattleCueKind.RoundLimit, false, 0, 0,
                        e.Outcome == BattleOutcome.Victory ? RoundLimitVictoryText : RoundLimitDefeatText, null);
                default:
                    return default;
            }
        }

        private static BattleCue Make(BattleCueKind kind, bool heavy, int amount) => new BattleCue(kind, heavy, amount, 0, null, null);
    }
}
