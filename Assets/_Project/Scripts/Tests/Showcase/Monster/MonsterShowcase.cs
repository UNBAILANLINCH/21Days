// 职责：走 Boot 真实流程，以虚拟输入验证巡逻、察觉、追逐、普攻拒伤与抓捕。
// 2026-10-18 用户定案：普通巡逻只警戒、追逐和抓捕，不因任何命中进入回合制；正式剧情/BOSS入口由 Battle 回放覆盖。
// 普通巡逻保持真实路线与种类数值，不修改规则、不瞬移、不替换战斗配置。
using System;
using System.Collections;
using System.Collections.Generic;
using Game.Battle;
using Game.Core.Input;
using Game.Core.UI;
using Game.Mirror;
using Game.Monster;
using Game.Player;
using Game.Stealth;
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

        // ── 正面接近与潜行的走位常量 ─────────────────────────────────────────────────────────────

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

        /// <summary>路径预判在任一巡逻者两米内按潜行计算，避开贴身察觉。</summary>
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

        private BattleFlow flow;
        private BattleArena arena;
        private IUIService ui;

        protected override string Module => "Monster";

        /// <summary>世界由流程加载（标题「开始」→ MonsterEncounterState → SampleScene），基类不再叠加载一份。</summary>
        protected override string ScenePath => null;

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

            yield return Check("看见玩家只转敌对，不开启回合制", () => !step.IsEscalating && !flow.IsBattleRunning && !arena.IsLoaded);
            yield return RetreatFromMonster();
        }

        [UnityTest]
        public IEnumerator Hostile_ChasesFleeingPlayerAndHitsIt()
        {
            yield return EnterWorld();
            ResolveBattle();
            // 从南侧接近并向南逃，避免先被北侧的巡逻者乙命中。
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
            yield return Check("怪物追上命中后扣实时血量并击倒，保持世界场景，不开启回合制",
                () => player.Health < healthBefore && playerRules.Knockdown != null && playerRules.Knockdown.IsDowned
                      && step.Facts.IsTrue(StealthFactKeys.ChaseCaught)
                      && !step.IsEscalating && !flow.IsBattleRunning && !arena.IsLoaded, 5f);
            yield return Snapshot("普通巡逻追击命中·实时抓捕");
        }

        /// <summary>
        /// 2026-10-07 内容改动连锁：demo 场景那只怪换成了**市令**（`monster_species` 1003 → `yao` 3），
        /// 设计原文给它的 `defeat_method` 是「暗杀」（`06_怪物分层.md:144`；同文 `:121` R9 的口径是
        /// 「只能暗杀」填 `killable = false`），所以 <c>MonsterRules.ApplyDamage</c> 对它**一律拒伤**——
        /// 常规攻击打不死它。这是 S3「本体打不过任何怪」的设计意图，不是缺陷。
        /// <para>
        /// 本用例演已察觉的普通巡逻拒绝普攻伤害，并且不会开启回合制。
        /// 旧版「生命归零 → 状态 Dead → 倒下后不再移动」的断言随内容退场，规则本身仍有覆盖：
        /// 常规击杀致死走 EditMode 的 <c>MonsterRulesTests</c>（用可击杀的种类），
        /// 而这只怪的死亡途径是绕背处决，由 <c>StealthShowcase</c> 的回放演。
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator Attacked_ImmuneToNormalAttack_DoesNotStartTurnBasedBattle()
        {
            yield return EnterWorld();
            ResolveBattle();
            // 2026-10-15（W6b）：改从巡逻线以南接近——从观察点直线追过去要横穿巡逻者乙（协作者的线）的巡逻线，
            // W6b 实测被它三刀砍死、一刀都没挥出去（出手 0 次）。断言不动。
            yield return GoThroughSouthGap(new[] { SouthStagingPoint });

            int healthBefore = monster.Health;
            yield return Step("在南侧走廊上跟着市令挪，等它正前方落点在空地里、别的巡逻者这几秒看不到那块地，再走向它、按攻击键打它一下", null, 0f);
            yield return WaitFrontWindow();
            yield return WaitUntil("走入视野让普通巡逻察觉，然后停下迎击", () =>
            {
                if (IsAware(monster.Mode))
                {
                    Input.ReleaseStick();
                    return true;
                }

                Vector2 spot = FrontSpot();
                if (FrontArea.Contains(spot)) StepToward(spot);
                else Input.ReleaseStick();
                return false;
            }, FrontEngageTimeoutSeconds);
            Input.ReleaseStick();
            int swingsBefore = StrikeSwings;
            yield return StrikeWhenItComes(monster);
            yield return Check($"真实普攻被采到（出手 {StrikeSwings - swingsBefore} 次）：市令拒伤，保持实时警戒/追逐，不进入回合制",
                () => StrikeSwings > swingsBefore && monster.Health == healthBefore && IsAware(monster.Mode)
                      && !step.IsEscalating && !flow.IsBattleRunning && !arena.IsLoaded, 2f);
            yield return Snapshot("普通巡逻受击·拒伤不进回合制");
        }

        // ───────────────────────── 东南角巡夜人：追上打中 → 击倒（不升级）→ 挨到生命归零才判死 ─────────────────────────

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
        }

        /// <summary>读取真实战斗服务，确认普通巡逻没有按种类映射；显式剧情/BOSS数据仍保留。</summary>
        private void ResolveBattle()
        {
            flow = ResolveService<BattleFlow>();
            arena = ResolveService<BattleArena>();
            ui = ResolveService<IUIService>();
            BossRoster roster = ResolveService<BossRoster>();
            Assert.That(flow, Is.Not.Null);
            Assert.That(arena, Is.Not.Null);
            Assert.That(ui, Is.Not.Null);
            Assert.That(roster, Is.Not.Null);
            int kind = monsterRules.Kind == null ? 0 : monsterRules.Kind.KindId;
            Assert.That(flow.CanEscalate(kind), Is.False, $"普通巡逻种类 {kind} 不应自动升级回合制");
            Assert.That(roster.TryGet("monster_1003", out _), Is.True, "保留显式ID入口");
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

        // ───────────────────────── 正面走位 ─────────────────────────

        /// <summary>市令正前方、往南偏 <see cref="FrontSpotSouthDegrees"/> 度的落点（橙区里、视野锥里）。</summary>
        private Vector2 FrontSpot() => monster.Position + TurnTowardSouth(monster.Facing, FrontSpotSouthDegrees) * FrontSpotDistance;

        /// <summary>
        /// 等一个「冲进市令正前方」的窗口（等的时候在南侧走廊上跟着落点挪）：它还在巡逻（没被惊动）、正前方（偏南）落点在空地里，
        /// 且**别的巡逻者**（市令自己要的就是看见你）接下来「跑过去 + 在那儿待 <see cref="FrontLingerSeconds"/> 秒」这段时间都看不到这条路
        /// （<see cref="ShowcasePatrolForecast.AnyMaySee"/>）。
        /// <para>
        /// 不要求它停步（W6b 第三跑：要求停步时 40 秒里它停了三次、落点三次都出了空地，没等到）：落点偏南、离巡逻者乙的巡逻线近 2.9 米，
        /// 已在乙的警戒半径 4.5 之外，它走着时落点跟着挪，冲过去的路上照样朝落点追，它一看见你就朝你走来。
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

    }
}
