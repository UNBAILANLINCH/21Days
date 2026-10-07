// 职责：一场回合制战斗的驱动器——按 07 二、战斗过程的顺序推进回合，并在每次动作后把事件摆出来。
//
// 流程（`docs/design/spotlight/07_回合制作战文档.md:46-58`）：
//   玩家回合 →（可选）用道具 → 选招式 → 结算伤害 → 回位 → 敌方回合
// 其中「回位」是表现，规则层只要在施放招式后把回合交给敌方（07:56）。
//
// 为什么新建（复用 → 扩展 → 新建）：
// - 复用：工程里没有任何回合制 / 怒气 / 招式流程的类型（`09_BOSS战.md:278`「工程侧一行代码都没有」）。
// - 扩展：`Runtime/Monster/EncounterStep`（遭遇流程）管的是敌我交互与收尾，不是回合；本次也不许改它。
// - 新建：以上两条都不成立，故新建本类。它只做规则推进，不做界面、不读场景、不写剧情状态。
//
// 概率（跳过回合、选招）只走注入的 IRandomStream，保证可测试、可回放。

using System.Collections.Generic;
using Game.Core.Simulation;

namespace Game.TurnBased
{
    /// <summary>一场战斗。只被接线侧驱动，本身不订阅任何生命周期。</summary>
    public sealed class BattleSession
    {
        private readonly BattleSettings settings;
        private readonly IRandomStream random;
        private readonly IBattleItemInventory inventory;
        private readonly List<BattleEvent> events = new List<BattleEvent>();
        private BattlePhase phase;
        private BattleOutcome outcome;
        private int completedRounds;

        /// <summary>由 `TurnBasedKernel` 构造。</summary>
        internal BattleSession(
            in BattleEntryDecision entry,
            in BattleSettings settings,
            in PlayerBattleSnapshot playerSnapshot,
            in BossBattleSnapshot bossSnapshot,
            IRandomStream random,
            IBattleItemInventory inventory)
        {
            this.settings = settings;
            this.random = random;
            this.inventory = inventory;

            Entry = entry;
            Player = new PlayerBattleRules(settings.PlayerSkills, playerSnapshot);
            Boss = new BossBattleRules(bossSnapshot, settings.BossSkills, settings.Drunk, settings.InheritsDrunkValue);
            Items = new BattleItemLedger(settings.ItemOncePerBattle);

            ApplyEntry();
        }

        /// <summary>进入战斗的判定结果（含先手权与偷袭扣血比例）。</summary>
        public BattleEntryDecision Entry { get; }

        /// <summary>玩家侧状态。</summary>
        public PlayerBattleRules Player { get; }

        /// <summary>BOSS 侧状态。</summary>
        public BossBattleRules Boss { get; }

        /// <summary>本场道具账本。</summary>
        public BattleItemLedger Items { get; }

        /// <summary>当前阶段。</summary>
        public BattlePhase Phase => phase;

        /// <summary>战斗结果；未结束时是 <see cref="BattleOutcome.None"/>。</summary>
        public BattleOutcome Outcome => outcome;

        /// <summary>战斗是否已结束。</summary>
        public bool IsOver => phase == BattlePhase.Ended;

        /// <summary>已经打完的完整回合数（玩家回合 + BOSS 回合算一个）。</summary>
        public int CompletedRounds => completedRounds;

        /// <summary>最近一次动作产生的事件（每次动作前清空，<b>不是</b>全程流水账）。</summary>
        public IReadOnlyList<BattleEvent> Events => events;

        /// <summary>
        /// 战斗结果对应的剧情出口键（`BattleExitKeys`）；未结束时返回 null。
        /// </summary>
        public string ExitKey
        {
            get
            {
                switch (outcome)
                {
                    case BattleOutcome.Victory:
                        return BattleExitKeys.Victory;
                    case BattleOutcome.Defeat:
                        return BattleExitKeys.Downed;
                    default:
                        return null;
                }
            }
        }

        /// <summary>
        /// 用一件道具（07:58「玩家可以在施放招式之前使用道具」）。
        /// 只在玩家回合、且本回合还没出招时可用；晕眩回合按 <see cref="StunTurnPolicy"/> 决定。
        /// </summary>
        public ItemUseDecision TryUseItem(string itemId)
        {
            ClearEvents();

            if (phase != BattlePhase.PlayerTurn)
            {
                return ItemUseDecision.Refuse(ItemUseReject.WrongPhase);
            }

            if (Player.CannotActThisTurn && settings.Flow.StunTurnPolicy == StunTurnPolicy.SkipTurn)
            {
                // 被晕眩时「直接跳过」口径下，道具也用不了（原文没写，等 C91）。
                return ItemUseDecision.Refuse(ItemUseReject.WrongPhase);
            }

            if (Player.HasCastThisTurn)
            {
                // 道具只能在施放招式之前用（07:58）。
                return ItemUseDecision.Refuse(ItemUseReject.WrongPhase);
            }

            ItemUseDecision decision = Items.TryUse(itemId, inventory);
            if (decision.Allowed)
            {
                events.Add(BattleEvent.ItemUsed(itemId));
            }

            return decision;
        }

        /// <summary>
        /// 玩家施放一个招式。成功则结算伤害与效果，并把回合交给敌方（07:56）。
        /// </summary>
        public SkillCastResult TryCastSkill(PlayerSkill skill)
        {
            ClearEvents();

            if (phase != BattlePhase.PlayerTurn)
            {
                int rageNow = Player.Rage;
                return SkillCastResult.Refuse(skill, SkillCastReject.NotPlayerTurn, rageNow, Player.ExtraDamageCharges);
            }

            SkillCastResult result = Player.TryCast(skill);
            if (!result.Accepted)
            {
                return result;
            }

            events.Add(BattleEvent.SkillCast(skill, result.Damage));

            if (result.HealReductionPercent > 0)
            {
                Boss.ReceiveHealReduction(result.HealReductionPercent, result.HealReductionRounds);
            }

            Boss.ApplyDamage(result.Damage);

            phase = BattlePhase.BossTurn;

            if (Boss.IsDefeated)
            {
                End(BattleOutcome.Victory);
            }

            return result;
        }

        /// <summary>
        /// 结束玩家回合、直接交给敌方（无招式可用时用：界面上的「结束回合」，
        /// 以及晕眩回合里按 <see cref="StunTurnPolicy.ItemsOnly"/> 过完道具阶段之后的推进）。
        /// </summary>
        public void SkipPlayerTurn()
        {
            ClearEvents();

            if (phase == BattlePhase.PlayerTurn)
            {
                phase = BattlePhase.BossTurn;
            }
        }

        /// <summary>
        /// 推进敌方回合：先按醉酒四档结算「跳过与否」，再（没跳过时）按 6:3:1 选招出招；
        /// 之后回到玩家回合（07:56、07:70-84）。
        /// </summary>
        public void RunBossTurn()
        {
            ClearEvents();

            if (IsOver || phase != BattlePhase.BossTurn)
            {
                return;
            }

            ResolveBossAction();

            if (Player.IsDefeated)
            {
                End(BattleOutcome.Defeat);
                return;
            }

            Boss.TickHealReduction();
            completedRounds++;

            if (TryResolveRoundLimit())
            {
                return;
            }

            BeginPlayerTurn();
        }

        /// <summary>把最近一次动作产生的事件清掉。</summary>
        public void ClearEvents() => events.Clear();

        private void ApplyEntry()
        {
            // 偷袭开战：BOSS 生命先扣 20%（07:18）。
            if (Entry.BossHealthLossPercent > 0)
            {
                int loss = Boss.PercentOfHealth(Entry.BossHealthLossPercent, Entry.BossHealthLossBase);
                Boss.ApplyDamage(loss);
                events.Add(BattleEvent.SneakHealthLoss(loss));
            }

            if (Boss.IsDefeated)
            {
                End(BattleOutcome.Victory);
                return;
            }

            if (Entry.Initiative == BattleInitiative.Boss)
            {
                phase = BattlePhase.BossTurn;
                return;
            }

            BeginPlayerTurn();
        }

        private void BeginPlayerTurn()
        {
            phase = BattlePhase.PlayerTurn;

            if (Player.BeginTurn())
            {
                return;
            }

            // 被晕眩：按配置口径处理（原文没写玩家被晕眩时怎么过回合，等 C91）。
            if (settings.Flow.StunTurnPolicy == StunTurnPolicy.SkipTurn)
            {
                events.Add(BattleEvent.PlayerStunnedTurnSkipped(Player.StunRoundsRemaining));
                phase = BattlePhase.BossTurn;
            }

            // ItemsOnly 口径：回合留在玩家这边，但 CannotActThisTurn 为真，只能走
            // TryUseItem 或 SkipPlayerTurn（接线侧照这个分支收输入）。
        }

        private void ResolveBossAction()
        {
            DrunkTurnOutcome drunkOutcome = Boss.Drunk.BeginMonsterTurn(random);

            if (drunkOutcome.Skipped)
            {
                // 屏幕中央闪现提示所需的结果标记（07:71-73），本模块不做界面。
                events.Add(BattleEvent.BossTurnSkipped(
                    drunkOutcome.TierAtTurnStart,
                    drunkOutcome.Reason,
                    drunkOutcome.HintText));
                return;
            }

            DrunkTier tierAtCast = drunkOutcome.TierAtTurnStart;
            BossSkill skill = BossSkillRules.SelectWeighted(settings.BossSkills, random);

            switch (skill)
            {
                case BossSkill.Skill1:
                {
                    int damage = Boss.Skill1DamageNow();
                    if (Boss.NextSkill1DamageBonusPending)
                    {
                        // 「下次招式 1 伤害 +30%」只用一次（07:80）。
                        Boss.ConsumeSkill1DamageBonus();
                    }

                    Player.ApplyDamage(damage);
                    events.Add(BattleEvent.BossSkillUsed(BossSkill.Skill1, damage));
                    break;
                }

                case BossSkill.Skill2:
                {
                    int drunkAdded = Boss.Drunk.Add(settings.BossSkills.Skill2DrinkAddDrunk);
                    int healed = Boss.Heal(settings.BossSkills.Skill2HealPercent, settings.BossSkills.Skill2HealBase);
                    Boss.GrantSkill1DamageBonus();
                    events.Add(BattleEvent.BossDrank(drunkAdded, healed));
                    break;
                }

                default:
                {
                    int damage = Boss.Skill3DamageNow();
                    Player.ApplyDamage(damage);
                    events.Add(BattleEvent.BossSkillUsed(BossSkill.Skill3, damage));

                    int stunRounds = BossSkillRules.Skill3StunRounds(tierAtCast, settings.BossSkills);
                    if (stunRounds > 0)
                    {
                        // 薄醉态的高额伤害附带「晕眩玩家 1 回合」（07:82）。
                        Player.ApplyStun(stunRounds);
                        events.Add(BattleEvent.PlayerStunned(stunRounds));
                    }

                    break;
                }
            }
        }

        private bool TryResolveRoundLimit()
        {
            int limit = settings.Flow.RoundLimit;
            if (limit <= 0 || completedRounds < limit)
            {
                return false;
            }

            BattleOutcome resolved;
            switch (settings.Flow.RoundLimitOutcome)
            {
                case RoundLimitOutcome.Victory:
                    resolved = BattleOutcome.Victory;
                    break;
                case RoundLimitOutcome.Defeat:
                    resolved = BattleOutcome.Defeat;
                    break;
                default:
                    // 按剩余生命比例判；相等时判负（原文没写，占位取保守解释，等 C91）。
                    resolved = (long)Player.Health * Boss.MaxHealth > (long)Boss.Health * Player.MaxHealth
                        ? BattleOutcome.Victory
                        : BattleOutcome.Defeat;
                    break;
            }

            events.Add(BattleEvent.RoundLimitReached(resolved));
            End(resolved);
            return true;
        }

        private void End(BattleOutcome result)
        {
            outcome = result;
            phase = BattlePhase.Ended;
            events.Add(result == BattleOutcome.Victory ? BattleEvent.BossDefeated() : BattleEvent.PlayerDefeated());
        }
    }
}
