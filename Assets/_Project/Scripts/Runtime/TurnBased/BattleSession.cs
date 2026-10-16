// 职责：一场回合制战斗的驱动器——按 07 二、战斗过程的顺序推进回合，并在每次动作后把事件摆出来。
//
// 流程（`docs/design/spotlight/07_回合制作战文档.md:46-58`）：
//   玩家回合 →（可选）用道具 → 选招式 → 结算伤害 → 回位 → 敌方回合
// 其中「回位」是表现，规则层只要在施放招式后把回合交给敌方（07:56）。
//
// 为什么新建（复用 → 扩展 → 新建）：
// - 复用：工程里没有任何回合制 / 怒气 / 招式流程的类型（`09_BOSS战.md` 2026-10-07 版 §7「工程侧一行代码都没有」）。
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
            Boss = new BossBattleRules(bossSnapshot, settings.BossSkills, settings.Drunk, settings.InheritsDrunkValue,
                settings.BossProfile);
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

            if (Player.IsSkillLocked)
            {
                // 「金锁禁锢」期间所有道具置暗（阶段九 BOSS 人形态招式 2，07:176）。
                return ItemUseDecision.Refuse(ItemUseReject.WrongPhase);
            }

            ItemUseDecision decision = Items.TryUse(itemId, inventory);
            if (decision.Allowed)
            {
                events.Add(BattleEvent.ItemUsed(itemId, ApplyItemEffect(itemId))); // 效果占位见 ApplyItemEffect
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

            // 护心鳞先吃这一招（阶段九 BOSS，07:92：「可以抵挡一次玩家的任何招式」；
            // 龙形态「需要两次攻击才能被击碎」= 一层要挨两次，由 BossBattleRules 自己数）。
            if (Boss.TryAbsorbPlayerAttack())
            {
                events.Add(BattleEvent.BossShieldBlocked(Boss.ShieldLayers));
            }
            else
            {
                Boss.ApplyDamage(result.Damage);
            }

            phase = BattlePhase.BossTurn;

            if (Boss.IsDefeated)
            {
                ResolveBossDefeat();
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
            // 按 BOSS 覆盖：阶段九 BOSS「龙族感知敏锐，偷袭与正面效果相同」（07:90），档案里把百分比配成 0。
            int lossPercent = settings.BossProfile.OverridesSneakPenalty
                ? settings.BossProfile.SneakHealthLossPercentOverride
                : Entry.BossHealthLossPercent;

            if (lossPercent > 0)
            {
                int loss = Boss.PercentOfHealth(lossPercent, Entry.BossHealthLossBase);
                Boss.ApplyDamage(loss);
                events.Add(BattleEvent.SneakHealthLoss(loss));
            }

            if (Boss.IsDefeated)
            {
                ResolveBossDefeat();
                return;
            }

            if (Entry.Initiative == BattleInitiative.Boss)
            {
                phase = BattlePhase.BossTurn;
                return;
            }

            BeginPlayerTurn();
        }

        /// <summary>
        /// BOSS 生命归零之后：还有下一个形态就**转形态**（阶段九 BOSS，07:94「在人形态生命值耗尽后为龙形态……
        /// 生命值恢复为100%」），否则判胜。转形态时按配置决定清谁的怒气（原文没写，占位不清；C94）。
        /// </summary>
        private void ResolveBossDefeat()
        {
            if (Boss.TryAdvancePhase())
            {
                events.Add(BattleEvent.BossPhaseChanged(Boss.PhaseIndex, Boss.PhaseName));
                if (Boss.RageClearOnPhaseChange == RageClearTarget.Player)
                {
                    Player.ClearRage();
                }

                return;
            }

            End(BattleOutcome.Victory);
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

            // 选招：分形态的 BOSS（阶段九）按当前形态的招式组与附加规则选；不分形态时与加这一层之前逐字一致。
            // 自我禁锢期间「只能施放招式1」（07:192）优先。
            BossSkill skill;
            if (Boss.SelfImprisonRoundsRemaining > 0)
            {
                skill = BossSkill.Skill1;
            }
            else if (Boss.HasPhases)
            {
                skill = BossSkillRules.SelectForPhase(Boss.CurrentPhase(), Player.IsSkillLocked, random);
            }
            else
            {
                skill = BossSkillRules.SelectWeighted(settings.BossSkills, random);
            }

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
                    ResolveAttackEcho();
                    break;
                }

                case BossSkill.Skill2:
                {
                    ResolveBossSkill2();
                    break;
                }

                default:
                {
                    ResolveBossSkill3(tierAtCast);
                    break;
                }
            }

            // 龙形态自我禁锢到期：结束后 BOSS 获得护心麟（07:192）。
            if (Boss.TickSelfImprison())
            {
                events.Add(BattleEvent.BossShieldGained(Boss.ShieldLayers));
            }
        }

        /// <summary>
        /// 招式 2 按形态分三种：阶段一「饮酒」（07:80）、阶段九人形态「金锁禁锢」（07:176）、
        /// 阶段九龙形态「自我禁锢」（07:192）。不分形态时落回饮酒，与加这一层之前一致。
        /// </summary>
        private void ResolveBossSkill2()
        {
            switch (Boss.CurrentPhase().Skill2Kind)
            {
                case BossPhaseSkill2Kind.SkillLock:
                {
                    // 人形态：禁锢玩家 + 少量伤害（07:176「同时造成少量伤害」）。
                    int damage = Boss.Skill2DamageNow();
                    Player.ApplyDamage(damage);
                    events.Add(BattleEvent.BossSkillUsed(BossSkill.Skill2, damage));

                    int rounds = Boss.CurrentPhase().SkillLockRounds;
                    Player.ApplySkillLock(rounds);
                    events.Add(BattleEvent.PlayerSkillLocked(rounds));
                    break;
                }

                case BossPhaseSkill2Kind.SelfImprison:
                {
                    // 龙形态：BOSS 进入自我禁锢，这一招不造成伤害（07:192）。
                    Boss.BeginSelfImprison(Boss.CurrentPhase().SelfImprisonRounds);
                    events.Add(BattleEvent.BossSkillUsed(BossSkill.Skill2, 0));
                    break;
                }

                default:
                {
                    int drunkAdded = Boss.Drunk.Add(Boss.Skill2DrinkAddDrunk);
                    int healed = Boss.Heal(Boss.Skill2HealPercent, Boss.Skill2HealBase);
                    if (Boss.Skill2NextSkill1DamageBonusPercent > 0)
                    {
                        Boss.GrantSkill1DamageBonus();
                    }

                    events.Add(BattleEvent.BossDrank(drunkAdded, healed));
                    break;
                }
            }
        }

        /// <summary>
        /// 招式 3：基础伤害之外按形态处理两套特例——人形态「增加一次护心麟」（07:177）、
        /// 龙形态「消耗一层护心鳞换 +50%，没层则重获一层并回 10% 最大生命」（07:193）；阶段一另有薄醉晕眩（07:82）。
        /// </summary>
        private void ResolveBossSkill3(DrunkTier tierAtCast)
        {
            bool consumedShield = Boss.Skill3ConsumesShieldLayer && Boss.ConsumeShieldLayer();
            int damage = Boss.Skill3DamageNow();
            Player.ApplyDamage(damage);
            events.Add(BattleEvent.BossSkillUsed(BossSkill.Skill3, damage));

            if (Boss.Skill3GrantsShieldLayers > 0)
            {
                int layers = Boss.Skill3GrantsShieldLayers;
                Boss.GrantShield(layers, Boss.ShieldHitsPerLayer);
                events.Add(BattleEvent.BossShieldGained(layers));
            }

            if (!consumedShield && Boss.Skill3HealPercentWhenNoShield > 0 && Boss.ShieldLayers == 0)
            {
                // 龙形态：没有护心鳞时「引爆后」重新获得 1 层并回复 10% 最大生命（07:193）。
                Boss.GrantShield(1, Boss.ShieldHitsPerLayer);
                events.Add(BattleEvent.BossShieldGained(1));
                if (Boss.Heal(Boss.Skill3HealPercentWhenNoShield, HealthPercentBase.MaxHealth) > 0)
                {
                    events.Add(BattleEvent.BossDrank(0, Boss.MaxHealth * Boss.Skill3HealPercentWhenNoShield / 100));
                }
            }

            int stunRounds = Boss.Skill3StunRounds(tierAtCast);
            if (stunRounds > 0)
            {
                // 薄醉态的高额伤害附带「晕眩玩家 1 回合」（07:82）。
                Player.ApplyStun(stunRounds);
                events.Add(BattleEvent.PlayerStunned(stunRounds));
            }

            ResolveAttackEcho();
        }

        /// <summary>龙形态「在攻击后有 20% 概率附带一次招式 1 的攻击」（07:94）。没配概率时一个随机数都不抽。</summary>
        private void ResolveAttackEcho()
        {
            int percent = Boss.AttackEchoPercent;
            if (percent <= 0 || Boss.IsDefeated || Player.IsDefeated)
            {
                return;
            }

            if (random.Range(0, 100) >= percent)
            {
                return;
            }

            int damage = Boss.Skill1DamageNow();
            Player.ApplyDamage(damage);
            events.Add(BattleEvent.BossSkillUsed(BossSkill.Skill1, damage));
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

        /// <summary>
        /// 道具用掉之后的效果——查 `BattleItemSettings` 的效果表（sp07:199-211 的 10 件，2026-10-16 落地），
        /// 命中就交给 `PlayerBattleRules.ApplyItemEffect`；查不到 = 这件道具只记账、只置暗。
        /// 放在类尾是有意的：不挪动上面那些被模块 guide 按行号引用的方法。
        /// </summary>
        /// <returns>即时可报的数值（回复的生命 / 加的怒气；持续类效果为 0）。</returns>
        private int ApplyItemEffect(string itemId)
        {
            return settings.Items.TryGet(itemId, out BattleItemEffect effect)
                ? Player.ApplyItemEffect(effect)
                : 0;
        }
    }
}
