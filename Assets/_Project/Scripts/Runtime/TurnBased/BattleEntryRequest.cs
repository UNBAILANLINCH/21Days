// 职责：进入战斗判定的输入快照——五个快照量，全是值，没有任何对象引用。
//
// 为什么是快照而不是「传 Monster / Player 对象」：本模块是纯规则内核，测试与回放都要能构造输入；
// 引用游戏对象会让规则依赖场景、也让「同一份输入必然同一份输出」不再成立。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:15-28`（三个触发条件用到的量正好是这五个）。

namespace Game.TurnBased
{
    /// <summary>
    /// 进入战斗那一刻的局势快照。<b>由接线侧从潜行 / 感知 / 区域查询的结果翻译过来</b>，
    /// 本模块不查任何东西。
    /// </summary>
    public readonly struct BattleEntryRequest
    {
        /// <summary>
        /// 构造快照。
        /// </summary>
        /// <param name="playerSneaking">玩家是否处于潜行（07:17）。</param>
        /// <param name="sneakAttackSucceeded">偷袭是否成功命中（07:17「偷袭怪物成功」）。</param>
        /// <param name="playerInAlertZone">玩家是否处于怪物的警戒区域内（07:22）。</param>
        /// <param name="playerInHostileZone">玩家是否处于怪物的敌对区域内（07:22）。</param>
        /// <param name="monsterState">怪物当前警觉档位（07:22「怪物正处于警戒状态或敌对状态」）。</param>
        /// <param name="initiator">谁先动的手（07:18 / :28）。</param>
        public BattleEntryRequest(
            bool playerSneaking,
            bool sneakAttackSucceeded,
            bool playerInAlertZone,
            bool playerInHostileZone,
            MonsterAlertState monsterState,
            BattleInitiator initiator)
        {
            PlayerSneaking = playerSneaking;
            SneakAttackSucceeded = sneakAttackSucceeded;
            PlayerInAlertZone = playerInAlertZone;
            PlayerInHostileZone = playerInHostileZone;
            MonsterState = monsterState;
            Initiator = initiator;
        }

        /// <summary>玩家是否处于潜行。</summary>
        public bool PlayerSneaking { get; }

        /// <summary>偷袭是否成功命中。</summary>
        public bool SneakAttackSucceeded { get; }

        /// <summary>玩家是否在怪物的警戒区域内。</summary>
        public bool PlayerInAlertZone { get; }

        /// <summary>玩家是否在怪物的敌对区域内。</summary>
        public bool PlayerInHostileZone { get; }

        /// <summary>怪物当前警觉档位。</summary>
        public MonsterAlertState MonsterState { get; }

        /// <summary>谁先动的手。</summary>
        public BattleInitiator Initiator { get; }

        /// <summary>偷袭成功的完整条件：潜行 + 偷袭命中（07:17）。</summary>
        public bool SneakStrikeReady => PlayerSneaking && SneakAttackSucceeded;

        /// <summary>
        /// 「玩家在警戒 / 敌对区域，或怪物处于警戒 / 敌对态」——正面攻击与被打共用的前提（07:22、:27）。
        /// </summary>
        public bool InAlertOrHostileContext =>
            PlayerInAlertZone ||
            PlayerInHostileZone ||
            MonsterState == MonsterAlertState.Alert ||
            MonsterState == MonsterAlertState.Hostile;
    }
}
