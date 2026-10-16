// 职责：巡逻怪自己的行为回放——沿巡逻点位移并折返、感知（背后潜行不警戒 / 正面红区转敌对）、追击、攻击玩家，
//   以及（2026-10-15 起）实时遭遇**升级回合制**与**被抓**。
// 舞台是 SampleScene，走 Boot 真实流程（标题「开始」进场），虚拟手柄推摇杆、虚拟键盘按动作驱动场景里的真实玩家，
//   怪物由容器里的 MonsterRules 在逻辑 tick 里自己跑；读状态只读 MonsterModel / PlayerModel，不调规则、不瞬移。
// 本次重写理由：原版不加载 Boot，自己 new MonsterRules + 默认值 ScriptableObject、手动喂 MonsterIntent、瞬移玩家、
//   直接调 ApplyDamage，画面是代码生成的占位方块，与 demo 场景里的巡逻怪 enerme、巡逻点、镜头全都脱节，
//   验的也是 MonsterConfig 默认值而不是 MonsterConfig.asset 里调好的数值。
// 站位怎么算：怪物一直在动，「背后」「正面」都按它此刻的位置与朝向实时算——
//   背后 = 等它巡逻停顿（PatrolPause，停 2 秒、朝向不变）时，逐帧朝「位置 − 朝向 × 1 米」推摇杆，离它 2 米内改按住潜行；
//   正面 = 保持潜行站在巡逻线上不动，等它走到端点折返、迎面走来进入 2 米红区。
// 路线：出生点与巡逻怪之间隔着村口演出触发区，一律走北侧路线到巡逻线北侧观察点（GoToPatrolLookout）；
//   坐标与理由见 Framework/ShowcaseScenario.DemoScene.cs（RouteToPatrol / PatrolLookout），追怪出手用同文件的 StrikeMonster。
//
// ── 2026-10-15 升级回合制 / 被抓（PRP/combat-iteration T6.2，用户已拍板的行为）───────────────────────────────
//   市令（种类 1003）配了回合制战斗数据（BossRosterConfig `monster_1003`：生命 6、本场玩家生命上限 10），MonsterConfig 三个升级开关全开：
//   · 玩家普攻打中**未察觉**的市令 → 偷袭（玩家先手、市令开局先掉 20%）；打中**已警戒 / 敌对**的 → 正面攻击（玩家先手）；
//     **敌对的市令打中玩家**（含追上）→ 被打（市令先手）。判定看挥刀 / 出手那一刻怪的状态。背后处决（F）不升级。
//   · 打赢 → 市令死亡（无视只能暗杀）、按种类掉落、实时生命按比例写回 round(战斗剩余 / 战斗上限 × 实时上限)（至少 1，
//     出场四舍五入；进场方向仍是 ceil，两侧口径见 EncounterEscalationRequest.ScaleHealth / ScaleHealthBack）；
//     打输 → 实时生命 0 → 自由探索走镜碎页重开。
//   巡夜人（种类 1001，东南角）**没配**战斗数据：追上打中玩家 → 击倒（只能慢走、不能攻击），血量归零才判死，从不升级。
//   新增四条：Escalation_FrontalAttack_PlayerFirst / Escalation_MonsterStrikes_Ambushed / Escalation_UnawareStrike_SneakAttack /
//   Caught_Xunye_KnockdownThenDowned。开战方式只读战斗界面上看得见的东西（左上角回合提示、两根生命条的数字），
//   胜负靠回放内换一份 BOSS 招式数值定死（BattleSetup.OverrideSettings，不改资产，同 BattleShowcase）。
//   走位避让巡逻者乙：升级那三条**从巡逻线以南接近市令**（GoThroughSouthGap 经缝口绕到南侧走廊 z −1.5，两只的警戒半径之外），
//   不走观察点——从观察点下去必须横穿乙的巡逻线（z 5），按真实路点前推几乎等不到乙看不见的窗口（W6b 实测）。
//   动身前仍用 ShowcasePatrolForecast 预判「别的巡逻者这几秒看不到这条路、也看不到落点」（同 Identity ② / Stealth ②④ 的做法）。
//
//   **原四条的连带调整**（升级回合制是拍板行为，旧断言编码的是升级之前的世界）：
//   · ③ Hostile_ChasesFleeingPlayerAndHitsIt：「追上出手，玩家生命 -1」→ 这一下被交给回合制（被打开战，世界里不扣血），
//     之后回合制里一下击倒收尾（回放内数值，不需要输入）。追击那一段原样。
//   · ④ Attacked_ImmuneToNormalAttack_StillTurnsHostile：常规攻击打中市令不再在世界里结算（拒伤照旧、但这一下被交给回合制），
//     「连打几下」在暂停的世界里打不出来，改成回合制里输掉、回到世界后它仍满血活着。「它挨打转敌对」那一半在升级接管后看不到了，
//     方法名保留（原四条按名回归），建议主窗口决定是否改名 / 并进新增的升级用例。
//   · ② Sense_…：断言不动；末尾补一段撤离（奔跑离开敌对的市令），来不及（它转敌对时离你不到 2 米）就把升级出来的那一仗
//     （回放内数值一下击倒）打完再收尾，免得把一场进行中的回合制拖进退场流程。
//   · ② ③ ④ 改从巡逻线以南接近（③ 往南逃）：原来从观察点下去要横穿巡逻者乙的巡逻线，W6b 实测三条都出过「乙先出手、被测的那一段没演出来」。
//     ② 的「站着等它折返迎面走来」也不再站在巡逻线上干等（乙就在北边 1.6 米）：先退到南边 3 米，等它朝你走回来再迎上去站到它路上。
using System;
using System.Collections;
using System.Collections.Generic;
using Game.Battle;
using Game.Core.Input;
using Game.Core.UI;
using Game.Loot;
using Game.Mirror;
using Game.Monster;
using Game.Player;
using Game.Stealth;
using Game.TurnBased;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.Showcase.Monster
{
    [Category("Showcase")]
    public sealed class MonsterShowcase : ShowcaseScenario
    {
        /// <summary>背后贴近的目标：怪物身后多远（米），在近身感知半径 1.5 以内。</summary>
        private const float BehindDistance = 1f;

        /// <summary>
        /// 离怪物多近时改按住潜行（米）：要留出近身感知半径 1.5 之外的余量。
        /// 2026-10-07 由 2 米放到 3 米——巡逻点修正到世界坐标 (13.86..21.86, z 3.4) 之后，怪真的贴在观察点
        /// 南边 4.2 米，玩家从北侧绕到它背后时 2 米只留 0.5 米余量，会在按下潜行前先踩进 1.5 米的近身圈
        /// （这条用例因此从"怪在 10 米外、怎么走都不会被发现"的假绿变成真红）。
        /// </summary>
        private const float SneakFromDistance = 3f;

        // ── 升级回合制那几条的走位常量 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 市令正前方的落点离它多远（米）：红区 2 之外、警戒半径 4.5（种类 1003）之内——跑到这里它从**正面**看见你、进警戒并朝你走来。
        /// </summary>
        private const float FrontSpotDistance = 3f;

        /// <summary>
        /// 正前方落点往南（−z，玩家来的那一侧）偏多少度：仍在它的视野锥里（半角 37.5°），落点 z ≈ 3.4 − 3 × sin25° ≈ 2.1，
        /// 离巡逻者乙的巡逻线（z 5）近 2.9 米；它察觉后朝你走来，交手的地方也就离乙更远。
        /// </summary>
        private const float FrontSpotSouthDegrees = 25f;

        /// <summary>
        /// 正前方落点必须落在的空地（玩家逻辑坐标 x 12.6..21、z −6..5.4，同 IdentityShowcase.KiteArea）：
        /// 西边 x 8..11、z 1..4 是村口演出触发区（一进就冻世界），北边 z ≥ 6 起是楼梯与塔，东边 x ≥ 22 是坡道。
        /// </summary>
        private static readonly Rect FrontArea = Rect.MinMaxRect(12.6f, -6f, 21f, 5.4f);

        /// <summary>等「市令停步、正前方落点在空地里、别的巡逻者看不到」窗口的上限（真实秒）：巡逻者乙约 4 秒一圈 + 停 5 秒，市令约 14 秒一个停顿。</summary>
        private const float FrontWindowTimeoutSeconds = 40f;

        /// <summary>跑到正前方落点之后还要在那一带待多久（秒）：它走过来（3 米 ÷ 2.2~2.5 米/秒）+ 出手 / 被打 + 余量。</summary>
        private const float FrontLingerSeconds = 3f;

        /// <summary>
        /// 跑进正前方落点那一段的预判参数：奔跑 5（下限 3.5 留给起步）、两头余量 0.6、每 0.4 米取样、步长 0.05 秒，
        /// 到了落点**还要待着**（等它走过来），探针一律按没潜行（跑的时候本来就没按潜行键）。
        /// </summary>
        private static readonly ShowcasePatrolForecast.Options FrontRunIn =
            new ShowcasePatrolForecast.Options(3.5f, 5.5f, 0.6f, 0.4f, 0.05f, true, 0f);

        /// <summary>
        /// 迎着它出手的距离（米）：玩家攻击距离 1（PlayerConfig.asset），市令出手距离 0.8（种类表 attack_range）——
        /// 站定等它走近，进 0.95 米就挥刀，赶在它够得着你之前打中它（这一下是「正面攻击」而不是「被打」）。
        /// </summary>
        private const float CounterStrikeDistance = 0.95f;

        /// <summary>站定迎击 / 站着等挨打的上限（真实秒）。</summary>
        private const float FrontEngageTimeoutSeconds = 10f;

        /// <summary>
        /// ② 等市令折返时退到它巡逻线以南多远（米）：离乙的巡逻线 4.6 米、离它自己的巡逻线 3 米——
        /// 两只的警戒半径 4.5 都够不着（市令的视野锥要 dx ≥ 3.9 才罩得住南边 3 米，那时距离已超过 4.5）。
        /// </summary>
        private const float ReturnWaitSouthOffset = 3f;

        /// <summary>
        /// ② 迎上去站到它路上时偏南多少（米）：仍在它的视野锥（dx ≥ 1.3）与 2 米红区（dx ≤ 1.73）的交集里；
        /// 离乙的巡逻线 2.6 米，乙的视野锥只在 dx 3.4~3.7 那一窄条够得着。
        /// </summary>
        private const float ReturnPathSouthOffset = 1f;

        /// <summary>② 等市令折返、朝你走回来的上限（真实秒）：最坏要走完一整趟 8 米再折回（4 秒 + 4 秒）+ 停一次 5 秒 + 余量。</summary>
        private const float ReturnWaitTimeoutSeconds = 25f;

        /// <summary>偷袭时站在市令背后多远（米）：攻击距离 1 之内、贴身察觉 1.5 之内（潜行免）。</summary>
        private const float SneakStrikeBehind = 0.75f;

        /// <summary>离**任意一只**巡逻怪多近时按住潜行（米）：潜行免的是贴身察觉 1.5（同 StealthShowcase.SyncSneakKey）。</summary>
        private const float SneakKeyDistance = 2f;

        /// <summary>到了市令背后还要在那儿待多久（秒）：对准朝向 + 挥刀 + 余量。</summary>
        private const float BehindLingerSeconds = 1.5f;

        /// <summary>
        /// 绕背那一段的预判参数（同 StealthShowcase.ApproachWindowOpen）：2 米开外步行 3、2 米内潜行 1.5，
        /// 两头余量 0.6、每 0.4 米取样，步长取逻辑 tick（1/60 秒：巡逻怪端点掉头常落在相邻两三个 tick 里），到了背后要待着，探针 2 米内按潜行判。
        /// </summary>
        private static readonly ShowcasePatrolForecast.Options SneakApproach =
            new ShowcasePatrolForecast.Options(1.5f, 3f, 0.6f, 0.4f, 1f / 60f, true, SneakKeyDistance);

        /// <summary>步行 / 潜行速度（米/秒，PlayerConfig.asset）：估绕背到达时间用。</summary>
        private const float WalkSpeed = 3f;
        private const float SneakSpeed = 1.5f;

        /// <summary>离南侧走廊上那一点多近算「还在候场」（米）：只在这一圈里等窗口，动身后不回头判（同 StealthShowcase 的观察点口径）。</summary>
        private const float LaneCommitRadius = 0.6f;

        /// <summary>偷袭整段（等窗口 → 绕背 → 挥刀，含它走开后退回观察点重来）的上限（真实秒）。</summary>
        private const float SneakStrikeTimeoutSeconds = 60f;

        // ── 回合制那一段 ─────────────────────────────────────────────────────────────────────────

        /// <summary>打赢那几条：回合制里 BOSS 只出招式 1、伤害 1——最坏每回合挨 1 下，本场 10 血稳赢 6 血的市令。</summary>
        private const int VictoryBossDamage = 1;

        /// <summary>等战斗界面开起来、第一次轮到你出招的上限（真实秒）：黑幕 + 叠加加载战斗场景 + 开场演出。</summary>
        private const float BattleOpenTimeoutSeconds = 20f;

        /// <summary>把一仗打完的上限（真实秒）：4 个回合左右，每回合双方演出约 3~4 秒。</summary>
        private const float FightTimeoutSeconds = 90f;

        /// <summary>战斗收场、回到世界的上限（真实秒）。</summary>
        private const float BackToWorldTimeoutSeconds = 30f;

        // ── 被抓（巡夜人）那一条 ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 东南角巡夜人那一带的空地（玩家逻辑坐标 x 20..32.4、z −11..−1.5）：`Ground_East` 东沿 x 33、南沿 z −11.6，
        /// 北边 z −1.2 起是坡道。它正前方的落点出了这块就先等它转向。
        /// </summary>
        private static readonly Rect SoutheastArea = Rect.MinMaxRect(20f, -11f, 32.4f, -1.5f);

        /// <summary>巡夜人正前方落点离它多远（米）：红区 2 之外、它的警戒半径 6（种类 1001）之内。</summary>
        private const float XunyeFrontDistance = 3.5f;

        /// <summary>等巡夜人察觉的上限（真实秒）：它一圈约 6 秒 + 停 5 秒。</summary>
        private const float XunyeNoticeTimeoutSeconds = 30f;

        /// <summary>被察觉后转身走开多久（秒）：拉开一点距离再站住，让它「追上来」打中。</summary>
        private const float XunyeBackOffSeconds = 0.8f;

        private IInputService inputService;
        private PlayerModel player;
        private MonsterModel monster;
        private MonsterConfig monsterConfig;
        private EncounterStep step;
        private MonsterRules monsterRules;
        private PlayerRules playerRules;

        /// <summary>场上全部巡逻者（与 Taming.TargetIds 逐位对齐，主怪在首位）与同序的巡逻路线；主怪的下标。</summary>
        private MonsterRules[] patrollers = Array.Empty<MonsterRules>();
        private Vector2[][] patrolRoutes = Array.Empty<Vector2[]>();
        private int mainIndex;

        /// <summary>预判用的那条路（逐帧复用，不每帧分配）。</summary>
        private readonly List<Vector2> forecastPath = new List<Vector2>(4);

        // 回合制那一段的服务（升级 / 被抓那几条用例在 ResolveBattle 里取）。
        private BattleFlow flow;
        private BattleArena arena;
        private BattleSetup battleSetup;
        private BattleScenePresenter presenter;
        private LootService loot;
        private IUIService ui;
        private BossDefinition battleRow;
        private IDisposable settingsOverride;

        /// <summary>打仗途中逐帧记下的玩家头顶生命条（打赢那一刻的剩余 / 上限，用来核对按比例写回）。</summary>
        private int lastBattleHealth = -1;
        private int lastBattleMax = -1;

        /// <summary>升级在途结束（交回结果）后第一帧读到的实时生命：交回写的就是它，之后世界恢复推进还可能被别的巡逻者打。</summary>
        private int liveHealthOnReturn = -1;

        /// <summary>潜行键此刻是不是按住的（逐帧同步，只在状态变化时动键）。</summary>
        private bool sneakHeld;

        // 偷袭那一刀的现场（挥刀被接管那一刻读）。
        private MonsterMode sneakStrikeMode;
        private bool sneakStrikeSneaking;

        protected override string Module => "Monster";

        /// <summary>世界由流程加载（标题「开始」→ MonsterEncounterState → SampleScene），基类不再叠加载一份。</summary>
        protected override string ScenePath => null;

        /// <summary>撤掉回放内换上的回合制数值（不改资产，见 BattleSetup.OverrideSettings）。</summary>
        [TearDown]
        public void ReleaseSettingsOverride()
        {
            settingsOverride?.Dispose();
            settingsOverride = null;
            sneakHeld = false;
        }

        [UnityTest]
        public IEnumerator Patrol_WalksAlongPointsAndTurnsBack()
        {
            yield return EnterWorld();
            yield return GoToPatrolLookout();

            EncounterSceneView view = UnityEngine.Object.FindObjectOfType<EncounterSceneView>();
            Vector2[] points = view == null ? new Vector2[0] : view.PatrolPositions();
            // 诊断：列出**运行时**所有 PatrolPoint* 的世界坐标——磁盘上的 localPosition 与 PatrolPositions()
            // 的读数对不上时，靠它分清是「读到了别的物体」还是坐标系在某个环节被换了。
            string patrolTransformsDiag = "";
            foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>())
            {
                if (t.name.StartsWith("PatrolPoint"))
                {
                    patrolTransformsDiag += $"{t.name}@{t.position} ";
                }
            }
            if (points.Length < 2)
            {
                Assert.Fail("SampleScene 的 EncounterSceneView 至少要两个巡逻点，才能演巡逻折返");
            }

            float minX = Mathf.Min(points[0].x, points[1].x) - 0.05f;
            float maxX = Mathf.Max(points[0].x, points[1].x) + 0.05f;
            bool stayedOnSegment = true;
            string patrolDiag = "还没取到";
            Vector2 origin = Vector2.zero;
            yield return Step("站在观察点，看怪物沿巡逻点走动", () => origin = monster.Position, 0f);
            yield return Check("怪物在巡逻（走或停顿张望），警戒条为 0（状态色灰）",
                () => IsPatrolling() && monster.Alert <= 0f, 2f);
            yield return Check("怪物沿巡逻线走出 1 米以上", () => Vector2.Distance(origin, monster.Position) >= 1f, 8f);
            yield return Snapshot("巡逻位移");

            // 补前提：玩家走到观察点的路上可能短暂惊到怪，之后它会有一段「走回巡逻轨道」的行程
            // （同样是 PatrolWalk，但朝向斜着指向 waypoint，不在两巡逻点之间），那种状态下测「端点掉头」不成立。
            // 这里先等它真的回到巡逻段上再开始测——补前提，不是放宽判定。
            yield return WaitUntil("怪物回到两巡逻点之间",
                () => monster.Position.x >= minX && monster.Position.x <= maxX, 10f);

            float heading = 0f;
            yield return Step("继续看它走到巡逻端点", () => heading = Mathf.Sign(monster.Facing.x), 0f);
            yield return Check("怪物到端点后掉头往回走，全程不离开两巡逻点之间",
                () =>
                {
                    if (monster.Position.x < minX || monster.Position.x > maxX)
                    {
                        stayedOnSegment = false;
                    }

                    // 诊断快照：把三个判据的实时取值记下来，失败时随报告输出（不再靠截图猜朝向）。
                    patrolDiag = $"点数={points.Length} 点0=({points[0].x:F2},{points[0].y:F2})"
                                 + $" 点1=({points[1].x:F2},{points[1].y:F2})"
                                 + $" x={monster.Position.x:F2} 段=[{minX:F2},{maxX:F2}] 在段内={stayedOnSegment}"
                                 + $" 巡逻中={IsPatrolling()} 模式={monster.Mode} facingX={monster.Facing.x:F2}"
                                 + $" sign={Mathf.Sign(monster.Facing.x)} heading={heading}";

                    return stayedOnSegment && IsPatrolling() && Mathf.Sign(monster.Facing.x) != heading;
                },
                20f); // 8 米巡逻线（2026-10-07 由 4 米拉长）：单程 4 秒 + 端点停顿 2 秒
            yield return Step($"折返判据快照：{patrolDiag} ‖ 运行时 PatrolPoint：{patrolTransformsDiag}", null, 0f);
            yield return Snapshot("巡逻折返");
        }

        [UnityTest]
        public IEnumerator Sense_SneakBehindStaysCalm_FrontRedZoneTurnsHostile()
        {
            yield return EnterWorld();
            ResolveBattle();
            yield return Step($"回放内换一份数值（不改资产）：回合制里市令只出招式 1、伤害 {battleRow.PlayerHealth}（= 本场玩家生命）"
                              + "——最后它转敌对时离你不到 2 米、转眼就会打中你，那一下交给回合制后一下击倒收尾",
                () => settingsOverride = battleSetup.OverrideSettings(WithBossOnlySkill1(battleSetup.Settings, battleRow.PlayerHealth)), 0f);
            // 2026-10-15（W6b）：改从巡逻线以南接近——从观察点下到它背后要横穿巡逻者乙（协作者的线）的巡逻线，
            // W6b 第二跑在「绕到背后」那一步被乙三刀砍死。动身判据同偷袭那条（SneakWindowOpen）。断言不动。
            yield return GoThroughSouthGap(new[] { SouthStagingPoint });

            yield return Step("在南侧走廊上跟着怪物挪，等它停步张望（巡逻停顿，朝向不变）、这几秒谁都扫不到「走廊 → 它背后」这条路", null, 0f);
            yield return WaitUntil("怪物进入巡逻停顿，去它背后的路接下来几秒谁都看不到（按当前位置 / 朝向 / 巡逻路线预判）", () =>
            {
                Vector2 behind = monster.Position - monster.Facing * BehindDistance;
                StepToward(SouthLanePoint(behind.x));
                return SneakWindowOpen(behind);
            }, FrontWindowTimeoutSeconds);
            Input.ReleaseStick();

            bool closeBehind = false;
            bool stayedCalm = true;
            yield return Step("绕到怪物背后，离它 2 米内按住潜行，贴到身后 1 米", null, 0f);
            yield return SneakUpBehind(seen => closeBehind = seen, calm => stayedCalm = calm);
            yield return Check("背后 1.5 米内潜行：怪物仍在巡逻、警戒条始终为 0（状态色保持灰）",
                () => closeBehind && stayedCalm);
            yield return Snapshot("背后潜行不警戒");

            // 2026-10-15（W6b）：原来就站在它背后（巡逻线上）干等它折返——巡逻者乙的巡逻线就在北边 1.6 米，等的那十来秒里
            // 乙一扫到人就锁定（警戒后转身朝你走、一直看着你），W6b 第三跑因此被乙砍死。改成先退到巡逻线以南 3 米
            // （主怪与乙的警戒半径都够不着）等市令折返、朝你这边走回来，再迎上去站到它要走来的路上（巡逻线偏南 1 米：
            // 仍在它的视野锥与 2 米红区里），保持潜行站着不动。断言不动。
            Vector2 waitSpot = new Vector2(player.Position.x, monster.Position.y - ReturnWaitSouthOffset);
            yield return Step("保持潜行退到巡逻线以南 3 米，等怪物走到端点折返、朝你这边走回来", null, 0f);
            yield return WaitUntil("怪物折返，朝你这边走回来（离你 4.5~7 米）", () =>
            {
                StepToward(waitSpot);
                float dx = player.Position.x - monster.Position.x;
                return monster.Mode == MonsterMode.PatrolWalk && Mathf.Sign(monster.Facing.x) == Mathf.Sign(dx)
                       && Mathf.Abs(dx) >= 4.5f && Mathf.Abs(dx) <= 7f && (player.Position - waitSpot).magnitude <= 0.3f;
            }, ReturnWaitTimeoutSeconds);

            Vector2 pathSpot = new Vector2(player.Position.x, monster.Position.y - ReturnPathSouthOffset);
            yield return Step("迎上去站到它要走来的路上（巡逻线偏南 1 米），保持潜行站着不动，等它迎面走来", null, 0f);
            yield return Check("怪物迎面走进 2 米正面红区即转敌对（潜行挡不住正面，状态色变红）",
                () =>
                {
                    StepToward(pathSpot);
                    return monster.Mode == MonsterMode.Hostile && player.IsSneaking;
                }, 15f);
            Input.ReleaseStick();
            yield return Snapshot("正面红区敌对");
            yield return Input.Release(inputService.Actions.Gameplay.Sneak);

            // 2026-10-15：敌对的市令打中玩家会被交给回合制（升级回合制，文件头）。检查点都已经过了：
            // 来得及就奔跑撤离（奔跑 5 > 它追击 2.5）；来不及（它转敌对时离你不到 2 米，W6b 实测 0.1 秒后就打中了）
            // 就把那一仗打完再收尾——仗没打完就退场，战斗界面的飘字动画会撞上已销毁的界面（交付报告的 Runtime 问题）。不记检查点。
            yield return RetreatFromMonster();
            if (step.IsEscalating || flow.IsBattleRunning)
            {
                yield return FinishBattleQuietly();
            }
        }

        [UnityTest]
        public IEnumerator Hostile_ChasesFleeingPlayerAndHitsIt()
        {
            yield return EnterWorld();
            ResolveBattle();
            yield return Step($"回放内换一份数值（不改资产）：回合制里市令只出招式 1、伤害 {battleRow.PlayerHealth}（= 本场玩家生命）"
                              + "——它追上来那一下会被交给回合制（被打、市令先手），一下击倒收尾",
                () => settingsOverride = battleSetup.OverrideSettings(WithBossOnlySkill1(battleSetup.Settings, battleRow.PlayerHealth)), 0f);
            // 2026-10-15（W6b）：改从巡逻线以南接近、往南逃——原来从观察点下去、往北逃，正好撞进巡逻者乙（协作者的线）的巡逻线：
            // W6b 实测它先出手把人击倒，市令那一下随即被交给回合制，追击那一段根本没演出来。断言不动。
            yield return GoThroughSouthGap(new[] { SouthStagingPoint });

            yield return Step("在南侧走廊上跟着市令挪，等它正前方落点在空地里、别的巡逻者这几秒看不到那块地，再迎着它走到它正前方（偏南）", null, 0f);
            yield return WaitFrontWindow();
            yield return WaitUntil("怪物转敌对（状态色变红）", () =>
            {
                if (monster.Mode == MonsterMode.Hostile)
                {
                    Input.ReleaseStick();
                    return true;
                }

                Vector2 ahead = monster.Position + TurnTowardSouth(monster.Facing, FrontSpotSouthDegrees) * 1.2f - player.Position;
                Input.SetStick(ahead.magnitude > 0.1f ? ahead.normalized : Vector2.zero);
                return false;
            }, 8f);
            Input.ReleaseStick();

            Vector2 chaseFrom = Vector2.zero;
            yield return Step("转身往南逃 1.2 秒（背离巡逻者乙的巡逻线）", () => chaseFrom = monster.Position, 0f);
            yield return Walk(Vector2.down, 1.2f);
            yield return Check("怪物追着玩家移动（位移 ≥ 1.5 米），仍然敌对",
                () => Vector2.Distance(chaseFrom, monster.Position) >= 1.5f && monster.Mode == MonsterMode.Hostile, 2f);
            yield return Snapshot("追击");

            int healthBefore = 0;
            yield return Step("停下不动，等怪物追上", () => healthBefore = player.Health, 0f);
            // 2026-10-15：原断言「玩家生命 -1」编码的是升级之前的世界——敌对的市令打中玩家（含追上）现在按升级规则交给回合制。
            yield return Check("怪物追上后出手：这一下按升级规则交给了回合制（「被打」开战），世界里不扣血",
                () => step.IsEscalating && player.Health == healthBefore, 5f);
            yield return Snapshot("怪物出手");
            yield return FinishBattleQuietly();
        }

        /// <summary>
        /// 2026-10-07 内容改动连锁：demo 场景那只怪换成了**市令**（`monster_species` 1003 → `yao` 3），
        /// 设计原文给它的 `defeat_method` 是「暗杀」（`06_怪物分层.md:144`；同文 `:121` R9 的口径是
        /// 「只能暗杀」填 `killable = false`），所以 <c>MonsterRules.ApplyDamage</c> 对它**一律拒伤**——
        /// 常规攻击打不死它。这是 S3「本体打不过任何怪」的设计意图，不是缺陷。
        /// <para>
        /// 本用例因此演这条边界本身：**打上去没有伤害，但它照样挨打转敌对、照样追打你**。
        /// 旧版「生命归零 → 状态 Dead → 倒下后不再移动」的断言随内容退场，规则本身仍有覆盖：
        /// 常规击杀致死走 EditMode 的 <c>MonsterRulesTests</c>（用可击杀的种类），
        /// 而这只怪的死亡途径是绕背处决，由 <c>StealthShowcase</c> 的回放演。
        /// </para>
        /// <para>
        /// 2026-10-15（升级回合制拍板）：打中配了回合制战斗数据的市令，这一下在扣血**之前**就被交给回合制——
        /// 世界里照旧不扣它的血，但「挨打转敌对、连打几下」在暂停的世界里演不出来了。本用例改演：世界里不掉血 + 交给回合制，
        /// 回合制里输掉（回放内数值）回到世界后它仍满血活着。打赢回合制才能解决它，见 <see cref="Escalation_FrontalAttack_PlayerFirst"/>。
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator Attacked_ImmuneToNormalAttack_StillTurnsHostile()
        {
            yield return EnterWorld();
            ResolveBattle();
            yield return Step($"回放内换一份数值（不改资产）：回合制里市令只出招式 1、伤害 {battleRow.PlayerHealth}（= 本场玩家生命），一下击倒收尾",
                () => settingsOverride = battleSetup.OverrideSettings(WithBossOnlySkill1(battleSetup.Settings, battleRow.PlayerHealth)), 0f);
            // 2026-10-15（W6b）：改从巡逻线以南接近——从观察点直线追过去要横穿巡逻者乙（协作者的线）的巡逻线，
            // W6b 实测被它三刀砍死、一刀都没挥出去（出手 0 次）。断言不动。
            yield return GoThroughSouthGap(new[] { SouthStagingPoint });

            int healthBefore = monster.Health;
            yield return Step("在南侧走廊上跟着市令挪，等它正前方落点在空地里、别的巡逻者这几秒看不到那块地，再走向它、按攻击键打它一下", null, 0f);
            yield return WaitFrontWindow();
            yield return StrikeMonster();
            yield return Check($"怪物在世界里的生命仍是 {healthBefore}（只能暗杀的怪不在世界里吃常规伤害）；"
                               + $"这一下按升级规则交给了回合制（出手 {StrikeSwings} 次）",
                () => monster.Health == healthBefore && step.IsEscalating, 2f);
            yield return Snapshot("打不动：常规攻击交给回合制");

            yield return Step("回合制里输掉这一仗（回放内数值），回到世界", null, 0f);
            yield return FinishBattleQuietly();
            yield return Check($"回到世界后它仍活着、生命依然是 {healthBefore}、没有进入 Dead——世界里的常规攻击解决不了它",
                () => monster.Health == healthBefore && monster.Mode != MonsterMode.Dead, 3f);
            yield return Snapshot("常规攻击解决不了它");
        }

        // ───────────────────────── ⑤ 正面普攻打中已察觉的市令 → 正面攻击、玩家先手 → 打赢 ─────────────────────────

        /// <summary>
        /// 让市令从正面察觉你（警戒 / 敌对），站定等它走近，赶在它够到你之前普攻打中它 → 升级回合制「正面攻击」：玩家先手、市令满血开局；
        /// 打赢 → 回到世界：市令倒下（打赢视同击杀）、按种类掉落入包、实时生命按比例写回、遭遇没因此推剧情（自由探索 EncounterId = 0）。
        /// </summary>
        [UnityTest]
        public IEnumerator Escalation_FrontalAttack_PlayerFirst()
        {
            yield return EnterWorld();
            ResolveBattle();
            yield return Step($"回放内换一份数值（不改资产）：回合制里市令只出招式 1、伤害 {VictoryBossDamage}——这一仗稳赢，演「打赢 → 回世界」",
                () => settingsOverride = battleSetup.OverrideSettings(WithBossOnlySkill1(battleSetup.Settings, VictoryBossDamage)), 0f);
            yield return GoThroughSouthGap(new[] { SouthStagingPoint });

            int[] dropIds = CopyDropIds(monsterRules);
            int[] dropsBefore = CountItems(loot, dropIds);
            yield return Step("在南侧走廊上跟着市令挪，等它正前方（偏南）落点在空地里、别的巡逻者这几秒看不到那儿，"
                              + "切到奔跑冲进它正前方的橙区，让它看见你", null, 0f);
            yield return WaitFrontWindow();
            yield return RunInFrontUntilNoticed();

            MonsterMode modeAtStrike = MonsterMode.PatrolWalk;
            int monsterHealthBefore = monster.Health;
            yield return Step($"它察觉了（状态色变）：站定面朝它，等它走近到 {CounterStrikeDistance} 米就普攻（攻击键）", null, 0f);
            yield return CounterStrike(mode => modeAtStrike = mode);
            yield return Check($"打中那一刻市令已察觉（{modeAtStrike}）：这一下交给了回合制（升级在途），世界里没扣它的血",
                () => step.IsEscalating && (modeAtStrike == MonsterMode.Alert || modeAtStrike == MonsterMode.Hostile)
                      && monster.Health == monsterHealthBefore, 2f);

            var opening = new BattleOpeningRecord();
            yield return WatchBattleOpening(opening, BattleOpenTimeoutSeconds);
            yield return Check($"开战方式：正面攻击、玩家先手——第一次轮到你出招时是「{BattleHudRules.TurnText(1, true)}」、出招前没挨打；"
                               + $"市令满血开局 {battleRow.MaxHealth}/{battleRow.MaxHealth}（{opening.Describe()}）",
                () => opening.CommandAwaited && !opening.PlayerHitBeforeCommand
                      && opening.TurnTextAtCommand == BattleHudRules.TurnText(1, true)
                      && opening.BossHealthAtCommand == battleRow.MaxHealth && opening.BossMaxAtCommand == battleRow.MaxHealth);
            yield return Snapshot("正面攻击·玩家先手");

            yield return Step("出招（怒气满放招式 3，否则招式 1），把这一仗打赢", null, 0f);
            yield return FightUntil(() =>
            {
                TrackBattleHealth();
                return BattleResultShown(BattleCueRules.VictoryText);
            }, FightTimeoutSeconds, false);
            yield return Check($"市令倒地：屏幕中央「{BattleCueRules.VictoryText}」完全显出（你还剩 {lastBattleHealth}/{lastBattleMax}）",
                () => BattleResultShown(BattleCueRules.VictoryText), 1f);
            yield return Snapshot("回合制打赢");

            yield return WaitBackToWorld();
            int expectedLive = lastBattleMax > 0
                ? EncounterEscalationRequest.ScaleHealthBack(lastBattleHealth, lastBattleMax, playerRules.MaxHealth)
                : -1;
            yield return Check("回到世界：市令倒下（生命 0、Dead——打赢回合制视同击杀，无视「只能暗杀」）",
                () => monster.Health == 0 && monster.Mode == MonsterMode.Dead, 3f);
            yield return Check($"按它的种类掉落一次：背包里 [{string.Join(", ", dropIds)}] 各 +1",
                () => dropIds.Length > 0 && DropsGained(loot, dropIds, dropsBefore), 3f);
            yield return Check($"实时生命按比例写回：战斗里剩 {lastBattleHealth}/{lastBattleMax} → 实时 {expectedLive}/{playerRules.MaxHealth}"
                               + $"（round(剩余 / 上限 × 实时上限)，至少 1——出场四舍五入，不是进场的 ceil；交回那一刻读到 {liveHealthOnReturn}）",
                () => expectedLive > 0 && liveHealthOnReturn == expectedLive);
            yield return Check("遭遇没因此推剧情：自由探索（EncounterId = 0），没有待消费的胜负、没承载战斗结果",
                () => step.EncounterId == 0 && step.PendingResult == EncounterStep.Result.None && !step.BattleSettlement.HasValue);
            yield return Snapshot("回到世界·市令倒下");
        }

        // ───────────────────────── ⑥ 站进红区不出手，被敌对的市令打中 → 被打、市令先手 → 打输 ─────────────────────────

        /// <summary>
        /// 站进市令正前方、不出手，等它走过来打中你 → 升级回合制「被打」：市令先手。
        /// **这一仗选打输**（回放内数值：市令一下击倒）——打赢那条出口由 <see cref="Escalation_FrontalAttack_PlayerFirst"/> 演，
        /// 这里演另一条：实时生命归零 → 自由探索弹镜碎页 → 交回后重开（回出生点、生命回满）。
        /// </summary>
        [UnityTest]
        public IEnumerator Escalation_MonsterStrikes_Ambushed()
        {
            yield return EnterWorld();
            ResolveBattle();
            yield return Step($"回放内换一份数值（不改资产）：回合制里市令只出招式 1、伤害 {battleRow.PlayerHealth}（= 本场玩家生命）——这一仗选打输",
                () => settingsOverride = battleSetup.OverrideSettings(WithBossOnlySkill1(battleSetup.Settings, battleRow.PlayerHealth)), 0f);
            yield return GoThroughSouthGap(new[] { SouthStagingPoint });

            yield return Step("在南侧走廊上跟着市令挪，等它正前方（偏南）落点在空地里、别的巡逻者这几秒看不到那儿，"
                              + "切到奔跑冲进它正前方，让它看见你", null, 0f);
            yield return WaitFrontWindow();
            yield return RunInFrontUntilNoticed();

            int liveBefore = player.Health;
            yield return Step("站进它的红区、不出手：站定，等它走过来打中你", null, 0f);
            yield return WaitUntil("市令打中你：这一下交给了回合制（升级在途）", () =>
            {
                if (step.IsEscalating)
                {
                    Input.ReleaseStick();
                    return true;
                }

                if (IsAware(monster.Mode))
                {
                    Input.ReleaseStick();
                }
                else
                {
                    DriveToFrontSpot();
                }

                return false;
            }, FrontEngageTimeoutSeconds);
            Input.ReleaseStick();
            yield return Check($"被它打中：世界里没扣血（生命仍是 {liveBefore}），这一下交给了回合制",
                () => step.IsEscalating && player.Health == liveBefore);

            var opening = new BattleOpeningRecord();
            yield return WatchBattleOpening(opening, BattleOpenTimeoutSeconds);
            yield return Check($"开战方式：被打、市令先手——开场是「{BattleHudRules.TurnText(1, false)}」，你还没出招它就先出了手（{opening.Describe()}）",
                () => opening.FirstTurnText == BattleHudRules.TurnText(1, false) && opening.PlayerHitBeforeCommand && !opening.CommandAwaited);
            yield return Snapshot("被打·市令先手");

            yield return WaitUntil("这一仗收场（打输）", () => !flow.IsBattleRunning && !step.IsEscalating, BackToWorldTimeoutSeconds);
            yield return Check("打输：实时生命归零，镜碎页弹出（自由探索的失败出口），遭遇停下",
                () => player.Health == 0 && ui.Get<MirrorShatterView>() != null && !step.IsActive, 10f);
            yield return Snapshot("打输·镜碎页");

            yield return Step("按确认键交回镜碎页：重开", null, 0f);
            float deadline = Time.realtimeSinceStartup + 15f;
            while (ui.Get<MirrorShatterView>() != null && Time.realtimeSinceStartup < deadline)
            {
                yield return Input.Press(inputService.Actions.UI.Submit);
                yield return new WaitForSecondsRealtime(0.3f);
            }

            yield return Check($"重开：遭遇重新在跑、生命回满 {playerRules.MaxHealth}、回到出生点附近",
                () => step.IsActive && player.Health == playerRules.MaxHealth
                      && Vector2.Distance(player.Position, DemoSpawnPoint) <= 1.5f, 20f);
            yield return WaitCurtainRevealed();
            yield return Snapshot("镜碎后重开");
        }

        // ───────────────────────── ⑦ 潜行绕到未察觉的市令背后普攻 → 偷袭：玩家先手、市令开局扣 20% → 打赢 ─────────────────────────

        /// <summary>
        /// 潜行绕到**未察觉**的市令背后，按**攻击键**（普攻，不是 F 处决）打中它 → 升级回合制「偷袭」：玩家先手，
        /// 市令开局先掉 20%（<c>BossBattleRules.PercentOfHealth</c>：整数向下取整，6 × 20% = 1 → 开局 5/6）；把这一仗打赢、回到世界。
        /// </summary>
        [UnityTest]
        public IEnumerator Escalation_UnawareStrike_SneakAttack()
        {
            yield return EnterWorld();
            ResolveBattle();
            yield return Step($"回放内换一份数值（不改资产）：回合制里市令只出招式 1、伤害 {VictoryBossDamage}——这一仗稳赢",
                () => settingsOverride = battleSetup.OverrideSettings(WithBossOnlySkill1(battleSetup.Settings, VictoryBossDamage)), 0f);
            yield return GoThroughSouthGap(new[] { SouthStagingPoint });

            yield return Step("在南侧走廊上跟着市令挪，等它停步、这几秒谁都扫不到「走廊 → 它背后」这条路，再绕到它背后"
                              + "（离任一巡逻者 2 米内按住潜行），贴到身后对准它按攻击键（普攻，不是 F）", null, 0f);
            yield return SneakStrikeFromBehind();
            yield return Check($"打中那一刻市令没察觉（{sneakStrikeMode}）、你在潜行（{sneakStrikeSneaking}）：这一下交给了回合制（偷袭）",
                () => step.IsEscalating && (sneakStrikeMode == MonsterMode.PatrolWalk || sneakStrikeMode == MonsterMode.PatrolPause)
                      && sneakStrikeSneaking);
            yield return ReleaseSneak();

            int percent = battleSetup.Settings.Entry.SneakBossHealthLossPercent;
            int expectedBoss = battleRow.MaxHealth - battleRow.MaxHealth * percent / 100;
            var opening = new BattleOpeningRecord();
            yield return WatchBattleOpening(opening, BattleOpenTimeoutSeconds);
            yield return Check($"开战方式：偷袭、玩家先手——第一次轮到你出招时是「{BattleHudRules.TurnText(1, true)}」、出招前没挨打；"
                               + $"市令开局生命 {expectedBoss}/{battleRow.MaxHealth}（{battleRow.MaxHealth} − ⌊{battleRow.MaxHealth} × {percent}%⌋）"
                               + $"（{opening.Describe()}）",
                () => opening.CommandAwaited && !opening.PlayerHitBeforeCommand
                      && opening.TurnTextAtCommand == BattleHudRules.TurnText(1, true)
                      && opening.BossHealthAtCommand == expectedBoss && opening.BossMaxAtCommand == battleRow.MaxHealth);
            yield return Snapshot("偷袭·市令开局扣血");

            yield return Step("出招（怒气满放招式 3，否则招式 1），把这一仗打完", null, 0f);
            yield return FightUntil(() => BattleResultShown(BattleCueRules.VictoryText), FightTimeoutSeconds, false);
            yield return WaitBackToWorld();
            yield return Check("打完回到世界：市令倒下（打赢回合制视同击杀）",
                () => monster.Health == 0 && monster.Mode == MonsterMode.Dead, 3f);
            yield return Snapshot("偷袭打完·回到世界");
        }

        // ───────────────────────── ⑧ 东南角巡夜人：追上打中 → 击倒（不升级）→ 挨到生命归零才判死 ─────────────────────────

        /// <summary>
        /// 巡夜人（种类 1001）没配回合制战斗数据：被它察觉、追上打中 → **击倒**（倒地 / 挣扎：不能攻击、只能慢慢挪），
        /// 全程不升级回合制；继续挨打到生命归零才判死 → 自由探索弹镜碎页。
        /// 去东南角的路同 StealthShowcase ④：缝口候场 → 预判出窗口后一口气跑过两条巡逻线西端 → 东南角候场点。
        /// </summary>
        [UnityTest]
        public IEnumerator Caught_Xunye_KnockdownThenDowned()
        {
            yield return EnterWorld();
            ResolveBattle();
            int xunyeIndex = ShowcasePatrolForecast.IndexOf(step, XunyePatrollerId);
            MonsterRules xunye = xunyeIndex < 0 ? null : patrollers[xunyeIndex];
            if (xunye == null)
            {
                Assert.Fail("前置不成立：场上找不到巡夜人（patrolActors 里 stableId = patrol_xunyeren 的那只），这条回放演不了。");
            }

            int xunyeKind = xunye.Kind == null ? 0 : xunye.Kind.KindId;
            yield return Check($"巡夜人（种类 {xunyeKind}）没配回合制战斗数据：BossRosterConfig 里没有它那一行，被它打中不会升级",
                () => !flow.CanEscalate(xunyeKind));

            yield return GoThroughSouthGap(new[] { SoutheastTransit, XunyeStagingPoint });
            yield return Check("到了巡夜人巡逻线西北的候场点，一路没惊动任何巡逻者",
                () => (player.Position - XunyeStagingPoint).magnitude <= 0.6f && AllPatrollersCalm());

            bool escalated = false;
            yield return Step("走进巡夜人正前方的视野里（橙区），让它看见你", null, 0f);
            yield return WaitUntil("巡夜人察觉你（警戒 / 敌对）", () =>
            {
                escalated |= step.IsEscalating;
                MonsterModel model = xunye.Model;
                if (IsAware(model.Mode))
                {
                    Input.ReleaseStick();
                    return true;
                }

                Vector2 spot = model.Position + model.Facing * XunyeFrontDistance;
                if (!SoutheastArea.Contains(spot))
                {
                    Input.ReleaseStick();
                    return false;
                }

                Vector2 delta = spot - player.Position;
                Input.SetStick(delta.magnitude > 0.2f ? delta.normalized : Vector2.zero);
                return false;
            }, XunyeNoticeTimeoutSeconds);
            Input.ReleaseStick();

            int healthBefore = player.Health;
            KnockdownRules knockdown = playerRules.Knockdown;
            Vector2 away = player.Position - xunye.Model.Position;
            yield return Step($"转身走开 {XunyeBackOffSeconds} 秒再站住，等它追上来", null, 0f);
            yield return Walk(away.magnitude > 0.01f ? away.normalized : Vector2.left, XunyeBackOffSeconds);
            yield return WaitUntil("巡夜人追上来打中你", () =>
            {
                escalated |= step.IsEscalating;
                return player.Health < healthBefore || (knockdown != null && knockdown.IsDowned);
            }, FrontEngageTimeoutSeconds);
            yield return Check(knockdown == null
                    ? "被打中就被击倒（取不到击倒状态机：StealthKernel 没接上玩家？）"
                    : $"被打中就被击倒：{knockdown.Phase}（不能攻击、移动速度倍率 {knockdown.MoveSpeedMultiplier:0.##} < 1，只能慢慢挪）；"
                      + $"生命 {healthBefore} → {player.Health}；没升级回合制",
                () => knockdown != null && knockdown.IsDowned && !knockdown.CanAttack(true) && knockdown.MoveSpeedMultiplier < 1f
                      && player.Health < healthBefore && !escalated && !step.IsEscalating);
            yield return Snapshot("被巡夜人击倒");

            yield return Step("倒在地上不动，它接着打", null, 0f);
            yield return WaitUntil("生命归零", () =>
            {
                escalated |= step.IsEscalating;
                return player.Health <= 0;
            }, 15f);
            yield return Check("挨到生命归零才判死：镜碎页弹出（自由探索的失败出口），遭遇停下；全程没升级回合制",
                () => player.Health == 0 && ui.Get<MirrorShatterView>() != null && !step.IsActive && !escalated, 10f);
            yield return Snapshot("生命归零·镜碎页");
        }

        // ───────────────────────── 进场与驱动 ─────────────────────────

        private bool IsPatrolling()
        {
            return monster.Mode == MonsterMode.PatrolWalk || monster.Mode == MonsterMode.PatrolPause;
        }

        private static bool IsAware(MonsterMode mode) => mode == MonsterMode.Alert || mode == MonsterMode.Hostile;

        /// <summary>标题「开始」进世界（EnterDemoWorld），等容器里的输入服务、玩家 / 怪物模型与规则、遭遇与怪物配置可用；取场上全部巡逻者。</summary>
        private IEnumerator EnterWorld()
        {
            yield return EnterDemoWorld("遭遇逻辑在跑、输入服务与玩家 / 怪物模型可用", () =>
            {
                inputService = ResolveService<IInputService>();
                player = ResolveService<PlayerModel>();
                monster = ResolveService<MonsterModel>();
                monsterConfig = ResolveService<MonsterConfig>();
                step = ResolveService<EncounterStep>();
                monsterRules = ResolveService<MonsterRules>();
                playerRules = ResolveService<PlayerRules>();
                return inputService != null && inputService.Actions != null
                       && player != null && monster != null && monsterConfig != null
                       && step != null && monsterRules != null && playerRules != null;
            }, "进世界后容器里取不到 PlayerModel / MonsterModel / IInputService / MonsterConfig / EncounterStep / MonsterRules / PlayerRules，后续步骤无法驱动");

            patrollers = ShowcasePatrolForecast.Patrollers(step);
            patrolRoutes = ShowcasePatrolForecast.RoutesOf(patrollers);
            mainIndex = Array.IndexOf(patrollers, monsterRules);
            sneakHeld = false;
            lastBattleHealth = -1;
            lastBattleMax = -1;
            liveHealthOnReturn = -1;
        }

        /// <summary>
        /// 升级 / 被抓那几条要的战斗侧服务与市令那一行战斗数据。取不到就是接线不够（BattleInstaller 没挂 / 名册没这一行），前置条件不成立，直接中断。
        /// </summary>
        private void ResolveBattle()
        {
            flow = ResolveService<BattleFlow>();
            arena = ResolveService<BattleArena>();
            battleSetup = ResolveService<BattleSetup>();
            presenter = BattlePresenter;
            loot = ResolveService<LootService>();
            ui = ResolveService<IUIService>();
            BossRoster roster = ResolveService<BossRoster>();
            if (flow == null || arena == null || battleSetup == null || presenter == null || loot == null || ui == null || roster == null)
            {
                Assert.Fail("进世界后容器里取不到 BattleFlow / BattleArena / BattleSetup / 战斗表现 / LootService / IUIService / BossRoster"
                            + "（检查 Boot 场景 GameBootstrap 上的 BattleInstaller 与 LootInstaller）");
            }

            int kind = monsterRules.Kind == null ? 0 : monsterRules.Kind.KindId;
            if (!roster.TryGetByKind(kind, out battleRow))
            {
                Assert.Fail($"BossRosterConfig 里没有市令（种类 {kind}）那一行回合制战斗数据：升级回合制演不了（应有 monster_1003）。");
            }
        }

        /// <summary>
        /// 逐帧朝「怪物位置 − 朝向 × 1 米」推摇杆；离怪物 2 米内改按住潜行（按住到用例结束）。
        /// 记录两件事：是否出现过「潜行中、在怪物背后半圆、1.5 米内」的时刻；全程怪物有没有进入警戒 / 敌对。
        /// 出现过背后近身后再原地停 0.8 秒给人看，最多 4 秒。
        /// </summary>
        private IEnumerator SneakUpBehind(Action<bool> reportClose, Action<bool> reportCalm)
        {
            bool sneaking = false;
            bool close = false;
            bool calm = true;
            float closeSince = -1f;
            float deadline = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < deadline)
            {
                Vector2 toPlayer = player.Position - monster.Position;
                if (!sneaking && toPlayer.magnitude <= SneakFromDistance)
                {
                    yield return Input.Hold(inputService.Actions.Gameplay.Sneak);
                    sneaking = true;
                    continue;
                }

                Vector2 behind = monster.Position - monster.Facing * BehindDistance;
                Vector2 toBehind = behind - player.Position;
                if (toBehind.magnitude <= 0.15f)
                {
                    Input.ReleaseStick();
                }
                else
                {
                    Input.SetStick(toBehind.normalized);
                }

                if (monster.Mode == MonsterMode.Alert || monster.Mode == MonsterMode.Hostile)
                {
                    calm = false;
                }

                if (player.IsSneaking && toPlayer.magnitude <= 1.5f && Vector2.Dot(monster.Facing, toPlayer) < 0f && !close)
                {
                    close = true;
                    closeSince = Time.realtimeSinceStartup;
                }

                if (close && Time.realtimeSinceStartup - closeSince >= 0.8f)
                {
                    break;
                }

                yield return null;
            }

            Input.ReleaseStick();
            reportClose(close);
            reportCalm(calm);
        }

        /// <summary>
        /// ② 收尾的撤离：切到奔跑，朝「背离市令、偏南」跑，直到离它超过警戒半径 + 0.5 米、它不再敌对或满 4 秒；
        /// 只在巡逻线附近那块空地（<see cref="FrontArea"/>）里跑，南边远离巡逻者乙的巡逻线（z = 5）。不记报告条目。
        /// </summary>
        private IEnumerator RetreatFromMonster()
        {
            if (player.Health <= 0 || step.IsEscalating)
            {
                yield break;
            }

            yield return Input.PressUntil(inputService.Actions.Gameplay.Run, () => player.IsRunning);
            float escape = monsterRules.Config.ResolveAlertRadius(monsterRules.Kind) + 0.5f;
            float deadline = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < deadline && !step.IsEscalating
                   && Vector2.Distance(player.Position, monster.Position) <= escape && monster.Mode == MonsterMode.Hostile)
            {
                Vector2 away = player.Position - monster.Position;
                Vector2 direction = (away.magnitude > 0.01f ? away.normalized : Vector2.down) + Vector2.down;
                Vector2 next = player.Position + direction.normalized * 0.5f;
                if (!FrontArea.Contains(next))
                {
                    direction = new Vector2(FrontArea.center.x - player.Position.x, FrontArea.yMin - player.Position.y);
                }

                Input.SetStick(direction.normalized);
                yield return null;
            }

            Input.ReleaseStick();
            yield return Input.PressUntil(inputService.Actions.Gameplay.Run, () => !player.IsRunning);
        }

        // ───────────────────────── 升级回合制：正面走位 ─────────────────────────

        /// <summary>市令正前方、往南偏 <see cref="FrontSpotSouthDegrees"/> 度的落点（橙区里、视野锥里）。</summary>
        private Vector2 FrontSpot() => monster.Position + TurnTowardSouth(monster.Facing, FrontSpotSouthDegrees) * FrontSpotDistance;

        /// <summary>
        /// 等一个「冲进市令正前方」的窗口（等的时候在南侧走廊上跟着落点挪）：它还在巡逻（没被惊动）、正前方（偏南）落点在空地里，
        /// 且**别的巡逻者**（市令自己要的就是看见你）接下来「跑过去 + 在那儿待 <see cref="FrontLingerSeconds"/> 秒」这段时间都看不到这条路
        /// （<see cref="ShowcasePatrolForecast.AnyMaySee"/>）。
        /// <para>
        /// 不要求它停步（W6b 第三跑：要求停步时 40 秒里它停了三次、落点三次都出了空地，没等到）：落点偏南、离巡逻者乙的巡逻线近 2.9 米，
        /// 已在乙的警戒半径 4.5 之外，它走着时落点跟着挪，冲过去的路上照样朝落点追（<see cref="RunInFrontUntilNoticed"/>），它一看见你就朝你走来。
        /// </para>
        /// </summary>
        private IEnumerator WaitFrontWindow()
        {
            yield return WaitUntil("市令还在巡逻、正前方落点在空地里，别的巡逻者这几秒看不到去那儿的路（按当前位置 / 朝向 / 巡逻路线预判）", () =>
            {
                Vector2 spot = FrontSpot();
                // 等的时候在南侧走廊上跟着落点挪（主怪 / 巡逻者乙都看不到这条走廊），窗口一开就是一段短直路。
                StepToward(SouthLanePoint(spot.x));
                if (!IsPatrolling() || !FrontArea.Contains(spot))
                {
                    return false;
                }

                float runSeconds = Vector2.Distance(player.Position, spot) / FrontRunIn.SpeedLow;

                forecastPath.Clear();
                forecastPath.Add(player.Position);
                forecastPath.Add(spot);
                return !ShowcasePatrolForecast.AnyMaySee(patrollers, patrolRoutes, forecastPath, runSeconds + FrontLingerSeconds,
                    FrontRunIn, player.Snapshot, mainIndex);
            }, FrontWindowTimeoutSeconds);
            Input.ReleaseStick();
        }

        /// <summary>切到奔跑朝正前方落点冲，直到市令察觉（警戒 / 敌对）；随后松杆、切回步行。不记步骤（调用方已记）。</summary>
        private IEnumerator RunInFrontUntilNoticed()
        {
            yield return Input.PressUntil(inputService.Actions.Gameplay.Run, () => player.IsRunning);
            yield return WaitUntil("市令从正面看见你（警戒 / 敌对，状态色变）", () =>
            {
                if (IsAware(monster.Mode) || step.IsEscalating)
                {
                    Input.ReleaseStick();
                    return true;
                }

                DriveToFrontSpot();
                return false;
            }, 10f);
            Input.ReleaseStick();
            yield return Input.PressUntil(inputService.Actions.Gameplay.Run, () => !player.IsRunning);
        }

        /// <summary>朝市令正前方的落点推摇杆；落点不在空地里就松杆原地等（它转向 / 走开了）。</summary>
        private void DriveToFrontSpot()
        {
            Vector2 spot = FrontSpot();
            if (!FrontArea.Contains(spot))
            {
                Input.ReleaseStick();
                return;
            }

            Vector2 delta = spot - player.Position;
            Input.SetStick(delta.magnitude > 0.2f ? delta.normalized : Vector2.zero);
        }

        /// <summary>
        /// 站定迎击：它察觉了就松杆、面朝它（<see cref="ShowcaseScenario.FaceMonster()"/>），等它走近到 <see cref="CounterStrikeDistance"/> 米、
        /// 攻击冷却好了就按攻击键（按到冷却转起来为止，最多 0.3 秒）。玩家不往前凑：两边对冲时 0.8~1 米那一小段只够两三个 tick，
        /// 站着让它一个走近，按键才稳稳落在「它够不着你、你够得着它」的那一段里。它没察觉（还没看见 / 跟丢）就接着往它正前方走。
        /// 被接管（升级在途）那一刻把它的状态报出来（世界随即暂停，状态就停在出手那一 tick）。
        /// </summary>
        private IEnumerator CounterStrike(Action<MonsterMode> reportMode)
        {
            bool faced = false;
            float deadline = Time.realtimeSinceStartup + FrontEngageTimeoutSeconds;
            while (!step.IsEscalating && player.Health > 0 && Time.realtimeSinceStartup < deadline)
            {
                if (!IsAware(monster.Mode))
                {
                    faced = false;
                    DriveToFrontSpot();
                    yield return null;
                    continue;
                }

                Vector2 toMonster = monster.Position - player.Position;
                float distance = toMonster.magnitude;
                if (!faced || (distance > 0.01f && Vector2.Dot(player.Facing, toMonster / distance) < 0.5f))
                {
                    Input.ReleaseStick();
                    yield return FaceMonster();
                    faced = true;
                    continue;
                }

                Input.ReleaseStick();
                if (distance > CounterStrikeDistance || player.AttackCooldownLeft > 0f)
                {
                    yield return null;
                    continue;
                }

                yield return Input.PressUntil(inputService.Actions.Gameplay.Attack,
                    () => player.AttackCooldownLeft > 0f || step.IsEscalating, 0.3f);
                float settle = Time.realtimeSinceStartup + 0.2f;
                while (!step.IsEscalating && Time.realtimeSinceStartup < settle)
                {
                    yield return null;
                }
            }

            Input.ReleaseStick();
            if (step.IsEscalating)
            {
                reportMode(monster.Mode);
            }
        }

        // ───────────────────────── 升级回合制：偷袭走位 ─────────────────────────

        /// <summary>
        /// 偷袭：在南侧走廊上跟着它背后那一点挪、等窗口（<see cref="SneakWindowOpen"/>），放行一次就算动身；朝「市令位置 − 朝向 × 0.75 米」走，
        /// 离任一巡逻者 2 米内按住潜行；贴到背后、它仍没察觉、你在潜行时，对准它按攻击键。
        /// 它在你贴上去之前走起来了（潜行 1.5 追不上巡逻 2.0）或被惊动了，就退回南侧走廊等下一次（同 StealthShowcase 的退让口径）。
        /// 挥刀被接管那一刻记下它的状态与你是否在潜行。不记报告条目。
        /// </summary>
        private IEnumerator SneakStrikeFromBehind()
        {
            bool committed = false;
            float deadline = Time.realtimeSinceStartup + SneakStrikeTimeoutSeconds;
            while (!step.IsEscalating && player.Health > 0 && Time.realtimeSinceStartup < deadline)
            {
                yield return SyncSneakKey();
                Vector2 toPlayer = player.Position - monster.Position;
                float distance = toPlayer.magnitude;
                Vector2 behind = monster.Position - monster.Facing * SneakStrikeBehind;
                bool unaware = monster.Mode == MonsterMode.PatrolWalk || monster.Mode == MonsterMode.PatrolPause;

                // 贴到背后了：对准它、挥刀。
                if (committed && unaware && player.IsSneaking && distance <= CounterStrikeDistance
                    && Vector2.Dot(monster.Facing, toPlayer) < 0f)
                {
                    Vector2 toMonster = -toPlayer / Mathf.Max(distance, 0.01f);
                    if (Vector2.Dot(player.Facing, toMonster) < 0.5f)
                    {
                        // 轻推一下把朝向对准它（潜行 1.5 × 0.3，一两帧只挪几厘米）。
                        Input.SetStick(toMonster * 0.3f);
                        yield return null;
                        Input.ReleaseStick();
                        yield return null;
                        continue;
                    }

                    Input.ReleaseStick();
                    if (player.AttackCooldownLeft > 0f)
                    {
                        yield return null;
                        continue;
                    }

                    MonsterMode modeBefore = monster.Mode;
                    bool sneakingBefore = player.IsSneaking;
                    yield return Input.PressUntil(inputService.Actions.Gameplay.Attack,
                        () => player.AttackCooldownLeft > 0f || step.IsEscalating, 0.3f);
                    if (step.IsEscalating)
                    {
                        // 世界在接管那一 tick 停住：此刻读到的就是挥刀那一刻它的状态。
                        sneakStrikeMode = monster.Mode;
                        sneakStrikeSneaking = sneakingBefore && player.IsSneaking;
                        break;
                    }

                    sneakStrikeMode = modeBefore;
                    continue;
                }

                // 被惊动了，或它在你贴上去之前走起来了：退回南侧走廊等下一次。
                Vector2 lane = SouthLanePoint(behind.x);
                if (!unaware || (monster.Mode == MonsterMode.PatrolWalk && distance > SneakStrikeBehind + 0.6f))
                {
                    committed = false;
                    StepToward(lane);
                    yield return null;
                    continue;
                }

                // 没动身时在南侧走廊上跟着它背后那一点挪（主怪 / 巡逻者乙都看不到这条走廊）。
                bool onLane = (player.Position - lane).magnitude <= LaneCommitRadius;
                if (!committed)
                {
                    if (!onLane)
                    {
                        StepToward(lane);
                        yield return null;
                        continue;
                    }

                    if (!SneakWindowOpen(behind))
                    {
                        Input.ReleaseStick();
                        yield return null;
                        continue;
                    }

                    committed = true;
                }

                StepToward(behind);
                yield return null;
            }

            Input.ReleaseStick();
        }

        /// <summary>
        /// 偷袭的动身判据（同 StealthShowcase.ApproachWindowOpen）：市令在停步、剩余停顿够「2 米开外步行、2 米内潜行」走到背后再待
        /// <see cref="BehindLingerSeconds"/> 秒；且场上**每一只**巡逻者（含市令自己：它停在端点时起步那一下会掉头）接下来这几秒都扫不到这一整段。
        /// </summary>
        private bool SneakWindowOpen(Vector2 behindPoint)
        {
            if (monster.Mode != MonsterMode.PatrolPause)
            {
                return false;
            }

            float length = (behindPoint - player.Position).magnitude;
            float arrival = Mathf.Max(0f, length - SneakKeyDistance) / WalkSpeed + Mathf.Min(length, SneakKeyDistance) / SneakSpeed;
            if (monster.PatrolPauseLeft < arrival + BehindLingerSeconds)
            {
                return false;
            }

            forecastPath.Clear();
            forecastPath.Add(player.Position);
            forecastPath.Add(behindPoint);
            return !ShowcasePatrolForecast.AnyMaySee(patrollers, patrolRoutes, forecastPath, length / SneakSpeed + BehindLingerSeconds,
                SneakApproach, player.Snapshot, -1);
        }

        /// <summary>朝目标推一帧摇杆；0.1 米内松杆。</summary>
        private void StepToward(Vector2 target)
        {
            Vector2 delta = target - player.Position;
            if (delta.magnitude <= 0.1f)
            {
                Input.ReleaseStick();
                return;
            }

            Input.SetStick(delta.normalized);
        }

        /// <summary>离最近那只活着的巡逻者多远（米）；一只都取不到时返回极大值。</summary>
        private float NearestPatrollerDistance()
        {
            float best = float.MaxValue;
            for (int i = 0; i < patrollers.Length; i++)
            {
                MonsterRules rules = patrollers[i];
                if (rules == null || rules.Model.Health <= 0)
                {
                    continue;
                }

                best = Mathf.Min(best, (rules.Model.Position - player.Position).magnitude);
            }

            return best;
        }

        /// <summary>潜行键跟着「离最近那只巡逻者的距离」走：2 米内按住、走远了松开；只在状态变化时动键（同 StealthShowcase.SyncSneakKey）。</summary>
        private IEnumerator SyncSneakKey()
        {
            bool want = NearestPatrollerDistance() <= SneakKeyDistance;
            if (want == sneakHeld)
            {
                yield break;
            }

            sneakHeld = want;
            if (want)
            {
                yield return Input.Hold(inputService.Actions.Gameplay.Sneak);
            }
            else
            {
                yield return Input.Release(inputService.Actions.Gameplay.Sneak);
            }
        }

        /// <summary>松开潜行键，并把逐帧同步的状态一起复位。</summary>
        private IEnumerator ReleaseSneak()
        {
            sneakHeld = false;
            yield return Input.Release(inputService.Actions.Gameplay.Sneak);
        }

        // ───────────────────────── 回合制收场 ─────────────────────────

        /// <summary>打仗途中逐帧记下玩家头顶生命条（打赢那一刻的剩余 / 上限）。</summary>
        private void TrackBattleHealth()
        {
            BattleView view = presenter.View;
            int health = ReadBattleHealthBar(view, PlayerHudName, 0);
            int max = ReadBattleHealthBar(view, PlayerHudName, 1);
            if (health >= 0 && max > 0)
            {
                lastBattleHealth = health;
                lastBattleMax = max;
            }
        }

        /// <summary>
        /// 记一条等待「这一仗收场、回到世界」：升级不再在途、战斗流程空闲、战斗场景已卸载；再等黑幕揭开。
        /// 升级结束那一帧读一次实时生命（<see cref="liveHealthOnReturn"/>）：交回结果时写的就是它，之后世界恢复推进还可能被别的巡逻者打。
        /// </summary>
        private IEnumerator WaitBackToWorld()
        {
            liveHealthOnReturn = -1;
            yield return WaitUntil("这一仗收场、回到世界（升级不再在途、战斗场景已卸载）", () =>
            {
                if (!step.IsEscalating && liveHealthOnReturn < 0)
                {
                    liveHealthOnReturn = player.Health;
                }

                return !step.IsEscalating && !flow.IsBattleRunning && !arena.IsLoaded;
            }, BackToWorldTimeoutSeconds);
            yield return WaitCurtainRevealed();
        }

        /// <summary>
        /// 把这一仗打完再收尾（回放内数值让它一下击倒，被打开局不用出招；正面 / 偷袭开局轮到你就按招式 1），回到世界。
        /// 胜负不是调用方要看的东西，不记检查点；仗没打完就收尾，退场销毁作用域会撞上进行中的战斗。
        /// </summary>
        private IEnumerator FinishBattleQuietly()
        {
            yield return FightUntil(() => !flow.IsBattleRunning, FightTimeoutSeconds, true);
            yield return WaitUntil("这一仗打完、回到世界（升级不再在途）",
                () => !flow.IsBattleRunning && !step.IsEscalating && !arena.IsLoaded, BackToWorldTimeoutSeconds);
            yield return WaitCurtainRevealed();
        }

        // ───────────────────────── 被抓那一条 ─────────────────────────

        /// <summary>场上活着的巡逻怪都还在巡逻两态（没有谁被惊动）。</summary>
        private bool AllPatrollersCalm()
        {
            for (int i = 0; i < patrollers.Length; i++)
            {
                MonsterRules rules = patrollers[i];
                if (rules != null && rules.Model.Health > 0 && !(rules.Model.Mode == MonsterMode.PatrolWalk
                                                                 || rules.Model.Mode == MonsterMode.PatrolPause))
                {
                    return false;
                }
            }

            return true;
        }

        // ───────────────────────── 掉落核对（同 StealthShowcase ④）─────────────────────────

        /// <summary>这只怪的种类掉落表（<see cref="MonsterRules.DropItemIds"/>）抄一份；没接种类表时为空。</summary>
        private static int[] CopyDropIds(MonsterRules rules)
        {
            IReadOnlyList<int> ids = rules.DropItemIds;
            var copy = new int[ids == null ? 0 : ids.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = ids[i];
            }

            return copy;
        }

        /// <summary>背包里这些物品各有几个（同一物品出现几次就各查一次，按位置对应）。</summary>
        private static int[] CountItems(LootService lootService, int[] itemIds)
        {
            var counts = new int[itemIds.Length];
            for (int i = 0; i < itemIds.Length; i++)
            {
                counts[i] = lootService.Items.TryGetValue(itemIds[i], out int count) ? count : 0;
            }

            return counts;
        }

        /// <summary>掉落表里每一种物品都按它在表里出现的次数涨了：只结一次掉落、且只结这一只怪的那一份。</summary>
        private static bool DropsGained(LootService lootService, int[] itemIds, int[] before)
        {
            int[] now = CountItems(lootService, itemIds);
            for (int i = 0; i < itemIds.Length; i++)
            {
                int expected = 0;
                for (int j = 0; j < itemIds.Length; j++)
                {
                    if (itemIds[j] == itemIds[i])
                    {
                        expected++;
                    }
                }

                if (now[i] - before[i] != expected)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
