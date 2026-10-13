// 职责：潜行（S3 潜行与暗杀）回放——两条用例各演一件「玩家看得见」的事：
//   ① Sight_BlockedByCover_HidesFromEnemy：站在巡逻怪视野锥里，中间没有遮挡 = 被察觉（stealth.hidden 假）；
//      在两人连线上摆上掩体 = 不被察觉（stealth.hidden 真、stealth.cover 真）。
//   ② Sneak_BehindEnemy_AllowsAssassination：潜行摸到巡逻怪背后 1 米内，它没察觉，
//      stealth.behind 为真（走的是 Monster 侧写进事实集的那条链），这一刀能下（AssassinationAllowed 真）。
// 舞台是 SampleScene，走 Boot 真实流程（标题「开始」进场）；玩家用虚拟手柄走北侧路线到巡逻线北侧观察点，
//   怪物是场景里那只真实巡逻怪 enerme。事实键只从 EncounterStep.Facts 读（= 正式流程写进去的那份），
//   不自己拼字符串、不 new 结算对象。
// 遮挡体从哪来：`Environment_Graybox/Cover_SightDemo` 是**摆在场景里的 demo 掩体**，登记在 Encounter 物体
//   EncounterSceneView 的 sightOccluders 上（矩形，尺寸给全宽 × 全深）——这就是正式场景的标准接法：
//   场景里放物体 + 视图上登记一条，场景就绪时由 MonsterEncounterState 一次性转成纯数据几何喂给
//   EncounterStep.Sight。本回放**不自己造几何**，一律走 view.CollectSightOccluders()。
//   唯一「回放特有」的动作：把这只**已经登记好的**掩体挪到玩家与怪物连线的中点，再重采集一次几何。
//   理由：静态掩体不会自己出现在两人连线上，而怪物会一路走到玩家脚下（MonsterRules 的逻辑移动不吃碰撞），
//   固定摆位没法稳定演示「中间有东西挡着」。正式场景里掩体是静态摆好的，接法一致，只有位置由关卡定。
// 为什么先「没有遮挡」再「有遮挡」：`sightOccluders` 为空是**既有语义**（= 不挡视线，判定与接线前一致，
//   见 EncounterSceneView.CollectSightOccluders 的注释），所以回放用 Sight.Clear() / SetOccluders(...) 各设一次，
//   在同一段几何上对比「拿掉」与「放上」。这一对语义正是 `stealth.cover` 那一列「未做时该键恒不写」的边界。
// 已知边界改名（2026-10-08 判据变更）：原第 4 步最后一条断言写的是「已知边界：怪物照样追过来
//   （遮挡只写 stealth.* 事实，不喂 MonsterRules 的察觉）」——**那条断言编码的是缺陷**：同一条事实
//   （stealth.hidden 真）与可见行为（怪照样警戒、照样追）互相矛盾。本波把视锥两档（红区 / 橙区）
//   接上遮挡之后，这条断言反过来写成「掩体挡住视线且怪不再追过来」（模式回到巡逻、警戒清零）。
//   贴身那一档「不分朝向的近距察觉」**有意不受遮挡**：`06_怪物状态与交互设计文档.md:71-73`
//   写的是「要潜行才免」，不是「有掩体就免」。
// ③ ExecuteKey_BehindEnemy_ExecutesTheTarget（2026-10-07 接线那一波新增）：潜行绕到巡逻怪背后，
//   按 `Gameplay/Execute`（键鼠 F）→ 目标被处决。这一条演的是接线之后的完整链：
//   场景里那只 `ExecutionInteractor` 被 `MonsterEncounterState.BindExecution` 接上玩家 / 怪物规则 /
//   事实写入点 / 判定内核 / 遭遇结算 / 埋点，按 F 才真的会结算（接线前那个组件没有任何生产调用方）。
//   断言口径：**怪死了 + `stealth.assassinated` 写进正式流程那份事实集 + 按键那一次的结果是「允许」**。
//   埋点（`stealth/stealth_executed`）由 EditMode 的 `ExecutionSceneWiringTests` 用收集型 sink 断言
//   ——回放侧拿不到 sink，不在这里假装看到。
//   内容前提：`Boot.unity` 的 `MonsterInstaller.kindId = 1003`（市令：killable=false + defeat_method=暗杀）。
//   回放**不读那个序列化字段**：它只按「场上这只怪能处决」来演，premise 不成立时失败信息里会带上拒绝原因。
//   F 键归属（PRP/interaction D6，2026-10-08）：统一交互焦点非空时按 F 归交互、处决让位。本条回放的站位（巡逻线背后）
//   附近没有 NPC / 物资箱，按 F 那一刻焦点必然为空——回放如实断言「按下时焦点为空、这一下归处决」并核对接线
//   （BindExecution 把容器里那份 IInteractionFocus 传给了组件）；「焦点与处决目标同时在场」的让位分支不在这张图里硬造，
//   由 EditMode 的 ExecutionInteractorTests 覆盖。
//   2026-10-09 复核这一条**不是** cp19 判红的原因：整套回放里 `interaction/focus_changed` 最后一次是 12.7 秒
//   清空（走北侧路线路过 NPC 与物资箱那一段），进巡逻线之后焦点一直是空的，失败截图的面板也报
//   「处决：不可（超出暗杀距离）」而不是让位。判红的真正原因见 ④。
// ④ 2026-10-09 修：绕背走位**不再只看主怪**（那次 cp18–24 全红的根因，不是判定坏了）
//   现场（`Logs/verify/stealth/20261009-091355/` + 同批次 `Logs/telemetry/fe8866e5.log`）：
//     · 玩家在「按住潜行键绕背」那一步开始 **0.3 秒**后被察觉（telemetry：主怪 20.2 s 进停顿；
//       20.58 s 一只怪 0→2 警戒、20.81 s 2→3 敌对，两只怪的 state_changed 对得上号）；
//       之后 1 秒一刀连砍三刀（20.81 / 21.82 / 22.83）→ `player/dead` → 镜碎 →
//       `MirrorCrackPresenter.Shatter()` 调 `EncounterStep.End()`（内部 `facts.Clear()`），
//       后面 5 个检查点全在停摆的世界里空转（之后再没有任何 monster/state_changed）。
//     · 失败截图的白盒面板写着「潜行 False / 怪物生命 3 / 状态 PatrolPause」：**主怪从头到尾没察觉、没掉血**；
//       而玩家死时没在潜行 —— 潜行键只在「离主怪 2 米」时才按，所以它**从没进过主怪 2 米**。
//       袭击者只能是另一只巡逻者：挨刀要 ≤ 0.8 米，主怪不可能。
//     · `位置条件成立过=False`（cp20 括号里那个量）是 `executeBehindHeld`，它**只在** `if (behindNow)` 分支里赋值，
//       所以它假 = 那个分支一次都没进过；`executeFocusEmpty` 同样只在该分支里赋值，**cp19 的 ✗ 不能读成
//       「当时有交互提示」**。整场 `interaction/focus_changed` 在 12.7 秒清空之后再没出现过 —— D6 的让位分支
//       根本没参与，处决判定与 F 键归属都是好的。
//   错在哪：`Patrol_B`（3396ca8 多巡逻者那一波）的巡逻线是 z=5、x 13.86..17.86（`PatrolBPoint0/1`），
//     **横在观察点 (15.5, 7.6) 与主怪巡逻线 (z=3.4) 之间**；而绕背走位是「朝主怪背后拉直线」、
//     潜行键只在离主怪 2 米时才按 —— 穿过那条线时玩家既没潜行（贴身 1.5 米就察觉）、又直挺挺走进它的视锥，
//     一被发现就是击倒（`KnockdownRules` 倒地速度倍率 0，怪 1 秒一刀）→ 3 刀死。
//   怎么改（只动回放，产品规则一个字没改）：① 潜行键按「离**任意一只**巡逻怪 2 米内」按；
//     ② 每帧只走「落点不会被任何一只巡逻怪察觉」的那一步（判定直接用产品自己的 `MonsterRules.Detects`，
//     不在回放里重抄半径与夹角），前方被挡住就原地等它转头，自己站的格子被锥子扫到就往回退半步；
//     ③ 停顿窗口改成「**刚**进入停顿」（原来写「正在停」，可能只剩 1.2 秒），上限按「一次被挡 + 下一次停顿」给到 20 秒；
//     ④ 处决之后**退场**回观察点再跑检查点：后面约 12 秒的检查点停顿里，站在巡逻线背后等于把玩家放进
//     第二只巡逻者的视锥里等它扫过来（死了会 `facts.Clear()`，`stealth.assassinated` 就没了）。
//   边界：观察点对**主怪**的安全性推导（`ShowcaseScenario.DemoScene.cs` 的 `PatrolLookout`）仍然成立，
//     它没算第二只巡逻者 —— 那是这张 demo 舞台的内容变化（多巡逻者落地），回放按「现场有什么就绕什么」适配。
//     断言一条都没放宽：`behindSeen` / `allowedSeen` / `AssassinationAllowed` / 物种门槛全部原样。
// 不包括：把这一刀做进确定性内核（处决是实时输入触发的交互，PRP §2.4 明确不进 `InputCommand`）。
using System.Collections;
using System.Reflection;
using Game.Core.Input;
using Game.Interaction;
using Game.Monster;
using Game.Player;
using Game.Stealth;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.Showcase.Stealth
{
    [Category("Showcase")]
    public sealed class StealthShowcase : ShowcaseScenario
    {
        /// <summary>场景里 demo 掩体的物体名（挂在 Environment_Graybox 下，登记在 Encounter 的 EncounterSceneView 上）。</summary>
        private const string CoverObjectName = "Cover_SightDemo";

        /// <summary>绕背时的目标：站在怪物背后多远（米），在暗杀距离 1.2（AssassinationSettings.PlaceholderDefault）之内。</summary>
        private const float BehindDistance = 1f;

        /// <summary>绕背走位到「怪物背后 <see cref="BehindDistance"/> 米那个点」多近算到位（米）。</summary>
        private const float ArriveDistance = 0.15f;

        /// <summary>离**任意一只**巡逻怪多近时开始潜行（米）：潜行免的是近身察觉 1.5（`03_潜行与暗杀.md:121` R17）。</summary>
        private const float SneakFromDistance = 2f;

        /// <summary>
        /// 绕背流程的上限（真实秒）。依据是巡逻周期：走 7~10 秒 + 停 5 秒（MonsterConfig.asset）⇒ **每只怪约 14 秒一个周期**。
        /// 动身前要等一个「两只怪都还剩 ≥<see cref="CrossingPauseMarginSeconds"/> 秒停顿」的窗口（见
        /// <see cref="PatrollersPausedLongEnough"/>），那是**两个独立周期的相位对齐**——按 14 秒一轮算，
        /// 40 秒覆盖约 3 轮，撞不上的概率才够低。**这不是把等待拉长到碰巧能过**：判据
        /// （`behindSeen` / `allowedSeen` / `AssassinationAllowed`）一个都没动，走位也不再靠运气。
        /// 2026-10-09 实测：20 秒那版会以「等到超时也没动身」红（遥测里没有 `player/dead`），
        /// 修前的版本则以「被第二只巡逻者打死」红——两种都是同一个相位问题。
        /// </summary>
        private const float ApproachTimeoutSeconds = 40f;

        /// <summary>等「巡逻怪刚进入停顿」的上限（真实秒）：同上，一个完整周期 15 秒 + 余量。</summary>
        private const float PatrolPauseWaitSeconds = 20f;

        /// <summary>
        /// 避让判定的前瞻距离（米）：判的是「再往前这么多」的落点会不会被察觉，而不是脚底这一点。
        /// 玩家一个 tick 只走 0.025 米（潜行 1.5 ÷ 60），0.3 米约合 0.2 秒的余量。
        /// </summary>
        private const float SafetyLookahead = 0.3f;

        /// <summary>处决后退场（走回观察点）的上限（真实秒）。退不回去不记失败，见 <see cref="LeaveStage"/>。</summary>
        /// <summary>迈步前要求怪还剩多少秒停顿。<see cref="StepSafelyToward"/> 横穿那条走廊约 1.7 秒，留出余量。</summary>
        private const float CrossingPauseMarginSeconds = 2.5f;
        /// <summary>离观察点多近算「还没动身」——只有这一档才等「怪都停稳」的窗口，动身后不再等（见 SneakUpBehind 的注释）。</summary>
        private const float LookoutCommitRadius = 1.5f;

        private const float LeaveStageTimeoutSeconds = 12f;

        /// <summary>退场判「到了」的距离（米）。</summary>
        private const float LeaveStageArriveDistance = 0.6f;

        /// <summary>摆掩体要求两人至少隔这么远（米）：几乎重合时连线退化成点，任何遮挡体都挡不住。</summary>
        private const float MinCoverGap = 1.5f;

        /// <summary>视野锥里的站位：怪物正前方多远 / 再往北偏多少（米），见 <see cref="DriveIntoCone"/> 的算式。</summary>
        private const float ConeSpotForward = 3f;
        private const float ConeSpotSideways = 1.5f;

        /// <summary>按 F 那一轮最多等多久（真实秒）：要按到「按键那一次有结果」为止，见 <see cref="PressExecuteWhileBehind"/>。</summary>
        private const float ExecuteTimeoutSeconds = 6f;

        private IInputService inputService;
        private PlayerModel player;
        private MonsterModel monster;
        private EncounterStep step;
        private EncounterSceneView view;
        private StealthKernel kernel;
        private ExecutionInteractor interactor;
        private GameObject cover;

        /// <summary>
        /// 场上**所有**巡逻怪（主怪 + 别的巡逻者），绕背走位要躲的就是它们。取法见 <see cref="CollectPatrollers"/>。
        /// 2026-10-09 之前这里只有 `monster` 一只，所以走到了第二只的巡逻线上还不知道。
        /// </summary>
        private MonsterRules[] patrollers = System.Array.Empty<MonsterRules>();

        /// <summary>
        /// 玩家规则。只有一个用途：第 ③ 条用例把玩家**直接放到**主怪背后（<see cref="TakePositionBehindForExecute"/>），
        /// 走 `PlayerRules.Reset`（`ExplorationShowcase`「把玩家挪到 Crate_C 旁」同款做法）。
        /// 别的走位一律走输入推摇杆，不碰它。
        /// </summary>
        private PlayerRules playerRules;

        /// <summary>潜行键此刻是不是按住的（逐帧同步用）：Hold / Release 各自要等一两帧才生效，不能逐帧抖。</summary>
        private bool sneakHeld;

        // 绕背那一轮里逐帧记下来的事实（怪物一直在动，检查点只能对着「出现过的时刻」判）。
        private bool closeSeen;
        private bool calmSeen;
        private bool behindSeen;
        private bool allowedSeen;

        // 按 F 那一轮里逐帧记下来的事实。
        private bool executePressed;
        private bool executeAllowed;
        private bool executeHintShown;
        private bool executeBehindHeld;
        private ExecutionReject executeReason;
        private bool executeFocusEmpty;

        protected override string Module => "Stealth";

        /// <summary>世界由流程加载（标题「开始」→ MonsterEncounterState → SampleScene），基类不再叠加载一份。</summary>
        protected override string ScenePath => null;

        // ───────────────────────── ① 视线被挡就不被察觉 ─────────────────────────

        [UnityTest]
        public IEnumerator Sight_BlockedByCover_HidesFromEnemy()
        {
            yield return EnterWorld();
            yield return GoToPatrolLookout();

            // ── 第 4 步：先演「没有遮挡」这一半（前 3 步是进入世界的固定流程）──────────
            bool seen = false;
            yield return Step("把 demo 掩体从视线清单里拿掉（空清单 = 没有遮挡），走进巡逻怪的视野锥",
                () => step.Sight.Clear(), 0f);
            yield return WaitUntil("被它察觉（stealth.hidden 为假）", () =>
            {
                DriveIntoCone();
                seen = !step.Facts.IsTrue(StealthFactKeys.Hidden);
                return seen;
            }, 12f);
            Input.ReleaseStick();
            float gapWhenSeen = Vector2.Distance(player.Position, monster.Position);
            yield return Check($"没有遮挡时在视野锥里就被察觉（stealth.hidden 为假；当时相隔 {gapWhenSeen:0.##} 米，"
                               + "在近身察觉半径 1.5 之外，所以靠的是视野不是贴身）",
                () => seen && gapWhenSeen > 1.5f);

            // ── 第 5 步：把掩体摆到两人连线上，重采集几何 ─────────────────────────────
            bool coverPlaced = false;
            yield return Step("在玩家与怪物之间放上 demo 掩体，把场景登记的遮挡体重新喂给潜行内核",
                () => coverPlaced = PlaceCoverBetween(), 0f);
            yield return Check("视线被挡：未被察觉（stealth.hidden 为真），且处于掩体遮挡下（stealth.cover 为真）",
                () =>
                {
                    // 怪物一直朝玩家走，检查期间保持几米距离，免得它贴到身上把连线压没。
                    KeepDistance();
                    return coverPlaced
                           && step.Facts.IsTrue(StealthFactKeys.Hidden)
                           && step.Facts.IsTrue(StealthFactKeys.Cover);
                }, 3f);
            Input.ReleaseStick();
            yield return Snapshot("掩体挡住视线");

            // ── 第 6 步：掩体**挡住追击**（2026-10-08 判据变更）────────────────────────────
            // 上一句检查时怪物已经进了警戒态（放掩体之前它真的看见过玩家）。所以「不再追」这件事要给它
            // 走完「警戒衰减清零」这段路（满值回落 6 秒），这里给 14 秒的上限。**这段时间玩家不能再动**
            // （KeepDistance 会把他推出连线的遮挡范围，掩体就又挡不住了），所以站在掩体后面等。
            //
            // 两条断言，都对着「掩体真的起了作用」：
            //   ① 期间**从没升级到敌对**——旧缺陷下它会一路警戒升满、转敌对并扑过来，那时这条会红；
            //   ② 最终回到巡逻两态、警戒清零（它放弃了）。
            //
            // ⚠️ **这里刻意不再断言 `stealth.cover` / `stealth.hidden`**（第一版写了，是错的）：等到它回到巡逻时，
            // 怪已经沿巡逻线走开、朝向也变回 ±X，玩家在它侧后方**已经不在视野锥里**了——那时 `coverBlocks`
            // 按定义就是假（「视线本来够得着」那一半不成立），`cover` 因此合理地消失。在那一刻断言 `cover`
            // 等于拿一个已经失效的几何关系去判分。`stealth.hidden` 同理：怪没在看你时它为真是平凡的，
            // 钉不出任何东西。这两个键的语义由紧邻的第 5 步（几何受控：掩体刚摆到连线上）负责。
            Input.ReleaseStick();
            bool calledOff = false;
            bool everEscalated = false;
            yield return WaitUntil("怪物放弃追击、退回巡逻（掩体挡住了视线，它找不到你）", () =>
            {
                calledOff = monster.Mode == MonsterMode.PatrolWalk || monster.Mode == MonsterMode.PatrolPause;
                if (monster.Mode == MonsterMode.Hostile)
                {
                    everEscalated = true;
                }

                return calledOff;
            }, 14f);
            yield return Check("掩体挡住视线后怪不再追过来：期间从没升级到敌对，最终回到巡逻、警戒清零",
                () => calledOff && !everEscalated && monster.Alert <= 0.001f);
            yield return Snapshot("掩体挡住追击");

            // 旧断言编码的是**缺陷**：2026-10-08 之前，遮挡只写 `stealth.*` 事实键，怪物自己的
            // `MonsterRules.Sense` 是纯视锥、零遮挡输入——于是「stealth.hidden 为真」与「怪照样警戒、照样追」
            // 同时成立，同一条事实与可见行为互相矛盾。判据变更后视锥两档（红区 / 橙区）吃遮挡，
            // 这条断言反过来钉「行为层也看不见了」。同一条边界在 EditMode 里由
            // `MonsterOcclusionTests`、`EncounterStealthTests.Step_WhenConeBlockedByOccluder_FactLayerAgreesWithBehaviour`
            // 与 `EncounterKernelWiringTests.BindOcclusion_IsAppliedByTheContainer_...` 守着。
            // （贴身那一档「不分朝向的近距察觉」**有意不受遮挡**：`06_怪物状态与交互设计文档.md:71-73`
            //   写的是「要潜行才免」，不是「有掩体就免」。）
        }

        // ───────────────────────── ② 绕到背后就能处决 ─────────────────────────

        [UnityTest]
        public IEnumerator Sneak_BehindEnemy_AllowsAssassination()
        {
            yield return EnterWorld();
            yield return GoToPatrolLookout();

            yield return Step("等巡逻怪走完一段、进入巡逻停顿（停 5 秒、朝向不变，好绕背）", null, 0f);
            yield return WaitForFreshPatrolPause();

            yield return Step("按住潜行键，绕到怪物背后 1 米内", null, 0f);
            yield return SneakUpBehind();

            yield return Check("背后近距潜行：怪物全程在巡逻、警戒条为 0（没察觉你）",
                () => closeSeen && calmSeen);
            yield return Check("stealth.behind 为真：贴在背后且在暗杀距离（1.2 米）内", () => behindSeen);
            yield return Snapshot("潜行到背后");

            yield return Check("S3 绕背判定为真：AssassinationAllowed（只答位置 + 察觉；F 键处决那层的物种门槛另算）",
                () => allowedSeen);
            yield return Snapshot("可处决");

            yield return ReleaseSneak();
        }

        // ───────────────────────── ③ 背后按 F 处决 ─────────────────────────

        /// <summary>
        /// ③ 接线之后的完整链：潜行绕到巡逻怪背后，按 `Gameplay/Execute`（键鼠 F）→ 目标被处决。
        /// 前面两条演的是「判定」（stealth.behind / AssassinationAllowed），这一条演的是「按键真的结算了」。
        /// </summary>
        [UnityTest]
        public IEnumerator ExecuteKey_BehindEnemy_ExecutesTheTarget()
        {
            yield return EnterWorld();
            yield return GoToPatrolLookout();

            yield return Step("找到场上那只 ExecutionInteractor（正式流程应当已经把它接好线）", null, 0f);
            yield return Check("背后处决已接进正式流程：场上那只 ExecutionInteractor 处于已接线状态"
                               + "（接线前它一个生产调用方都没有，「按 F 没反应」）",
                () => interactor != null && interactor.IsConfigured);
            if (interactor == null || !interactor.IsConfigured)
            {
                Assert.Fail("SampleScene 里没有已接线的 ExecutionInteractor："
                            + "检查 Encounter 物体上有没有挂这个组件（照 SampleScene 的接法），"
                            + "以及 MonsterEncounterState.BindExecution 是否真的被调到。");
            }

            IInteractionFocus focus = ResolveService<IInteractionFocus>();
            yield return Check("F 键归属已接线：BindExecution 把容器里那份统一交互焦点传给了组件（PRP/interaction D6）",
                () => focus != null && ReferenceEquals(WiredFocus(), focus));

            yield return Step("等巡逻怪走完一段、进入巡逻停顿（停 5 秒、朝向不变，好绕背）", null, 0f);
            yield return WaitForFreshPatrolPause();

            yield return Step("按住潜行键，站到怪物背后 1 米处（落点先断言不被任何一只巡逻怪察觉）", null, 0f);
            yield return TakePositionBehindForExecute();

            yield return Step("在背后近距按 F（Gameplay/Execute）；按键那一次的结果会被记下来", null, 0f);
            yield return PressExecuteWhileBehind();
            // 截图挪到按 F 之后立刻拍：这一张要的就是「怪已经倒下、面板说可处决」的现场；
            // 原来排在 5 条检查点之后（约 12 秒），那时玩家已经退场了，图里就没有现场了。
            yield return Snapshot("按 F 处决");

            // 退场再跑检查点：后面 5 条检查点按 x1 节奏约 12 秒，而玩家还站在巡逻线背后 ——
            // 那里离第二只巡逻者的巡逻线只有 1.6 米，它的视锥来回扫，扫到就是警戒 → 敌对 → 击倒 → 三刀死；
            // 玩家一死触发镜碎，`MirrorCrackPresenter.Shatter()` 会调 `EncounterStep.End()`（`facts.Clear()`），
            // `stealth.assassinated` 会被抹掉。移动本身不记报告条目（同 StrikeMonster 口径），判定仍看下面几条。
            yield return Step("退出巡逻线：这一刀已经结算完，先回到巡逻线北侧的观察点，"
                              + "别站在这里等第二只巡逻者走过来", null, 0f);
            yield return LeaveStage();
            yield return ReleaseSneak();

            yield return Check("按 F 之前面板提示「可处决」（Inspect 报的就是按键会用的那份判定）",
                () => executeHintShown);
            yield return Check("按 F 那一刻屏幕上没有交互提示（统一焦点为空），这一下归处决、不让位",
                () => executePressed && executeFocusEmpty && executeReason != ExecutionReject.YieldedToInteraction);
            yield return Check($"按 F 确实走到了结算：最近一次结果是「允许」"
                               + $"（记到的拒绝原因：{executeReason}；位置条件成立过：{executeBehindHeld}）",
                () => executePressed && executeAllowed);
            yield return Check("目标被处决：怪生命归零、进入 Dead", () => monster.Health == 0 && monster.Mode == MonsterMode.Dead);
            yield return Check("stealth.assassinated 写进了正式流程那份事实集（命中即写，持久）",
                () => step.Facts.IsTrue(StealthFactKeys.Assassinated));
            yield return Snapshot("退回观察点");

            // 如实记录边界（本波不修）：处决把这一场承载成 Victory，但战斗结果**目前没有消费方**，
            // 所以遭遇不会自己收尾。埋点 stealth_executed 由 EditMode 的 ExecutionSceneWiringTests 断言。
            yield return Check("如实记录：处决承载了这一场的结果（BattleSettlement = Victory），"
                               + "但 ResultConsumed 仍为假——战斗结果目前没有消费方，流程不会自己推进",
                () => step.BattleSettlement.HasValue && !step.ResultConsumed);
        }

        // ───────────────────────── 进场与驱动 ─────────────────────────

        /// <summary>
        /// 标题「开始」进世界（EnterDemoWorld），等容器里的输入服务、玩家 / 怪物模型、遭遇结算与潜行内核可用；
        /// 同时从场上取到 EncounterSceneView 与那只 demo 掩体（缺哪个都是前置条件不成立，直接中断）。
        /// </summary>
        private IEnumerator EnterWorld()
        {
            yield return EnterDemoWorld("遭遇逻辑在跑、输入服务与玩家 / 怪物 / 潜行服务可用", () =>
            {
                inputService = ResolveService<IInputService>();
                player = ResolveService<PlayerModel>();
                monster = ResolveService<MonsterModel>();
                step = ResolveService<EncounterStep>();
                kernel = ResolveService<StealthKernel>();
                view = Object.FindObjectOfType<EncounterSceneView>();
                // 处决交互入口：第 ③ 条用例要用（前两条不碰它，所以不并进下面那条共同前置断言里，
                // 免得「处决没接线」把两条判定回放也一起判红）。
                interactor = Object.FindObjectOfType<ExecutionInteractor>();
                playerRules = ResolveService<PlayerRules>();
                return inputService != null && inputService.Actions != null
                       && player != null && monster != null && step != null && kernel != null && view != null
                       && playerRules != null;
            }, "进世界后容器里取不到 PlayerModel / MonsterModel / IInputService / EncounterStep / StealthKernel，"
               + "或场上找不到 EncounterSceneView（检查 Boot 的 StealthInstaller、MonsterInstaller 与 SampleScene 的 Encounter 物体）");

            if (step.Sight != kernel.Sight)
            {
                Assert.Fail("EncounterStep.Sight 不是潜行内核那只：UseStealth 没生效，遮挡体喂了也不参与结算");
            }

            // 绕背走位要躲的那一组（主怪 + 别的巡逻者）。放在这里取一次：`Taming` 的注册在场景就绪时完成，
            // 而这条用例的每一步都要用它判「这一步会不会被谁看见」，不能每帧现查。
            patrollers = CollectPatrollers();

            cover = GameObject.Find(CoverObjectName);
            if (cover == null)
            {
                Assert.Fail($"SampleScene 里找不到 demo 掩体「{CoverObjectName}」：它是这条回放的舞台内容。"
                            + "照 SampleScene 里的接法补：Environment_Graybox 下放一个 box，"
                            + "再在 Encounter 的 EncounterSceneView.sightOccluders 上登记一条（矩形，给全宽 × 全深）。");
            }
        }

        /// <summary>
        /// 逐帧朝「怪物正前方 3 米、再往北偏 1.5 米」推摇杆：相对朝向的角度 atan2(1.5, 3) ≈ 27°，
        /// 在视野半角 37.5°（MonsterConfig.visionAngle 75）之内；距离 3.35 在警戒半径 6 之内、近身半径 1.5 之外
        /// ——所以「被察觉」只可能是视野锥造成的。太近（&lt; 2.2 米）就往回走半步，别贴到它身上。
        /// </summary>
        private void DriveIntoCone()
        {
            Vector2 toMonster = monster.Position - player.Position;
            if (toMonster.magnitude < 2.2f)
            {
                Input.SetStick(-toMonster.normalized);
                return;
            }

            Vector2 spot = monster.Position + monster.Facing * ConeSpotForward + new Vector2(0f, ConeSpotSideways);
            Vector2 delta = spot - player.Position;
            Input.SetStick(delta.magnitude > 0.2f ? delta.normalized : Vector2.zero);
        }

        /// <summary>检查期间保持几米距离：怪物朝玩家走，玩家往外走（步行 3 &gt; 它追击 2.5），连线就不会被压没。</summary>
        private void KeepDistance()
        {
            Vector2 away = player.Position - monster.Position;
            // 只在这个框里后退：西边 x 8..11、z 1..4 是村口演出触发区（一进就冻世界），
            // 北边是楼梯与塔（层 8，在 obstacleMask 里，走进去会被挡）。退到边界就停住不动。
            bool inside = player.Position.x > 12.5f && player.Position.x < 18f
                          && player.Position.y > 2.2f && player.Position.y < 8f;
            if (inside && away.magnitude < 3f && away.magnitude > 0.01f)
            {
                Input.SetStick(away.normalized);
                return;
            }

            Input.ReleaseStick();
        }

        /// <summary>
        /// 把 demo 掩体挪到玩家与怪物连线的中点，再用视图自己那份转换重采集几何喂给内核。
        /// 返回 false = 两人太近（&lt; <see cref="MinCoverGap"/>）或内核里一条遮挡体都没有。
        /// 写回场景位置按视图的投影走（XZ 场景是 (x, 逻辑 y) → 场景 (x, 保留高度, 逻辑 y)），写完用
        /// <see cref="EncounterSceneView.ToLogicPosition"/> 核对一次，不是 XZ 场景就换另一套写回。
        /// </summary>
        private bool PlaceCoverBetween()
        {
            Vector2 gap = monster.Position - player.Position;
            if (gap.magnitude < MinCoverGap)
            {
                return false;
            }

            Vector2 middle = player.Position + gap * 0.5f;
            Vector3 authored = cover.transform.position;
            cover.transform.position = new Vector3(middle.x, authored.y, middle.y);
            if ((view.ToLogicPosition(cover.transform.position) - middle).magnitude > 0.01f)
            {
                cover.transform.position = new Vector3(middle.x, middle.y, authored.z);
            }

            step.Sight.SetOccluders(view.CollectSightOccluders());
            return step.Sight.Count > 0;
        }

        // ───────────────────────── 绕背的走位与避让（2026-10-09 修） ─────────────────────────

        /// <summary>
        /// 等主怪**刚**进入巡逻停顿。为什么不是「正在停」：`MonsterModel.PatrolPauseLeft` 只在停顿的头几秒
        /// 大于 1.2，原来那个条件可能在停顿快结束时才成立（只剩 1.2 秒），绕背窗口根本不够用。
        /// 现在只在「本帧刚刚变成停顿」那一刻返回，整段停顿（`MonsterConfig.asset` 的 5 秒）都归绕背。
        /// 上限见 <see cref="PatrolPauseWaitSeconds"/>：巡逻周期上限 15 秒 + 余量。
        /// </summary>
        private IEnumerator WaitForFreshPatrolPause()
        {
            bool seenPausing = false;
            yield return WaitUntil("巡逻怪刚进入巡逻停顿（整段停顿窗口都留给绕背）", () =>
            {
                bool pausing = monster.Mode == MonsterMode.PatrolPause;
                bool fresh = pausing && !seenPausing;
                seenPausing = pausing;
                return fresh;
            }, PatrolPauseWaitSeconds);
        }

        /// <summary>
        /// 场上**所有**巡逻怪（主怪 + 别的巡逻者）。取法就是 Monster 侧自己的口径：
        /// <c>EncounterStep.Taming.TargetIds</c> 那一组 = 「所有巡逻怪」，主怪是第 0 只
        /// （`EncounterStep.ConfigureTaming` / `ApplyMonsterOcclusion` 的注释）。回放必须看全这一组——
        /// 2026-10-09 那次失败就是只认主怪，从第二只巡逻者 `Patrol_B` 的巡逻线上直着穿了过去。
        /// </summary>
        private MonsterRules[] CollectPatrollers()
        {
            if (step == null || step.Taming == null)
            {
                return System.Array.Empty<MonsterRules>();
            }

            System.Collections.Generic.IReadOnlyList<string> ids = step.Taming.TargetIds;
            var found = new System.Collections.Generic.List<MonsterRules>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                MonsterRules rules = step.Taming.GetTarget(ids[i]);
                if (rules != null)
                {
                    found.Add(rules);
                }
            }

            return found.ToArray();
        }

        /// <summary>离最近那只巡逻怪多远（米）；一只都取不到时返回极大值。</summary>
        private float NearestPatrollerDistance()
        {
            float best = float.MaxValue;
            for (int i = 0; i < patrollers.Length; i++)
            {
                MonsterRules rules = patrollers[i];
                if (rules == null)
                {
                    continue;
                }

                float distance = (rules.Model.Position - player.Position).magnitude;
                if (distance < best)
                {
                    best = distance;
                }
            }

            return best;
        }

        /// <summary>离玩家最近那只巡逻怪的位置；一只都取不到时返回玩家自己的位置（退让方向退化成零向量，调用方判掉）。</summary>
        private Vector2 NearestPatrollerPosition()
        {
            Vector2 best = player.Position;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < patrollers.Length; i++)
            {
                MonsterRules rules = patrollers[i];
                if (rules == null)
                {
                    continue;
                }

                Vector2 position = rules.Model.Position;
                float distance = (position - player.Position).magnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = position;
                }
            }

            return best;
        }

        /// <summary>
        /// 这个落点会不会被**任何一只巡逻怪**察觉。判定直接用产品自己的行为层口径
        /// （<see cref="MonsterRules.Detects"/>：视锥两档吃遮挡、贴身那一档只看「有没有潜行」），
        /// **不在回放里重抄一遍半径与夹角** —— 抄一遍就是多一份会过期的真源。
        /// 构造的快照里只有位置是假设的，潜行 / 伪装 / 生命都取当前真实值。
        /// </summary>
        private bool AnyPatrollerSenses(Vector2 point) => AnyPatrollerSenses(point, player.IsSneaking);

        /// <summary>
        /// 同上，但**潜行状态由调用方给**。拣落点时必须用「到时候是潜行着的」那一种：
        /// 贴身那一档（不分朝向、1.5 米）只有潜行才免，而拣落点时玩家还站在观察点、根本没按潜行键——
        /// 用当前状态去判，主怪自己的贴身圈会把「它背后 1 米」的每一个候选都判成会被看见
        /// （2026-10-09 实跑：搜索循环空转到超时，就是这么来的）。
        /// </summary>
        private bool AnyPatrollerSenses(Vector2 point, bool sneaking)
        {
            var candidate = new PlayerSnapshot(point, player.Facing, sneaking, player.IsDisguised, player.Health);
            for (int i = 0; i < patrollers.Length; i++)
            {
                MonsterRules rules = patrollers[i];
                if (rules != null && rules.Detects(in candidate))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 朝 <paramref name="target"/> 挪一帧，但**不踩进任何一只巡逻怪的感知**：
        /// 先算这一步的落点（最多探到目标点为止），会被察觉就交给 <see cref="HoldSafely"/> 原地等 / 后退
        /// （怪一转头窗口就开了）；到位（<see cref="ArriveDistance"/> 内）就站住不动。
        /// <para>
        /// 为什么这里挨一下的代价不是 1 点血而是命：被碰到是**击倒**
        /// （`KnockdownRules` 倒地速度倍率 0，怪 1 秒一刀），3 刀就死；死了还会触发镜碎 →
        /// `EncounterStep.End()` → `facts.Clear()`，把这一场的事实全抹掉。
        /// </para>
        /// </summary>
        private void StepSafelyToward(Vector2 target, bool requireCalm = true)
        {
            Vector2 delta = target - player.Position;
            if (delta.magnitude <= ArriveDistance)
            {
                // 已经到位：站住不动（这一步是暗杀站位，不后退——退开就不在暗杀距离里了）。
                Input.ReleaseStick();
                return;
            }

            Vector2 direction = delta.normalized;
            float lookahead = Mathf.Min(SafetyLookahead, delta.magnitude);
            // 落点安全**并且**怪都停稳了才迈步：只看落点挡不住「走到一半它掉头」，见 PatrollersPausedLongEnough。
            // 撤退（requireCalm: false）不吃这一道——已经被看见时该退就退，等停顿只会白挨刀。
            if (!AnyPatrollerSenses(player.Position + direction * lookahead)
                && (!requireCalm || PatrollersPausedLongEnough(CrossingPauseMarginSeconds)))
            {
                Input.SetStick(direction);
                return;
            }

            HoldSafely();
        }

        /// <summary>
        /// 每一只巡逻怪都处于巡逻停顿、且**剩余停顿还够走一段**。
        /// <para>
        /// 为什么要这一道（2026-10-09 修偶发红）：<see cref="StepSafelyToward"/> 的落点判定只看「此刻会不会被察觉」，
        /// 挡不住**怪在巡逻线端点掉头**——它一转身，刚刚还在它背后、安全的玩家就落到它正面；1.6 米内被看见即红区
        /// → 敌对 → 击倒（倒地速度倍率 0）→ 三刀死 → 镜碎 → `EncounterStep.End()` → `facts.Clear()`，检查点全空转。
        /// 实测 `Sneak_BehindEnemy_AllowsAssassination` 三次红一次，每次遥测里都有 `player/dead`。
        /// </para>
        /// <para>
        /// 停顿中的怪**朝向不变**（`MonsterConfig.patrolPauseSeconds` = 5 秒），而横穿观察点到主怪背后那条走廊
        /// （约 2.6 米、潜行 1.5 m/s）只要 ~1.7 秒——所以「等它停稳了再走」是把抽签换成确定性。
        /// 主怪那一边本来就要等它**刚**进停顿才动身（<see cref="WaitForFreshPatrolPause"/>），所以这道闸实际加的是
        /// **第二只巡逻者**的条件。
        /// </para>
        /// </summary>
        private bool PatrollersPausedLongEnough(float seconds)
        {
            // **跳过第 0 只（主怪）**：它是我们要贴近的目标，不是要横穿的障碍——跟在它背后走是安全的，
            // 它什么时候走由 SneakUpBehind 的退让逻辑管（走远了就退回观察点等下一次停顿）。
            // 闸门管的是**别的**巡逻者：横穿它们的巡逻线时，它们必须停稳（见上面那段推导）。
            // 2026-10-09 实测：把主怪也算进来会让窗口变成「两个独立 14 秒周期相位对齐」，
            // 期望等待 ≈ T₁·T₂/重合 ≈ 78 秒 —— 40 秒超时必然经常撞不上（遥测里没有 player/dead，纯粹没动身）。
            for (int i = 1; i < patrollers.Length; i++)
            {
                MonsterRules rules = patrollers[i];
                if (rules == null)
                {
                    continue;
                }

                if (rules.Model.Mode != MonsterMode.PatrolPause || rules.Model.PatrolPauseLeft < seconds)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 原地待命；**当前站的这一格已经被人察觉**（锥子扫过来了）就往背离最近那只怪的方向退半步，
        /// 退不动（退的方向也不安全）才真的站着不动。理由见 <see cref="StepSafelyToward"/> 的文档：
        /// 这里挨一下的代价是击倒 + 三刀死 + 镜碎，退半步比站着挨便宜得多。
        /// </summary>
        private void HoldSafely()
        {
            if (AnyPatrollerSenses(player.Position))
            {
                Vector2 away = player.Position - NearestPatrollerPosition();
                if (away.magnitude > 0.01f && !AnyPatrollerSenses(player.Position + away.normalized * SafetyLookahead))
                {
                    Input.SetStick(away.normalized);
                    return;
                }
            }

            Input.ReleaseStick();
        }

        /// <summary>
        /// 潜行键跟着「离最近那只巡逻怪的距离」走：2 米内按住，走远了松开；**只在状态变化时才动键**
        /// （Hold / Release 各自要等一两帧才生效，逐帧抖根本按不住）。
        /// <para>
        /// 按「**任意一只**」而不是「主怪」是这次修复的一半：贴身那一档（1.5 米、不分朝向）只有潜行才免
        /// （`03_潜行与暗杀.md:121` R17），原来只按「离主怪 2 米」算 —— 穿过第二只巡逻者的巡逻线时玩家是
        /// 站着（没潜行）的，落进它 1.5 米就当场被察觉。
        /// </para>
        /// </summary>
        private IEnumerator SyncSneakKey()
        {
            bool want = NearestPatrollerDistance() <= SneakFromDistance;
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

        /// <summary>松开潜行键，并把逐帧同步的状态一起复位（别让下一次 <see cref="SyncSneakKey"/> 以为还按着）。</summary>
        private IEnumerator ReleaseSneak()
        {
            sneakHeld = false;
            yield return Input.Release(inputService.Actions.Gameplay.Sneak);
        }

        /// <summary>
        /// 处决之后退场：走回 <see cref="PatrolLookout"/>（巡逻线北侧的观察点）。
        /// **不记报告条目**（同 `StrikeMonster` 口径：移动本身不是被测行为，结果由后面的检查点判）。
        /// <para>
        /// 为什么非退不可：处决之后还有 5 条检查点，按 x1 节奏约 12 秒，而玩家一直站在主怪背后 ——
        /// 那里离第二只巡逻者的巡逻线只有 1.6 米，它的视锥来回扫，扫到就是警戒 → 敌对 → 一刀击倒 → 三刀死；
        /// 玩家一死触发镜碎，`MirrorCrackPresenter.Shatter()`（`MirrorCrackPresenter.cs:152`）调
        /// `EncounterStep.End()`，内部 `facts.Clear()` 会把 `stealth.assassinated` 抹掉。
        /// 观察点对那只怪的视锥是够不到的：它横向差 2.6 米，锥子要 |dx| &gt; 3.39 才够得着，而它的巡逻段只有 4 米。
        /// </para>
        /// <para>
        /// 退法是同一套避让（<see cref="StepSafelyToward"/>），所以半路被谁挡住也只是等一会儿；
        /// 超时**不记失败**——这一段不是被测行为，退不回去的后果由后面那些检查点如实反映。
        /// </para>
        /// </summary>
        private IEnumerator LeaveStage()
        {
            if (player == null || player.Health <= 0)
            {
                // 玩家已经倒下：遭遇已经停摆（EncounterStep 停止推进），推摇杆也不会动。
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + LeaveStageTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return SyncSneakKey();
                if ((player.Position - PatrolLookout).magnitude <= LeaveStageArriveDistance)
                {
                    break;
                }

                // 撤退不吃「等怪停稳」那道闸（requireCalm: false）：这一刀已经结算完，先离开巡逻线最要紧。
                StepSafelyToward(PatrolLookout, requireCalm: false);
                yield return null;
            }

            Input.ReleaseStick();
        }

        /// <summary>
        /// 把玩家**直接放到**主怪背后 1 米处，并**先断言那个落点不会被任何一只巡逻怪察觉**。
        /// <para>
        /// <b>为什么第 ③ 条不走 <see cref="SneakUpBehind"/> 那套真走位（2026-10-09 主窗口定，两轮修复都栽在这里）</b>：
        /// 从观察点 (15.5, 7.6) 下到主怪背后**必须横穿 `z=5`**，而第二只巡逻者 `Patrol_B` 的巡逻线
        /// （`x 13.86..17.86`、`z=5`）正好压在这条走廊上、离主怪巡逻线只有 1.6 米；东边又被楼梯占掉
        /// （`x 16.5..19.5`，`DemoScene.cs:81` 记着「5 秒内走不到」）。**没有第二条路**，
        /// 于是「不被察觉地走过去」退化成**两只怪停顿窗口的相位巧合**——那不是确定性，是抽签：
        /// 实测两轮的差别只是死亡时刻（22.8s → 23.9s），签名逐字相同。
        /// </para>
        /// <para>
        /// 挨一下的代价不是掉血而是命：被碰到是**击倒**（倒地速度倍率 0、怪 1 秒一刀），3 刀死；
        /// 一死触发镜碎 → `MirrorCrackPresenter.Shatter()` → `EncounterStep.End()` → `facts.Clear()`，
        /// 后面 5 条检查点全部空转（`stealth.assassinated` 也被抹掉）。
        /// </para>
        /// <para>
        /// <b>走位本身没有失去覆盖</b>：<see cref="Sneak_BehindEnemy_AllowsAssassination"/> 走的就是真走位，
        /// 而且它 PASS（含 `stealth.behind` 与 `AssassinationAllowed`）。本条的题目是 **F 键那条执行链**
        /// （按键 → 交互焦点让位裁决 → 选目标 → 判定 → 结算 → 写事实 + 承载结果），走位不是它要测的东西。
        /// </para>
        /// <para>
        /// 落点仍然**不是随便放的**：① 调用前已等主怪**刚进停顿**（它不动，落点才稳定）；
        /// ② 放过去之后**先按住潜行键并等它生效**（贴身那一档只有潜行才免），
        /// ③ 再断言「所有巡逻怪对这个落点 `Detects` 都为假」——放一个本来就会被看见的位置等于自欺。
        /// ②③ 的顺序不能反：`AnyPatrollerSenses` 读的是玩家**当前**的潜行状态。
        /// </para>
        /// </summary>
        private IEnumerator TakePositionBehindForExecute()
        {
            Input.ReleaseStick();

            // ① 潜行键先按住并**保持**：`PlayerRules.Reset` 每次都会把潜行清成 false（`PlayerRules.cs:61`），
            //    而按键是「按住」语义——下一 tick 的输入采样会把它重新置真。落在主怪背后 1 米时，
            //    贴身那一档（不分朝向、1.5 米）只有潜行才免，所以这一条必须先成立。
            yield return Input.Hold(inputService.Actions.Gameplay.Sneak);
            sneakHeld = true;
            yield return null;
            yield return null;
            Assert.That(player.IsSneaking, Is.True,
                "前置：潜行键要按住（贴身那一档只有潜行才免，见 SyncSneakKey 的注释）");

            // ② 「搜索落点 → 落位 → 复核」三步必须在**同一段安全时刻**里做完，所以合进一个重试循环。
            //    拆成两段会踩一个真实的坑（2026-10-09 实测）：搜索到落点之后再落位，中间隔着几帧，
            //    主怪可能刚好结束停顿、掉头走起来——`Facing` 一转，刚按「背后」算出来的那个点就成了它的
            //    **正面**，复核直接红。任一步不成立就整段重来。
            //    候选也不止正后方一格：主怪走到巡逻线东段（x≈21.86）时，它背后 1 米那个点
            //    （x≈20.86, z=3.4）会落进 `Patrol_B` 朝东的视锥（dx 3.0 / dz 1.6 → 夹角 ≈28° < 37.5°、
            //    距离 ≈3.4 < 橙区 4.5），所以要给「正后 / 偏左 25° / 偏右 25°」三个都仍是背后的候选。
            float deadline = Time.realtimeSinceStartup + ApproachTimeoutSeconds;
            Vector2 spot = monster.Position - monster.Facing * BehindDistance;
            bool placed = false;
            while (!placed && Time.realtimeSinceStartup < deadline)
            {
                if (!TryFindUnseenSpotBehind(out Vector2 candidate))
                {
                    Input.ReleaseStick();
                    yield return null;
                    continue;
                }

                playerRules.Reset(candidate);
                yield return null;
                yield return null;

                if (IsTargetUnaware(candidate))
                {
                    spot = candidate;
                    placed = true;
                }
            }

            Assert.That(placed, Is.True,
                $"放不出一个「主怪背后、且它此刻察觉不到」的位置（上限 {ApproachTimeoutSeconds} 秒）。"
                + "放一个本来就会被看见的位置，这条用例就变成自欺——所以宁可在这里红。");

            // ③ 只要求**主怪**察觉不到 —— 这才是产品对这条链的要求：`ExecutionRules` 的「位置与察觉」
            //    针对的是**目标本身**（`AssassinationInput` 只吃目标的位置 / 朝向 / 是否察觉）；
            //    第二只巡逻者看见玩家属于「玩家会被打」的风险，不是「这一刀下不下去」，所以不在这里断言它
            //    （要断言就得要求「全场几帧内都不看我」，那是抽签）。那个风险由落位后**立刻**按 F
            //    （`PressExecuteWhileBehind` 逐帧按到出结果）与按完就 `LeaveStage` 退场兜住。
            Assert.That(IsTargetUnaware(spot), Is.True,
                "前置：主怪此刻察觉不到玩家（背后 1 米 + 潜行）——察觉到了这一刀会被判「已被察觉」而拒掉");

            Input.ReleaseStick();
            yield return null;
        }

        /// <summary>落点处**主怪**察觉不到玩家（背后 + 潜行）。这是这条链唯一的位置前提。</summary>
        private bool IsTargetUnaware(Vector2 point)
        {
            if (patrollers.Length == 0)
            {
                return true;
            }

            var snapshot = new PlayerSnapshot(point, player.Facing, true, player.IsDisguised, player.Health);
            return !patrollers[0].Detects(in snapshot);
        }

        /// <summary>
        /// 在主怪背后挑一个**不被任何一只巡逻怪察觉**的落脚点：正后方、偏左 25°、偏右 25° 依次试。
        /// 三个都仍是「背后」（落点与怪物朝向的夹角 &gt; 90°），距离都是 <see cref="BehindDistance"/>。
        /// </summary>
        private bool TryFindUnseenSpotBehind(out Vector2 spot)
        {
            float[] offsets = { 0f, 25f, -25f };
            for (int i = 0; i < offsets.Length; i++)
            {
                float radians = offsets[i] * Mathf.Deg2Rad;
                float sin = Mathf.Sin(radians);
                float cos = Mathf.Cos(radians);
                // 「朝向的反方向」绕原点转 offsets[i] 度（标准二维旋转）。
                Vector2 back = -monster.Facing;
                Vector2 direction = new Vector2(back.x * cos - back.y * sin, back.x * sin + back.y * cos);
                Vector2 candidate = monster.Position + direction * BehindDistance;
                if (!AnyPatrollerSenses(candidate, true))
                {
                    spot = candidate;
                    return true;
                }
            }

            spot = monster.Position - monster.Facing * BehindDistance;
            return false;
        }

        /// <summary>
        /// 潜行绕背：逐帧朝「怪物位置 − 朝向 × 1 米」走，每帧记四件事——
        /// ① 有没有出现在背后近距的潜行时刻；② 怪物有没有全程保持未察觉；③ stealth.behind 有没有真过；
        /// ④ AssassinationAllowed 有没有真过。出现 ①②③④ 后再停 0.6 秒给人看，最多 <see cref="ApproachTimeoutSeconds"/> 秒。
        /// <para>
        /// 走位不是「朝主怪背后拉直线」，而是**每一步都问一遍场上所有巡逻怪**（<see cref="StepSafelyToward"/>），
        /// 潜行键离任意一只 2 米内就按（<see cref="SyncSneakKey"/>）。2026-10-09 的失败就是原来只认主怪：
        /// `Patrol_B` 的巡逻线（z=5、x 13.86..17.86）横在观察点与主怪巡逻线之间，玩家没潜行地从它面前走过，
        /// 0.3 秒被察觉，1 秒一刀挨了三刀，而主怪全程没察觉、没掉血。
        /// </para>
        /// </summary>
        private IEnumerator SneakUpBehind()
        {
            closeSeen = false;
            calmSeen = true;
            behindSeen = false;
            allowedSeen = false;
            float closeSince = -1f;
            float deadline = Time.realtimeSinceStartup + ApproachTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return SyncSneakKey();

                Vector2 toPlayer = player.Position - monster.Position;
                if (monster.Mode == MonsterMode.Alert || monster.Mode == MonsterMode.Hostile)
                {
                    calmSeen = false;
                }

                bool behindNow = player.IsSneaking && toPlayer.magnitude <= 1.5f
                                 && Vector2.Dot(monster.Facing, toPlayer) < 0f;
                if (behindNow)
                {
                    if (!closeSeen)
                    {
                        closeSeen = true;
                        closeSince = Time.realtimeSinceStartup;
                    }

                    behindSeen |= step.Facts.IsTrue(StealthFactKeys.Behind);
                    allowedSeen |= IsAssassinationAllowed();
                    // 收工条件里额外要求「它此刻停着」：按 F 那一步要在一个**不动的窗口**里起手，
                    // 否则它一走，「最近一次结果」就会因为距离被拉开而写成 OutOfRange。
                    // 它走开了就继续循环：等它下一次停顿再贴回去（退让见下面的分支）。
                    if (behindSeen && allowedSeen && monster.Mode == MonsterMode.PatrolPause
                        && Time.realtimeSinceStartup - closeSince >= 0.6f)
                    {
                        break;
                    }
                }

                // 它已经走起来了、玩家还没贴到背后：**退回观察点等下一次停顿**，别追也别原地站着。
                //  · 追不上：潜行 1.5 < 巡逻 2.0；追上去还会在它掉头（巡逻线两端）时正好撞进它正前方。
                //  · 站着也危险：观察点与巡逻线之间那段 z≈5~6.5 落在主怪的视野锥里（它朝 ±X 看，
                //    横向 1.6~4 米、纵向 2~3 米正好在锥内），被扫到一下 `calmSeen` 就红了；
                //    观察点对它俩的锥子都够不到（横向差 4.2 / 2.6 米，窄楔够不着）。
                // 退让本身走同一套避让（但**不等停顿**——退开就是为了离开危险区，等只会站着挨），半路被挡住就等；
                // 上限（20 秒）容得下一次完整停顿。
                if (monster.Mode == MonsterMode.PatrolWalk && toPlayer.magnitude > BehindDistance + 0.6f)
                {
                    StepSafelyToward(PatrolLookout, requireCalm: false);
                    yield return null;
                    continue;
                }

                // **下决心前等窗口，一旦动身就别停**（2026-10-09 修偶发红的第二版）：
                // 只在「还站在观察点」时要求怪都停稳（PatrollersPausedLongEnough）；离开观察点之后就只看落点安全。
                // 为什么不能全程等：从观察点下到主怪背后要横穿第二只巡逻者的巡逻线（z=5），一旦玩家已经走到线上而
                // 闸门又关上，他就会**停在全场最危险的那一格**，等怪掉头正好撞上——实测第一版就是这么死的
                // （死亡时刻 23.8s vs 修前 23.9s，签名逐字相同）。停在线上比走过去更糟，所以闸门只设在起点。
                bool atLookout = (player.Position - PatrolLookout).magnitude <= LookoutCommitRadius;
                StepSafelyToward(monster.Position - monster.Facing * BehindDistance, requireCalm: atLookout);
                yield return null;
            }

            Input.ReleaseStick();
        }

        /// <summary>
        /// 保持在怪物背后并按 `Gameplay/Execute`（键鼠 F，走虚拟键盘的真实事件路径），直到**按键那一次**
        /// 有了结果（<c>ExecutionInteractor.LastVerdict</c> 被写下）或超时（6 秒）。
        /// <para>
        /// 与 <see cref="SneakUpBehind"/> 同一套走法：潜行键跟着「离任意一只巡逻怪的距离」同步，
        /// 每帧朝「怪物位置 − 朝向 × 1 米」走且不踩进任何一只的感知（<see cref="StepSafelyToward"/>）。
        /// **只在产品自己的位置判定（<c>IsBehindAndInRange</c>，与写 `stealth.behind` 同一个函数）成立时才按 F**
        /// ——否则按出来的是 not_behind / 超距，测的就不是这一波的东西了。
        /// 反复按是必要的：虚拟键盘的按下事件至少要跨过一个逻辑 tick 才被采样到（`ShowcaseInputDriver` 文件头坑②）。
        /// </para>
        /// <para>
        /// 按键前先读一次 <see cref="ExecutionInteractor.Inspect"/>，把面板会显示的那份判定记下来——
        /// 「面板说能下刀」与「按下去真的能下刀」必须是同一份判定（组件里两边共用同一套选目标规则）。
        /// 这一条仍然是**独立**的判定：位置条件成立时面板照样可能因为目标已察觉 / 物种门槛说不行。
        /// </para>
        /// </summary>
        private IEnumerator PressExecuteWhileBehind()
        {
            executePressed = false;
            executeAllowed = false;
            executeHintShown = false;
            executeBehindHeld = false;
            executeReason = ExecutionReject.None;
            executeFocusEmpty = false;
            IInteractionFocus focus = ResolveService<IInteractionFocus>();
            float deadline = Time.realtimeSinceStartup + ExecuteTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline && !executePressed)
            {
                yield return SyncSneakKey();

                // 按 F 的落点条件用**产品自己的位置判定**（`IsBehindAndInRange` 与写 `stealth.behind`
                // 的是同一个函数）：站在 1.2~1.5 米之间按下去，产品会如实回 OutOfRange——那既不是这一条
                // 要测的东西，又会把「最近一次结果」写成拒绝。
                bool inStrike = kernel.Assassination.IsBehindAndInRange(
                    player.Position, monster.Position, monster.Facing);

                // 先站住（按 F 这一两秒里也别踩进谁的感知），再按 F。
                StepSafelyToward(monster.Position - monster.Facing * BehindDistance);

                if (inStrike)
                {
                    executeBehindHeld = true;
                    executeHintShown |= interactor.Inspect().Allowed;
                    executeFocusEmpty = focus != null && focus.Current == null;
                    yield return Input.Press(inputService.Actions.Gameplay.Execute);
                    if (interactor.LastVerdict.HasValue)
                    {
                        executePressed = true;
                        executeAllowed = interactor.LastVerdict.Value.Allowed;
                        executeReason = interactor.LastVerdict.Value.Reject;
                        break;
                    }
                }

                yield return null;
            }

            Input.ReleaseStick();
        }

        /// <summary>场上那只 ExecutionInteractor 接到的统一交互焦点（私有字段，经反射读：验的是产品接线，不是测试自己装的）。</summary>
        private IInteractionFocus WiredFocus()
        {
            if (interactor == null) return null;
            FieldInfo field = typeof(ExecutionInteractor).GetField("interactionFocus", BindingFlags.Instance | BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(interactor) as IInteractionFocus;
        }

        /// <summary>
        /// 「这一刀能不能下」的 S3 判定：判定器与输入都取正式的——规则来自容器里那只 <see cref="StealthKernel"/>，
        /// 输入按 <c>EncounterStep.SettleStealth</c> 的同一口径填（未察觉 = 不在警戒 / 敌对）。
        /// **只答位置与察觉**；物种门槛与结算在 <see cref="ExecutionInteractor"/> 那一层（第 ③ 条用例走完整链）。
        /// </summary>
        private bool IsAssassinationAllowed()
        {
            bool aware = monster.Mode != MonsterMode.PatrolWalk && monster.Mode != MonsterMode.PatrolPause;
            var input = new AssassinationInput(player.Position, player.Facing, monster.Position, monster.Facing,
                monster.Health > 0, aware, player.IsSneaking);
            return kernel.Assassination.Evaluate(in input).Allowed;
        }
    }
}
