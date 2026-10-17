// 职责：钉住「实时遭遇打赢 / 被击倒 → 回写剧情」这条链（roadmap C5 剩下的那一半，PRP/battle-to-narrative）：
//   空 payload 的 Battle 阶段（活遭遇档）由 EncounterBattleBridge 认领 → StartBattle 把这一场标成「一仗」
//   → 遭遇结算出的 BattleResult 被取走（同一 tick，见下面「取走」那几条）→ 用事件里的三项身份回写剧情。
//   覆盖：Victory → cleared；被击倒 → Downed（阶段声明了它）；demo 形状（对白 → 空 payload 战斗档 → 处决 → cleared）；
//   非空 payload 的 BOSS 战一个字都不许被碰；阶段没声明结果码 → 拒绝、阶段原地不动；旧代数的结果 → 安静丢掉、不抛；
//   Aborted（收服）只消费不回写；End()/Begin() 之后结果不丢（镜碎那条时序）；容器级接线（MonsterInstaller 装出来的桥）。
// 为什么新建：`EncounterBattleSettlementTests` 钉的是**战斗侧承载结果**这一半，`NarrativeBattleRejectionTests` 钉的是
//   **剧情侧拒绝路径**那一半；两端之间的「谁在什么时候取走、用哪套身份回写」此前一条用例都没有——
//   那一层坏掉的表现是「剧情永远停在 Battle 阶段」，编译不报、单测不报、回放里看不出（同 §1.11 ② 的教训）。
// 夹具口径与 NarrativeBattleStageTests / NarrativeBattleRejectionTests 一致（真表 + 真 NarrativeService + 假 UI/时钟），
//   容器那两条与 MonsterDropSinkWiringTests 一致（按 Boot 的接法挂 MonsterInstaller，SerializedObject 写私有配置字段）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Boot;
using Game.Core.Config;
using Game.Core.Events;
using Game.Core.Input;
using Game.Core.Platform;
using Game.Core.Replay;
using Game.Core.Save;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Dialogue;
using Game.Interaction;
using Game.Mirror;
using Game.Monster;
using Game.Narrative;
using Game.Player;
using Game.Quest;
using Game.Session;
using Game.Tests.EditMode.Core;
using Game.Tests.EditMode.Telemetry;
using MessagePipe;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;

namespace Game.Tests.EditMode.Monster
{
    /// <summary>
    /// 实时遭遇结果 → 剧情那条链的用例。
    /// <para>
    /// <b>夹具为什么这么写（2026-10-09 第一版踩过的坑，别再改回去）</b>：`NarrativeService.InitializeAsync`
    /// 会**扫全场**把每一个 `NarrativeTrigger` 注册进它自己的 `NarrativeConditionSource`（`NarrativeService.cs` 的
    /// `FindObjectsByType` 那两行），而 `NarrativeConditionSource.Register` 对「同一个 id 的另一个活目标」直接抛
    /// 「重复叙事目标」（`NarrativeConditionSource.cs` 的 `Register`）。第一版给所有 harness 用了**同一个常量 id**，
    /// 于是：本夹具的容器用例建第二条 harness 时，它的扫描先把第一条 harness 的目标注册进去，紧接着它自己的
    /// `Configure` 又要注册同 id 的另一个实例 → SetUp/用例体当场抛；而抛点在「物体已建、harness 还没返回」之间，
    /// 那两个物体再也没人清理，**两条同 id 的目标就永久留在场景里**，之后每一条用例（含
    /// `NarrativeBattleStageTests` / `NarrativeBattleRejectionTests`）的 `InitializeAsync` 扫描都会撞上它们。
    /// 所以这里有三条硬纪律：① **id 每个 harness 唯一**（两份同 id 目标在结构上不可能出现）；
    /// ② 物体**先登记进静态表再 Configure**（Configure 抛了也留得下清理线索）；③ `SetUp` 第一件事就是扫掉上一轮可能
    /// 留下的物体——SetUp 抛异常时 NUnit **不会**跑 TearDown，只靠 TearDown 兜不住。
    /// </para>
    /// </summary>
    public sealed class EncounterBattleBridgeTests
    {
        /// <summary>剧情目标 id 的前缀；真正的 id 每个 harness 拼一段 Guid，理由见类注释 ①。</summary>
        private const string TargetIdPrefix = "live_encounter_boss_";

        /// <summary>本夹具造的叙事目标物体名（清扫上一个进程可能留下的同类物体时按它认，见 `SweepLeakedTargets`）。</summary>
        private const string TargetObjectName = "EncounterBattleBridgeTestTarget";

        /// <summary>本夹具造过、可能还留在场景里的目标物体（静态：跨用例兜底清理由 `SetUp` 做）。</summary>
        private static readonly List<GameObject> sceneTargets = new List<GameObject>();

        /// <summary>`sample_battle_variants` 的入口就是 `fight`（空 payload、声明了四种结果）——「被击倒」那条路的现成素材。</summary>
        private const string VariantsStory = "sample_battle_variants";

        /// <summary>`sample_battle`：brief（对白）→ fight（空 payload，**只声明 Victory**）→ cleared。拒绝路径的现成素材。</summary>
        private const string DemoStory = "sample_battle";

        /// <summary>
        /// `<c>sample_battle_drill</c>`：demo 场景里 `Npc_BattleDrill`（「较量」）真正用的那一份——与 `sample_battle`
        /// 同形，但 fight **同时声明 `Victory` 与 `Downed`**（2026-10-10：活遭遇档按叙事指南只需声明它可能产出的结果码，
        /// 而 demo 里玩家站在巡逻线边上，被击倒才是常态；只声明 Victory 会让这一局卡在战斗阶段）。
        /// </summary>
        private const string DrillStory = "sample_battle_drill";

        private readonly List<NarrativeHarness> harnesses = new List<NarrativeHarness>();
        private PlayerConfig playerConfig;
        private MonsterConfig monsterConfig;
        private SimulationConfig simulationConfig;
        private RecordingTelemetrySink sink;
        private TelemetryService telemetry;
        private NarrativeHarness main;
        private PlayerRules player;
        private MonsterRules monster;
        private EncounterStep step;
        private EncounterBattleBridge bridge;
        private GameObject host;
        private IObjectResolver container;

        [SetUp]
        public void SetUp()
        {
            // 第一件事：扫掉上一条用例可能留在场景里的目标物体（SetUp 抛异常时 NUnit 不跑 TearDown）。
            SweepLeakedTargets();
            playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
            monsterConfig = ScriptableObject.CreateInstance<MonsterConfig>();
            simulationConfig = ScriptableObject.CreateInstance<SimulationConfig>();
            sink = new RecordingTelemetrySink();
            telemetry = new TelemetryService(TelemetryOptions.Default, new FakeTelemetryClock(), sink);

            main = NewHarness();
            player = new PlayerRules(playerConfig, new PlayerModel(), NullTelemetryScope.Instance);
            monster = new MonsterRules(monsterConfig, new MonsterModel(), new RandomService(3ul), NullTelemetryScope.Instance);
            step = new EncounterStep(player, monster);
            step.Begin(Vector2.zero, new[] { new Vector2(10f, 0f), new Vector2(20f, 0f) });
            bridge = new EncounterBattleBridge(step);
            bridge.UseTelemetry(telemetry.Scope("monster"));
            bridge.Bind(main.Narrative, main.Stages);
        }

        [TearDown]
        public void TearDown()
        {
            container?.Dispose();
            container = null;
            if (host != null)
            {
                UnityEngine.Object.DestroyImmediate(host);
                host = null;
            }

            for (int i = harnesses.Count - 1; i >= 0; i--) harnesses[i].Dispose();
            harnesses.Clear();
            main = null;
            // 兜底：即使某个 harness 的 Dispose 半路抛了，场景也要干净（id 唯一 + 静态表两道保险，见类注释）。
            SweepLeakedTargets();
            bridge?.Dispose();
            bridge = null;
            telemetry?.Dispose();
            telemetry = null;
            Destroy(playerConfig);
            Destroy(monsterConfig);
            Destroy(simulationConfig);
            playerConfig = null;
            monsterConfig = null;
            simulationConfig = null;
        }

        // ────────────────────── 正向：空 payload 阶段 + 遭遇结果 → 剧情推进 ──────────────────────

        /// <summary>
        /// 活遭遇档 + 打赢 → 剧情推进到 `cleared`（`sample_battle_variants` 的 Victory 出口）。
        /// 顺带钉住两件事：桥给这一场登了记（`EncounterId` = 事件的 activationId，**不是** targetId），
        /// 以及结果被消费掉（`ResultConsumed`——不解冻的话遭遇从结算那一 tick 起就不再推进）。
        /// </summary>
        [Test]
        public void LiveEncounterVictory_AdvancesTheNarrativeToCleared()
        {
            BattleStageEnteredEvent stage = ReachLiveEncounter();

            Tick();
            Assert.That(step.EncounterId, Is.EqualTo(stage.ActivationId), "桥把这一场标成了「一仗」");
            Assert.That(step.ActivationId, Is.EqualTo(stage.ActivationId), "两个参数都用事件的 activationId");

            KillMonsterByDamage();
            Tick();
            Assert.That(step.BattleSettlement.HasValue, Is.True);
            Assert.That(step.BattleSettlement.Value.ExitKey, Is.EqualTo("Victory"));
            Assert.That(step.ResultConsumed, Is.True, "桥在同一 tick 里取走并消费");

            bridge.Tick(); // 回写（正式流程由 ITickable 每渲染帧驱动）

            Assert.That(main.Narrative.Capture().Current, Is.Null, "cleared 是 End：整段走完");
            Assert.That(main.Narrative.Capture().StoryFlags, Has.Member("world.samplebattle.cleared"));
            Assert.That(HasEvent("monster/battle_result_forwarded"), Is.True);
        }

        /// <summary>被击倒 → 用 `Downed` 回写（阶段声明了 Downed 时成功，走 `downed` 这条 End 出口）。</summary>
        [Test]
        public void LiveEncounterDowned_WritesBackDownedAndAdvances()
        {
            ReachLiveEncounter();
            Tick();

            KillPlayer();
            Tick();

            Assert.That(step.PendingResult, Is.EqualTo(EncounterStep.Result.Defeat));
            Assert.That(step.BattleSettlement.HasValue, Is.True);
            Assert.That(step.BattleSettlement.Value.Kind, Is.EqualTo(BattleOutcome.Downed));
            Assert.That(step.ResultConsumed, Is.True);

            bridge.Tick();

            Assert.That(main.Narrative.Capture().Current, Is.Null);
            Assert.That(main.Narrative.Capture().Outcome, Is.EqualTo("Downed"));
            Assert.That(main.Narrative.Capture().StoryFlags, Has.Member("world.samplebattle.downed"));
        }

        /// <summary>
        /// demo 形状（主窗口 2026-10-09 说的那条路）：对白 → 空 payload 的战斗档 → **处决掉那只怪** → 剧情自己走到 cleared。
        /// 处决这一支刻意按 `ExecutionResolver` 的真实顺序来：先让目标死亡，再由它调一次 `SettleBattle`（此时 `PendingResult` 还是 None），
        /// 结算的 `PendingResult` 要到下一 tick 才由 `EncounterStep.Step` 补上——桥必须认得出这一种。
        /// </summary>
        [Test]
        public void DemoShape_ExecuteTheMonsterAfterDialogue_ReachesCleared()
        {
            BattleStageEnteredEvent stage = ReachVictoryOnlyLiveEncounter();
            Assert.That(stage.Payload, Is.Empty, "`sample_battle.fight` 的 payload 为空 = 活遭遇档");
            Tick();

            Assert.That(monster.ApplyExecution(), Is.True, "处决：killable=false 的怪唯一的死法");
            Assert.That(step.SettleBattle(new BattleResult(BattleOutcome.Victory)), Is.True,
                "ExecutionResolver 在处决命中那一刻就承载结果（PendingResult 还没有）");

            Tick(); // EncounterStep 这一 tick 才把 PendingResult 补成 Victory，桥当场取走

            Assert.That(step.PendingResult, Is.EqualTo(EncounterStep.Result.Victory));
            Assert.That(step.ResultConsumed, Is.True);

            bridge.Tick();

            Assert.That(main.Narrative.Capture().Current, Is.Null);
            Assert.That(main.Narrative.Capture().StoryFlags, Has.Member("world.samplebattle.cleared"));
        }

        /// <summary>
        /// demo 场景「较量」那一份内容（`sample_battle_drill`）的**被击倒**出口：对白 → 空 payload 战斗档 → 玩家阵亡
        /// → 剧情走到 `downed`、写 `world.samplebattle.downed`。
        /// <para>
        /// 为什么这条不是重复：`sample_battle_variants` 证明的是**桥**认得出 Downed，这一条证明的是**demo 用的内容**
        /// 真的声明了它——2026-10-10 00:52 那一局遥测里 `battle_result_rejected {result: Downed, activation: 2}` 就是
        /// 「机制通了、内容没声明」的现场，表现是剧情永远停在战斗阶段（再按 E 只是把对白重放一遍）。
        /// </para>
        /// </summary>
        [Test]
        public void DrillStoryDowned_AdvancesToTheDownedEnd()
        {
            BattleStageEnteredEvent stage = ReachDialogueGatedFight(DrillStory, "brief");
            Assert.That(stage.Payload, Is.Empty, "`sample_battle_drill.fight` 的 payload 为空 = 活遭遇档");
            Tick();

            KillPlayer();
            Tick();

            Assert.That(step.BattleSettlement.Value.Kind, Is.EqualTo(BattleOutcome.Downed));

            bridge.Tick();

            Assert.That(main.Narrative.Capture().Current, Is.Null, "downed 是 End：整段走完，再按 E 能重开一局");
            Assert.That(main.Narrative.Capture().Outcome, Is.EqualTo("Downed"));
            Assert.That(main.Narrative.Capture().StoryFlags, Has.Member("world.samplebattle.downed"));
            Assert.That(HasEvent("narrative/battle_result_rejected"), Is.False, "demo 内容声明了 Downed，不许再走拒绝那条路");
        }

        /// <summary>
        /// 实时遭遇升级回合制（PRP/combat-iteration D5）打赢主怪 → 遭遇当场判 Victory → 桥**原样**在下一 tick 取走、回写 cleared。
        /// 钉的是「桥不改也接得住升级那一路」：完成升级发生在 tick 之外（战斗收场的黑幕下），结果照旧经 `BattleSettlement` 交给桥。
        /// </summary>
        [Test]
        public void EscalationVictoryOnPrimary_IsTakenAndForwardedByTheBridge()
        {
            var serialized = new SerializedObject(monsterConfig);
            // 三个开关全开：登记那一 tick 里怪可能已贴身察觉到玩家，这一刀落成偷袭还是正面攻击都不影响本条要钉的事。
            serialized.FindProperty("escalateOnSneakStrike").boolValue = true;
            serialized.FindProperty("escalateOnFrontalStrike").boolValue = true;
            serialized.FindProperty("escalateWhenStruck").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            step.BindEscalation(new AcceptingEscalationPort());
            step.Begin(Vector2.zero, new[] { new Vector2(0.5f, 0f), new Vector2(10f, 0f) }); // 怪在玩家右侧 0.5 米、背对玩家
            BattleStageEnteredEvent stage = ReachLiveEncounter();
            Tick();
            Assert.That(step.EncounterId, Is.EqualTo(stage.ActivationId), "前提：桥给这一场登了记");

            SimulationContext attack = NewContext(InputCommand.ButtonAttack);
            step.Step(in attack);
            bridge.Step(in attack);
            Assert.That(step.IsEscalating, Is.True, "前提：玩家这一刀升级成了回合制");

            Assert.That(step.CompleteEscalation(true, 10, 10), Is.True); // 战斗侧在收场黑幕下交回「打赢」
            Assert.That(step.PendingResult, Is.EqualTo(EncounterStep.Result.Victory));

            Tick();
            Assert.That(step.ResultConsumed, Is.True, "桥照常取走");
            bridge.Tick();

            Assert.That(main.Narrative.Capture().Current, Is.Null);
            Assert.That(main.Narrative.Capture().StoryFlags, Has.Member("world.samplebattle.cleared"));
        }

        // ────────────────────── 负对照 ──────────────────────

        /// <summary>
        /// 非空 payload 的战斗阶段**不许**被这段逻辑碰：那是 BOSS 战，归 `Game.Battle` 的 `BattleFlow`。
        /// 「没被碰」的判据是双向的——桥不登记（`EncounterId` 恒为 0 → 遭遇侧根本不结算），剧情也停在原地。
        /// </summary>
        [Test]
        public void BossStageWithPayload_IsNotTouchedByTheBridge()
        {
            BattleStageEnteredEvent stage = ReachBossFight();
            Assert.That(stage.Payload, Is.EqualTo("sample_boss"));

            Tick();
            Assert.That(step.EncounterId, Is.Zero, "BOSS 战不归桥登记");

            KillMonsterByDamage();
            Tick();
            Assert.That(step.PendingResult, Is.EqualTo(EncounterStep.Result.None), "没登记 → 遭遇侧不结算");
            Assert.That(step.BattleSettlement.HasValue, Is.False);

            bridge.Tick();
            Assert.That(main.Narrative.Capture().Current.StageId, Is.EqualTo("fight"), "回写要由 BattleFlow 发起，桥不许代劳");
        }

        /// <summary>
        /// 阶段没声明该结果码 → **不许推进**。现成素材是 `sample_battle.fight`（只声明 `Victory`）而玩家被击倒：
        /// `CompleteBattleAsync` 返回 false 并自己埋 `battle_result_rejected`，桥把它当**正常拒绝**（不抛、不重试）。
        /// </summary>
        [Test]
        public void DownedOnAVictoryOnlyStage_IsRejectedAndTheStageStays()
        {
            ReachVictoryOnlyLiveEncounter();
            Tick();

            KillPlayer();
            Assert.DoesNotThrow(() => Tick(), "正常拒绝不许炸");
            Assert.DoesNotThrow(() => bridge.Tick(), "回写是异步的：被拒也不抛");

            Assert.That(main.Narrative.Capture().Current.StageId, Is.EqualTo("fight"), "内容没声明 Downed，阶段原地不动");
            Assert.That(step.ResultConsumed, Is.True, "结果照样被消费掉：遭遇不许一直冻着");
            Assert.That(HasEvent("narrative/battle_result_rejected"), Is.True, "拒绝路径必须留痕（PRP §2.6）");
            Assert.That(HasEvent("monster/battle_result_forwarded"), Is.True);
        }

        /// <summary>
        /// 身份对不上（读档换代之后回来的旧结果）→ **安静丢掉、不抛**，阶段原地不动。
        /// 走的是真路径：先取走结果，再让 Session 重载一次（Generation++），最后才回写。
        /// </summary>
        [Test]
        public void ResultFromAStaleGeneration_IsDroppedQuietly()
        {
            BattleStageEnteredEvent stage = ReachLiveEncounter();
            Tick();
            KillMonsterByDamage();
            Tick();
            Assert.That(step.ResultConsumed, Is.True, "前置：结果已经取到桥手里（带的是旧代数）");

            main.Saves.Commit(main.Saves.Capture());
            main.Sessions.Publish(new SessionStartedEvent(2, false));
            Assert.That(main.Narrative.Generation, Is.GreaterThan(stage.Generation), "前置：读档换代");
            Assert.That(main.Narrative.Capture().Current.StageId, Is.EqualTo("fight"), "前置：剧情还在同一个战斗阶段");

            Assert.DoesNotThrow(() => bridge.Tick());

            Assert.That(main.Narrative.Capture().Current.StageId, Is.EqualTo("fight"), "旧结果不许推进阶段");
            Assert.That(HasEvent("narrative/battle_result_rejected"), Is.True);
        }

        /// <summary>
        /// 收服收尾（`Aborted`）**不是战斗结果**：桥只把它消费掉（解开冻结），不回写、不抛。
        /// 负对照相对上面几条：同样是「有 PendingResult」，结果码为空时剧情一个字都不该收到。
        /// </summary>
        [Test]
        public void AbortedBattle_IsConsumedWithoutWriteBack()
        {
            ReachLiveEncounter();
            Tick();

            step.AbortBattle();
            Tick();

            Assert.That(step.PendingResult, Is.EqualTo(EncounterStep.Result.Aborted));
            Assert.That(step.ResultConsumed, Is.True);
            Assert.That(step.BattleSettlement.HasValue, Is.False);

            bridge.Tick();

            Assert.That(main.Narrative.Capture().Current.StageId, Is.EqualTo("fight"));
            Assert.That(HasEvent("narrative/battle_result_applied"), Is.False);
        }

        // ────────────────────── 镜碎那条时序（取走 vs End / Begin） ──────────────────────

        /// <summary>
        /// 玩家一死会触发镜碎：`MirrorCrackPresenter.Tick`（渲染帧入口点，排在整段逻辑 tick 之后）读到第三道裂痕就
        /// `step.End()`。**End() 并不清结果**（只置 `IsActive = false`），但桥的取走仍然发生在同一个 tick 里——
        /// 这一条钉住「End 之后回写照样发得出去」。
        /// </summary>
        [Test]
        public void MirrorEndAfterTheTake_LosesNothing()
        {
            ReachLiveEncounter();
            Tick();

            KillPlayer();
            Tick(); // 结算 + 取走（同一 tick）

            step.End(); // 镜碎（MirrorCrackPresenter.Shatter()）

            Assert.That(step.PendingResult, Is.EqualTo(EncounterStep.Result.Defeat), "End() 不清结果：这是核实过的前提");
            Assert.That(step.ResultConsumed, Is.True, "已经消费掉了，遭遇不会停在冻结态");

            bridge.Tick();

            Assert.That(main.Narrative.Capture().Outcome, Is.EqualTo("Downed"));
        }

        /// <summary>
        /// 真正会抹掉结果的是**下一场遭遇的 `Begin()`**（镜碎页交回后重载场景）。结果已经抄在桥自己身上，
        /// Begin 抹不掉它——这一条就是「取走放在结算同一个 tick」的理由本身。
        /// </summary>        [Test]
        public void NextEncounterBeginAfterTheTake_LosesNothing()
        {
            ReachLiveEncounter();
            Tick();

            KillPlayer();
            Tick();
            Assert.That(step.ResultConsumed, Is.True);

            step.Begin(Vector2.zero, new[] { new Vector2(10f, 0f), new Vector2(20f, 0f) }); // 重进遭遇
            Assert.That(step.BattleSettlement.HasValue, Is.False, "Begin 把 EncounterStep 里的痕迹抹干净了");

            bridge.Tick();

            Assert.That(main.Narrative.Capture().Outcome, Is.EqualTo("Downed"), "结果已经取到桥手里，Begin 抹不掉");
        }

        /// <summary>
        /// 处决那条路的窄窗口：`ExecutionResolver` 在**渲染帧**就承载了结果，而 `PendingResult` 要等下一 tick 的
        /// `EncounterStep.Step` 才补上——如果遭遇在这之前就停了（`End`），结果只挂在 `BattleSettlement` 上。
        /// 桥按「`EncounterId` 就是本场 activationId」精确认领它，照样交出去（不是凭「有个结果就写」）。
        /// </summary>
        [Test]
        public void SettlementStillTakenAfterTheEncounterStopped()
        {
            ReachLiveEncounter();
            Tick(); // 登记

            Assert.That(monster.ApplyExecution(), Is.True);
            Assert.That(step.SettleBattle(new BattleResult(BattleOutcome.Victory)), Is.True);
            step.End(); // 场景先停了，EncounterStep.Step 还没轮到把 PendingResult 补成 Victory

            Assert.That(step.PendingResult, Is.EqualTo(EncounterStep.Result.None), "前置：PendingResult 还停在 None");

            bridge.Step(NewContext());

            // `ConsumeResult` 的契约是「有终局结果可标记」才置位：这条路上 `PendingResult` 还是 None，它返回 false，
            // 所以 `ResultConsumed` 保持原样——**这不代表结果丢了**，它已经抄进桥自己的字段；
            // 下面那两条（剧情真的走完）才是「取到了」的证据。
            Assert.That(step.ResultConsumed, Is.False, "EncounterStep 侧没有可标记的终局（PendingResult == None）");

            bridge.Tick();

            Assert.That(main.Narrative.Capture().Current, Is.Null);
            Assert.That(main.Narrative.Capture().StoryFlags, Has.Member("world.samplebattle.cleared"));
        }

        /// <summary>
        /// **同一份结算只许交一遍**。上面那条路上 `EncounterStep` 侧没有终局可标记（`PendingResult == None`，
        /// `ConsumeResult` 返回 false），那份结算会一直挂在 `BattleSettlement` 上；此时再来一条**同身份**的
        /// 阶段通知（真实里的「重试 / 读档重发」就是同身份重发），不许把它再交一次。
        /// </summary>
        [Test]
        public void SameSettlement_IsForwardedOnlyOnce()
        {
            ReachLiveEncounter();
            Tick();
            Assert.That(monster.ApplyExecution(), Is.True);
            Assert.That(step.SettleBattle(new BattleResult(BattleOutcome.Victory)), Is.True);
            step.End();
            bridge.Step(NewContext());
            bridge.Tick();
            Assert.That(main.Narrative.Capture().Current, Is.Null, "前置：结果已经交出去，剧情走到 cleared");
            int forwarded = CountEvents("monster/battle_result_forwarded");

            main.Stages.Publish(main.Stages.Published[0]); // 同一条事件原样重发（= 重试 / 读档重发）
            bridge.Step(NewContext());
            bridge.Tick();

            Assert.That(CountEvents("monster/battle_result_forwarded"), Is.EqualTo(forwarded), "同一份结算只许交一遍");
        }

        // ────────────────────── 容器级：MonsterInstaller 真的把桥接上了吗 ──────────────────────

        /// <summary>
        /// 按 Boot 的接法装 `MonsterInstaller`，再端到端跑一遍：容器里的桥订阅得到剧情事件、登记得了这一仗、
        /// **排在 `EncounterStep` 之后**（一个 tick 内结算与取走都发生完），并且剧情侧真的接上了（回写走到 cleared）。
        /// 这一层（注册 + TryResolve + Bind + AddStep 顺序）是「手工 new 一个桥」的用例覆盖不到的。
        /// </summary>
        [Test]
        public void ContainerBuiltFromMonsterInstaller_BindsTheBridgeAndKeepsTheStepOrder()
        {
            NarrativeHarness fresh = NewHarness();
            BuildContainer(fresh);

            EncounterStep containerStep = container.Resolve<EncounterStep>();
            EncounterBattleBridge containerBridge = container.Resolve<EncounterBattleBridge>();
            SimulationRunner runner = container.Resolve<SimulationRunner>();
            Assert.That(containerBridge, Is.Not.Null, "MonsterInstaller 必须注册这个入口点");
            Assert.That(runner.StepCount, Is.EqualTo(2), "步骤表 = EncounterStep + 桥（本容器只装 MonsterInstaller）");

            containerStep.Begin(Vector2.zero, new[] { new Vector2(10f, 0f) });
            Assert.That(fresh.Narrative.StartAsync(VariantsStory, fresh.TargetId).GetAwaiter().GetResult(), Is.True);
            Assert.That(fresh.Stages.Published, Has.Count.EqualTo(1), "停到空 payload 的战斗档上发布一次");

            runner.AdvanceOneTick();
            Assert.That(containerStep.EncounterId, Is.EqualTo(fresh.Stages.Published[0].ActivationId), "桥给这一场登了记");

            container.Resolve<MonsterRules>().ApplyExecution();
            containerStep.SettleBattle(new BattleResult(BattleOutcome.Victory));
            runner.AdvanceOneTick();
            Assert.That(containerStep.ResultConsumed, Is.True,
                "结算与取走必须落在同一个 tick：桥排在 EncounterStep 之后（AddStep 顺序被这一条钉住）");

            containerStep.Begin(Vector2.zero, new[] { new Vector2(10f, 0f) }); // 下一场遭遇抹掉遭遇侧的痕迹
            containerBridge.Tick();                                            // 回写（正式流程由 ITickable 驱动）

            Assert.That(fresh.Narrative.Capture().Current, Is.Null);
            Assert.That(fresh.Narrative.Capture().StoryFlags, Has.Member("world.samplebattle.cleared"));
        }

        /// <summary>
        /// 负对照：容器里**没有**剧情侧（没装 NarrativeInstaller 的测试作用域 / 独立原型场景）时，桥不 Bind——
        /// 不登记、不结算、不回写，也不抛，行为与接线前逐字一致。
        /// </summary>
        [Test]
        public void ContainerWithoutNarrative_LeavesTheBridgeInert()
        {
            BuildContainer(null);

            EncounterStep containerStep = container.Resolve<EncounterStep>();
            SimulationRunner runner = container.Resolve<SimulationRunner>();
            containerStep.Begin(Vector2.zero, new[] { new Vector2(10f, 0f) });
            container.Resolve<MonsterRules>().ApplyExecution();

            Assert.DoesNotThrow(() => runner.AdvanceOneTick(), "没接剧情是合法装配，不许炸");
            Assert.DoesNotThrow(() => container.Resolve<EncounterBattleBridge>().Tick());

            Assert.That(containerStep.EncounterId, Is.Zero, "没人发布战斗阶段 → 桥不登记");
            Assert.That(containerStep.PendingResult, Is.EqualTo(EncounterStep.Result.None), "EncounterId 为 0 → 遭遇侧不结算");
        }

        // ────────────────────── 夹具 ──────────────────────

        /// <summary>推进一个逻辑 tick：先 `EncounterStep`、后桥——与 `MonsterInstaller` 的 AddStep 顺序一致。</summary>
        private void Tick()
        {
            SimulationContext context = NewContext();
            step.Step(in context);
            bridge.Step(in context);
        }

        private static SimulationContext NewContext(uint buttons = 0u)
        {
            var command = new InputCommand(Vector2.zero, Vector2.zero, buttons, Vector2.zero, 0);
            return new SimulationContext(0, 0.1f, in command, new RandomService(3ul));
        }

        /// <summary>
        /// 一律接受、什么都不做的升级端口：本组只关心「升级打赢之后结果怎么走到剧情」，
        /// 战斗侧（BattleFlow 的遭遇版）由 `Battle/BattleFlowEncounterTests` 覆盖。
        /// </summary>
        private sealed class AcceptingEscalationPort : IEncounterEscalationPort
        {
            public bool CanEscalate(int kindId) => true;

            public bool TryEscalate(EncounterStep encounter, in EncounterEscalationRequest request, out string rejectReason)
            {
                rejectReason = null;
                return true;
            }
        }

        /// <summary>`sample_battle_variants` 的入口就是战斗阶段（空 payload）：一步停到活遭遇档上。</summary>
        private BattleStageEnteredEvent ReachLiveEncounter()
        {
            Assert.That(main.Narrative.StartAsync(VariantsStory, main.TargetId).GetAwaiter().GetResult(), Is.True);
            Assert.That(main.Stages.Published, Has.Count.EqualTo(1));
            return main.Stages.Published[0];
        }

        /// <summary>`sample_battle`（只声明 Victory）要走过 brief 那段对白才到 fight：测试 UI 让展示失败，再手工提交出口。</summary>
        private BattleStageEnteredEvent ReachVictoryOnlyLiveEncounter() => ReachDialogueGatedFight(DemoStory, "brief");

        /// <summary>`sample_boss_battle`（payload = sample_boss）：同一个走法，只是 fight 的 payload 非空。</summary>
        private BattleStageEnteredEvent ReachBossFight() => ReachDialogueGatedFight("sample_boss_battle", "taunt");

        private BattleStageEnteredEvent ReachDialogueGatedFight(string story, string dialogueStage)
        {
            main.Ui.Throws = true;
            Assert.That(Error(main.Narrative.StartAsync(story, main.TargetId)), Is.TypeOf<InvalidOperationException>(),
                "前置：停在 " + dialogueStage + " 那段对白上（测试 UI 拉不起来）");
            NarrativeSaveData.Frame frame = main.Narrative.Capture().Current;
            Assert.That(frame.StageId, Is.EqualTo(dialogueStage));

            int before = main.Stages.Published.Count;
            var done = new NarrativeIntent(main.Narrative.Generation, frame.ActivationId, frame.TargetId, frame.ActionRequestId, "Done");
            Assert.That(main.Narrative.SubmitAsync(done).GetAwaiter().GetResult(), Is.True);
            Assert.That(main.Stages.Published, Has.Count.EqualTo(before + 1), "停到战斗档上发布一次");
            return main.Stages.Published[before];
        }

        /// <summary>常规击杀（`MonsterRules.ApplyDamage`）：走上 `EncounterStep.Step` 里那条自动结算。</summary>
        private void KillMonsterByDamage()
        {
            var damage = new DamageIntent(monsterConfig.MaxHealth);
            var attacker = new PlayerSnapshot(Vector2.zero, Vector2.right, false, false, 3, false);
            Assert.That(monster.ApplyDamage(in damage, in attacker), Is.True, "前置：这只怪能被常规伤害打死");
        }

        private void KillPlayer() => player.ApplyDamage(new DamageIntent(playerConfig.MaxHealth));

        /// <summary>
        /// 装一个只含 `MonsterInstaller`（+ 可选的剧情侧）的容器：按 Boot 的接法挂注册器、用 SerializedObject 写私有配置字段。
        /// 框架侧只补 MonsterInstaller 真正解析得到的那几样（同 MonsterDropSinkWiringTests）。
        /// </summary>
        private void BuildContainer(NarrativeHarness narrativeHarness)
        {
            host = new GameObject("GameBootstrap(遭遇桥接线测试)");
            MonsterInstaller installer = host.AddComponent<MonsterInstaller>();
            AttachConfig(installer, monsterConfig);

            var builder = new ContainerBuilder();
            var configService = new StubConfigService(ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes()));
            builder.RegisterInstance(configService).As<IConfigService>();
            builder.RegisterInstance(new YaoCatalog(configService));
            builder.RegisterInstance(new RandomService(11ul)).As<IRandomService>();
            builder.RegisterInstance(TelemetryOptions.Default);
            builder.RegisterInstance(new FakeTelemetryClock()).As<ITelemetryClock>();
            builder.RegisterInstance(new ITelemetrySink[] { new RecordingTelemetrySink() });
            builder.Register<TelemetryService>(Lifetime.Singleton).As<ITelemetryService>();
            builder.RegisterInstance(playerConfig);
            builder.RegisterInstance(new PlayerModel());
            builder.Register(resolver => new PlayerRules(
                    resolver.Resolve<PlayerConfig>(), resolver.Resolve<PlayerModel>(), NullTelemetryScope.Instance),
                Lifetime.Singleton);
            builder.RegisterInstance(simulationConfig);
            builder.RegisterInstance(new LocalClock()).As<IClock>();
            builder.RegisterInstance(new SilentInputSource()).As<IInputSource>();
            builder.Register<SimulationRunner>(Lifetime.Singleton);
            builder.Register<ReplayStateRegistry>(Lifetime.Singleton).As<IReplayStateProvider>();

            // 这一行就是本组容器用例的主角：剧情侧注册成**具体类型 + 两个消息接口**，MonsterInstaller 才 TryResolve 得到。
            if (narrativeHarness != null)
            {
                builder.RegisterInstance(narrativeHarness.Narrative);
                builder.RegisterInstance(narrativeHarness.Stages)
                    .As<ISubscriber<BattleStageEnteredEvent>>()
                    .As<IPublisher<BattleStageEnteredEvent>>();
            }

            installer.Install(builder);
            container = builder.Build();
        }

        /// <summary>按 Boot 的接法给注册器赋配置：私有序列化字段用 SerializedObject 写（不给生产代码开 setter）。</summary>
        private static void AttachConfig(GameplayInstaller installer, UnityEngine.Object config)
        {
            var serialized = new SerializedObject(installer);
            serialized.FindProperty("config").objectReferenceValue = config;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Destroy(UnityEngine.Object target)
        {
            if (target != null) UnityEngine.Object.DestroyImmediate(target);
        }

        /// <summary>
        /// 清掉本夹具可能留在场景里的目标物体。**`SetUp` 与 `TearDown` 都要调**：SetUp 抛异常时 NUnit 不跑 TearDown，
        /// 只靠 TearDown 兜不住；而这些物体一旦留下，会被后面每一次 `NarrativeService.InitializeAsync` 的全场扫描
        /// 注册进各自的事实源（`NarrativeConditionSource.Register` 对同 id 的另一个活目标会抛「重复叙事目标」）。
        /// </summary>
        private static void SweepLeakedTargets()
        {
            // ① 本夹具在这个进程里造过、还没来得及销毁的（静态表是「Create 抛在半路」时的清理线索）。
            for (int i = sceneTargets.Count - 1; i >= 0; i--)
            {
                if (sceneTargets[i] != null) UnityEngine.Object.DestroyImmediate(sceneTargets[i]);
            }

            sceneTargets.Clear();

            // ② 上一个进程 / 上一个编辑器会话留下的**同名**物体：第一版没有唯一 id 时留下的那种。
            //    只按本夹具专用的物体名认（别的测试夹具用别的名字），所以不会误伤别人。
            foreach (NarrativeTrigger trigger in UnityEngine.Object.FindObjectsByType<NarrativeTrigger>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (trigger != null && trigger.gameObject.name == TargetObjectName)
                {
                    UnityEngine.Object.DestroyImmediate(trigger.gameObject);
                }
            }
        }

        private static Exception Error(UniTask<bool> task)
        {
            Assert.That(task.Status.IsCompleted(), Is.True);
            try { task.GetAwaiter().GetResult(); return null; }
            catch (Exception e) { return e; }
        }

        private bool HasEvent(string name)
        {
            foreach (string line in sink.Lines)
                if (line.IndexOf(name, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private int CountEvents(string name)
        {
            int count = 0;
            foreach (string line in sink.Lines)
                if (line.IndexOf(name, StringComparison.Ordinal) >= 0) count++;
            return count;
        }

        private NarrativeHarness NewHarness()
        {
            var harness = NarrativeHarness.Create(telemetry.Scope("narrative"));
            harnesses.Add(harness);
            return harness;
        }

        /// <summary>剧情侧的最小夹具（真表 + 真服务 + 假 UI/时钟），主用例与容器用例各一份。</summary>
        private sealed class NarrativeHarness : IDisposable
        {
            private DialogueConfig dialogueConfig;
            private GameObject targetObject;

            private NarrativeHarness() { }

            public JsonSaveService Saves { get; private set; }
            public Bus<SessionStartedEvent> Sessions { get; private set; }
            public Bus<BattleStageEnteredEvent> Stages { get; private set; }
            public PendingUI Ui { get; private set; }
            public DialogueService Dialogue { get; private set; }
            public NarrativeService Narrative { get; private set; }

            /// <summary>这份 harness 的叙事目标 id（**每个 harness 唯一**，见外层类注释 ①）。</summary>
            public string TargetId { get; private set; }

            public static NarrativeHarness Create(ITelemetryScope narrativeScope)
            {
                var config = new TableConfig();
                var saves = new JsonSaveService(new TestPlatform(), null, null);
                var playerModel = new PlayerModel();
                playerModel.Restore(new PlayerSaveData { Health = 10 });
                var conditions = new NarrativeConditionSource(playerModel, saves);
                var dialogues = new DialogueCatalog(config);
                var dialogueRules = new DialogueRules(new DialogueReadData(), 500, null);
                var ui = new PendingUI();
                var controller = new DialogueController(dialogueRules, dialogues, ui, new UnusedAssets(), new TestClock(), null);
                var dialogueConfig = ScriptableObject.CreateInstance<DialogueConfig>();
                var dialogue = new DialogueService(dialogues, dialogueRules, controller, dialogueConfig, conditions,
                    new Pause(), new NoInput(), ui, null);
                var sessions = new Bus<SessionStartedEvent>();
                var stages = new Bus<BattleStageEnteredEvent>();
                // 玩家锚点来源改为统一交互的登记表（PRP/interaction D7）；不 Start，Actor 为空。
                var narrative = new NarrativeService(new NarrativeCatalog(config, dialogues), dialogue,
                    new InteractionRegistry(), conditions, saves, sessions, new Bus<QuestCompletedEvent>(),
                    new Bus<NarrativeChangedEvent>(), narrativeScope, stages);
                narrative.InitializeAsync(default).GetAwaiter().GetResult();

                // 走过对白阶段的那两条用例要有登记过的叙事目标：`DriveAsync` 找不到目标会抛「叙事目标已失效」。
                // **id 每个 harness 唯一**、物体**先登记进静态表再 Configure**：两条都是防「污染别的用例」的硬纪律，
                // 理由见外层类注释（第一版用同一个常量 id，把三个测试类一起弄红了）。
                // Actor 传 null：本组用例直接调 `NarrativeService.StartAsync`，不走交互距离判定那条路
                //（交互路径由 `NarrativeBattleStageTests` 覆盖），所以不需要玩家锚点。
                var targetObject = new GameObject(TargetObjectName);
                sceneTargets.Add(targetObject);
                string targetId = TargetIdPrefix + Guid.NewGuid().ToString("N").Substring(0, 8);
                targetObject.AddComponent<NarrativeTrigger>().Configure(targetId, "SampleBoss", narrative, null);

                return new NarrativeHarness
                {
                    Saves = saves, Sessions = sessions, Stages = stages, Ui = ui,
                    Dialogue = dialogue, Narrative = narrative, dialogueConfig = dialogueConfig,
                    TargetId = targetId, targetObject = targetObject,
                };
            }

            public void Dispose()
            {
                // 先拆场景物体再拆服务：`DestroyImmediate` 会触发 `NarrativeTrigger.OnDestroy → Unbind`，
                // 那时 `NarrativeService` 还得活着（`Unregister` 走的是它的 Conditions）。
                GameObject target = targetObject;
                targetObject = null;
                if (target != null)
                {
                    sceneTargets.Remove(target);
                    UnityEngine.Object.DestroyImmediate(target);
                }
                Narrative?.Dispose();
                Dialogue?.Dispose();
                if (dialogueConfig != null) UnityEngine.Object.DestroyImmediate(dialogueConfig);
                dialogueConfig = null;
                Narrative = null;
                Dialogue = null;
            }
        }

        private sealed class TableConfig : IConfigService
        {
            public global::cfg.Tables Tables { get; } = ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes());
            public ulong ContentHash => 0;
        }

        private sealed class TestPlatform : IPlatformService
        {
            public PlatformKind Kind => PlatformKind.Standalone;
            public string SaveRoot { get; } = Path.Combine(Path.GetTempPath(), "encounter-battle-bridge-" + Guid.NewGuid().ToString("N"));
            public bool IsTouchPrimary => false;
            public void Vibrate(VibrationKind kind) { }
        }

        /// <summary>同步消息总线（发布即在当前调用栈里通知订阅者，同 MessagePipe），并把发出去的每一条记下来。</summary>
        private sealed class Bus<T> : ISubscriber<T>, IPublisher<T>
        {
            private readonly List<IMessageHandler<T>> handlers = new List<IMessageHandler<T>>();
            public List<T> Published { get; } = new List<T>();

            public IDisposable Subscribe(IMessageHandler<T> handler, params MessageHandlerFilter<T>[] filters)
            {
                handlers.Add(handler);
                return new Subscription(() => handlers.Remove(handler));
            }

            public void Publish(T message)
            {
                Published.Add(message);
                foreach (IMessageHandler<T> handler in handlers.ToArray()) handler.Handle(message);
            }
        }

        private sealed class Subscription : IDisposable
        {
            private Action release;
            public Subscription(Action release) => this.release = release;
            public void Dispose() { release?.Invoke(); release = null; }
        }

        /// <summary>UI 假实现：`Throws` 打开时对白展示直接失败（本组用例借它停在指定阶段上）。</summary>
        private sealed class PendingUI : IUIService, IHudVisibility
        {
            public bool Throws { get; set; }
            public int Opens { get; private set; }
            public bool IsHudHidden => false;
            public void SetHudHidden(bool hidden) { }

            public UniTask<T> OpenAsync<T>(object arg = null, CancellationToken ct = default) where T : UIView
            {
                Opens++;
                if (Throws) return UniTask.FromException<T>(new InvalidOperationException("测试展示失败"));
                var source = new UniTaskCompletionSource<T>();
                ct.Register(() => source.TrySetCanceled(ct));
                return source.Task;
            }

            public UniTask CloseAsync(UIView view, CancellationToken ct = default) => UniTask.CompletedTask;
            public UniTask CloseTopAsync(CancellationToken ct = default) => UniTask.CompletedTask;
            public T Get<T>() where T : UIView => null;
            public void SetLayerVisible(UILayer layer, bool visible) { }
            public bool IsLayerVisible(UILayer layer) => true;
        }

        private sealed class Pause : IWorldPauseService
        {
            public bool IsPaused => false;
            public IDisposable Acquire(object owner) => new Subscription(() => { });
        }

        private sealed class NoInput : IInputService
        {
            public GameInput Actions => null;
            public void EnableMap(string map) { }
            public void DisableMap(string map) { }
        }

        private sealed class TestClock : IClock
        {
            public DateTime UtcNow => DateTime.UnixEpoch;
            public float GameTime => 0;
            public float UnscaledTime => 0;
            public float DeltaTime => 0;
            public float UnscaledDeltaTime => 0;
        }

        private sealed class UnusedAssets : IAssetService
        {
            public UniTask<AssetHandle<T>> LoadAsync<T>(string key, CancellationToken ct = default) where T : UnityEngine.Object => throw new NotSupportedException();
            public UniTask<IReadOnlyList<AssetHandle<T>>> LoadAllAsync<T>(string label, CancellationToken ct = default) where T : UnityEngine.Object => throw new NotSupportedException();
            public UniTask<GameObject> InstantiateAsync(string key, Transform parent = null, CancellationToken ct = default) => throw new NotSupportedException();
            public void ReleaseInstance(GameObject instance) => throw new NotSupportedException();
            public UniTask<SceneHandle> LoadSceneAsync(string key, LoadSceneMode mode, CancellationToken ct = default) => throw new NotSupportedException();
        }

        /// <summary>只递一份现成的 `cfg.Tables`（同 MonsterKindCatalogTests 的最小假实现）。</summary>
        private sealed class StubConfigService : IConfigService
        {
            public StubConfigService(global::cfg.Tables tables) => Tables = tables;
            public global::cfg.Tables Tables { get; }
            public ulong ContentHash => throw new NotSupportedException("假配置服务不提供内容指纹");
        }

        /// <summary>不采样任何输入的输入源：本组用例直接调 Step / AdvanceOneTick，不读设备。</summary>
        private sealed class SilentInputSource : IInputSource
        {
            public void Sample(long tick) { }
            public InputCommand Current => default;
        }
    }
}
