// 职责：钉住遭遇侧的「实时遭遇升级回合制」（PRP/combat-iteration D5，用户 2026-10-14 拍板）：
//   ① 三种触发各产出对应的请求、这一下伤害被吞、进入升级在途；
//   ② 负对照——没装配端口、种类没配数据、开关关、背后处决、操控被驯服的怪、已在途或结果待消费、默认字段的附加怪；
//   ③ 完成升级——胜：无视 killable 让目标死亡、掉落一次、血量按比例写回（取整边界）、主怪判 Victory；负：生命归零判 Downed；
//      重复交回 / 不在途交回都无效并留痕；放弃升级不改血量；
//   ④ 升级在途期间 tick 早退：不再请求、不再挨打、不触发被抓；在途状态不进存档 / 快照；
//   ⑤ 回放期间整条功能关掉（2026-10-16 代码审查后补）：接了播放器且它在放 → 这一下照常扣血、不请求、不埋点；
//      播放器没在放 → 照常升级（负对照）；回放接管时把实时玩法留下的「在途」清掉，tick 才能继续推进。
// 为什么新建：`EncounterChaseCaughtTests` / `EncounterExposureTests` 各管一段 tick 结算，升级是本波新接的第三段；
//   战斗侧那一半（BattleFlow 的遭遇版）在 `Battle/BattleFlowEncounterTests`，这里用假端口只测遭遇侧的判定与结算。
// 夹具：多数用例不接种类表（全局默认：怪物生命 3、伤害 1、攻击距离 0.8、冷却 1 秒；玩家生命 3），假端口对种类 0 也说「能升级」；
//   「打赢掉落 + 无视 killable」那几条接**真表**的种类 1003「市令」（killable = false、drop_items = [1101]，妖物 id 3）。
using System;
using System.Collections.Generic;
using Game.Core.Config;
using Game.Core.Replay;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Mirror;
using Game.Monster;
using Game.Narrative;
using Game.Player;
using Game.Stealth;
using Game.Tests.EditMode.Core;
using Game.Tests.EditMode.Telemetry;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Monster
{
    public sealed class EncounterEscalationTests
    {
        private const string RequestedEvent = "monster/escalation_requested";
        private const string CompletedEvent = "monster/escalation_completed";
        private const string PlayerId = "player";
        private const string PrimaryId = "enerme";
        private const string AdditionalId = "xunye";
        private const float Dt = 0.1f;

        /// <summary>种类 1003「市令」：killable = false、defeat_method = 暗杀（`Tables/Data/monster_species/1003.json` → 妖 3）。</summary>
        private const int ShilingKindId = 1003;

        /// <summary>它指向的妖物 id 与掉落（`Tables/Data/yao/3.json`）；改表要同步改这里。</summary>
        private const int ShilingYaoId = 3;

        private static readonly int[] ShilingDrops = { 1101 };

        /// <summary>主怪在玩家右侧 0.5 米、朝 +X 巡逻（背对玩家）：巡逻态 = 未察觉，玩家面朝右一刀就打得到。</summary>
        private static readonly Vector2[] PrimaryAheadUnaware = { new Vector2(0.5f, 0f), new Vector2(10f, 0f) };

        /// <summary>主怪在玩家右侧 0.5 米、朝玩家（红区）：第一 tick 就转敌对并出手。</summary>
        private static readonly Vector2[] PrimaryAdjacentFacing = { new Vector2(0.5f, 0f), new Vector2(-10f, 0f) };

        /// <summary>主怪在远处：不参与。</summary>
        private static readonly Vector2[] PrimaryFar = { new Vector2(30f, 0f), new Vector2(40f, 0f) };

        /// <summary>附加怪在玩家上方 0.5 米、朝玩家：第一 tick 就转敌对并出手。</summary>
        private static readonly Vector2[] AdditionalAdjacentFacing = { new Vector2(0f, 0.5f), new Vector2(0f, -10f) };

        /// <summary>附加怪在玩家右侧 0.5 米、背对玩家：玩家面朝右一刀打得到它（主怪放远处时用）。</summary>
        private static readonly Vector2[] AdditionalAheadUnaware = { new Vector2(0.5f, 0f), new Vector2(10f, 0f) };

        private static readonly Vector2[] AdditionalFar = { new Vector2(0f, 30f), new Vector2(0f, 40f) };

        private PlayerConfig playerConfig;
        private MonsterConfig monsterConfig;
        private StealthConfig stealthConfig;
        private SimulationConfig simulationConfig;
        private RecordingTelemetrySink sink;
        private TelemetryService telemetry;

        [SetUp]
        public void SetUp()
        {
            playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
            monsterConfig = ScriptableObject.CreateInstance<MonsterConfig>();
            stealthConfig = ScriptableObject.CreateInstance<StealthConfig>();
            // 回放夹具要用它建推进器：`new SimulationConfig()` 会触发 Unity 的「必须用 CreateInstance」告警，
            // 所以在 SetUp 里统一建一份（不用 SerializedObject 改字段，用的就是默认 1/60）。
            simulationConfig = ScriptableObject.CreateInstance<SimulationConfig>();
            sink = new RecordingTelemetrySink();
            telemetry = new TelemetryService(TelemetryOptions.Default, new FakeTelemetryClock(), sink);
        }

        [TearDown]
        public void TearDown()
        {
            telemetry.Dispose();
            UnityEngine.Object.DestroyImmediate(playerConfig);
            UnityEngine.Object.DestroyImmediate(monsterConfig);
            UnityEngine.Object.DestroyImmediate(stealthConfig);
            UnityEngine.Object.DestroyImmediate(simulationConfig);
        }

        // ──────────────────────── ① 三种触发 ────────────────────────

        /// <summary>玩家打中未察觉（巡逻中）的怪 → 偷袭情形：请求带目标 / 种类 / 实时血量，这一下伤害被吞，进入在途。</summary>
        [Test]
        public void PlayerHitsUnawareMonster_RequestsSneakStrike_AndSwallowsTheHit()
        {
            Rig rig = NewRig(PrimaryAheadUnaware);
            int monsterHealth = rig.Primary.Model.Health;

            Tick(rig.Step, InputCommand.ButtonAttack);

            Assert.That(rig.Port.Requests, Has.Count.EqualTo(1));
            EncounterEscalationRequest request = rig.Port.Requests[0];
            Assert.That(request.Trigger, Is.EqualTo(EncounterEscalationTrigger.PlayerHitUnaware));
            Assert.That(request.TargetId, Is.EqualTo(PrimaryId));
            Assert.That(request.KindId, Is.Zero, "没接种类表：种类 0（真端口据此拒绝，假端口这里放行）");
            Assert.That(request.PlayerHealth, Is.EqualTo(playerConfig.MaxHealth));
            Assert.That(request.PlayerMaxHealth, Is.EqualTo(playerConfig.MaxHealth));
            Assert.That(rig.Primary.Model.Health, Is.EqualTo(monsterHealth), "这一下交给了回合制，不扣怪物的血");
            Assert.That(rig.Step.IsEscalating, Is.True);
            Assert.That(rig.Step.EscalationTargetId, Is.EqualTo(PrimaryId));
            Assert.That(LastLineFor(RequestedEvent), Does.Contain("\"trigger\":\"sneak_strike\"").And.Contain("\"reject\":\"none\""));
        }

        /// <summary>玩家打中已警戒 / 敌对的怪 → 正面攻击情形。</summary>
        [Test]
        public void PlayerHitsAwareMonster_RequestsFrontalStrike()
        {
            Rig rig = NewRig(PrimaryAheadUnaware);
            rig.Primary.Provoke(Vector2.zero);
            Assert.That(rig.Primary.Model.Mode, Is.EqualTo(MonsterMode.Hostile), "前提：已敌对");

            Tick(rig.Step, InputCommand.ButtonAttack);

            Assert.That(rig.Port.Requests, Has.Count.EqualTo(1));
            Assert.That(rig.Port.Requests[0].Trigger, Is.EqualTo(EncounterEscalationTrigger.PlayerHitAware));
            Assert.That(rig.Step.IsEscalating, Is.True);
        }

        /// <summary>怪打中玩家 → 被打情形：这一下不扣玩家的血。</summary>
        [Test]
        public void MonsterHitsPlayer_RequestsStruck_AndPlayerKeepsHealth()
        {
            Rig rig = NewRig(PrimaryAdjacentFacing);

            Tick(rig.Step);

            Assert.That(rig.Primary.AttackedThisStep, Is.True, "前提：怪这一 tick 出手了");
            Assert.That(rig.Port.Requests, Has.Count.EqualTo(1));
            Assert.That(rig.Port.Requests[0].Trigger, Is.EqualTo(EncounterEscalationTrigger.MonsterHitPlayer));
            Assert.That(rig.Port.Requests[0].TargetId, Is.EqualTo(PrimaryId));
            Assert.That(rig.Player.Model.Health, Is.EqualTo(playerConfig.MaxHealth), "这一下交给了回合制，不扣玩家的血");
            Assert.That(rig.Step.IsEscalating, Is.True);
        }

        /// <summary>勾了「完整参与者」的附加怪同样能升级（目标 id 是它自己）。</summary>
        [Test]
        public void FullParticipantAdditional_HitByPlayer_Escalates()
        {
            Rig rig = NewRig(PrimaryFar, additionalRoute: AdditionalAheadUnaware, additionalFull: true);

            Tick(rig.Step, InputCommand.ButtonAttack);

            Assert.That(rig.Port.Requests, Has.Count.EqualTo(1));
            Assert.That(rig.Port.Requests[0].TargetId, Is.EqualTo(AdditionalId));
            Assert.That(rig.Step.EscalationTargetId, Is.EqualTo(AdditionalId));
        }

        // ──────────────────────── ② 负对照：逐字走原路径 ────────────────────────

        /// <summary>没装配端口（独立原型场景 / 纯 Monster 测试作用域）：开关全开也照旧扣血，不请求、不埋点。</summary>
        [Test]
        public void WithoutPort_PlayerHitDamagesAsBefore()
        {
            Rig rig = NewRig(PrimaryAheadUnaware, bindPort: false);
            int monsterHealth = rig.Primary.Model.Health;

            Tick(rig.Step, InputCommand.ButtonAttack);

            Assert.That(rig.Primary.Model.Health, Is.EqualTo(monsterHealth - playerConfig.AttackDamage));
            Assert.That(rig.Step.IsEscalating, Is.False);
            Assert.That(rig.Step.EscalationPort, Is.Null);
            Assert.That(LastLineFor(RequestedEvent), Is.Null);
        }

        /// <summary>种类没配回合制数据（端口 CanEscalate 为否）：照旧扣血，埋 not_configured。</summary>
        [Test]
        public void KindNotConfigured_HitDamagesAsBefore_AndTracksNotConfigured()
        {
            Rig rig = NewRig(PrimaryAheadUnaware);
            rig.Port.Configured = false;
            int monsterHealth = rig.Primary.Model.Health;

            Tick(rig.Step, InputCommand.ButtonAttack);

            Assert.That(rig.Primary.Model.Health, Is.EqualTo(monsterHealth - playerConfig.AttackDamage));
            Assert.That(rig.Port.Requests, Is.Empty, "没配数据不发起");
            Assert.That(rig.Step.IsEscalating, Is.False);
            Assert.That(LastLineFor(RequestedEvent), Does.Contain("\"reject\":\"not_configured\""));
        }

        [Test]
        public void KindNotConfigured_MonsterHitStillDamagesAndCountsAsCaught()
        {
            Rig rig = NewRig(PrimaryFar, additionalRoute: AdditionalAdjacentFacing, additionalFull: true,
                caughtPolicy: ChaseCaughtPolicy.KnockdownThenDeath);
            rig.Port.Configured = false;

            Tick(rig.Step);

            Assert.That(rig.Additional.AttackedThisStep, Is.True, "前提：普通巡逻真实命中玩家");
            Assert.That(rig.Player.Model.Health, Is.EqualTo(playerConfig.MaxHealth - rig.Additional.AttackDamage), "不能吞掉实时伤害");
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.ChaseCaught), Is.True, "未配置回合制仍走原抓捕结算");
            Assert.That(rig.Step.IsEscalating, Is.False);
            Assert.That(rig.Port.Requests, Is.Empty);
            Assert.That(LastLineFor(RequestedEvent), Does.Contain("\"reject\":\"not_configured\""));
            Assert.That(LastLineFor("monster/chase_caught"), Is.Not.Null);
        }

        /// <summary>开关关（MonsterConfig 代码默认 = 旧行为）：照旧扣血，埋 disabled，端口连问都不问。</summary>
        [Test]
        public void SwitchOff_HitDamagesAsBefore_AndTracksDisabled()
        {
            Rig rig = NewRig(PrimaryAheadUnaware, switchesOn: false);
            Assert.That(monsterConfig.AllowsEscalation(EncounterEscalationTrigger.PlayerHitUnaware), Is.False, "代码默认全关");
            int monsterHealth = rig.Primary.Model.Health;

            Tick(rig.Step, InputCommand.ButtonAttack);

            Assert.That(rig.Primary.Model.Health, Is.EqualTo(monsterHealth - playerConfig.AttackDamage));
            Assert.That(rig.Port.KindQueries, Is.Empty);
            Assert.That(rig.Port.Requests, Is.Empty);
            Assert.That(LastLineFor(RequestedEvent), Does.Contain("\"reject\":\"disabled\""));
        }

        /// <summary>
        /// 只开「正面攻击」那一个开关：打中**未察觉**的怪照旧扣血（三个开关各管各的）；
        /// 挨了这一刀的怪当场转敌对、同一 tick 反手打中玩家——「被打」开关关着，也照旧扣玩家的血。
        /// </summary>
        [Test]
        public void OnlyFrontalSwitchOn_SneakStrikeAndCounterHitDoNotEscalate()
        {
            Rig rig = NewRig(PrimaryAheadUnaware, switchesOn: false);
            SetSwitches(sneak: false, frontal: true, struck: false);
            int monsterHealth = rig.Primary.Model.Health;

            Tick(rig.Step, InputCommand.ButtonAttack);

            Assert.That(rig.Primary.Model.Health, Is.EqualTo(monsterHealth - playerConfig.AttackDamage));
            Assert.That(rig.Primary.AttackedThisStep, Is.True, "前提：怪反手打中了玩家");
            Assert.That(rig.Player.Model.Health, Is.EqualTo(playerConfig.MaxHealth - 1));
            Assert.That(rig.Port.Requests, Is.Empty);
            Assert.That(CountOf(RequestedEvent), Is.EqualTo(2), "两下都是候选，各留一条 disabled");
        }

        /// <summary>
        /// 端口拒绝（如已有仗在途）：照旧扣血，埋端口给的原因，不进在途。同一 tick 里挨了刀的怪反手打中玩家，
        /// 那一下同样先问端口、同样被拒、同样照旧扣玩家的血——两个拦截点各问一次。
        /// </summary>
        [Test]
        public void PortRejects_HitsResolveAsBefore_AndTrackPortReason()
        {
            Rig rig = NewRig(PrimaryAheadUnaware);
            rig.Port.Accept = false;
            rig.Port.RejectReason = "battle_busy";
            int monsterHealth = rig.Primary.Model.Health;

            Tick(rig.Step, InputCommand.ButtonAttack);

            Assert.That(rig.Port.Requests, Has.Count.EqualTo(2), "玩家那一刀、怪反手那一下，各问一次、都被拒");
            Assert.That(rig.Port.Requests[0].Trigger, Is.EqualTo(EncounterEscalationTrigger.PlayerHitUnaware));
            Assert.That(rig.Port.Requests[1].Trigger, Is.EqualTo(EncounterEscalationTrigger.MonsterHitPlayer));
            Assert.That(rig.Primary.Model.Health, Is.EqualTo(monsterHealth - playerConfig.AttackDamage));
            Assert.That(rig.Player.Model.Health, Is.EqualTo(playerConfig.MaxHealth - 1));
            Assert.That(rig.Step.IsEscalating, Is.False);
            Assert.That(LastLineFor(RequestedEvent), Does.Contain("\"reject\":\"battle_busy\""));
        }

        /// <summary>
        /// 背后处决走 `ExecutionInteractor → ExecutionResolver → MonsterRules.ApplyExecution` 另一条路，不经过两个拦截点：
        /// 开关全开、端口在，处决照样当场杀死目标，一次升级都不发起。
        /// </summary>
        [Test]
        public void BackExecution_DoesNotEscalate()
        {
            Rig rig = NewRig(PrimaryAheadUnaware);
            var resolver = new ExecutionResolver(new AssassinationRules(new AssassinationSettings(120f, 1.2f, false)),
                rig.Step.FactSink, rig.Step, telemetry.Scope("stealth"));
            MonsterModel target = rig.Primary.Model;
            var input = new ExecutionInput(new AssassinationInput(rig.Player.Model.Position, Vector2.right, target.Position,
                target.Facing, true, false, true), true);

            Assert.That(resolver.TryExecute(in input, rig.Primary).Allowed, Is.True, "前提：站在背后、处决成立");
            Tick(rig.Step);

            Assert.That(rig.Primary.Model.Health, Is.Zero);
            Assert.That(rig.Port.Requests, Is.Empty);
            Assert.That(rig.Step.IsEscalating, Is.False);
        }

        /// <summary>
        /// 玩家在操控被驯服的附加怪：别处的怪此刻感知到的是<b>被操控的那只</b>，不是玩家本体
        /// （`TamingRules.AdvanceTargets` 取快照时按 <c>CurrentControlId</c> 换位置），
        /// 所以主怪够不着玩家 —— 既不升级，玩家也不挨打。
        /// </summary>
        [Test]
        public void ControllingTamedMonster_StruckDoesNotEscalate()
        {
            Rig rig = NewRig(PrimaryAdjacentFacing, additionalRoute: AdditionalFar, additionalFull: true);
            Assert.That(rig.Step.Taming.TryTame(AdditionalId), Is.True);
            Assert.That(rig.Step.Taming.TryControl(AdditionalId), Is.True);
            Assert.That(rig.Step.Taming.IsControllingEnemy, Is.True, "前提：正在操控被驯服的怪");

            Tick(rig.Step);

            Assert.That(rig.Primary.AttackedThisStep, Is.False, "主怪追的是被操控的那只（在 30 米外），这一 tick 打不到人");
            Assert.That(rig.Port.Requests, Is.Empty);
            Assert.That(rig.Player.Model.Health, Is.EqualTo(playerConfig.MaxHealth), "够不着，照旧不扣血");
        }

        /// <summary>被驯服的目标不升级：玩家打中它（未操控时）照旧走原路径。</summary>
        [Test]
        public void TamedTarget_HitByPlayerDoesNotEscalate()
        {
            Rig rig = NewRig(PrimaryFar, additionalRoute: AdditionalAheadUnaware, additionalFull: true);
            Assert.That(rig.Step.Taming.TryTame(AdditionalId), Is.True);

            Tick(rig.Step, InputCommand.ButtonAttack);

            Assert.That(rig.Port.Requests, Is.Empty);
        }

        /// <summary>挂着没被消费的结果（处决主怪后 BattleSettlement 还没人取走）：不升级，埋 in_flight，照旧扣血。</summary>
        [Test]
        public void UnconsumedSettlement_BlocksEscalation_AndTracksInFlight()
        {
            Rig rig = NewRig(PrimaryFar, additionalRoute: AdditionalAheadUnaware, additionalFull: true);
            Assert.That(rig.Step.SettleBattle(new BattleResult(BattleOutcome.Victory)), Is.True, "前提：一份结果挂着没人消费");
            int monsterHealth = rig.Additional.Model.Health;

            Tick(rig.Step, InputCommand.ButtonAttack);

            Assert.That(rig.Port.Requests, Is.Empty);
            Assert.That(rig.Additional.Model.Health, Is.EqualTo(monsterHealth - playerConfig.AttackDamage));
            Assert.That(LastLineFor(RequestedEvent), Does.Contain("\"reject\":\"in_flight\""));
        }

        /// <summary>字段为默认值的附加怪（巡逻者乙）：它打中玩家照旧扣血、玩家打中它照旧扣它的血，一次都不问端口。</summary>
        [Test]
        public void DefaultAdditional_NeverEscalates()
        {
            Rig struck = NewRig(PrimaryFar, additionalRoute: AdditionalAdjacentFacing, additionalFull: false);
            Tick(struck.Step);
            Assert.That(struck.Additional.AttackedThisStep, Is.True, "前提：巡逻者乙出手了");
            Assert.That(struck.Player.Model.Health, Is.EqualTo(playerConfig.MaxHealth - 1));
            Assert.That(struck.Port.Requests, Is.Empty);

            Rig hit = NewRig(PrimaryFar, additionalRoute: AdditionalAheadUnaware, additionalFull: false);
            int health = hit.Additional.Model.Health;
            Tick(hit.Step, InputCommand.ButtonAttack);
            Assert.That(hit.Additional.Model.Health, Is.EqualTo(health - playerConfig.AttackDamage));
            Assert.That(hit.Port.Requests, Is.Empty);
            Assert.That(hit.Port.KindQueries, Is.Empty, "不是候选：连种类都不问");
        }

        // ──────────────────────── ③ 在途：早退、不再挨打、不触发被抓 ────────────────────────

        /// <summary>升级在途期间 tick 早退：双方都不推进，不再请求，怪就贴在身边也不再打人。</summary>
        [Test]
        public void WhileEscalating_TickReturnsEarly_NoFurtherRequestsOrHits()
        {
            Rig rig = NewRig(PrimaryAdjacentFacing);
            Tick(rig.Step);
            Assert.That(rig.Step.IsEscalating, Is.True, "前提：被打那一下升级了");
            Vector2 monsterAt = rig.Primary.Model.Position;
            Vector2 playerAt = rig.Player.Model.Position;

            for (int i = 0; i < 30; i++)
            {
                Tick(rig.Step, InputCommand.ButtonAttack, new Vector2(1f, 0f));
            }

            Assert.That(rig.Port.Requests, Has.Count.EqualTo(1), "在途期间不再发起");
            Assert.That(rig.Player.Model.Health, Is.EqualTo(playerConfig.MaxHealth), "3 秒里怪的冷却早就好了，但它没再出手");
            Assert.That(rig.Primary.Model.Position, Is.EqualTo(monsterAt));
            Assert.That(rig.Player.Model.Position, Is.EqualTo(playerAt), "玩家也不推进（世界马上要按住）");
            Assert.That(rig.Player.Model.PreviousPosition, Is.EqualTo(playerAt), "上一 tick 位置对齐，视图不来回插值");
        }

        /// <summary>
        /// 被抓那一下交给了回合制：口径 Death 的被抓后果**不**触发（chase.caught 不写、不判死），在途期间也不再触发。
        /// 对照组见 `EncounterChaseCaughtTests.Caught_ByFullParticipantAdditional_CountsAsCaught`（同站位、同口径、不升级 → 判死）。
        /// </summary>
        [Test]
        public void StruckByFullParticipant_EscalatesInsteadOfCaught()
        {
            Rig rig = NewRig(PrimaryFar, additionalRoute: AdditionalAdjacentFacing, additionalFull: true,
                caughtPolicy: ChaseCaughtPolicy.Death);

            Tick(rig.Step);
            Tick(rig.Step);

            Assert.That(rig.Port.Requests, Has.Count.EqualTo(1));
            Assert.That(rig.Port.Requests[0].TargetId, Is.EqualTo(AdditionalId));
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.ChaseCaught), Is.False);
            Assert.That(rig.Player.Model.Health, Is.EqualTo(playerConfig.MaxHealth));
            Assert.That(LastLineFor("monster/chase_caught"), Is.Null);
        }

        /// <summary>升级在途是瞬态：读档恢复（Restore）与新一场遭遇（Begin）都从「不在途」起步。</summary>
        [Test]
        public void EscalatingState_IsNotSavedAndIsClearedByRestoreAndBegin()
        {
            Rig rig = NewRig(PrimaryAheadUnaware);
            Tick(rig.Step, InputCommand.ButtonAttack);
            Assert.That(rig.Step.IsEscalating, Is.True);

            rig.Step.Restore(rig.Step.Capture(1));
            Assert.That(rig.Step.IsEscalating, Is.False, "存档里没有在途这一项，恢复后一律不在途");

            rig.Step.Begin(Vector2.zero, PrimaryAheadUnaware);
            Tick(rig.Step, InputCommand.ButtonAttack);
            Assert.That(rig.Step.IsEscalating, Is.True, "前提：新一场里再升级一次");
            rig.Step.Begin(Vector2.zero, PrimaryAheadUnaware);
            Assert.That(rig.Step.IsEscalating, Is.False, "新一场遭遇不继承上一场的在途");
            Assert.That(rig.Step.CompleteEscalation(true, 10, 10), Is.False, "上一场那一仗晚到的交回被忽略");
        }

        // ──────────────── ③b 回放期间关掉升级（2026-10-16 代码审查后补） ────────────────
        // 升级的结果在模拟循环之外写回，重放时端口还绑着就会走一条录制时没走过的路（同「处决不进回放」一条规矩）。

        /// <summary>
        /// 接了回放播放器且它正在放：两个拦截点逐字走原路径——这一下照常扣血、不请求升级、不进在途。
        /// 判据是真实 <see cref="Game.Core.Replay.ReplayPlayer"/>.IsActive，不是自造的一个布尔开关。
        /// </summary>
        [Test]
        public void WhileReplaying_PlayerHitDamagesAsBefore_AndDoesNotEscalate()
        {
            Rig rig = NewRig(PrimaryAheadUnaware);
            int monsterHealth = rig.Primary.Model.Health;

            rig.Step.BindReplay(NewReplayPlayer(active: true));
            Tick(rig.Step, InputCommand.ButtonAttack);

            Assert.That(rig.Step.IsReplaying, Is.True, "前提：判据用的就是播放器的 IsActive");
            Assert.That(rig.Port.Requests, Is.Empty, "回放里不许发起升级");
            Assert.That(rig.Step.IsEscalating, Is.False);
            Assert.That(rig.Primary.Model.Health, Is.EqualTo(monsterHealth - 1), "这一下照常扣血（原路径，不被吞）");
            Assert.That(LastLineFor(RequestedEvent), Is.Null, "不是「被拒」，是这条功能整个关掉——不埋点");
        }

        /// <summary>
        /// 负对照：接了播放器但它**没在放**（载入未播 / 放完 Stop 之后）——实时玩法照常升级。
        /// 这一条守着「判据用 IsActive，不是用『有回放载入』」：判错会让回放结束后的整局游戏再也升不了级。
        /// </summary>
        [Test]
        public void NotReplaying_StillEscalates()
        {
            Rig rig = NewRig(PrimaryAheadUnaware);

            rig.Step.BindReplay(NewReplayPlayer(active: false));
            Tick(rig.Step, InputCommand.ButtonAttack);

            Assert.That(rig.Step.IsReplaying, Is.False);
            Assert.That(rig.Port.Requests, Has.Count.EqualTo(1));
            Assert.That(rig.Step.IsEscalating, Is.True);
        }

        /// <summary>
        /// 回放接管时把实时玩法留下的「在途」清掉：否则那个标记只在交回结果 / 恢复快照时回落，
        /// 而回放里没人会来交回 —— `Step` 会被钉死在早退上（整段回放一格都不推进），并留下一条 <c>escalation_abandoned</c>。
        /// </summary>
        [Test]
        public void ReplayTakingOver_ClearsStaleEscalation_AndTicksResume()
        {
            Rig rig = NewRig(PrimaryAheadUnaware);
            Tick(rig.Step, InputCommand.ButtonAttack);
            Assert.That(rig.Step.IsEscalating, Is.True, "前提：实时玩法正处在升级在途");

            rig.Step.BindReplay(NewReplayPlayer(active: true));
            Tick(rig.Step);

            Assert.That(rig.Step.IsEscalating, Is.False, "回放接管 = 那一场升级作废");
            Assert.That(LastLineFor("monster/escalation_abandoned"), Is.Not.Null);
            Vector2 before = rig.Primary.Model.Position;
            Tick(rig.Step);
            Assert.That(rig.Primary.Model.Position, Is.Not.EqualTo(before), "早退解除后双方规则照常推进");
        }

        // ──────────────────────── ④ 完成升级 ────────────────────────

        /// <summary>
        /// 胜（用户拍板「打赢视同击杀」）：killable = false 的市令也死、按种类掉落一次；血量按比例写回
        /// （4 / 10 → round(0.4 × 3) = 1，出场四舍五入）；重复交回无效，掉落不翻倍。
        /// </summary>
        [Test]
        public void CompleteVictory_KillsUnkillableTarget_DropsOnce_AndScalesHealthBack()
        {
            Rig rig = NewRig(PrimaryAheadUnaware, kindId: ShilingKindId);
            var drops = new EncounterDropTests.RecordingDropSink();
            rig.Step.BindDropSink(drops);
            Assert.That(rig.Primary.IsKillable, Is.False, "前提：真表里市令 killable = false");
            Tick(rig.Step, InputCommand.ButtonAttack);
            Assert.That(rig.Port.Requests[0].KindId, Is.EqualTo(ShilingKindId));

            Assert.That(rig.Step.CompleteEscalation(true, 4, 10), Is.True);

            Assert.That(rig.Primary.Model.Health, Is.Zero, "无视 killable");
            Assert.That(rig.Primary.Model.Mode, Is.EqualTo(MonsterMode.Dead));
            Assert.That(drops.Drops, Has.Count.EqualTo(1));
            Assert.That(drops.Drops[0].Yao, Is.EqualTo(ShilingYaoId));
            Assert.That(drops.Drops[0].Items, Is.EqualTo(ShilingDrops));
            Assert.That(rig.Player.Model.Health, Is.EqualTo(1), "round(4 / 10 × 3) = 1（出场四舍五入，不是进场的 ceil）");
            Assert.That(rig.Step.IsEscalating, Is.False);
            string completed = LastLineFor(CompletedEvent);
            Assert.That(completed, Does.Contain("\"victory\":true").And.Contain("\"hp_before\":3").And.Contain("\"hp_after\":1"));

            Assert.That(rig.Step.CompleteEscalation(true, 10, 10), Is.False, "只接受一次");
            Tick(rig.Step);
            Assert.That(drops.Drops, Has.Count.EqualTo(1), "下一 tick 的死亡分支也不再掉第二次");
            Assert.That(LastLineFor("monster/escalation_completion_ignored"), Is.Not.Null, "无效的交回留痕");
            Assert.That(CountOf(CompletedEvent), Is.EqualTo(1));
        }

        /// <summary>胜且目标是主怪、这一场登记过「一仗」：当场判 Victory（桥照常在下一 tick 取走）。</summary>
        [Test]
        public void CompleteVictory_OnPrimaryInStoryBattle_SettlesVictory()
        {
            Rig rig = NewRig(PrimaryAheadUnaware);
            rig.Step.StartBattle(1, 1);
            Tick(rig.Step, InputCommand.ButtonAttack);

            rig.Step.CompleteEscalation(true, 10, 10);

            Assert.That(rig.Step.PendingResult, Is.EqualTo(EncounterStep.Result.Victory));
            Assert.That(rig.Step.BattleSettlement.HasValue, Is.True);
            Assert.That(rig.Step.BattleSettlement.Value.Kind, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(rig.Player.Model.Health, Is.EqualTo(playerConfig.MaxHealth), "满血打赢 → 满血写回");
        }

        /// <summary>胜但目标是附加怪：它自己死、整场不结算（整场胜负只认主怪）。</summary>
        [Test]
        public void CompleteVictory_OnAdditional_DoesNotSettleTheEncounter()
        {
            Rig rig = NewRig(PrimaryFar, additionalRoute: AdditionalAheadUnaware, additionalFull: true);
            rig.Step.StartBattle(1, 1);
            Tick(rig.Step, InputCommand.ButtonAttack);

            rig.Step.CompleteEscalation(true, 10, 10);

            Assert.That(rig.Additional.Model.Health, Is.Zero);
            Assert.That(rig.Primary.Model.Health, Is.GreaterThan(0));
            Assert.That(rig.Step.PendingResult, Is.EqualTo(EncounterStep.Result.None));
        }

        /// <summary>负：玩家生命归零，走既有的失败流程（剧情阶段内判 Downed）；怪物不受影响。</summary>
        [Test]
        public void CompleteDefeat_ZeroesHealth_AndSettlesDowned()
        {
            Rig rig = NewRig(PrimaryAdjacentFacing);
            rig.Step.StartBattle(1, 1);
            Tick(rig.Step);
            int monsterHealth = rig.Primary.Model.Health;

            Assert.That(rig.Step.CompleteEscalation(false, 0, 10), Is.True);

            Assert.That(rig.Player.Model.Health, Is.Zero);
            Assert.That(rig.Primary.Model.Health, Is.EqualTo(monsterHealth));
            Assert.That(rig.Step.PendingResult, Is.EqualTo(EncounterStep.Result.Defeat));
            Assert.That(rig.Step.BattleSettlement.Value.Kind, Is.EqualTo(BattleOutcome.Downed));
            Assert.That(LastLineFor(CompletedEvent), Does.Contain("\"victory\":false").And.Contain("\"hp_after\":0"));
        }

        /// <summary>负对照：不在途时交回无效（返回 false、不改血量、不杀怪），留一条 W 级痕迹。</summary>
        [Test]
        public void Complete_WhenNotEscalating_IsIgnored()
        {
            Rig rig = NewRig(PrimaryAheadUnaware);
            int monsterHealth = rig.Primary.Model.Health;

            Assert.That(rig.Step.CompleteEscalation(false, 0, 10), Is.False);

            Assert.That(rig.Player.Model.Health, Is.EqualTo(playerConfig.MaxHealth));
            Assert.That(rig.Primary.Model.Health, Is.EqualTo(monsterHealth));
            Assert.That(LastLineFor("monster/escalation_completion_ignored"), Is.Not.Null);
        }

        /// <summary>放弃升级（战斗没开成）：不改血量、不判胜负，下一 tick 起遭遇照常推进。</summary>
        [Test]
        public void Abort_ExitsEscalationWithoutTouchingHealth_ThenTicksResume()
        {
            // 被升级的是附加怪；主怪放在远处巡逻，拿它的位置证明「下一 tick 起照常推进」。
            Rig rig = NewRig(PrimaryFar, additionalRoute: AdditionalAheadUnaware, additionalFull: true);
            Tick(rig.Step, InputCommand.ButtonAttack);
            Assert.That(rig.Step.IsEscalating, Is.True, "前提：升级在途");
            int monsterHealth = rig.Additional.Model.Health;

            Assert.That(rig.Step.AbortEscalation(), Is.True);
            Assert.That(rig.Step.AbortEscalation(), Is.False, "不在途时什么都不做");

            Assert.That(rig.Step.IsEscalating, Is.False);
            Assert.That(rig.Player.Model.Health, Is.EqualTo(playerConfig.MaxHealth));
            Assert.That(rig.Additional.Model.Health, Is.EqualTo(monsterHealth), "被吞的那一下作废，不补扣");
            Assert.That(LastLineFor("monster/escalation_aborted"), Is.Not.Null);
            Vector2 before = rig.Primary.Model.Position;
            Tick(rig.Step);
            Assert.That(rig.Primary.Model.Position, Is.Not.EqualTo(before), "主怪照常巡逻推进");
        }

        /// <summary>
        /// 端口在接受的那次调用里就同步打完整场（测试假表现层就是这样）：那次交回必须被接受——
        /// 调用方要在问端口之前就进入在途（端口契约里写明的那一条）。
        /// </summary>
        [Test]
        public void PortCompletesSynchronously_CompletionIsAccepted()
        {
            Rig rig = NewRig(PrimaryAheadUnaware);
            bool? accepted = null;
            rig.Port.OnAccepted = step => accepted = step.CompleteEscalation(true, 10, 10);

            Tick(rig.Step, InputCommand.ButtonAttack);

            Assert.That(accepted, Is.True);
            Assert.That(rig.Primary.Model.Health, Is.Zero);
            Assert.That(rig.Step.IsEscalating, Is.False);
            Assert.That(LastLineFor("monster/escalation_completion_ignored"), Is.Null);
        }

        // ──────────────────────── ⑤ 血量换算的取整边界 ────────────────────────
        // 两个方向各一套口径（2026-10-16 代码审查）：进场 ceil（别人的血不能更惨），出场 round（原样打完不许回血）。

        [TestCase(10, 10, 3, 3, TestName = "ScaleHealth_FullBack_IsFull")]
        [TestCase(1, 3, 3, 1, TestName = "ScaleHealth_OneThird_IsExactlyOne")]
        [TestCase(4, 10, 3, 2, TestName = "ScaleHealth_PointFour_RoundsUpToTwo")]
        [TestCase(7, 10, 3, 3, TestName = "ScaleHealth_PointSeven_RoundsUpToThree")]
        [TestCase(1, 10, 3, 1, TestName = "ScaleHealth_Low_RoundsUpToOne")]
        [TestCase(0, 10, 3, 1, TestName = "ScaleHealth_Zero_IsAtLeastOne")]
        [TestCase(1, 3, 10, 4, TestName = "ScaleHealth_OneThirdOfTen_RoundsUpToFour")]
        public void ScaleHealth_RoundsUpAndIsAtLeastOne(int health, int fromMax, int toMax, int expected) =>
            Assert.That(EncounterEscalationRequest.ScaleHealth(health, fromMax, toMax), Is.EqualTo(expected));

        [TestCase(10, 10, 3, 3, TestName = "ScaleHealthBack_Full_IsFull")]
        [TestCase(4, 10, 3, 1, TestName = "ScaleHealthBack_PointFour_RoundsDownToOne")]
        [TestCase(5, 10, 3, 2, TestName = "ScaleHealthBack_Half_RoundsUpToTwo")]
        [TestCase(7, 10, 3, 2, TestName = "ScaleHealthBack_PointSeven_RoundsDownToTwo")]
        [TestCase(1, 3, 3, 1, TestName = "ScaleHealthBack_OneThird_IsExactlyOne")]
        [TestCase(1, 10, 3, 1, TestName = "ScaleHealthBack_Low_RoundsToZeroButStaysAtLeastOne")]
        [TestCase(0, 10, 3, 1, TestName = "ScaleHealthBack_Zero_IsAtLeastOne")]
        public void ScaleHealthBack_RoundsToNearestAndIsAtLeastOne(int health, int fromMax, int toMax, int expected) =>
            Assert.That(EncounterEscalationRequest.ScaleHealthBack(health, fromMax, toMax), Is.EqualTo(expected));

        /// <summary>
        /// 原样往返不走样（2026-10-16 代码审查指出的「凭空回血」）：实时 1/3 → 战斗 4/10 → 一下没挨打地打赢 →
        /// 写回 1/3。出场那一步若沿用进场的 <c>ceil</c> 就是 2（1/3 变 2/3），这格的期望值正是那条口径的分界
        /// ——<see cref="ScaleHealthBack_RoundsToNearestAndIsAtLeastOne"/> 的 <c>PointFour</c> 一档守着同一个数。
        /// </summary>
        [Test]
        public void RoundTrip_OneThirdLeftWithNoDamage_ComesBackExactly()
        {
            const int liveMax = 3;
            const int battleMax = 10;

            int inBattle = EncounterEscalationRequest.ScaleHealth(1, liveMax, battleMax);
            int back = EncounterEscalationRequest.ScaleHealthBack(inBattle, battleMax, liveMax);

            Assert.That(inBattle, Is.EqualTo(4), "进场：ceil(1/3 × 10) = 4");
            Assert.That(back, Is.EqualTo(1), "出场：round(4/10 × 3) = 1，不是 ceil 的 2");
        }

        /// <summary>同一条出场口径走完成升级：回合制里剩 4 / 10 → 实时上限 3 → 1（满血 10/10 仍是 3）。</summary>
        [Test]
        public void CompleteVictory_WritesBackWithNearestRounding()
        {
            Rig rig = NewRig(PrimaryAheadUnaware);
            Tick(rig.Step, InputCommand.ButtonAttack);

            rig.Step.CompleteEscalation(true, 4, 10);

            Assert.That(rig.Player.Model.Health, Is.EqualTo(1));
        }

        /// <summary>打赢时至少留 1：回合制里剩 1 / 10 → 实时上限 3，四舍五入是 0，兜底为 1（换算不等于判死）。</summary>
        [Test]
        public void CompleteVictory_AlmostDead_StillLeavesOne()
        {
            Rig rig = NewRig(PrimaryAheadUnaware);
            Tick(rig.Step, InputCommand.ButtonAttack);

            rig.Step.CompleteEscalation(true, 1, 10);

            Assert.That(rig.Player.Model.Health, Is.EqualTo(1));
        }

        /// <summary>同一条出场口径走完成升级：回合制里剩 1 / 3 → 实时上限 3 → 1（恰好整除，不受取整方向影响）。</summary>
        [Test]
        public void CompleteVictory_OneThirdLeft_WritesBackExactlyOne()
        {
            Rig rig = NewRig(PrimaryAheadUnaware);
            Tick(rig.Step, InputCommand.ButtonAttack);

            rig.Step.CompleteEscalation(true, 1, 3);

            Assert.That(rig.Player.Model.Health, Is.EqualTo(1));
        }

        // ──────────────────────── 夹具 ────────────────────────

        /// <summary>
        /// 一套最小可用的 <see cref="ReplayPlayer"/>（同 DriftDetectionTests 的装法：随机源 / 回放配置 / 音频 / 埋点传 null）。
        /// <paramref name="active"/> 是私有的 <c>active</c> 字段（<see cref="ReplayPlayer.IsActive"/> 的背书），
        /// 它只能由 Load / Stop 改，而 Load 要一份真录像——本组只关心「此刻在不在回放」，所以直接置位。
        /// </summary>
        private ReplayPlayer NewReplayPlayer(bool active)
        {
            var frameClock = new StaticClock();
            var inputSwitch = new InputSourceSwitch(new SilentInputSource());
            var runner = new SimulationRunner(simulationConfig, frameClock, inputSwitch, new RandomService(1UL), null);
            var player = new ReplayPlayer(runner, inputSwitch, frameClock, null, null, null, null, null);
            System.Reflection.FieldInfo field = typeof(ReplayPlayer).GetField(
                "active", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "ReplayPlayer 的 active 字段改名了：本组的两条回放负对照失去判据");
            field.SetValue(player, active);
            Assert.That(player.IsActive, Is.EqualTo(active), "前提：判据读得到");
            return player;
        }

        /// <summary>不采样任何输入的输入源：回放播放器只是被构造出来当判据，不推进任何 tick。</summary>
        private sealed class SilentInputSource : IInputSource
        {
            public void Sample(long tick)
            {
            }

            public InputCommand Current => default;
        }

        /// <summary>不走的时间源（同 EncounterBattleBridgeTests.TestClock）：本组不推进任何帧。</summary>
        private sealed class StaticClock : IClock
        {
            public DateTime UtcNow => DateTime.UnixEpoch;
            public float GameTime => 0f;
            public float UnscaledTime => 0f;
            public float DeltaTime => 0f;
            public float UnscaledDeltaTime => 0f;
        }

        private Rig NewRig(Vector2[] primaryRoute, int kindId = 0, Vector2[] additionalRoute = null, bool additionalFull = false,
            bool switchesOn = true, bool bindPort = true, ChaseCaughtPolicy? caughtPolicy = null)
        {
            if (switchesOn)
            {
                SetSwitches(true, true, true);
            }

            var player = new PlayerRules(playerConfig, new PlayerModel(), NullTelemetryScope.Instance);
            MonsterKindCatalog catalog = kindId == 0 ? null : NewCatalog();
            var primary = new MonsterRules(monsterConfig, new MonsterModel(), new RandomService(3ul), NullTelemetryScope.Instance,
                kind: null, kindCatalog: catalog, kindId: kindId);
            var step = new EncounterStep(player, primary);
            step.UseTelemetry(telemetry.Scope("monster"));
            if (caughtPolicy.HasValue)
            {
                var serialized = new SerializedObject(stealthConfig);
                serialized.FindProperty("chaseCaughtPolicy").intValue = (int)caughtPolicy.Value;
                serialized.FindProperty("chaseCaughtFailCount").intValue = 0;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                step.UseStealth(new StealthKernel(stealthConfig));
            }

            if (additionalRoute != null)
            {
                step.ConfigureTaming(PlayerId, "玩家", new[] { PrimaryId, AdditionalId }, new[] { "巡逻者甲", "巡夜人" },
                    new[] { primaryRoute, additionalRoute }, null, new[] { false, additionalFull });
            }
            else
            {
                step.ConfigureTaming(PlayerId, "玩家", new[] { PrimaryId }, new[] { "巡逻者甲" }, new[] { primaryRoute });
            }

            var port = new FakePort();
            if (bindPort)
            {
                step.BindEscalation(port);
            }

            step.Begin(Vector2.zero, primaryRoute);
            return new Rig(step, player, primary, additionalRoute == null ? null : step.Taming.GetTarget(AdditionalId), port);
        }

        private void SetSwitches(bool sneak, bool frontal, bool struck)
        {
            var serialized = new SerializedObject(monsterConfig);
            serialized.FindProperty("escalateOnSneakStrike").boolValue = sneak;
            serialized.FindProperty("escalateOnFrontalStrike").boolValue = frontal;
            serialized.FindProperty("escalateWhenStruck").boolValue = struck;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private MonsterKindCatalog NewCatalog()
        {
            var config = new StubConfigService(ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes()));
            return new MonsterKindCatalog(config, new YaoCatalog(config), monsterConfig.HostileRadius);
        }

        private static void Tick(EncounterStep step, uint buttons = 0u, Vector2 move = default)
        {
            var command = new InputCommand(move, Vector2.zero, buttons, Vector2.zero, 0);
            var context = new SimulationContext(0, Dt, in command, new RandomService(3ul));
            step.Step(in context);
        }

        private string LastLineFor(string eventName)
        {
            for (int i = sink.Lines.Count - 1; i >= 0; i--)
            {
                if (sink.Lines[i].Contains(eventName))
                {
                    return sink.Lines[i];
                }
            }

            return null;
        }

        private int CountOf(string eventName)
        {
            int count = 0;
            for (int i = 0; i < sink.Lines.Count; i++)
            {
                if (sink.Lines[i].Contains(eventName))
                {
                    count++;
                }
            }

            return count;
        }

        private readonly struct Rig
        {
            public Rig(EncounterStep step, PlayerRules player, MonsterRules primary, MonsterRules additional, FakePort port)
            {
                Step = step;
                Player = player;
                Primary = primary;
                Additional = additional;
                Port = port;
            }

            public EncounterStep Step { get; }
            public PlayerRules Player { get; }
            public MonsterRules Primary { get; }
            public MonsterRules Additional { get; }
            public FakePort Port { get; }
        }

        /// <summary>
        /// 假升级端口：只记录被问了什么、按脚本回答；<see cref="OnAccepted"/> 非空时在接受的那次调用里同步回调（模拟「同步打完整场」）。
        /// 不含任何战斗逻辑——战斗侧的实现由 `Battle/BattleFlowEncounterTests` 覆盖。
        /// </summary>
        private sealed class FakePort : IEncounterEscalationPort
        {
            public bool Configured { get; set; } = true;
            public bool Accept { get; set; } = true;
            public string RejectReason { get; set; } = "rule_rejected";
            public Action<EncounterStep> OnAccepted { get; set; }
            public List<int> KindQueries { get; } = new List<int>();
            public List<EncounterEscalationRequest> Requests { get; } = new List<EncounterEscalationRequest>();

            public bool CanEscalate(int kindId)
            {
                KindQueries.Add(kindId);
                return Configured;
            }

            public bool TryEscalate(EncounterStep encounter, in EncounterEscalationRequest request, out string rejectReason)
            {
                Requests.Add(request);
                if (!Accept)
                {
                    rejectReason = RejectReason;
                    return false;
                }

                rejectReason = null;
                OnAccepted?.Invoke(encounter);
                return true;
            }
        }

        /// <summary>只递一份现成的 <c>cfg.Tables</c>（同 EncounterDropTests 的最小假实现）。</summary>
        private sealed class StubConfigService : IConfigService
        {
            public StubConfigService(global::cfg.Tables tables) => Tables = tables;
            public global::cfg.Tables Tables { get; }
            public ulong ContentHash => throw new NotSupportedException("假配置服务不提供内容指纹");
        }
    }
}
