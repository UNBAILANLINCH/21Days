// 职责：ShowcaseScenario 的「SampleScene demo 内容」部分（partial）——只与这张回放舞台的内容相关的共用知识与操作：
//   出生点 / 村口演出触发区 / 北侧绕行路线 / 巡逻线观察点 / 楼梯口 / 长者站位 / 去东南角巡夜人的穿缝路线这些坐标常量，
//   以及进世界并等遭遇逻辑就绪（EnterDemoWorld）、按路线逐点走（WalkRoute）、走到巡逻线观察点（GoToPatrolLookout）、
//   经缝口绕到巡逻线以南（GoThroughSouthGap / SouthLanePoint / TurnTowardSouth）、
//   让东南角巡夜人看见你（ProvokeXunye / XunyeYardArea）、按稳定标识取巡逻者（FindPatroller）、
//   和巡逻怪交手（StrikeMonster / FaceMonster，可指定对手；StrikeWhenItComes 站定等它走近再挥）。
//   与通用回放引擎分开：ShowcaseScenario.cs 管报告节奏，BootFlow 管进退场，PlayerDrive 管输入设备与走路；
//   SampleScene 的 demo 内容一改（挪了巡逻怪、换了楼梯、移了触发区），只动这一个文件。
//
// 用法（ScenePath 返回 null，走 Boot 真实流程）：
//   yield return EnterDemoWorld("遭遇逻辑在跑、输入服务与玩家 / 怪物模型可用", () =>
//   {
//       inputService = ResolveService<IInputService>(); player = ResolveService<PlayerModel>();
//       return inputService != null && inputService.Actions != null && player != null;
//   }, "进世界后容器里取不到 PlayerModel / IInputService");
//   yield return GoToPatrolLookout();     // 切跑 → 北侧路线 → 观察点 → 切回步行
//   yield return StrikeMonster();         // 追上巡逻怪打中一下；StrikeSwings 记累计出手次数
//
// 坐标一律是玩家逻辑坐标（= 场景 XZ，y 是场景 z），见 ShowcaseScenario.PlayerDrive.cs 的坐标约定。
//
// 为什么新建（project-root.md「加能力的顺序」）：
//   复用 —— Player / Monster / Disguise / IsometricExploration / CharacterPuppet 五份回放各复制了一份路线常量、
//           进世界包装、切跑走到观察点、追怪出手，没有可复用的公共件。
//   扩展 —— 要调 Step / WaitUntil / WalkTo / ResolveService 等 protected 成员，只能落在基类；但这些是「这张场景的内容知识」，
//           塞进 PlayerDrive（通用走路）或 BootFlow（通用进退场）职责说不通，按职责单独一个 partial。
using System;
using System.Collections;
using System.Collections.Generic;
using Game.Core.Input;
using Game.Monster;
using Game.Player;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.Showcase
{
    public abstract partial class ShowcaseScenario
    {
        /// <summary><see cref="EnterDemoWorld"/> 等标题就绪的上限（真实秒）。</summary>
        protected const float DemoBootTimeoutSeconds = 20f;

        /// <summary><see cref="EnterDemoWorld"/> 点「开始」后等进世界、再等遭遇逻辑就绪的上限（真实秒）。</summary>
        protected const float DemoEnterTimeoutSeconds = 20f;

        /// <summary>
        /// 玩家出生点 (-4, 3.4)：离长者 3 米（交互半径 2 之外，要先走过去）、离巡逻怪约 18 米、离左墙（x -6）2 米。
        /// 在出生点附近左右走的回放按它算终点碰不碰墙、出不出画。
        /// </summary>
        protected static readonly Vector2 DemoSpawnPoint = new Vector2(-4f, 3.4f);

        /// <summary>
        /// 村口演出触发区 Trigger_VillageEntrance 的范围（x 8..11、z 1..4）：每次进入都会拉起演出、冻住世界，
        /// 而它正挡在出生点与巡逻怪之间——去巡逻怪那边的回放一律走 <see cref="RouteToPatrol"/> 从北侧绕开。
        /// </summary>
        protected static readonly Rect VillageEntranceTriggerArea = Rect.MinMaxRect(8f, 1f, 11f, 4f);

        /// <summary>
        /// 出生点去巡逻怪 / 楼梯那边的北侧绕行路线：(3.5,5) → (8.5,7.2) → (15.5,7.2)。
        /// 从北侧绕开 <see cref="VillageEntranceTriggerArea"/>；终点 (15.5,7.2) 是塔与楼梯之间的空地，
        /// 在巡逻线北侧近 4 米，巡逻怪（视野锥 ±37.5°、警戒半径 6）任何时候都看不到这里。
        /// </summary>
        protected static readonly IReadOnlyList<Vector2> RouteToPatrol = new[]
        {
            new Vector2(3.5f, 5f),
            new Vector2(8.5f, 7.2f),
            new Vector2(15.5f, 7.2f),
        };

        /// <summary>
        /// 巡逻线北侧的观察点 (15.5, 7.6)：取巡逻段（2026-10-07 由 4 米拉到 8 米，x 13.86..21.86、z 3.4）
        /// 的**偏西一侧**，并在**北侧留足 4.2 米**。
        /// <para>
        /// 为什么不是原来的「北侧 2 米」：那个位置的安全性是**按 4 米短巡逻线算的**——线短时 |dx| ≤ 2，
        /// 视野锥 ±37.5° 要求的 |dx| ≥ 2.6 恒成立。线拉到 8 米后 |dx| 最大到 6.4（观察点 x = 15.5、
        /// 线东端 x = 21.86），站太近就会被从西边走过来的怪正面看到（会转警戒，后面的绕背/停顿回放全崩）。
        /// </para>
        /// <para>
        /// 安全条件是两条判据取并集：**被视野锥排除**（|dx| &lt; 1.304·D，1.304 = 1/tan37.5°）或
        /// **距离超过橙区**（|dx|² + D² &gt; 36）。要让它们覆盖全部 |dx|，需要 1.304·D ≥ √(36 − D²)，
        /// 解得 D ≥ 3.65 米；这里取 D = 4.2 留余量（z = 7.6）。
        /// </para>
        /// <para>
        /// **x 为什么是 15.5 而不是巡逻线中点 20.86**：走中点那条直线要跨过楼梯区（x 16.5..19.5），
        /// `WalkTo` 在 5 秒内走不到（实测「走到 (20.86, 7.6) 超时」）。15.5 与 <see cref="RouteToPatrol"/>
        /// 的终点同 x，只需再往北 0.4 米，全程无遮挡；到怪背后最远约 12.5 米，潜行 1.5 m/s 约 8.3 秒
        /// （绕背窗口已相应放到 14 秒）。
        /// </para>
        /// </summary>
        protected static readonly Vector2 PatrolLookout = new Vector2(15.5f, 7.6f);

        /// <summary>走楼梯所在的场景 x：在楼梯宽度 16.5..19.5 内、离东沿留出胶囊半径。</summary>
        protected const float StairsLaneX = 18.5f;

        /// <summary>楼梯口地面离第 1 级台阶中心的距离（第 1 级 z 6..6.8，胶囊半径 0.3，站在 z≈5.5 仍是平地）。</summary>
        protected const float StairsFootOffset = 0.9f;

        /// <summary>从 <see cref="RouteToPatrol"/> 终点的空地下到楼梯口前的拐点：x 16 离第 1 级台阶西沿（16.5）留出胶囊半径，z 5.5 在台阶南沿之外。</summary>
        protected static readonly Vector2 StairsCorner = new Vector2(16f, 5.5f);

        /// <summary>
        /// 巡逻怪朝东走过这个 x 之后，楼梯口在它视野外的时间窗 ≥ 2.7 秒：楼梯口 z≈5.5 离巡逻线只有 2.1 米，怪物朝东走时会落进视野锥；
        /// 过了 x 16.3 它要么朝东看不到楼梯口（夹角 &gt; 37.5°）、要么折返朝西背对楼梯口，直到再走回 x 13.86 掉头，
        /// 这段时间足够玩家走过楼梯第 3 级（z ≥ 7.6，此后怎么都进不了警戒半径）。
        /// </summary>
        protected const float StairsSafeMonsterX = 16.3f;

        // ── 东南角巡夜人（PRP/combat-iteration W1 落位：巡逻点 (29,-10) → (32,-10) → (32,-6)，w0-placement.md 候选 A）────────
        //   院子去东南角只有一个口子：演出触发区（x 8..11）以东、主怪 / 巡逻者乙巡逻线西端（x 13.86）以西那条 2 米多宽的缝，
        //   再往南绕过围栏东端（x 10）。下面四个坐标与 StealthShowcase ④ 的私有常量逐字相同（推导写在那边的注释里；
        //   Stealth 本波不动，以后动它时改用这里这份）。

        /// <summary>巡夜人的稳定标识（SampleScene 里 `Patrol_Xunye` 的 TamingActor.stableId）。</summary>
        protected const string XunyePatrollerId = "patrol_xunyeren";

        /// <summary>
        /// 出生点到「缝口候场点」：沿 <see cref="RouteToPatrol"/> 的前两点，再到 (9.6, 7.4)。
        /// 候场点离巡逻者乙整条巡逻线 ≥ 4.89 米（警戒半径 4.5 之外），离主怪巡逻线更远——任何时候都看不到这里。
        /// </summary>
        protected static readonly IReadOnlyList<Vector2> SoutheastHoldRoute = new[]
        {
            new Vector2(3.5f, 5f),
            new Vector2(8.5f, 7.2f),
            new Vector2(9.6f, 7.4f),
        };

        /// <summary>
        /// 穿缝：(11.6, 4.6) → (11.6, -2.0)。离触发区东沿 0.6 米、离两条巡逻线西端 ≥ 2.26 米（红区 2 与贴身 1.5 之外），
        /// 只有被正面视野锥扫到才会察觉——动身前用 <see cref="ShowcasePatrolForecast"/> 预判「接下来这几秒谁都扫不到这段路」。
        /// </summary>
        protected static readonly IReadOnlyList<Vector2> SoutheastGapCrossing = new[]
        {
            new Vector2(11.6f, 4.6f),
            new Vector2(11.6f, -2f),
        };

        /// <summary>穿过缝之后的中转点 (18, -4)：离主怪巡逻线 ≥ 5.9 米、离宝箱 C/D/E ≥ 3.6 米、离坡道南沿 ≥ 3 米。</summary>
        protected static readonly Vector2 SoutheastTransit = new Vector2(18f, -4f);

        /// <summary>巡夜人巡逻线西北的候场点 (22.5, -7.5)：离它整条巡逻线 ≥ 6.96 米（它的警戒半径 6），离主怪巡逻线东端约 11 米。</summary>
        protected static readonly Vector2 XunyeStagingPoint = new Vector2(22.5f, -7.5f);

        /// <summary>
        /// 巡逻线以南的走廊 z = −1.5（PRP/combat-iteration W6b）：离主怪巡逻线（z 3.4）4.9 米、离巡逻者乙巡逻线（z 5）6.5 米，
        /// 都在两只的警戒半径之外（主怪 4.5；乙按 6 算也够），任何朝向都看不到这里。x 夹在 <see cref="SouthLaneMinX"/>..<see cref="SouthLaneMaxX"/>：
        /// 西边让开村口演出触发区（x ≤ 11）、东边让开坡道（x ≥ 22），离南边的宝箱（z ≈ −8）还有 6.5 米。
        /// <para>
        /// 为什么要从南边接近主怪：观察点 (15.5, 7.6) 在两条巡逻线北边，从那里下到主怪身边**必须横穿巡逻者乙的巡逻线**（z 5，离主怪那条只有 1.6 米），
        /// 按真实路点前推、并探上「到了之后在那儿待几秒」，几乎找不到乙看不见的窗口（W6b 实测 Identity ② 连续两次 40 秒等不到）。
        /// 从南边过去不用横穿任何一条巡逻线，乙离走廊与落点都远。
        /// </para>
        /// </summary>
        protected const float SouthLaneZ = -1.5f;

        /// <summary>南侧走廊可挪动的 x 范围（见 <see cref="SouthLaneZ"/>）。</summary>
        protected const float SouthLaneMinX = 13f;

        protected const float SouthLaneMaxX = 21f;

        /// <summary>南侧走廊上的候场点 (17, −1.5)：主怪巡逻线（x 13.86..21.86）的正南。</summary>
        protected static readonly Vector2 SouthStagingPoint = new Vector2(17f, SouthLaneZ);

        /// <summary>等缝口出现「接下来几秒谁都看不到」窗口的上限（真实秒）：巡逻者乙一圈约 4 秒 + 停 5 秒，主怪约 8 秒 + 停 5 秒（同 StealthShowcase ④）。</summary>
        protected const float GapCrossingTimeoutSeconds = 40f;

        // ── 东南角巡夜人：普通受击回放的对手（PRP/combat-iteration W7d，用户 2026-10-16 拍板）──────────────────────────
        //   2026-10-15 起打中市令 / 被市令打中都会升级回合制（世界暂停、叠加战斗场景），「站着挨打到死」「伪装挡攻击后挨打」
        //   「挨三下镜碎」这类回放的对手换成巡夜人：它是完整参与者，但种类 1001 没配回合制战斗数据（BossRosterConfig 没有那一行），
        //   打中你不升级（EncounterStep.TryEscalate 问端口 not_configured 后照原路径结算），按普通伤害扣血、进击倒，
        //   被抓后果按 KnockdownThenDeath、被抓次数从 0 起 → 只击倒；继续挨打到生命归零才判死（MonsterShowcase.Caught_Xunye_KnockdownThenDowned）。
        //   它的种类是只能暗杀（killable = false），普通攻击打它拒伤。没用巡逻者乙：那是协作者的线，乙很可能会被改成完整参与者，
        //   它沿用市令的种类（配了战斗数据），那时它出手也会升级回合制。去东南角走 GoThroughSouthGap（缝口候场 → 预判窗口 → 穿缝）。

        /// <summary>
        /// 巡夜人那一带的空地（玩家逻辑坐标 x 20..32.4、z −11..−1.5，同 MonsterShowcase.SoutheastArea）：`Ground_East` 东沿 x 33、南沿 z −11.6，
        /// 北边 z −1.2 起是坡道。它正前方的落点出了这块就先等它转向。
        /// </summary>
        protected static readonly Rect XunyeYardArea = Rect.MinMaxRect(20f, -11f, 32.4f, -1.5f);

        /// <summary>巡夜人正前方落点离它多远（米）：红区 2 之外、它的警戒半径 6（种类 1001）之内（同 MonsterShowcase.XunyeFrontDistance）。</summary>
        protected const float XunyeFrontSpotDistance = 3.5f;

        /// <summary>等巡夜人察觉的上限（真实秒）：它一圈约 6 秒 + 停 5 秒（同 MonsterShowcase.XunyeNoticeTimeoutSeconds）。</summary>
        protected const float XunyeProvokeTimeoutSeconds = 30f;

        /// <summary>长者 NPC 的物体名（对白 1001 的发起者，站在出生点东边 3 米）。</summary>
        protected const string ElderName = "Npc_Elder";

        /// <summary>站在长者西侧 1.2 米（交互半径 2 内，且最近的 NPC 就是长者）。</summary>
        protected static readonly Vector2 ElderStandOffset = new Vector2(-1.2f, 0f);

        /// <summary>
        /// <see cref="StrikeMonster(float)"/> 里按攻击键被逻辑帧采到的累计次数（按下后进了攻击冷却）。
        /// 写进检查点文字，区分「没按上」与「按上了没打中」。按实例累计（同一回放类的多条用例共用一个实例）。
        /// </summary>
        protected int StrikeSwings { get; private set; }

        /// <summary>
        /// 从标题「开始」进 SampleScene，再等遭遇逻辑激活（<c>EncounterStep.IsActive</c>）且 <paramref name="resolveServices"/> 为真；
        /// 之后仍取不到服务就 <c>Assert.Fail(<paramref name="missingMessage"/>)</c>（前置条件不成立，后续步骤全是噪音）。
        /// 最后把虚拟手柄与键盘建齐、摇杆回中，等两帧。
        /// </summary>
        /// <param name="readyWhat">就绪等待在报告里的描述，如「遭遇逻辑在跑、输入服务与玩家 / 怪物模型可用」。</param>
        /// <param name="resolveServices">取服务并赋给调用方字段、返回是否全部取到；等待期间逐帧调用，结束后再调一次判定。</param>
        /// <param name="missingMessage">取不到服务时的失败文字。</param>
        protected IEnumerator EnterDemoWorld(string readyWhat, Func<bool> resolveServices, string missingMessage)
        {
            yield return EnterWorldFromTitle(DemoBootTimeoutSeconds, DemoEnterTimeoutSeconds);
            yield return WaitUntil(readyWhat, () =>
            {
                bool resolved = resolveServices();
                EncounterStep step = ResolveService<EncounterStep>();
                return step != null && step.IsActive && resolved;
            }, DemoEnterTimeoutSeconds);

            if (!resolveServices())
            {
                Assert.Fail(missingMessage);
            }

            Input.Prime();
            Input.ReleaseStick();
            yield return null;
            yield return null;
        }

        /// <summary>按顺序逐点 <see cref="WalkTo"/>；某段超时照 WalkTo 口径记失败，继续走下一段。</summary>
        /// <param name="waypoints">途经点（玩家逻辑坐标），如 <see cref="RouteToPatrol"/>。</param>
        /// <param name="stopDistance">每个途经点离多近算到（米）。</param>
        /// <param name="timeoutPerLeg">每段最多走多久（真实秒）。</param>
        protected IEnumerator WalkRoute(IReadOnlyList<Vector2> waypoints, float stopDistance = 1f, float timeoutPerLeg = 10f)
        {
            for (int i = 0; i < waypoints.Count; i++)
            {
                yield return WalkTo(waypoints[i], stopDistance, timeoutPerLeg);
            }
        }

        /// <summary>
        /// 记一步「切到奔跑，沿北侧绕开村口演出触发区，走到巡逻线北侧观察点」：按走跑键切跑 → <see cref="RouteToPatrol"/>（每点 0.4 米 / 8 秒）
        /// → <see cref="PatrolLookout"/>（0.3 米 / 5 秒）→ 再按走跑键切回步行。需要先 <see cref="EnterDemoWorld"/>。
        /// </summary>
        protected IEnumerator GoToPatrolLookout()
        {
            IInputService inputService = ResolveService<IInputService>();
            PlayerModel player = ResolveService<PlayerModel>();
            yield return Step("切到奔跑，沿北侧绕开村口演出触发区，走到巡逻线北侧观察点", null, 0f);
            if (inputService == null || inputService.Actions == null || player == null)
            {
                Debug.LogWarning($"{ShowcaseOptions.Prefix}[{Module}] GoToPatrolLookout 取不到输入服务 / PlayerModel，没法走");
                yield break;
            }

            yield return Input.PressUntil(inputService.Actions.Gameplay.Run, () => player.IsRunning);
            yield return WalkRoute(RouteToPatrol, 0.4f, 8f);
            yield return WalkTo(PatrolLookout, 0.3f, 5f);
            yield return Input.PressUntil(inputService.Actions.Gameplay.Run, () => !player.IsRunning);
        }

        /// <summary>
        /// 记一步「切到奔跑，经缝口绕到巡逻线以南」：<see cref="SoutheastHoldRoute"/> 到缝口候场点 →
        /// 等「接下来这几秒谁的视野都扫不到缝里那段路」（<see cref="ShowcasePatrolForecast"/>，参数同 StealthShowcase ④ 穿缝）→
        /// 一口气跑过 <see cref="SoutheastGapCrossing"/> → 再按 <paramref name="thenRoute"/> 逐点走（每点 0.3 米 / 8 秒）→ 切回步行。
        /// 等窗口超时照 WaitUntil 口径记失败、照样往下走。需要先 <see cref="EnterDemoWorld"/>。
        /// </summary>
        /// <param name="thenRoute">过了缝之后要去的点，如 { <see cref="SouthStagingPoint"/> } 或 { <see cref="SoutheastTransit"/>, <see cref="XunyeStagingPoint"/> }。</param>
        protected IEnumerator GoThroughSouthGap(IReadOnlyList<Vector2> thenRoute)
        {
            IInputService inputService = ResolveService<IInputService>();
            PlayerModel player = ResolveService<PlayerModel>();
            EncounterStep encounter = ResolveService<EncounterStep>();
            yield return Step("切到奔跑，沿北侧绕到演出触发区东北的缝口，等两条巡逻线西端接下来几秒都扫不到缝里那段路，"
                              + "一口气跑过去，绕到巡逻线以南", null, 0f);
            if (inputService == null || inputService.Actions == null || player == null || encounter == null)
            {
                Debug.LogWarning($"{ShowcaseOptions.Prefix}[{Module}] GoThroughSouthGap 取不到输入服务 / PlayerModel / EncounterStep，没法走");
                yield break;
            }

            yield return Input.PressUntil(inputService.Actions.Gameplay.Run, () => player.IsRunning);
            yield return WalkRoute(SoutheastHoldRoute, 0.4f, 8f);

            MonsterRules[] patrollers = ShowcasePatrolForecast.Patrollers(encounter);
            Vector2[][] routes = ShowcasePatrolForecast.RoutesOf(patrollers);
            ShowcasePatrolForecast.Options crossing = ShowcasePatrolForecast.Options.Crossing;
            var path = new List<Vector2>(SoutheastGapCrossing.Count + 1);
            yield return WaitUntil("缝里那段路接下来几秒谁的视野锥都扫不到（按当前位置 / 朝向 / 巡逻路线预判）", () =>
            {
                path.Clear();
                path.Add(player.Position);
                for (int i = 0; i < SoutheastGapCrossing.Count; i++)
                {
                    path.Add(SoutheastGapCrossing[i]);
                }

                float horizon = (ShowcasePatrolForecast.PathLength(path) + crossing.SlackMeters) / crossing.SpeedLow;
                return !ShowcasePatrolForecast.AnyMaySee(patrollers, routes, path, horizon, crossing, player.Snapshot, -1);
            }, GapCrossingTimeoutSeconds);
            yield return WalkRoute(SoutheastGapCrossing, 0.3f, 6f);
            yield return WalkRoute(thenRoute, 0.3f, 8f);
            yield return Input.PressUntil(inputService.Actions.Gameplay.Run, () => !player.IsRunning);
        }

        /// <summary>
        /// 站定迎击：松杆面朝 <paramref name="monster"/>（<see cref="FaceMonster(MonsterModel)"/>），等它走近到 <paramref name="reach"/> 米、
        /// 攻击冷却好了就按攻击键（按到冷却转起来为止，最多 0.3 秒），挥出一下（按下被逻辑帧采到）就返回、<see cref="StrikeSwings"/> +1，不管打没打动。
        /// <para>
        /// 为什么站着等而不是追上去（同 MonsterShowcase.CounterStrike）：玩家攻击距离 1、怪物出手距离 0.8，两边对冲时这一段只够一两个 tick；
        /// 站着让它一个走近，按键才稳稳落在「它够不着你、你够得着它」那一段。2026-10-07 起挨一下就被击倒、倒地不能攻击，这一刀必须抢在它出手之前。
        /// </para>
        /// 玩家死亡、它死亡、升级在途或满 <paramref name="timeout"/> 真实秒就收手。不记报告条目，调用方随后自己 Check 结果。
        /// </summary>
        protected IEnumerator StrikeWhenItComes(MonsterModel monster, float reach = 0.95f, float timeout = 10f)
        {
            IInputService inputService = ResolveService<IInputService>();
            PlayerModel player = ResolveService<PlayerModel>();
            EncounterStep encounter = ResolveService<EncounterStep>();
            if (inputService == null || inputService.Actions == null || player == null || monster == null)
            {
                Debug.LogWarning($"{ShowcaseOptions.Prefix}[{Module}] StrikeWhenItComes 取不到输入服务 / PlayerModel / MonsterModel，没法出手");
                yield break;
            }

            bool faced = false;
            float deadline = Time.realtimeSinceStartup + timeout;
            while (player.Health > 0 && monster.Health > 0 && (encounter == null || !encounter.IsEscalating)
                   && Time.realtimeSinceStartup < deadline)
            {
                Vector2 toMonster = monster.Position - player.Position;
                float distance = toMonster.magnitude;
                if (!faced || (distance > 0.01f && Vector2.Dot(player.Facing, toMonster / distance) < 0.5f))
                {
                    Input.ReleaseStick();
                    yield return FaceMonster(monster);
                    faced = true;
                    continue;
                }

                Input.ReleaseStick();
                if (distance > reach || player.AttackCooldownLeft > 0f)
                {
                    yield return null;
                    continue;
                }

                yield return Input.PressUntil(inputService.Actions.Gameplay.Attack, () => player.AttackCooldownLeft > 0f, 0.3f);
                if (player.AttackCooldownLeft > 0f)
                {
                    StrikeSwings++;
                    break;
                }
            }

            Input.ReleaseStick();
        }

        /// <summary>
        /// 记一条等待：朝巡夜人正前方 <see cref="XunyeFrontSpotDistance"/> 米的落点（它的视野锥、橙区里）推摇杆，让它看见你；
        /// 它察觉（警戒 / 敌对）就松杆站住，落点出了 <see cref="XunyeYardArea"/> 就原地等它转向（同 MonsterShowcase.Caught_Xunye_KnockdownThenDowned）。
        /// 先经 <see cref="GoThroughSouthGap"/> 走到 <see cref="XunyeStagingPoint"/> 再调；超时照 WaitUntil 口径记失败、照样往下走。
        /// </summary>
        /// <param name="xunye">巡夜人的模型（<see cref="FindPatroller"/>(<see cref="XunyePatrollerId"/>).Model）。</param>
        protected IEnumerator ProvokeXunye(MonsterModel xunye, float timeout = XunyeProvokeTimeoutSeconds)
        {
            PlayerModel player = ResolveService<PlayerModel>();
            yield return WaitUntil("巡夜人察觉你（警戒 / 敌对）", () =>
            {
                if (xunye == null || player == null)
                {
                    return false;
                }

                if (xunye.Mode == MonsterMode.Alert || xunye.Mode == MonsterMode.Hostile)
                {
                    Input.ReleaseStick();
                    return true;
                }

                Vector2 spot = xunye.Position + xunye.Facing * XunyeFrontSpotDistance;
                if (!XunyeYardArea.Contains(spot))
                {
                    Input.ReleaseStick();
                    return false;
                }

                Vector2 delta = spot - player.Position;
                Input.SetStick(delta.magnitude > 0.2f ? delta.normalized : Vector2.zero);
                return false;
            }, timeout);
            Input.ReleaseStick();
        }

        /// <summary>场上稳定标识为 <paramref name="stableId"/> 的巡逻者（遭遇驯服表里登记的那份规则，如 <see cref="XunyePatrollerId"/>）；取不到为 null。</summary>
        protected MonsterRules FindPatroller(string stableId)
        {
            EncounterStep encounter = ResolveService<EncounterStep>();
            return encounter == null || encounter.Taming == null ? null : encounter.Taming.GetTarget(stableId);
        }

        /// <summary>
        /// 南侧走廊上「正对着 <paramref name="x"/>」的那一点（x 夹进走廊范围）：在这条走廊上左右挪，主怪 / 巡逻者乙都看不到。
        /// </summary>
        protected static Vector2 SouthLanePoint(float x) => new Vector2(Mathf.Clamp(x, SouthLaneMinX, SouthLaneMaxX), SouthLaneZ);

        /// <summary>
        /// 把朝向往**南**（−z）偏 <paramref name="degrees"/> 度：两种转法里取 y 更小的那一个。
        /// 给「怪物正前方、偏向南侧走廊」的落点用——仍在视野锥里（偏角小于半角 37.5°），但离北边巡逻者乙的巡逻线更远。
        /// </summary>
        protected static Vector2 TurnTowardSouth(Vector2 facing, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            var left = new Vector2(facing.x * cos - facing.y * sin, facing.x * sin + facing.y * cos);
            var right = new Vector2(facing.x * cos + facing.y * sin, -facing.x * sin + facing.y * cos);
            return left.y < right.y ? left : right;
        }

        /// <summary>
        /// 追上巡逻怪并打中一下：摇杆逐帧朝怪物推到 0.7 米内，等攻击冷却转好，<see cref="FaceMonster()"/> 对准朝向
        /// （普通攻击要求目标在朝向前方半圆、1 米内），再按攻击键（<see cref="ShowcaseInputDriver.PressUntil"/>，最多 0.3 秒）；
        /// 按下被采到就 <see cref="StrikeSwings"/> +1。没打中（怪物走开、朝向没对准）就重来，
        /// 直到怪物掉血、任一方死亡或满 <paramref name="timeout"/> 真实秒。不记报告条目，调用方随后自己 Check 结果。
        /// <para>
        /// 2026-10-15（PRP/combat-iteration 升级回合制）：打中配了回合制战斗数据的怪，这一下会被交给回合制
        /// （<c>EncounterStep.IsEscalating</c>，世界随即暂停、Gameplay 输入关掉）——那时怪物不会掉血，也就不再补刀，立即返回。
        /// </para>
        /// </summary>
        protected IEnumerator StrikeMonster(float timeout = 5f) => StrikeMonster(ResolveService<MonsterModel>(), timeout);

        /// <summary>同 <see cref="StrikeMonster(float)"/>，对手换成 <paramref name="monster"/>（如 <see cref="FindPatroller"/> 取到的巡夜人的模型）。</summary>
        protected IEnumerator StrikeMonster(MonsterModel monster, float timeout = 5f)
        {
            IInputService inputService = ResolveService<IInputService>();
            PlayerModel player = ResolveService<PlayerModel>();
            EncounterStep encounter = ResolveService<EncounterStep>();
            if (inputService == null || inputService.Actions == null || player == null || monster == null)
            {
                Debug.LogWarning($"{ShowcaseOptions.Prefix}[{Module}] StrikeMonster 取不到输入服务 / PlayerModel / MonsterModel，没法出手");
                yield break;
            }

            int before = monster.Health;
            float deadline = Time.realtimeSinceStartup + timeout;
            while (monster.Health == before && monster.Health > 0 && player.Health > 0
                   && (encounter == null || !encounter.IsEscalating)
                   && Time.realtimeSinceStartup < deadline)
            {
                Vector2 diff = monster.Position - player.Position;
                if (diff.magnitude > 0.7f)
                {
                    Input.SetStick(diff.normalized);
                    yield return null;
                    continue;
                }

                Input.ReleaseStick();
                while (player.AttackCooldownLeft > 0f && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                yield return FaceMonster(monster);
                yield return Input.PressUntil(inputService.Actions.Gameplay.Attack, () => player.AttackCooldownLeft > 0f, 0.3f);
                if (player.AttackCooldownLeft > 0f)
                {
                    StrikeSwings++;
                }

                float settle = Time.realtimeSinceStartup + 0.2f;
                while (monster.Health == before && (encounter == null || !encounter.IsEscalating)
                       && Time.realtimeSinceStartup < settle)
                {
                    yield return null;
                }
            }

            Input.ReleaseStick();
        }

        /// <summary>
        /// 攻击前对准巡逻怪：敌对怪物会一直走到玩家脚下，两者几乎重合时「目标在朝向前方半圆」判不准（实测连打落空、被反杀），
        /// 所以贴得太近（&lt; 0.3 米）先背向它退 0.12 秒拉开半步，再朝它轻推 0.06 秒（摇杆 0.3）把朝向对准，最后松杆、等一帧。
        /// </summary>
        protected IEnumerator FaceMonster() => FaceMonster(ResolveService<MonsterModel>());

        /// <summary>同 <see cref="FaceMonster()"/>，对准的是 <paramref name="monster"/>。</summary>
        protected IEnumerator FaceMonster(MonsterModel monster)
        {
            PlayerModel player = ResolveService<PlayerModel>();
            if (player == null || monster == null)
            {
                yield return null;
                yield break;
            }

            Vector2 gap = monster.Position - player.Position;
            if (gap.magnitude < 0.3f)
            {
                Vector2 away = gap.magnitude > 0.01f ? -gap.normalized : -player.Facing;
                float backUntil = Time.realtimeSinceStartup + 0.12f;
                while (Time.realtimeSinceStartup < backUntil)
                {
                    Input.SetStick(away);
                    yield return null;
                }
            }

            float faceUntil = Time.realtimeSinceStartup + 0.06f;
            while (Time.realtimeSinceStartup < faceUntil)
            {
                Vector2 toMonster = monster.Position - player.Position;
                if (toMonster.magnitude > 0.01f)
                {
                    Input.SetStick(toMonster.normalized * 0.3f);
                }

                yield return null;
            }

            Input.ReleaseStick();
            yield return null;
        }
    }
}
