// 职责：战斗界面的纯判定与文案——招式格亮 / 暗、怒气槽满、道具格显隐与明暗、玩家状态格、BOSS 状态字、剩余回合、
//   回合提示、各处鼠标悬停详情（数值一律现读配置，不写死）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：PlayerSkillRules.Describe / BossSkillRules.Describe 是写死数值的日志描述（「消耗 1 怒气」），悬停详情要按
//      TurnBasedConfig 的真实数值拼；BattleItemSlot.CanUse 只看件数与本场用过，不看「现在是不是能用道具的时点」。
//   2. 扩展不行：塞进 TurnBased 会让纯规则内核长出界面文案；塞进 BattleView（MonoBehaviour）就没法在 EditMode 里不建预制体地测。
// 07 画面表现（07_回合制作战文档.md:40-44）对应：招式够怒气高亮否则置暗（SkillLit）；怒气满槽更鲜艳（RageFull）；
//   道具可用高亮、用过置暗、未拥有不显示（ItemLook，置暗只看 UsedThisBattle——W1 结论：别看拒绝原因）；
//   玩家状态右下角显示剩余回合（RemainingRoundsText）；状态 / 招式 / 道具悬停弹详情（*Tooltip）。
using System.Collections.Generic;
using System.Text;
using Game.Core.Simulation;
using Game.TurnBased;

namespace Game.Battle
{
    /// <summary>战斗界面纯规则（无状态，EditMode 可测）。</summary>
    public static class BattleHudRules
    {
        /// <summary>
        /// 回合上限为 0（不限）时「剩余回合」显示的字。选「显示不限」而不是隐藏：07:43 明写状态区右下角要有剩余回合，
        /// 白盒阶段这一格空着与「没接线」分不出来；C91 定了上限后同一格直接变数字。
        /// </summary>
        public const string UnlimitedRoundsText = "不限";

        /// <summary>界面上三个招式格的顺序（07:48「玩家共三个招式」）。</summary>
        public static readonly IReadOnlyList<PlayerSkill> Skills = new[] { PlayerSkill.Skill1, PlayerSkill.Skill2, PlayerSkill.Skill3 };

        // ── 招式格 ────────────────────────────────────────────────────────────

        /// <summary>招式格标题（07 原文就叫「招式1 / 2 / 3」，正式招式名等策划）。</summary>
        public static string SkillTitle(PlayerSkill skill) => "招式" + (int)skill;

        /// <summary>招式格副标题：怒气消耗（数值取配置）。</summary>
        public static string SkillCostText(PlayerSkill skill, in PlayerSkillSettings settings) =>
            "怒气 " + PlayerSkillRules.RageCost(skill, settings);

        /// <summary>现在能不能出招：玩家回合、没倒下、没被晕眩、本回合还没出过招（同 <see cref="PlayerBattleRules.CanCast"/>）。</summary>
        public static bool CanCastNow(BattleSession session) =>
            session != null && session.Phase == BattlePhase.PlayerTurn && session.Player.CanCast;

        /// <summary>招式格高亮：能出招且怒气够（07:40）。不是玩家回合 / 晕眩 / 已出过招时一律置暗。</summary>
        public static bool SkillLit(int rage, int rageCost, bool canCastNow) => canCastNow && rage >= rageCost;

        /// <summary>按会话判一个招式格亮不亮。</summary>
        public static bool SkillLit(BattleSession session, PlayerSkill skill, in PlayerSkillSettings settings) =>
            session != null && SkillLit(session.Player.Rage, PlayerSkillRules.RageCost(skill, settings), CanCastNow(session));

        // ── 怒气槽 ────────────────────────────────────────────────────────────

        /// <summary>怒气满（07:41「怒气值满时，怒气槽颜色会更鲜艳」）。上限非正视为永不满。</summary>
        public static bool RageFull(int rage, int rageMax) => rageMax > 0 && rage >= rageMax;

        // ── 道具格 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 现在是不是能用道具的时点：玩家回合、还没出招（07:58「施放招式之前」）、没倒下，
        /// 且不是「晕眩直接跳过」口径下的晕眩回合（同 <see cref="BattleSession.TryUseItem"/> 的判定顺序）。
        /// </summary>
        public static bool CanUseItemsNow(BattleSession session, StunTurnPolicy stunPolicy)
        {
            if (session == null || session.Phase != BattlePhase.PlayerTurn) return false;
            PlayerBattleRules player = session.Player;
            if (player.IsDefeated || player.HasCastThisTurn) return false;
            return !(player.CannotActThisTurn && stunPolicy == StunTurnPolicy.SkipTurn);
        }

        /// <summary>
        /// 道具格样子（07:42）：未拥有（件数 0 且本场没用过）→ 不显示；本场用过 → 置暗；件数够且时点对 → 高亮；其余置暗。
        /// 置暗只看 <see cref="BattleItemSlot.UsedThisBattle"/>，不看上一条指令的拒绝原因（W1 结论）。
        /// </summary>
        public static BattleItemLook ItemLook(in BattleItemSlot slot, bool canUseItemsNow)
        {
            if (!slot.UsedThisBattle && slot.Count <= 0) return BattleItemLook.Hidden;
            if (slot.UsedThisBattle) return BattleItemLook.Dim;
            return slot.CanUse && canUseItemsNow ? BattleItemLook.Lit : BattleItemLook.Dim;
        }

        /// <summary>
        /// 要不要亮出「结束回合」：玩家回合、还没出招、却出不了招（晕眩回合按 ItemsOnly 口径留在玩家这边时，只能用道具或结束回合）。
        /// 占位口径 SkipTurn 下晕眩回合会被规则直接跳过，这个按钮不会出现。
        /// </summary>
        public static bool NeedsEndTurn(BattleSession session) =>
            session != null && session.Phase == BattlePhase.PlayerTurn && !session.Player.HasCastThisTurn
            && !session.Player.IsDefeated && !session.Player.CanCast;

        // ── 回合 / 状态 ───────────────────────────────────────────────────────

        /// <summary>剩余回合（07:43）：上限 ≤ 0 → 「不限」；否则 max(0, 上限 − 已打完回合)。</summary>
        public static string RemainingRoundsText(int roundLimit, int completedRounds) =>
            roundLimit <= 0 ? UnlimitedRoundsText : GameMath.Max(0, roundLimit - completedRounds).ToString();

        /// <summary>战斗结束后左上角的字。</summary>
        public const string BattleOverText = "战斗结束";

        /// <summary>左上角的回合提示，按会话现读（等玩家指令时用）。</summary>
        public static string TurnText(BattleSession session)
        {
            if (session == null) return string.Empty;
            if (session.Phase == BattlePhase.Ended) return BattleOverText;
            return TurnText(session.CompletedRounds + 1, session.Phase == BattlePhase.PlayerTurn);
        }

        /// <summary>
        /// 刚演完的这批动作属于第几回合：玩家这一批 = 已打完回合 + 1；BOSS 这一批里会话已把回合数 +1（玩家被打倒时不加，见
        /// BattleSession.RunBossTurn），所以是「已打完回合」本身，玩家倒下时再 +1。
        /// </summary>
        public static int PlayedRound(BattleSession session, bool bossBatch)
        {
            if (session == null) return 1;
            int completed = session.CompletedRounds;
            int round = bossBatch && !session.Player.IsDefeated ? completed : completed + 1;
            return GameMath.Max(1, round);
        }

        /// <summary>
        /// 左上角的回合提示，按给定回合数与归属拼（播放演出时用：BOSS 回合播放时会话已经回到玩家回合、回合数也已 +1，不能现读会话）。
        /// </summary>
        public static string TurnText(int round, bool playerTurn) =>
            "第 " + GameMath.Max(1, round) + " 回合 · " + (playerTurn ? "你的回合" : "敌方回合");

        /// <summary>
        /// BOSS 血条上方的状态字（07:66 / 07:70）：醉酒档位名，正常态不显示；挂着减疗时追加「减疗」。全都没有时返回空串（界面藏起这一行）。
        /// </summary>
        public static string BossStatusText(DrunkTier tier, int healReductionPercent)
        {
            bool hasTier = tier != DrunkTier.Normal;
            bool hasReduction = healReductionPercent > 0;
            if (!hasTier && !hasReduction) return string.Empty;
            if (!hasReduction) return DrunkTierRules.Name(tier);
            return hasTier ? DrunkTierRules.Name(tier) + " · 减疗" : "减疗";
        }

        /// <summary>玩家状态格（覆盖写进 <paramref name="into"/>）：晕眩（角标 = 还剩几回合不能行动）、额外伤害（角标 = 还剩几次）。</summary>
        public static void CollectPlayerStatuses(PlayerBattleRules player, in BattleSettings settings, List<BattleStatusEntry> into)
        {
            into.Clear();
            if (player == null) return;

            // 被晕眩的回合本身也算一回合：「下回合起还剩几回合」+「本回合正被晕眩」。
            int stunRounds = player.StunRoundsRemaining + (player.CannotActThisTurn ? 1 : 0);
            if (stunRounds > 0)
                into.Add(new BattleStatusEntry(BattleStatusKind.Stunned, "晕", stunRounds, StunTooltip(stunRounds, settings.Flow.StunTurnPolicy)));

            int charges = player.ExtraDamageCharges;
            if (charges > 0 && settings.PlayerSkills.Skill3ExtraDamage > 0)
                into.Add(new BattleStatusEntry(BattleStatusKind.ExtraDamage, "伤", charges, ExtraDamageTooltip(charges, settings.PlayerSkills)));
        }

        /// <summary>生命 / 醉酒条上的数字。</summary>
        public static string BarText(int value, int max) => value + " / " + max;

        // ── 悬停详情（数值全部现读配置） ──────────────────────────────────────

        /// <summary>招式悬停详情（07:50-54 的描述 + 配置里的消耗 / 伤害 / 效果数值）。</summary>
        public static string SkillTooltip(PlayerSkill skill, in PlayerSkillSettings s, int extraDamageCharges)
        {
            var text = new StringBuilder();
            text.Append("<b>").Append(SkillTitle(skill)).Append("</b>　[").Append((int)skill).Append("]\n");
            text.Append("怒气消耗：").Append(PlayerSkillRules.RageCost(skill, s));
            int gain = PlayerSkillRules.RageGain(skill, s);
            if (gain > 0) text.Append("　使用后怒气 +").Append(gain);
            text.Append("\n伤害：").Append(PlayerSkillRules.BaseDamage(skill, s));
            switch (skill)
            {
                case PlayerSkill.Skill1:
                    text.Append("\n闪身到敌人身前直接攻击，攻击结束退回原位。");
                    break;
                case PlayerSkill.Skill2:
                    text.Append("\n对方治疗效果 -").Append(s.Skill2HealReductionPercent).Append('%')
                        .Append(s.Skill2HealReductionRounds > 0 ? "，持续 " + s.Skill2HealReductionRounds + " 个敌方回合" : "，持续到战斗结束");
                    text.Append("\n闪身到敌人身前蓄力重击，攻击结束退回原位。");
                    break;
                case PlayerSkill.Skill3:
                    text.Append("\n之后 ").Append(s.Skill3ExtraDamageAttacks).Append(" 次攻击各 +").Append(s.Skill3ExtraDamage).Append(" 伤害");
                    text.Append("\n重击直接刺入敌人身体，攻击结束退回原位。");
                    break;
            }

            if (extraDamageCharges > 0 && s.Skill3ExtraDamage > 0)
                text.Append("\n本次附带额外伤害 +").Append(s.Skill3ExtraDamage).Append("（还剩 ").Append(extraDamageCharges).Append(" 次）");
            return text.ToString();
        }

        /// <summary>道具悬停详情：名字、件数、效果（占位效果取 BattleItemSettings）、每场限用与本场是否用过。</summary>
        public static string ItemTooltip(in BattleItemSlot slot, in BattleItemSettings items, bool oncePerBattle)
        {
            var text = new StringBuilder();
            text.Append("<b>").Append(slot.DisplayName).Append("</b>　×").Append(slot.Count).Append('\n');
            if (items.Heals(slot.ItemId))
            {
                text.Append("回复 ").Append(items.HealBase == HealthPercentBase.CurrentHealth ? "当前生命" : "生命上限")
                    .Append("的 ").Append(items.HealPercent).Append('%');
            }
            else
            {
                text.Append("战斗中暂无效果（占位，等 C91）");
            }

            text.Append("\n出招前使用，不占回合");
            if (oncePerBattle) text.Append("\n每场战斗限用一次");
            if (slot.UsedThisBattle) text.Append("\n本场已使用");
            return text.ToString();
        }

        /// <summary>BOSS 状态悬停详情：醉酒值与档位效果（07:70-73）、减疗、下次普攻加成（07:80）。</summary>
        public static string BossTooltip(BossBattleRules boss, in BattleSettings settings)
        {
            if (boss == null) return string.Empty;
            DrunkSettings drunk = settings.Drunk;
            DrunkTier tier = boss.Tier;
            var text = new StringBuilder();
            text.Append("<b>").Append(DrunkTierRules.Name(tier)).Append("</b>　醉酒 ")
                .Append(boss.DrunkValue).Append(" / ").Append(drunk.MaxDrunkValue).Append('\n');
            switch (tier)
            {
                case DrunkTier.Normal:
                    text.Append("无特殊效果");
                    break;
                case DrunkTier.DeadDrunk:
                    text.Append("敌方回合必定无法行动；醉酒值 -").Append(drunk.DeadDrunkDropValue)
                        .Append("，持续 ").Append(drunk.DeadDrunkDurationRounds).Append(" 回合");
                    break;
                default:
                    text.Append("敌方回合有 ").Append(DrunkTierRules.SkipChancePercent(tier, drunk)).Append("% 概率无法行动");
                    break;
            }

            if (boss.Drunk.DeadDrunkRoundsRemaining > 0)
                text.Append("\n酩酊还剩 ").Append(boss.Drunk.DeadDrunkRoundsRemaining).Append(" 回合");
            if (boss.HealReductionPercent > 0)
            {
                text.Append("\n受到治疗 -").Append(boss.HealReductionPercent).Append('%')
                    .Append(boss.HealReductionIsPermanent ? "，持续到战斗结束" : "，还剩 " + boss.HealReductionRoundsRemaining + " 回合");
            }

            if (boss.NextSkill1DamageBonusPending)
                text.Append("\n下次普通攻击伤害 +").Append(settings.BossSkills.Skill2NextSkill1DamageBonusPercent).Append('%');
            return text.ToString();
        }

        /// <summary>怒气槽悬停详情。</summary>
        public static string RageTooltip(int rage, in PlayerSkillSettings s) =>
            "<b>怒气</b>　" + rage + " / " + s.RageMax + "\n招式1 每次 +" + s.Skill1RageGain + "；攒满时怒气槽更鲜艳";

        /// <summary>剩余回合悬停详情。</summary>
        public static string RoundsTooltip(int roundLimit, int completedRounds) =>
            roundLimit <= 0
                ? "<b>剩余回合</b>\n本场不限回合数（占位，等 C91）"
                : "<b>剩余回合</b>\n还剩 " + RemainingRoundsText(roundLimit, completedRounds) + " 回合，用尽按配置判定胜负";

        private static string StunTooltip(int rounds, StunTurnPolicy policy) =>
            "<b>晕眩</b>\n还有 " + rounds + " 回合无法出招" +
            (policy == StunTurnPolicy.SkipTurn ? "，这些回合直接跳过" : "，只能用道具或结束回合");

        private static string ExtraDamageTooltip(int charges, in PlayerSkillSettings s) =>
            "<b>额外伤害</b>\n接下来 " + charges + " 次攻击各 +" + s.Skill3ExtraDamage + " 伤害（招式3 留下）";
    }
}
