// 职责：把**这一只 BOSS 自己的局势**（它察觉到玩家没有、玩家进没进它的警戒 / 敌对范围、谁先动手）
//   实现成 `Game.Monster.IBattleEntryContext`，供 `BattleSetup.TryCreate` 填进战斗请求。
//
// 为什么新建（复用 → 扩展 → 新建，2026-10-10「③ 方案 2」）：
//   1. 复用不行：原先唯一的生产者是 `Game.Monster.EncounterStep`，它描述的是**巡逻遭遇**——
//      「怪物模式」= 巡逻怪自己的状态机，「警戒 / 敌对区域」= 玩家相对**巡逻怪**的位置。
//      而 boss 战是剧情 NPC 在十几米外触发的（`Npc_SampleBoss` 与巡逻线隔了大半张图），
//      拿巡逻怪的平静快照去判开战理由，等价于「用别人的表情决定你要不要动手」——
//      2026-10-09 实测就是这样把整场 boss 战拒掉的（见 `BattleSetup.IsEncounterEngaged` 的注释）。
//   2. 扩展不行：把 boss 的局势塞进 `EncounterStep` 会让「一只巡逻怪的状态机」同时表达「一只 NPC 的察觉」，
//      而且 `EncounterStep` 是确定性内核（进快照、进回放），给它加一个非内核的 NPC 查询会污染状态哈希。
//
// 判据来自 `MonsterRules.SenseFrom`（**与巡逻怪逐字同一套档位**），不在这里重写第二份几何：
//   本波要消掉的正是「同一个玩家位置因对手不同而得到两种答案」这类分叉。
//
// ⚠️ 有意的边界（写在契约里，不是漏掉）：
//   · **不做遮挡查询**：本组件拿不到遭遇侧的遮挡体集合，玩家躲在掩体后也会被算进警戒圈。
//     要接的话应当复用 `StealthSight`（`EncounterSceneView.CollectSightOccluders` 是现成来源），
//     但那是「掩体对 boss 也生效」这条玩法口径，等策划定，不在本波私自扩。
//   · **没有视线，按距离判**：纸片 NPC 的旋转是朝向相机的（`CameraBillboard`），不是它的视线，
//     所以只比半径（理由写在 `SenseLevel` 上）。
//   · **不查种类表**：用 `MonsterConfig` 的全局值，不按怪物种类解析。boss 的感知数值属于 C90 / C91 的内容范围。
//   · **BOSS 不主动开战**：本组件只回答「局势是什么样」，不产生任何副作用、不推进状态（契约要求只读）。
//     「boss 主动攻击玩家」那条（07 的第三种进入方式）需要 boss 自己的状态机，本波不做。
using System;
using Game.Core.Logging;
using Game.Core.Telemetry;
using Game.Monster;
using Game.Player;
using Game.TurnBased;
using UnityEngine;

namespace Game.Battle
{
    /// <summary>
    /// 一只 BOSS 的局势快照来源（挂在 BOSS 那个场景物体上，与 `NarrativeTrigger` / `DialogueInteractable` 并列）。
    /// <para>
    /// 装法：`BattleInstaller` 的构建回调在拿不到本组件时用巡逻遭遇那份快照（原行为），
    /// 拿到就以本组件为准——**BOSS 的开战理由由它自己说了算**。
    /// </para>
    /// </summary>
    public sealed class BossEntryContext : MonoBehaviour, IBattleEntryContext
    {
        [Tooltip("这一份局势属于哪只 BOSS（= 剧情 Battle 阶段的 payload，如 sample_boss）。"
                 + "留空 = 谁来问都答（demo 里只有一只 BOSS）；填了就只有该 payload 开仗时才用这一份。")]
        [SerializeField] private string bossId = string.Empty;

        private MonsterConfig monsterConfig;
        private PlayerModel player;
        private ITelemetryScope telemetry = NullTelemetryScope.Instance;
        private bool warned;

        /// <summary>这一份局势对应的 BOSS id（空 = 通配，见字段 Tooltip）。</summary>
        public string BossId => bossId ?? string.Empty;

        /// <summary>这一份局势能不能用于 <paramref name="payload"/> 那只 BOSS 的开仗判定。</summary>
        public bool MatchesBoss(string payload) =>
            string.IsNullOrEmpty(bossId) || string.Equals(bossId, payload, StringComparison.Ordinal);

        /// <summary>
        /// 接数值来源与玩家模型（由 `BattleInstaller` 的构建回调注入）。
        /// <para>
        /// <b>为什么不做成序列化字段拖资产</b>（2026-10-10 定）：数值来源与玩家模型**容器里已经有了**
        /// （`MonsterInstaller` 把 `MonsterConfig` 注册成实例、`PlayerModel` 是根作用域单例）。
        /// 再在场景物体上拖一份就等于同一件事有两个出处，改了哪一份生效要看运气——
        /// 这与「两个写入方争同一帧」是同一类缺陷。所以这一层只认容器给的那一份。
        /// </para>
        /// </summary>
        public void Configure(MonsterConfig config, PlayerModel playerModel)
        {
            monsterConfig = config;
            player = playerModel;
        }

        /// <summary>接入埋点（本类只埋一条「缺配置」的 Warn，后置装配的理由同 `EncounterBattleBridge`）。</summary>
        public void UseTelemetry(ITelemetryScope scope) => telemetry = scope ?? NullTelemetryScope.Instance;

        /// <summary>本组件此刻是否回答了局势（没配 `MonsterConfig` 或没接玩家时为 false）。</summary>
        public bool IsReady => monsterConfig != null && player != null;

        /// <summary>玩家此刻是否处于潜行（07:17 偷袭触发条件的一半）。</summary>
        public bool PlayerSneaking => player != null && player.Snapshot.IsSneaking;

        /// <summary>
        /// 这一场里**已经**偷袭命中过目标（07:17 的另一半）。巡逻怪那边读的是本场遭遇的瞬时事实
        /// `stealth.assassinated`（`EncounterStep.Facts`）。
        /// <para>
        /// <b>BOSS 这一侧刻意保持 false、不接事实集</b>（2026-10-10）：那份事实集属于**巡逻遭遇**那一场
        /// （`EncounterFactLog`，进快照、遭遇结束就清），而 boss 战是另一场遭遇——把「刚才处决过市令」
        /// 读成「偷袭了这只 BOSS」正是 2026-10-09 记在案的错误推断（`HANDOVER` §1.11 ③）。要让它为真，
        /// 得先有「对 BOSS 的偷袭」这条玩法（`00_功能总览.md` §8.1 #5 未定），不是在这里接一根线。
        /// </para>
        /// <para>预留给将来那条玩法：现在写它只影响这一场请求，不改任何持久状态。</para>
        /// </summary>
        public bool SneakAttackLanded { get; set; }

        /// <summary>玩家是否进了**这只 BOSS** 的警戒范围（橙区，07:22）。</summary>
        public bool PlayerInAlertZone => SenseLevel() == 1;

        /// <summary>玩家是否进了**这只 BOSS** 的敌对范围（红区，07:22）。</summary>
        public bool PlayerInHostileZone => SenseLevel() == 2;

        /// <summary>
        /// BOSS 此刻察觉到了玩家（警戒或敌对）——「它自己是警戒 / 敌对状态」在本波的等价物：
        /// BOSS 没有状态机，所以用「这一刻的感知结果」当它的状态（判据同巡逻怪的 `Detects`）。
        /// </summary>
        public bool MonsterAware => SenseLevel() > 0;

        /// <summary>
        /// 谁先动的手（07:18、:28）。本组件只认玩家这一侧：
        /// BOSS 主动攻击玩家那条路（被打）需要 BOSS 自己的状态机，本波不做，所以永远回 <c>PlayerAttacked</c>。
        /// </summary>
        public BattleInitiator Initiator => BattleInitiator.PlayerAttacked;

        /// <summary>战斗侧判「局势快照」用的档位（`EncounterStep` 用怪物模式反推，这里用感知结果）。</summary>
        public MonsterAlertState MonsterState => SenseLevel() switch
        {
            2 => MonsterAlertState.Hostile,
            1 => MonsterAlertState.Alert,
            _ => MonsterAlertState.Unaware,
        };

        /// <summary>
        /// 这一刻的感知档位。传**零朝向**给 <see cref="MonsterRules.SenseFrom"/>，于是只剩半径判定：
        /// 敌对半径内 = 2、警戒半径内 = 1、贴身档 = 1、其余 0。
        /// <para>
        /// <b>为什么不用视野锥</b>（2026-10-10 实测后定）：本工程的纸片角色用 <c>CameraBillboard</c> 朝向相机
        /// （`pitfalls.md` 明写别给它加 <c>ExecuteAlways</c>），所以场景里那只 BOSS 的 <c>transform.up</c>
        /// 是**朝向相机的**，不是它的视线——拿它当视锥方向，玩家走到它「正面」在北边还是南边纯属巧合。
        /// 而巡逻怪的朝向由转向运动写（`MonsterRules` 自己维护），两者不是一回事。
        /// 要让 BOSS 也有视线，得先有「NPC 面朝哪边」的表现规则（归类 S6），本波按**距离**判，不假装有朝向。
        /// </para>
        /// </summary>
        private int SenseLevel()
        {
            if (!IsReady)
            {
                WarnOnce();
                return 0;
            }

            PlayerSnapshot target = player.Snapshot;
            Vector2 from = transform.position;
            return MonsterRules.SenseFrom(monsterConfig, null, new Vector2(from.x, from.y), Vector2.zero, in target);
        }

        private void WarnOnce()
        {
            if (warned)
            {
                return;
            }

            warned = true;
            Log.Warn($"{name}：BossEntryContext 没接 MonsterConfig 或没接玩家模型，这一只 BOSS 的局势快照不生效，"
                     + "开战方式退回原口径（巡逻遭遇快照 / 硬编码缺省）。");
            telemetry.TrackWarn("boss_entry_context_unbound", TelemetryProps.Of(("boss", name)));
        }
    }
}
