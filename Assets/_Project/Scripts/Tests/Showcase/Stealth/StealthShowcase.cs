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
// 已知边界（本波**不**就地改，只把它演出来并写进报告）：遮挡目前只写 stealth.hidden / stealth.cover 这些
//   **事实键**，不喂 MonsterRules.Sense——怪物照样看得见你、照样追过来。用例①的最后一个检查点专门钉这条边界，
//   让它在报告里显式出现，而不是让人以为「躲到掩体后面怪物就看不见了」。
// ③ ExecuteKey_BehindEnemy_ExecutesTheTarget（2026-10-07 接线那一波新增）：潜行绕到巡逻怪背后，
//   按 `Gameplay/Execute`（键鼠 F）→ 目标被处决。这一条演的是接线之后的完整链：
//   场景里那只 `ExecutionInteractor` 被 `MonsterEncounterState.BindExecution` 接上玩家 / 怪物规则 /
//   事实写入点 / 判定内核 / 遭遇结算 / 埋点，按 F 才真的会结算（接线前那个组件没有任何生产调用方）。
//   断言口径：**怪死了 + `stealth.assassinated` 写进正式流程那份事实集 + 按键那一次的结果是「允许」**。
//   埋点（`stealth/stealth_executed`）由 EditMode 的 `ExecutionSceneWiringTests` 用收集型 sink 断言
//   ——回放侧拿不到 sink，不在这里假装看到。
//   内容前提：`Boot.unity` 的 `MonsterInstaller.kindId = 1003`（市令：killable=false + defeat_method=暗杀）。
//   回放**不读那个序列化字段**：它只按「场上这只怪能处决」来演，premise 不成立时失败信息里会带上拒绝原因。
// 不包括：把这一刀做进确定性内核（处决是实时输入触发的交互，PRP §2.4 明确不进 `InputCommand`）。
using System.Collections;
using Game.Core.Input;
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

        /// <summary>离怪物多近时开始潜行（米）：潜行免的是近身察觉 1.5（`03_潜行与暗杀.md:121` R17）。</summary>
        private const float SneakFromDistance = 2f;

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

            // ─────────────────────────────────────────────────────────────────────────
            yield return Check("已知边界：怪物照样追过来（遮挡目前只写 stealth.* 事实，不喂 MonsterRules 的察觉）",
                () => monster.Mode == MonsterMode.Alert || monster.Mode == MonsterMode.Hostile, 3f);
            yield return Snapshot("遮挡不挡追击");
        }

        // ───────────────────────── ② 绕到背后就能处决 ─────────────────────────

        [UnityTest]
        public IEnumerator Sneak_BehindEnemy_AllowsAssassination()
        {
            yield return EnterWorld();
            yield return GoToPatrolLookout();

            yield return Step("等巡逻怪走完一段、进入巡逻停顿（停 2 秒、朝向不变，好绕背）", null, 0f);
            yield return WaitUntil("怪物进入巡逻停顿",
                () => monster.Mode == MonsterMode.PatrolPause && monster.PatrolPauseLeft > 1.2f, 15f);

            yield return Step("按住潜行键，绕到怪物背后 1 米内", null, 0f);
            yield return SneakUpBehind();

            yield return Check("背后近距潜行：怪物全程在巡逻、警戒条为 0（没察觉你）",
                () => closeSeen && calmSeen);
            yield return Check("stealth.behind 为真：贴在背后且在暗杀距离（1.2 米）内", () => behindSeen);
            yield return Snapshot("潜行到背后");

            yield return Check("S3 绕背判定为真：AssassinationAllowed（只答位置 + 察觉；F 键处决那层的物种门槛另算）",
                () => allowedSeen);
            yield return Snapshot("可处决");

            yield return Input.Release(inputService.Actions.Gameplay.Sneak);
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

            yield return Step("等巡逻怪走完一段、进入巡逻停顿（停 2 秒、朝向不变，好绕背）", null, 0f);
            yield return WaitUntil("怪物进入巡逻停顿",
                () => monster.Mode == MonsterMode.PatrolPause && monster.PatrolPauseLeft > 1.2f, 15f);

            yield return Step("按住潜行键，绕到怪物背后 1 米内", null, 0f);
            yield return SneakUpBehind();

            yield return Step("在背后近距按 F（Gameplay/Execute）；按键那一次的结果会被记下来", null, 0f);
            yield return PressExecuteWhileBehind();
            yield return Input.Release(inputService.Actions.Gameplay.Sneak);

            yield return Check("按 F 之前面板提示「可处决」（Inspect 报的就是按键会用的那份判定）",
                () => executeHintShown);
            yield return Check($"按 F 确实走到了结算：最近一次结果是「允许」"
                               + $"（记到的拒绝原因：{executeReason}；位置条件成立过：{executeBehindHeld}）",
                () => executePressed && executeAllowed);
            yield return Check("目标被处决：怪生命归零、进入 Dead", () => monster.Health == 0 && monster.Mode == MonsterMode.Dead);
            yield return Check("stealth.assassinated 写进了正式流程那份事实集（命中即写，持久）",
                () => step.Facts.IsTrue(StealthFactKeys.Assassinated));
            yield return Snapshot("按 F 处决");

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
                return inputService != null && inputService.Actions != null
                       && player != null && monster != null && step != null && kernel != null && view != null;
            }, "进世界后容器里取不到 PlayerModel / MonsterModel / IInputService / EncounterStep / StealthKernel，"
               + "或场上找不到 EncounterSceneView（检查 Boot 的 StealthInstaller、MonsterInstaller 与 SampleScene 的 Encounter 物体）");

            if (step.Sight != kernel.Sight)
            {
                Assert.Fail("EncounterStep.Sight 不是潜行内核那只：UseStealth 没生效，遮挡体喂了也不参与结算");
            }

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

        /// <summary>
        /// 潜行绕背：离怪物 2 米内按住潜行，逐帧朝「怪物位置 − 朝向 × 1 米」走；每帧记四件事——
        /// ① 有没有出现在背后近距的潜行时刻；② 怪物有没有全程保持未察觉；③ stealth.behind 有没有真过；
        /// ④ AssassinationAllowed 有没有真过。出现 ①②③④ 后再停 0.6 秒给人看，最多 5 秒。
        /// </summary>
        private IEnumerator SneakUpBehind()
        {
            closeSeen = false;
            calmSeen = true;
            behindSeen = false;
            allowedSeen = false;
            bool sneaking = false;
            float closeSince = -1f;
            // 观察点定到 (15.5, 7.6) 后，到怪背后最远约 12.5 米，潜行 1.5 m/s 要 8.3 秒；
            // 原来 5 秒的窗口会在「怪正好走到远端」时误判成失败，故放到 14 秒。
            float deadline = Time.realtimeSinceStartup + 14f;
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
                    if (behindSeen && allowedSeen && Time.realtimeSinceStartup - closeSince >= 0.6f)
                    {
                        break;
                    }
                }

                yield return null;
            }

            Input.ReleaseStick();
        }

        /// <summary>
        /// 保持在怪物背后并按 `Gameplay/Execute`（键鼠 F，走虚拟键盘的真实事件路径），直到**按键那一次**
        /// 有了结果（<c>ExecutionInteractor.LastVerdict</c> 被写下）或超时（6 秒）。
        /// <para>
        /// 与 <see cref="SneakUpBehind"/> 同一套走法：潜行键已经按住，每帧朝「怪物位置 − 朝向 × 1 米」走。
        /// **只在确实处在背后近距时才按 F**——否则按出来的是 not_behind，测的就不是这一波的东西了。
        /// 反复按是必要的：虚拟键盘的按下事件至少要跨过一个逻辑 tick 才被采样到（`ShowcaseInputDriver` 文件头坑②）。
        /// </para>
        /// <para>
        /// 按键前先读一次 <see cref="ExecutionInteractor.Inspect"/>，把面板会显示的那份判定记下来——
        /// 「面板说能下刀」与「按下去真的能下刀」必须是同一份判定（组件里两边共用同一套选目标规则）。
        /// </para>
        /// </summary>
        private IEnumerator PressExecuteWhileBehind()
        {
            executePressed = false;
            executeAllowed = false;
            executeHintShown = false;
            executeBehindHeld = false;
            executeReason = ExecutionReject.None;
            float deadline = Time.realtimeSinceStartup + ExecuteTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline && !executePressed)
            {
                Vector2 toPlayer = player.Position - monster.Position;
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

                bool behindNow = player.IsSneaking && toPlayer.magnitude <= 1.5f
                                 && Vector2.Dot(monster.Facing, toPlayer) < 0f;
                if (behindNow)
                {
                    executeBehindHeld = true;
                    executeHintShown |= interactor.Inspect().Allowed;
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
