// 职责：身份（S1 换皮与附身）回放，两条用例：
//   ① EnterIdentity_StopsEnemyAttack_ExitRestoresIt：在巡逻怪的**橙区**（红区外、警戒半径内）里借到一个身份，
//      敌对怪物照样盯着你、追着你，但**不出手**、身份也不露馅；身份一失效，它追上来就出手。
//   ② Exposure_RedZone_LosesIdentityAndMonsterChases：怪还没察觉时借身份，然后走进市令的**红区**——
//      当场露馅：借来的身份失效，市令转敌对朝你扑过来（起追）；回放赶在它打中之前撤离。
// 舞台是 SampleScene，走 Boot 真实流程（标题「开始」进场）：虚拟手柄推摇杆走北侧路线到巡逻线北侧观察点，
//   再走向场景里那只真实巡逻怪（enerme，Boot 的 MonsterInstaller.kindId = 1003 市令）。身份三件套
//   （IdentityRules / IdentityState / IdentityLedger）**全部从容器取**——MonsterInstaller 的构建回调已经把容器里那只
//   IdentityState 绑给 EncounterStep（还接了露馅结算 BindExposure），所以这里借身份就是正式流程里借身份，
//   不是回放自己 new 一套规则再手工 BindIdentity。取不到就直接 Assert.Fail，不静默退到手工线路。
// 走哪条失效路径（①）：**主动退出**（IdentityRules.TryExit）。理由有两条：
//   1. 原文只写了「触发规则 → 被发现 → 变回原主」（`01_换皮与附身.md:167` R25），主动退出的操作原文没写
//      （`:165` R26 [待定]），IdentityRules.TryExit 按「可退出、进冷却」处理 [推断]——它在真实容器里可用；
//   2. **时限到期这条路本波走不通**：`IdentityRules.AdvanceIdentity` 在 Runtime 下一个调用方都没有
//      （2026-10-07 全工程检索：只有 EditMode 测试调它），身份时限永远不会自己走完。
//      这条缺口列在交付报告的「建议补丁」里，回放**不替它兜底**（在回放里手推 AdvanceIdentity 会把缺口盖住）。
// 身份内容：`IdentityConfig.asset` 的 definitions 补过两条**占位**定义（都统面具 / 执事皮），
//   本回放用 `dutong`（都统面具）——出处 `01_换皮与附身.md:85`（sp03「阶段九个 · B 特殊怪物 · 都统」原句
//   「击杀后获得都统面具，可隐匿在巡逻队中」）与 `:152` R19；面具按「持有即生效」型理解，
//   与资产里 defaultDurationSeconds = 0（= 无时限）自洽。数值与内容整批待策划拍板（`00_功能总览.md` §8.1 #2）。
//
// ── 2026-10-15 改走位（PRP/combat-iteration D1 / T6.1）────────────────────────────────────────────
//   用户 2026-10-14 拍板「露馅 → 进追逐，且借来的身份当场失效」（IdentityConfig：exposureOutcome = Chase、六种原因都失去身份）。
//   警戒值那一条的口径是「红区一律露馅、橙区只在身份不生效时露馅」（IdentityRules.EvaluateExposure ⑥，`02:142-144` R32/R34），
//   所以 ① 原来那个站位（怪正前方 1.5 米 = 红区）借身份会**当场露馅**——身份只在**橙区**护着玩家。① 因此整段改在橙区里演：
//   · 敌对怪物会一直朝玩家走（MonsterRules：警戒 / 敌对态都朝目标移动），站着不动它几秒内就贴进红区。
//     所以「待在橙区」= **绕着它跑**（奔跑 5 > 它追击 2.5）：每帧按「它的追击速度 + 距离误差」定好径向分量，其余给切向，
//     把距离稳在红区半径与警戒半径正中（见 <see cref="Kite"/>）。
//   · 「贴着它也不挨打」这件事在完整参与者身上已经演不出来了：只要它够得着你（0.8 米），它一定正对着你（追击时朝向 = 指向你），
//     你一定在它的红区里 → 露馅 → 身份没了。这是拍板口径的必然结果（PRP D1「已知行为变化」），不是回放没写对。
//   · 原 ① 的「本体先挨一下」那一检查点**删掉了**：市令配了回合制战斗数据（BossRosterConfig 的 monster_1003）且三个升级开关全开，
//     它打中玩家那一下会被交给回合制（升级在途、世界暂停），本体挨打就进战斗了。「身份失效后恢复出手」仍然保留，
//     看的是「它出手了」这件事本身：这一下被交给了回合制（`EncounterStep.IsEscalating`，「被打」开战）。
//     之后在回合制里按招式键把这一仗打完、回到世界再收尾——仗没打完就收尾，退场销毁作用域时战斗的收场路径会撞上
//     已经释放的 UI 服务报错（交付报告里列为 Runtime 问题），那不是这条用例要验的东西。
//   ② 新增：露馅那一刻市令转敌对扑过来；检查点放在它打中之前，之后立刻奔跑撤离（奔跑 5 > 它 2.5），不让回合制把回放拖住。
//   绕圈 / 撤离都限制在巡逻线附近一块空地里（<see cref="KiteArea"/>）：西边是村口演出触发区（一进就冻世界），北边是楼梯与塔。
//   ② 的动身预判（T6.2b 同日修）：巡逻路线改取每只怪自己正在用的路点（ShowcasePatrolForecast.RouteOf，不再读 TamingActor 的错位坐标）；
//   PatrolMaySee 补上「跑到落点之后那几秒」的终点停留探针。
//   预判变准之后，「从观察点下去」那条路几乎等不到窗口：观察点在两条巡逻线北边，下去必须横穿巡逻者乙的巡逻线（z 5），
//   W6b 实测连续两次 40 秒都没等到（等不到照样冲进去，乙先出手、市令那一下被交给回合制）。所以 ② 改从**巡逻线以南**接近：
//   GoThroughSouthGap 经缝口绕到南侧走廊（z −1.5，主怪 / 乙的警戒半径之外），在走廊上跟着落点挪着等窗口；
//   落点改成市令正前方往南偏 25°、1.2 米（仍在视野锥与红区里），露馅后的撤离方向也就朝南、背离乙。两条用例的断言一个没动。
using System.Collections;
using Game.Battle;
using Game.Core.Input;
using Game.Identity;
using Game.Monster;
using Game.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Game.Tests.Showcase.Identity
{
    [Category("Showcase")]
    public sealed class IdentityShowcase : ShowcaseScenario
    {
        /// <summary>演示用的身份 id（占位内容，见文件头）：`IdentityConfig.asset` 的 definitions 第一条。</summary>
        private const string DemoIdentityId = "dutong";

        /// <summary>身份生效期间盯着看多久（真实秒）：怪物攻击冷却 1 秒，2.5 秒内不设防至少会挨两下。</summary>
        private const float NoAttackWatchSeconds = 2.5f;

        /// <summary>② 走进红区时站到怪物正前方多远（米）：红区半径 2 之内、视野锥里。</summary>
        private const float RedZoneApproachDistance = 1.2f;

        /// <summary>② 红区落点往南（玩家来的那一侧）偏多少度：仍在视野锥里（半角 37.5°），离北边巡逻者乙的巡逻线更远。</summary>
        private const float RedZoneSouthDegrees = 25f;

        /// <summary>玩家奔跑速度（米/秒，`PlayerConfig.asset` 的 runSpeed）：绕圈与撤离都按奔跑算径向分量。</summary>
        private const float PlayerRunSpeed = 5f;

        /// <summary>绕圈时距离误差的增益（每差 1 米，径向速度多给多少米/秒）。</summary>
        private const float KiteGain = 2f;

        /// <summary>
        /// 绕圈 / 撤离限定的空地（玩家逻辑坐标 x 12.6..21、z −6..5.4）：巡逻线（z 3.4、x 13.86..21.86）附近能跑开的那一块。
        /// 西边 x 8..11、z 1..4 是村口演出触发区（一进就冻世界），北边 z ≥ 6 起是楼梯与塔，东边 x ≥ 22 是坡道，
        /// 南边 z ≈ −8 是宝箱。
        /// </summary>
        private static readonly Rect KiteArea = Rect.MinMaxRect(12.6f, -6f, 21f, 5.4f);

        /// <summary>
        /// 选方向时相对「背离怪物」最多偏多少度：不超过 90°，否则就是朝它跑过去了（贴着空地边沿也只滑不回头）。
        /// </summary>
        private const float MaxSteerDegrees = 85f;

        /// <summary>选方向时的角度步长（度）与前瞻距离（米）。</summary>
        private const float SteerStepDegrees = 5f;
        private const float SteerLookaheadMeters = 0.8f;

        /// <summary>下一步每出空地 1 米记多少「度」的代价：宁可多偏一点角度，也别出空地。</summary>
        private const float OutsidePenaltyPerMeter = 60f;

        /// <summary>
        /// 绝对不能踩进的地方：村口演出触发区（x 8..11、z 1..4）外扩 0.6 米（胶囊半径 0.3 + 余量）——一进就冻住世界。
        /// </summary>
        private static readonly Rect TriggerKeepOut = Rect.MinMaxRect(7.4f, 0.4f, 11.6f, 4.6f);

        /// <summary>
        /// ② 撤离时额外允许的一条退路：观察点那条南北走廊（x 14.8..16.3、z 5.4..7.8，塔与楼梯之间）。
        /// 玩家是从观察点冲下来的，市令转身朝北追，「正背离它」就是退回观察点；动身前的预判已经保证巡逻者乙这几秒看不到这条路。
        /// </summary>
        private static readonly Rect LookoutCorridor = Rect.MinMaxRect(14.8f, 5.4f, 16.3f, 7.8f);

        /// <summary>② 动身前等「别的巡逻者这几秒看不到这条路」窗口的上限（真实秒），见 <see cref="OthersMaySee"/>。</summary>
        private const float GoWindowTimeoutSeconds = 40f;

        /// <summary>② 预判时玩家跑到落点之后还要在那一带待多久（秒）：露馅 + 起追 + 开始撤离。</summary>
        private const float GoWindowLingerSeconds = 2f;

        /// <summary>预判时玩家推进速度的下限（米/秒）与时间 / 距离步长。</summary>
        private const float PredictSpeedLow = 3.5f;
        private const float PredictStepSeconds = 0.05f;
        private const float PredictSampleMeters = 0.4f;
        private const float PredictLateralMeters = 0.5f;

        /// <summary>① 收尾：在回合制里把升级出来的那一仗打完的上限（真实秒）。</summary>
        private const float FinishBattleTimeoutSeconds = 90f;

        /// <summary>② 撤离的上限（真实秒）：拉开到它的警戒半径之外就算甩开。</summary>
        private const float FleeTimeoutSeconds = 6f;

        private IInputService inputService;
        private PlayerModel player;
        private MonsterModel monster;
        private MonsterRules monsterRules;
        private EncounterStep step;
        private IdentityRules identityRules;
        private IdentityState identityState;
        private IdentityLedger identityLedger;
        private IdentityId demoIdentity;

        /// <summary>场上全部巡逻怪（主怪在首位，= <c>Taming.TargetIds</c> 的顺序）与同序的巡逻路线（取不到为 null）。</summary>
        private MonsterRules[] patrollers = System.Array.Empty<MonsterRules>();
        private Vector2[][] patrolRoutes = System.Array.Empty<Vector2[]>();

        /// <summary>绕圈的方向（+1 逆时针 / −1 顺时针）：沿用上一帧的，免得左右来回抖。</summary>
        private float kiteSign = 1f;

        protected override string Module => "Identity";

        /// <summary>世界由流程加载（标题「开始」→ MonsterEncounterState → SampleScene），基类不再叠加载一份。</summary>
        protected override string ScenePath => null;

        /// <summary>红区半径（MonsterConfig.hostileRadius，全局，种类表不开放）。</summary>
        private float RedRadius => monsterRules.Config.HostileRadius;

        /// <summary>警戒半径（种类优先，市令 4.5）。</summary>
        private float AlertRadius => monsterRules.Config.ResolveAlertRadius(monsterRules.Kind);

        /// <summary>橙区里站多远：红区半径与警戒半径的正中（市令：(2 + 4.5) / 2 = 3.25 米）。</summary>
        private float OrangeStandDistance => (RedRadius + AlertRadius) * 0.5f;

        private float Gap => Vector2.Distance(player.Position, monster.Position);

        // ───────────────────────── ① 橙区里借身份：不出手；退出后恢复 ─────────────────────────

        [UnityTest]
        public IEnumerator EnterIdentity_StopsEnemyAttack_ExitRestoresIt()
        {
            yield return EnterWorld();
            yield return GoToPatrolLookout();

            // ── 第 4 步：本体走进它正前方的橙区，让它盯上、转敌对 ─────────────────────────────
            // 站位 = 「怪物位置 + 朝向 × 橙区正中距离」：在视野锥里、红区之外，它只会进警戒（橙），
            // 4 秒（种类表 alert_fill_seconds）一直看得见才升满转敌对。这期间它朝玩家走，所以一旦被盯上就开始绕圈（Kite）。
            // 站位落在空地外（比如它在巡逻线西端朝西，那一点在触发区里）就先在观察点等它走开。
            yield return Step("切到奔跑，走进巡逻怪正前方的橙区（红区外、警戒半径内），等它盯上你、转敌对（状态色变红）", null, 0f);
            yield return Input.PressUntil(inputService.Actions.Gameplay.Run, () => player.IsRunning);
            float minGapBody = float.MaxValue;
            yield return WaitUntil("在橙区里被它盯上并转敌对", () =>
            {
                if (monster.Mode == MonsterMode.Hostile || monster.Mode == MonsterMode.Alert)
                {
                    minGapBody = Mathf.Min(minGapBody, Gap);
                    Kite();
                    return monster.Mode == MonsterMode.Hostile;
                }

                DriveToOrangeSpot();
                return false;
            }, 20f);
            yield return Check($"怪物转敌对（状态色变红）、身份还没借（本体）；被盯上之后一直在橙区里（离它最近 {minGapBody:0.##} 米，红区 {RedRadius:0.##}）",
                () => monster.Mode == MonsterMode.Hostile && !identityState.IsInEffect && minGapBody > RedRadius);

            // ── 第 5 步：在橙区里借身份（hold 0：边绕边借，别停下来让它贴进红区）──────────────
            IdentityEnterResult enterResult = IdentityEnterResult.InvalidId;
            yield return Step("在橙区里借入身份：都统面具（占位内容，出处 01_换皮与附身.md:85 / :155）",
                () =>
                {
                    Kite();
                    enterResult = identityRules.TryEnter(identityState, identityLedger, demoIdentity);
                }, 0f);
            yield return Check($"身份生效中（TryEnter 返回 {enterResult}，来源应为面具）",
                () =>
                {
                    Kite();
                    return enterResult == IdentityEnterResult.Entered
                           && identityState.IsInEffect
                           && identityState.CurrentOrigin == IdentityOrigin.Mask;
                });

            // ── 第 6 步：身份生效中，在橙区里绕着它，盯着看它出不出手、身份露不露馅 ──────────────
            int healthInEffect = player.Health;
            bool damagedWhileInEffect = false;
            bool exposedWhileInEffect = false;
            bool escalatedWhileInEffect = false;
            float minGap = float.MaxValue;
            float maxGap = 0f;
            yield return Step($"身份生效中，在橙区里绕着这只敌对怪物跑 {NoAttackWatchSeconds} 秒", null, 0f);
            float watchUntil = Time.realtimeSinceStartup + NoAttackWatchSeconds;
            yield return WaitUntil($"过去 {NoAttackWatchSeconds} 秒", () =>
            {
                Kite();
                damagedWhileInEffect |= player.Health < healthInEffect;
                exposedWhileInEffect |= !identityState.IsInEffect;
                escalatedWhileInEffect |= step.IsEscalating;
                minGap = Mathf.Min(minGap, Gap);
                maxGap = Mathf.Max(maxGap, Gap);
                return Time.realtimeSinceStartup >= watchUntil;
            }, NoAttackWatchSeconds + 1f);
            yield return Check($"身份生效期间一次都没挨打（生命保持 {healthInEffect}）、身份没露馅，怪物仍然敌对",
                () => !damagedWhileInEffect && !escalatedWhileInEffect && player.Health == healthInEffect
                      && !exposedWhileInEffect && identityState.IsInEffect && monster.Mode == MonsterMode.Hostile);
            yield return Check($"这段时间一直待在橙区：离它 {minGap:0.##}～{maxGap:0.##} 米（红区 {RedRadius:0.##}、警戒半径 {AlertRadius:0.##}）",
                () => minGap > RedRadius && maxGap <= AlertRadius);
            yield return Snapshot("橙区里身份生效，怪物不出手");

            // ── 第 7 步：退出身份（主动退出），停下来，验证它恢复出手 ─────────────────────────
            // 基准血量必须在**退出之前**取（2026-10-07 第一轮踩过：退出后再取会把第一下算进基准）。
            // 它出手的结果有两种，都算「恢复出手」：掉血；或这一下被交给了回合制（市令配了战斗数据、「被打」开关开着，
            // 文件头写了为什么）。
            int healthBeforeExit = player.Health;
            IdentityExitReason exitReason = IdentityExitReason.None;
            yield return Step("摘下面具：主动退出身份（01_换皮与附身.md:168 R26 原文没写主动退出，占位口径），站住不跑了",
                () =>
                {
                    exitReason = identityRules.TryExit(identityState);
                    Input.ReleaseStick();
                }, 0f);
            yield return Check($"身份已退出（TryExit 返回 {exitReason}，回到本体）",
                () => exitReason == IdentityExitReason.Voluntary && !identityState.IsInEffect && !identityState.IsBorrowing);

            yield return Check("身份一失效，市令追上来就出手：这一下按升级规则交给了回合制（「被打」开战）",
                () => step.IsEscalating, 5f);
            yield return Snapshot("身份失效后恢复出手");

            // ── 第 8 步：收尾——这一仗不是本回放要验的，打完它只为回到世界、别让收尾撞上进行中的战斗 ─────────
            BattleFlow flow = ResolveService<BattleFlow>();
            // 容器里登记的是接口（BattleInstaller），同 BattleShowcase 的取法。
            BattleScenePresenter presenter = ResolveService<IBattlePresenter>() as BattleScenePresenter;
            yield return Step("收尾：在回合制里按招式键把这一仗打完、回到世界（胜负不是本回放要看的）", null, 0f);
            yield return FightUntilOver(flow, presenter);
            yield return WaitUntil("这一仗打完、回到世界（升级不再在途）",
                () => flow == null || (!flow.IsBattleRunning && !step.IsEscalating), 10f);
            yield return WaitCurtainRevealed();
            yield return Snapshot("回合制打完·回到世界");
        }

        // ───────────────────────── ② 借着身份走进红区：露馅、身份失效、市令起追 ─────────────────────────

        [UnityTest]
        public IEnumerator Exposure_RedZone_LosesIdentityAndMonsterChases()
        {
            yield return EnterWorld();
            yield return GoThroughSouthGap(new[] { SouthStagingPoint });

            // ── 第 4 步：怪还没察觉时借身份（南侧走廊在主怪与巡逻者乙的警戒半径之外，见 DemoScene.cs 的 SouthLaneZ）──
            IdentityEnterResult enterResult = IdentityEnterResult.InvalidId;
            yield return Step("怪还没察觉你时借入身份：都统面具（占位内容）",
                () => enterResult = identityRules.TryEnter(identityState, identityLedger, demoIdentity), 0f);
            yield return Check($"身份生效中（TryEnter 返回 {enterResult}），怪物还在巡逻、没察觉你",
                () => enterResult == IdentityEnterResult.Entered && identityState.IsInEffect
                      && (monster.Mode == MonsterMode.PatrolWalk || monster.Mode == MonsterMode.PatrolPause));

            // ── 第 5 步：借着身份径直走进它正前方的红区 ─────────────────────────────────────
            // 路上先经过橙区：身份生效中橙区不露馅（它会进警戒、朝你走）；一进红区（2 米内、视野锥里）当场露馅。
            // 露馅那一刻（身份失效被观察到的同一帧）就开始撤离：它敌对后 2.5 m/s 扑过来，出手距离 0.8 米，不能等。
            int healthBefore = player.Health;
            int exposuresBefore = identityState.ExposureCount;
            Vector2 monsterAtExposure = monster.Position;
            bool exposed = false;
            yield return Step("在南侧走廊上跟着市令挪，等它正前方（偏南）那一点落在空地里、别的巡逻者这几秒都看不到这条路，再切到奔跑，"
                              + "借着身份径直走进市令正前方的红区（2 米内、视野锥里）", null, 0f);
            // 等市令**停步**：停着的怪朝向不变、落点不动，预判的那条路才就是真要跑的那条路
            // （2026-10-15 第二跑：它边走边算落点，实际跑的路偏出预判，从巡逻者乙面前 1 米多擦过去，被它直接看进红区）。
            yield return WaitUntil("市令停步、正前方在空地里，别的巡逻者这几秒看不到去那儿的路（按当前位置 / 朝向 / 巡逻路线预判）",
                () =>
                {
                    Vector2 spot = RedZoneSpot();
                    // 等的时候在南侧走廊上跟着落点挪（主怪 / 巡逻者乙都看不到这条走廊），窗口一开就是一段短直路。
                    Vector2 lane = SouthLanePoint(spot.x) - player.Position;
                    Input.SetStick(lane.magnitude > 0.15f ? lane.normalized : Vector2.zero);
                    float runSeconds = Vector2.Distance(player.Position, spot) / PredictSpeedLow;
                    return monster.Mode == MonsterMode.PatrolPause && monster.PatrolPauseLeft > runSeconds + 0.5f
                           && KiteArea.Contains(spot) && !OthersMaySee(spot);
                }, GoWindowTimeoutSeconds);
            Input.ReleaseStick();
            yield return Input.PressUntil(inputService.Actions.Gameplay.Run, () => player.IsRunning);
            yield return WaitUntil("走进红区那一刻露馅：借来的身份当场失效", () =>
            {
                if (!identityState.IsInEffect)
                {
                    exposed = true;
                    monsterAtExposure = monster.Position;
                    Flee();
                    return true;
                }

                Vector2 ahead = RedZoneSpot() - player.Position;
                Input.SetStick(ahead.magnitude > 0.1f ? ahead.normalized : Vector2.zero);
                return false;
            }, 15f);
            yield return Check($"露馅：借来的身份当场失效（不再借身份，露馅次数 {exposuresBefore} → {identityState.ExposureCount}）",
                () =>
                {
                    Flee();
                    return exposed && !identityState.IsInEffect && !identityState.IsBorrowing
                           && identityState.ExposureCount > exposuresBefore;
                });

            // ── 第 6 步：市令转敌对、朝玩家扑过来——检查点放在它打中之前 ───────────────────────────
            yield return Check("市令转敌对（状态色变红），朝你扑过来（起追）",
                () =>
                {
                    Flee();
                    Vector2 moved = monster.Position - monsterAtExposure;
                    return monster.Mode == MonsterMode.Hostile && moved.magnitude > 0.2f
                           && Vector2.Dot(moved, player.Position - monsterAtExposure) > 0f;
                }, 1.5f);
            yield return Snapshot("露馅·市令起追");

            // ── 第 7 步：撤离（奔跑 5 > 它追击 2.5），别让它打中——打中就会被交给回合制，把回放拖进战斗里 ─────
            yield return Step("撤离：赶在它打中之前跑开，拉开到它的警戒半径之外", null, 0f);
            yield return WaitUntil("拉开到它的警戒半径之外（或它不再敌对）", () =>
            {
                Flee();
                return Gap > AlertRadius + 0.5f || monster.Mode != MonsterMode.Hostile;
            }, FleeTimeoutSeconds);
            Input.ReleaseStick();
            yield return Check($"撤离途中没被打中：生命保持 {healthBefore}、没被拖进回合制",
                () => player.Health == healthBefore && !step.IsEscalating);
            yield return Snapshot("撤离·甩开市令");
        }

        // ───────────────────────── 进场与驱动 ─────────────────────────

        /// <summary>
        /// 标题「开始」进世界（EnterDemoWorld），等容器里的输入服务、玩家 / 怪物模型与规则、遭遇与身份三件套都可解析。
        /// 身份三件套取不到就是接线不够（IdentityInstaller 没挂上 Boot / 没拖配置），前置条件不成立，直接中断。
        /// </summary>
        private IEnumerator EnterWorld()
        {
            yield return EnterDemoWorld("遭遇逻辑在跑、输入服务与玩家 / 怪物 / 身份服务可用", () =>
            {
                inputService = ResolveService<IInputService>();
                player = ResolveService<PlayerModel>();
                monster = ResolveService<MonsterModel>();
                monsterRules = ResolveService<MonsterRules>();
                step = ResolveService<EncounterStep>();
                identityRules = ResolveService<IdentityRules>();
                identityState = ResolveService<IdentityState>();
                identityLedger = ResolveService<IdentityLedger>();
                return inputService != null && inputService.Actions != null
                       && player != null && monster != null && monsterRules != null && step != null
                       && identityRules != null && identityState != null && identityLedger != null;
            }, "进世界后容器里取不到 PlayerModel / MonsterModel / MonsterRules / EncounterStep / IInputService / "
               + "IdentityRules / IdentityState / IdentityLedger，后续步骤无法驱动"
               + "（检查 Boot 场景 GameBootstrap 上的 IdentityInstaller 与它的 Config 字段）");

            CollectPatrollers();
            kiteSign = 1f;

            demoIdentity = IdentityId.From(DemoIdentityId);
            if (!identityRules.Catalog.TryGet(demoIdentity, out _))
            {
                Assert.Fail($"IdentityConfig.asset 的 definitions 里没有身份 \"{DemoIdentityId}\"："
                            + "本回放演示的就是「借一个身份」，表是空的就什么都演不出来。"
                            + "补一条定义（本波补的占位定义见 identity-module-guide.md 的「验证入口」一节）。");
            }
        }

        /// <summary>② 的红区落点：市令正前方往南偏 <see cref="RedZoneSouthDegrees"/> 度、<see cref="RedZoneApproachDistance"/> 米（视野锥里、红区里）。</summary>
        private Vector2 RedZoneSpot() =>
            monster.Position + TurnTowardSouth(monster.Facing, RedZoneSouthDegrees) * RedZoneApproachDistance;

        /// <summary>
        /// 朝「怪物正前方、橙区正中」那一点推摇杆（在它的视野锥里、红区之外）；那一点不在 <see cref="KiteArea"/> 里
        /// （比如它在巡逻线西端朝西，那一点落进演出触发区）就松杆在原地等它走开。
        /// </summary>
        private void DriveToOrangeSpot()
        {
            Vector2 spot = monster.Position + monster.Facing * OrangeStandDistance;
            if (!KiteArea.Contains(spot))
            {
                Input.ReleaseStick();
                return;
            }

            Vector2 delta = spot - player.Position;
            Input.SetStick(delta.magnitude > 0.15f ? delta.normalized : Vector2.zero);
        }

        /// <summary>
        /// 绕着怪物跑、把距离稳在橙区正中（<see cref="OrangeStandDistance"/>）：奔跑速度固定，按
        /// 「它此刻的追击速度 + 距离误差 × 增益」定径向分量（cos θ），其余给切向——它追多快玩家就退多快，
        /// 多出来的速度绕圈，所以不需要一条很长的退路。追击时它恒朝向玩家（MonsterRules 的移动会把朝向转向目标），
        /// 玩家始终在它的视野锥里、留在橙区：它一直「看得见」你，所以一直敌对。
        /// </summary>
        private void Kite()
        {
            Vector2 offset = player.Position - monster.Position;
            float distance = offset.magnitude;
            Vector2 away = distance > 0.01f ? offset / distance : -monster.Facing;
            float chaseSpeed = monster.Mode == MonsterMode.Hostile
                ? monsterRules.Config.PatrolSpeed * monsterRules.Config.HostileSpeedMultiplier
                : monster.Mode == MonsterMode.Alert
                    ? monsterRules.Config.PatrolSpeed * monsterRules.Config.AlertSpeedMultiplier
                    : 0f;
            float cos = Mathf.Clamp((chaseSpeed + KiteGain * (OrangeStandDistance - distance)) / PlayerRunSpeed, -1f, 1f);
            Input.SetStick(PickDirection(away, Mathf.Acos(cos) * Mathf.Rad2Deg, false));
        }

        /// <summary>
        /// 撤离：尽量正对着「背离威胁」的方向跑（偏转越小拉开越快：奔跑 5 × cos θ − 它追击 2.5）。
        /// 威胁 = 市令 + 4 米内已经警觉 / 敌对的别的巡逻者（按远近加权），只在下一步会出 <see cref="KiteArea"/> 时才偏开。
        /// </summary>
        private void Flee()
        {
            Vector2 offset = player.Position - monster.Position;
            Vector2 away = offset.magnitude > 0.01f ? offset.normalized : -monster.Facing;
            for (int i = 1; i < patrollers.Length; i++)
            {
                MonsterRules rules = patrollers[i];
                if (rules == null || rules.Model.Health <= 0
                    || (rules.Model.Mode != MonsterMode.Alert && rules.Model.Mode != MonsterMode.Hostile))
                {
                    continue;
                }

                Vector2 fromOther = player.Position - rules.Model.Position;
                float distance = fromOther.magnitude;
                if (distance > 0.01f && distance < 4f)
                {
                    away += fromOther / distance * ((4f - distance) / 4f);
                }
            }

            Input.SetStick(PickDirection(away.magnitude > 0.01f ? away.normalized : -monster.Facing, 0f, true));
        }

        /// <summary>
        /// 选跑的方向：在「背离方向 ± <see cref="MaxSteerDegrees"/>」里逐 <see cref="SteerStepDegrees"/> 度打分——
        /// 离理想角（± <paramref name="angle"/>，绕圈方向沿用 <see cref="kiteSign"/>）越近越好，
        /// 下一步出 <see cref="KiteArea"/> 按出去的米数加代价；踩进 <see cref="TriggerKeepOut"/> 的直接不要。
        /// 偏转封顶在 90° 以内：贴着边沿也只沿边滑，不会掉头朝怪物跑过去（第一版就是这么让它贴进红区的）。
        /// </summary>
        /// <param name="allowLookout">撤离时为 true：观察点那条走廊（<see cref="LookoutCorridor"/>）也算空地。</param>
        private Vector2 PickDirection(Vector2 away, float angle, bool allowLookout)
        {
            Vector2 position = player.Position;
            float target = angle * kiteSign;
            float bestScore = float.MaxValue;
            float bestAngle = target;
            for (float candidate = -MaxSteerDegrees; candidate <= MaxSteerDegrees + 0.01f; candidate += SteerStepDegrees)
            {
                Vector2 next = position + Rotate(away, candidate) * SteerLookaheadMeters;
                if (TriggerKeepOut.Contains(next))
                {
                    continue;
                }

                float outside = OutsideDistance(next, KiteArea);
                if (allowLookout)
                {
                    outside = Mathf.Min(outside, OutsideDistance(next, LookoutCorridor));
                }

                float score = Mathf.Abs(candidate - target) + OutsidePenaltyPerMeter * outside;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestAngle = candidate;
                }
            }

            if (Mathf.Abs(bestAngle) > 1f)
            {
                kiteSign = Mathf.Sign(bestAngle);
            }

            return Rotate(away, bestAngle);
        }

        /// <summary>点到矩形 <paramref name="area"/> 的距离（在里面为 0）。</summary>
        private static float OutsideDistance(Vector2 point, Rect area)
        {
            float dx = Mathf.Max(area.xMin - point.x, 0f, point.x - area.xMax);
            float dy = Mathf.Max(area.yMin - point.y, 0f, point.y - area.yMax);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>把单位向量逆时针转 <paramref name="degrees"/> 度（标准二维旋转）。</summary>
        private static Vector2 Rotate(Vector2 direction, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            return new Vector2(direction.x * cos - direction.y * sin, direction.x * sin + direction.y * cos);
        }

        // ───────────────────────── ② 动身前的预判：别让巡逻者乙撞进来 ─────────────────────────

        /// <summary>
        /// 场上全部巡逻怪（Monster 侧自己的口径：<c>EncounterStep.Taming.TargetIds</c>，主怪在首位）与同序的巡逻路线。
        /// <para>
        /// 2026-10-15（PRP/combat-iteration T6.2b）：路线改取每只怪**自己正在用的**路点（<see cref="ShowcasePatrolForecast.RouteOf"/>，
        /// 即 <c>MonsterRules.Capture()</c> 的 WaypointX / WaypointY）。原先按稳定标识对上视图里的 TamingActor、取
        /// <c>TamingActor.PatrolPositions(view)</c>——那是巡逻点 Transform 的**此刻**世界坐标，产品只在场景就绪那一刻读一次；
        /// 回放进场后再读，巡逻者乙拿到的是另一套错位坐标（W6a 在 Stealth 回放里实测到），预判里的乙就朝一个不存在的路点走。
        /// </para>
        /// </summary>
        private void CollectPatrollers()
        {
            patrollers = ShowcasePatrolForecast.Patrollers(step);
            patrolRoutes = ShowcasePatrolForecast.RoutesOf(patrollers);
        }

        /// <summary>
        /// 从脚下跑到 <paramref name="spot"/>、再在那一带待 <see cref="GoWindowLingerSeconds"/> 秒，这段时间里
        /// **市令以外**的巡逻怪会不会察觉到玩家。
        /// <para>
        /// 为什么要这一道（2026-10-15 首跑）：市令的巡逻线北边 1.6 米就是巡逻者乙的巡逻线，从观察点直冲市令正前方要从它身边过；
        /// 它不是完整参与者，不触发露馅，但借身份期间会跟着转敌对贴上来——身份一失效它立刻出手（首跑露馅后 15 毫秒挨了它一刀），
        /// 击倒让玩家跑不动，市令随后追上、这一下被交给回合制。所以动身前按它当前的位置 / 朝向 / 巡逻进度往前推
        /// （照 <c>MonsterRules.Step</c> 的巡逻两态），每一刻用产品自己的档位判定（<see cref="MonsterRules.SenseFrom"/>，
        /// 不含遮挡 = 更保守）问「玩家这一刻可能在的那段路」它看不看得见。任何一只已经警觉也不走。
        /// </para>
        /// </summary>
        private bool OthersMaySee(Vector2 spot)
        {
            Vector2 start = player.Position;
            float length = Vector2.Distance(start, spot);
            float horizon = length / PredictSpeedLow + GoWindowLingerSeconds;
            for (int i = 1; i < patrollers.Length; i++)
            {
                MonsterRules rules = patrollers[i];
                if (rules == null || rules.Model.Health <= 0)
                {
                    continue;
                }

                if (rules.Model.Mode != MonsterMode.PatrolWalk && rules.Model.Mode != MonsterMode.PatrolPause)
                {
                    return true;
                }

                if (PatrolMaySee(rules, i < patrolRoutes.Length ? patrolRoutes[i] : null, start, spot, horizon))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 把一只巡逻怪按巡逻两态往前推 <paramref name="horizon"/> 秒（朝当前路点走、到点换下一个、走够 NextPauseAfter 停
        /// PatrolPauseSeconds；下一次停顿的随机间隔取下限），每一刻判它能不能看见「从 <paramref name="from"/> 起、
        /// 按 <see cref="PredictSpeedLow"/>..奔跑速度推进、到 <paramref name="to"/> 为止」那段路上玩家这一刻可能在的位置。
        /// </summary>
        private bool PatrolMaySee(MonsterRules rules, Vector2[] route, Vector2 from, Vector2 to, float horizon)
        {
            MonsterModel model = rules.Model;
            MonsterConfig config = rules.Config;
            Vector2 position = model.Position;
            Vector2 facing = model.Facing;
            int index = model.WaypointIndex;
            bool pausing = model.Mode == MonsterMode.PatrolPause;
            float walked = model.PatrolWalkElapsed;
            float pauseAfter = model.NextPauseAfter;
            float pauseLeft = model.PatrolPauseLeft;
            float length = Vector2.Distance(from, to);
            Vector2 lateral = length > 0.01f ? new Vector2(-(to - from).y, (to - from).x) / length : Vector2.zero;
            for (float t = 0f; t <= horizon; t += PredictStepSeconds)
            {
                // 走得最慢的那一种也早该到了：此后玩家就站在落点那一带（露馅 + 起追 + 开始撤离），终点照样要探。
                // 2026-10-15（T6.2b）修：原先 near 不夹到 length，跑到落点之后 near > far、内层循环一次都不跑，
                // GoWindowLingerSeconds 那几秒的终点停留从来没探过（同 StealthShowcase.PatrolMaySeePath 的 holdAtEnd）。
                float near = Mathf.Min(Mathf.Max(0f, PredictSpeedLow * t - PredictSampleMeters), length);
                float far = Mathf.Min(length, PlayerRunSpeed * t + PredictSampleMeters);
                for (float s = near; s <= far + 0.001f; s += PredictSampleMeters)
                {
                    Vector2 point = length > 0.01f ? Vector2.Lerp(from, to, Mathf.Clamp01(s / length)) : to;
                    // 路线两侧各再放 0.5 米：摇杆每帧重新瞄准，实际跑的路会左右偏一点。
                    for (int side = -1; side <= 1; side++)
                    {
                        var probe = new PlayerSnapshot(point + lateral * (side * PredictLateralMeters), player.Facing, false,
                            player.IsDisguised, player.Health);
                        if (MonsterRules.SenseFrom(config, rules.Kind, position, facing, in probe) > 0)
                        {
                            return true;
                        }
                    }
                }

                if (pausing)
                {
                    pauseLeft -= PredictStepSeconds;
                    if (pauseLeft <= 0f)
                    {
                        pausing = false;
                        walked = 0f;
                        pauseAfter = config.PatrolMinSeconds;
                    }
                }
                else if (route != null && route.Length > 0)
                {
                    index = Mathf.Clamp(index, 0, route.Length - 1);
                    Vector2 toWaypoint = route[index] - position;
                    float distance = toWaypoint.magnitude;
                    if (distance > 0f)
                    {
                        facing = toWaypoint / distance;
                        position += facing * Mathf.Min(distance, config.PatrolSpeed * PredictStepSeconds);
                    }

                    if ((route[index] - position).magnitude <= 0.001f)
                    {
                        index = (index + 1) % route.Length;
                    }

                    walked += PredictStepSeconds;
                    if (walked >= pauseAfter)
                    {
                        pausing = true;
                        pauseLeft = config.PatrolPauseSeconds;
                    }
                }
            }

            return false;
        }

        // ───────────────────────── ① 收尾：把升级出来的那一仗打完 ─────────────────────────

        /// <summary>
        /// 轮到玩家就按招式键（Dialogue/Choice1–3，战斗期间借用键盘 1/2/3；怒气满了按 3，不然按 1），
        /// 直到这一仗打完或超时（<see cref="FinishBattleTimeoutSeconds"/>）。照 BattleShowcase 的出招写法：
        /// 按下后等这一手被接住再进下一轮。**不记报告条目**——胜负不是本回放要验的，打完只为回到世界收尾。
        /// </summary>
        private IEnumerator FightUntilOver(BattleFlow flow, BattleScenePresenter presenter)
        {
            if (flow == null || presenter == null)
            {
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + FinishBattleTimeoutSeconds;
            while (flow.IsBattleRunning && Time.realtimeSinceStartup < deadline)
            {
                if (!presenter.IsAwaitingCommand || presenter.View == null)
                {
                    yield return null;
                    continue;
                }

                InputAction key = presenter.View.IsSkillLit(2)
                    ? inputService.Actions.Dialogue.Choice3
                    : inputService.Actions.Dialogue.Choice1;
                yield return Input.Press(key);
                float accepted = Time.realtimeSinceStartup + 2f;
                while (presenter.IsAwaitingCommand && flow.IsBattleRunning && Time.realtimeSinceStartup < accepted)
                {
                    yield return null;
                }
            }
        }
    }
}
