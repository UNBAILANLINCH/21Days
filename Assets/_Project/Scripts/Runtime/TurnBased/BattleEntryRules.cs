// 职责：进入战斗的判定——纯函数，输入五个快照量，输出「进不进 / 怎么进 / 谁先手 / 扣多少血」。
//
// 为什么是纯函数：判定要能被测试与回放逐条钉住；它不认识 Monster / Player 类型（见 BattleEntryRequest）。
//
// 出处（逐条对着写）：`docs/design/spotlight/07_回合制作战文档.md:13-28`
//   1. 偷袭（07:15-18）：玩家潜行并偷袭成功、玩家攻击 → 玩家先手，BOSS 生命 -20%
//   2. 正面攻击（07:20-23）：玩家在警戒 / 敌对区域内，或怪物处于警戒 / 敌对态，玩家攻击 → 玩家先手
//   3. 被打（07:25-28）：同上前提，玩家被 BOSS 攻击 → BOSS 先手
// 转写：`docs/design/features-spotlight/09_BOSS战.md:178-184`（R40）。

namespace Game.TurnBased
{
    /// <summary>进入战斗的判定规则（全是纯函数，无状态）。</summary>
    public static class BattleEntryRules
    {
        /// <summary>
        /// 判定这次接触能不能进战斗、以哪种方式进。
        /// <para>
        /// 判定顺序：先排输入自相矛盾（偷袭成功却又说 BOSS 先动手），再判偷袭，再判正面攻击 / 被打。
        /// 偷袭不要求「在警戒 / 敌对区域」——07:15-18 的触发条件里没有这一条，而潜行本身意味着没被察觉。
        /// </para>
        /// </summary>
        public static BattleEntryDecision Decide(in BattleEntryRequest request, in BattleEntrySettings settings)
        {
            if (request.SneakAttackSucceeded && request.Initiator == BattleInitiator.BossAttacked)
            {
                // 偷袭的定义是「玩家攻击 BOSS」（07:17），所以偷袭成功不可能是被打。
                return BattleEntryDecision.Refuse(BattleEntryReject.InconsistentInitiator);
            }

            if (request.SneakStrikeReady && request.Initiator == BattleInitiator.PlayerAttacked)
            {
                return BattleEntryDecision.Enter(
                    BattleEntryKind.SneakAttack,
                    BattleInitiative.Player,
                    settings.SneakBossHealthLossPercent,
                    settings.SneakLossBase);
            }

            if (request.InAlertOrHostileContext)
            {
                return request.Initiator == BattleInitiator.PlayerAttacked
                    ? BattleEntryDecision.Enter(BattleEntryKind.FrontalAttack, BattleInitiative.Player, 0, settings.SneakLossBase)
                    : BattleEntryDecision.Enter(BattleEntryKind.Ambushed, BattleInitiative.Boss, 0, settings.SneakLossBase);
            }

            // 潜行了但没偷袭成功，且不在警戒 / 敌对区域、怪物也没察觉 → 给一条更贴近现场的原因。
            return BattleEntryDecision.Refuse(
                request.PlayerSneaking && !request.SneakAttackSucceeded
                    ? BattleEntryReject.SneakStrikeNotLanded
                    : BattleEntryReject.NoTrigger);
        }

        /// <summary>进入方式的中文描述。</summary>
        public static string Describe(BattleEntryKind kind)
        {
            switch (kind)
            {
                case BattleEntryKind.SneakAttack:
                    return "偷袭";
                case BattleEntryKind.FrontalAttack:
                    return "正面攻击";
                case BattleEntryKind.Ambushed:
                    return "被 BOSS 攻击";
                default:
                    return "未进入";
            }
        }

        /// <summary>拒绝原因的中文描述。</summary>
        public static string Describe(BattleEntryReject reject)
        {
            switch (reject)
            {
                case BattleEntryReject.NoTrigger:
                    return "不在警戒 / 敌对区域，怪物也未察觉（07:22、:27）";
                case BattleEntryReject.SneakStrikeNotLanded:
                    return "潜行中但偷袭未成功（07:17）";
                case BattleEntryReject.InconsistentInitiator:
                    return "输入矛盾：偷袭成功却由 BOSS 先动手";
                case BattleEntryReject.InvalidSnapshot:
                    return "血量快照不合法";
                default:
                    return "未拒绝";
            }
        }
    }
}
