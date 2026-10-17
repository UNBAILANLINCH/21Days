// 职责：钉住 BOSS 主动发难（PRP/combat-iteration D6，BossAggro；2026-10-15 补发难半径 + 反应时间 + 拴绳 + 头顶提示）——
//   ① 状态迁移全路径：待机 →（进发难半径）警觉 →（愣满反应时间）冲刺 →（接触距离）接触 →（交互受理且在跟它打）战斗中
//      →（战斗场景在场）瞬移回原位 →（打完）解除武装 →（玩家离开察觉范围）待机；冲刺超出拴绳 → 撤回 → 原位后按玩家位置
//      进解除武装或待机；警觉中离开半径 → 待机、计时清零；
//   ② 接触那一刻先置「BOSS 先动手」、同一次接触只发起一次交互；交互被拒 / 没开成仗 → 理由复位、解除武装、不每帧重试；
//   ③ 每个守卫各一条负对照（对白 / 演出 / 别的战斗 / 暂停 / 剧情忙 / 剧情不可用）；警觉中被挡计时暂停，冲刺中被挡原地停住；
//   ④ 重新武装：没离开察觉范围不再发难，离开再进来会再警觉；
//   ⑤ 回归：名册没开发难 / 查不到这只 BOSS 时完全不动；被击败（物体关掉）不发难；玩家按 E 挑战开的仗理由仍是「玩家先动手」；
//   ⑥ 头顶提示：警觉与冲刺期间亮、其余关；没配提示物体不报错；
//   ⑦ 埋点：状态迁移各记一条，同一守卫持续挡着只记一条；发难半径不大于交互半径时绑定记 W 级并告警；
//   ⑧ 串到真实 BattleFlow（BattleFakes）：反应时间内按 E → 正面攻击、玩家先手且不冲；磨蹭超过反应时间 → 冲上来 → 被打、BOSS 先手。
// 为什么新建：BossAggro 是本波新增的组件，一个被测类一个测试类。外部世界（守卫 / 玩家位置 / 交互入口）用本文件内的假实现
//   （IBossAggroWorld 就是为此抽的口）；局势用真的 BossEntryContext + MonsterConfig + PlayerModel + XZ 投影，
//   所以「玩家在不在察觉范围 / 红区」走的是生产判据，不是假数据。
// 坐标约定：BOSS 原位 = 世界 (0, 2, 0)（高 2 米，验证冲刺只在地面平面上走、高度不变）；玩家逻辑 (x, y) ↔ 世界 (x, 2, y)。
//   名册这一行：发难半径 5、反应时间 3 秒、冲刺 3 米/秒、拴绳 0（= 半径 × 2 = 10 米）。
//   MonsterConfig 默认值：敌对半径 2、警戒半径 6、贴身 1.5；NarrativeTrigger 默认交互半径 2 → 接触距离 1.2。
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Battle;
using Game.Core.Input;
using Game.Core.Telemetry;
using Game.Monster;
using Game.Player;
using Game.Tests.EditMode.Telemetry;
using Game.TurnBased;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using NarrativeTrigger = Game.Narrative.NarrativeTrigger;
using StageEvent = Game.Narrative.BattleStageEnteredEvent;

namespace Game.Tests.EditMode.Battle
{
    public sealed class BossAggroTests
    {
        private const string BossId = "sample_boss";
        private const float ChargeSpeed = 3f;
        private const float AggroRadius = 5f;
        private const float ReactionSeconds = 3f;

        private static readonly Vector3 HomePosition = new Vector3(0f, 2f, 0f);

        /// <summary>交互半径内（能按 E）、红区里：离原位 1.8 米。</summary>
        private static readonly Vector2 ChallengeSpot = new Vector2(0f, 1.8f);

        /// <summary>发难半径里、交互半径外（「磨蹭」的位置）：离原位 4 米。</summary>
        private static readonly Vector2 LingerSpot = new Vector2(0f, 4f);

        /// <summary>发难半径外、仍在察觉范围（警戒半径 6）里：离原位 5.5 米。</summary>
        private static readonly Vector2 OutsideRadiusSpot = new Vector2(0f, 5.5f);

        /// <summary>察觉范围外：离原位 10 米。</summary>
        private static readonly Vector2 FarSpot = new Vector2(0f, 10f);

        /// <summary>远到拴绳之外（引着 BOSS 越追越远）：离原位 40 米。</summary>
        private static readonly Vector2 LureSpot = new Vector2(0f, 40f);

        private MonsterConfig config;
        private PlayerModel player;
        private GameObject bossObject;
        private BossEntryContext context;
        private BossAggro aggro;
        private FakeWorld world;
        private RecordingTelemetrySink sink;
        private TelemetryService telemetryService;

        // 串到真实 BattleFlow 的那两条才建（见 BuildFlowChain）。
        private BattleFlow flow;
        private BattleArena arena;
        private BattleFakes.Bus<StageEvent> bus;
        private BattleFakes.Presenter presenter;
        private InputService input;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<MonsterConfig>();
            player = new PlayerModel();
            bossObject = new GameObject("BossAggroTestBoss");
            bossObject.transform.position = HomePosition;
            context = bossObject.AddComponent<BossEntryContext>();
            bossObject.AddComponent<NarrativeTrigger>();
            aggro = bossObject.AddComponent<BossAggro>();
            SetAggroField("bossId", p => p.stringValue = BossId);

            context.Configure(config, player);
            context.UseTelemetry(NullTelemetryScope.Instance);
            context.UseProjection(XZProjection());
            world = new FakeWorld();
            sink = new RecordingTelemetrySink();
            telemetryService = new TelemetryService(TelemetryOptions.Default, new FakeTelemetryClock(), sink);
            PlacePlayer(FarSpot);
        }

        [TearDown]
        public void TearDown()
        {
            flow?.Dispose();
            flow = null;
            input?.Dispose();
            input = null;
            telemetryService?.Dispose();
            telemetryService = null;
            if (bossObject != null) UnityEngine.Object.DestroyImmediate(bossObject);
            bossObject = null;
            if (config != null) UnityEngine.Object.DestroyImmediate(config);
            config = null;
        }

        // ────────────────────── ① 待机 → 警觉 → 冲刺 ──────────────────────

        [Test]
        public void Idle_PlayerEntersAggroRadius_BecomesAlerted()
        {
            Bind();
            PlacePlayer(LingerSpot);

            aggro.Tick(0f);

            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Alerted));
            Assert.That(aggro.ReactionElapsed, Is.EqualTo(0f));
        }

        [Test]
        public void Idle_PlayerOutsideAggroRadius_StaysIdleEvenIfAware()
        {
            // 负对照：起冲看发难半径（以原位为圆心），不看察觉范围——5.5 米处 BossEntryContext 已察觉，BOSS 照样不动。
            Bind();
            PlacePlayer(OutsideRadiusSpot);
            Assert.That(context.MonsterAware, Is.True, "前置：在察觉范围里");

            Tick(10, 0.5f);

            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Idle));
            Assert.That(bossObject.transform.position, Is.EqualTo(HomePosition));
        }

        [Test]
        public void Alerted_BeforeReactionTime_DoesNotMove_ThenChargesWhenElapsed()
        {
            Bind();
            PlacePlayer(LingerSpot);
            aggro.Tick(0f);

            aggro.Tick(2.5f);
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Alerted), "反应时间没到：只警觉、不冲");
            Assert.That(bossObject.transform.position, Is.EqualTo(HomePosition));

            aggro.Tick(0.5f);

            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Charging), "愣满 3 秒玩家还在半径里 → 冲");
            Assert.That(world.InteractCalls, Is.EqualTo(0));
        }

        [Test]
        public void Alerted_PlayerLeavesRadius_ReturnsIdleAndResetsTimer()
        {
            Bind();
            PlacePlayer(LingerSpot);
            aggro.Tick(0f);
            aggro.Tick(2.5f);

            PlacePlayer(OutsideRadiusSpot);
            aggro.Tick(0.1f);
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Idle));
            Assert.That(aggro.ReactionElapsed, Is.EqualTo(0f), "计时清零");

            PlacePlayer(LingerSpot);
            aggro.Tick(0f);
            aggro.Tick(2.5f);

            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Alerted), "重新从 0 计：2.5 秒还不够（没清零的话早已超过 3 秒）");
        }

        [Test]
        public void Alerted_PlayerGone_ReturnsIdle()
        {
            // 场景里没有玩家 / 玩家倒地（外部世界报「没有可追的玩家」）。
            Bind();
            PlacePlayer(LingerSpot);
            aggro.Tick(0f);

            world.HasPlayer = false;
            aggro.Tick(0.1f);

            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Idle));
        }

        [Test]
        public void Charging_MovesTowardPlayerOnGroundPlane_AtChargeSpeed()
        {
            Bind();
            StartCharging();

            aggro.Tick(0.1f);

            Vector3 position = bossObject.transform.position;
            Assert.That(position.z, Is.EqualTo(ChargeSpeed * 0.1f).Within(1e-4f), "朝玩家（+z）走了 速度 × 帧间隔");
            Assert.That(position.x, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(position.y, Is.EqualTo(HomePosition.y), "只在地面平面上走，高度保持原位");
            Assert.That(world.InteractCalls, Is.EqualTo(0), "还没到接触距离");
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Charging));
        }

        // ────────────────────── ② 接触与交互 ──────────────────────

        [Test]
        public void Charging_ReachesContactDistance_SetsBossAttackedAndInteractsOnce()
        {
            Bind();
            var pending = new UniTaskCompletionSource<bool>();
            world.OnInteract = () => pending.Task; // 嘲讽对白还在播：交互迟迟不返回
            StartCharging();

            aggro.Tick(1f); // 一步走满：停在接触距离上，不越过去
            Tick(10, 0.1f);

            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Contact));
            Assert.That(context.Initiator, Is.EqualTo(BattleInitiator.BossAttacked), "先置理由，再发起交互");
            Assert.That(world.InteractCalls, Is.EqualTo(1), "同一次接触只发起一次交互");
            Assert.That(HorizontalDistanceToPlayer(), Is.EqualTo(aggro.ContactDistance).Within(1e-3f), "停在接触距离，不贴到玩家身上");
            pending.TrySetResult(false);
        }

        [Test]
        public void Contact_AcceptedAndFightingThisBoss_EntersInBattle()
        {
            Bind();
            world.OnInteract = () =>
            {
                world.FightingBossId = BossId; // 剧情走到 Battle 阶段、BattleFlow 登记了这一仗
                return UniTask.FromResult(true);
            };
            ReachContact();

            aggro.Tick(0f);

            Assert.That(aggro.State, Is.EqualTo(BossAggroState.InBattle));
            Assert.That(context.Initiator, Is.EqualTo(BattleInitiator.BossAttacked), "战斗中保持：开仗那一刻现取局势要读到它");
        }

        [Test]
        public void Contact_Rejected_ResetsInitiatorDisarmsAndDoesNotRetry()
        {
            Bind();
            world.InteractResult = false; // 剧情没受理（忙 / 条件）
            ReachContact();

            Tick(10, 0.5f); // 玩家仍站在原地（发难半径里）

            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Disarmed));
            Assert.That(context.Initiator, Is.EqualTo(BattleInitiator.PlayerAttacked), "作废的发难不许把之后的按 E 判成「被打」");
            Assert.That(world.InteractCalls, Is.EqualTo(1), "不每帧重冲重试");
            Assert.That(bossObject.transform.position, Is.EqualTo(HomePosition), "解除武装期间走回原位");
        }

        [Test]
        public void Contact_AcceptedButNoBattle_ResetsInitiatorAndDisarms()
        {
            // 交互受理了却没开成仗（没注册战斗表现 / 剧情走了别的分支）：同样作废。
            Bind();
            world.InteractResult = true;
            ReachContact();

            aggro.Tick(0f);

            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Disarmed));
            Assert.That(context.Initiator, Is.EqualTo(BattleInitiator.PlayerAttacked));
        }

        // ────────────────────── 战斗中与战后 ──────────────────────

        [Test]
        public void InBattle_WorldHiddenByBattle_TeleportsHome()
        {
            EnterBattleByContact();
            Assert.That(bossObject.transform.position, Is.Not.EqualTo(HomePosition), "前置：停在接触点");

            world.WorldHidden = true; // 战斗场景在场：世界被黑幕 / 战斗舞台挡着
            aggro.Tick(0f);

            Assert.That(bossObject.transform.position, Is.EqualTo(HomePosition));
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.InBattle), "回原位不等于打完");
        }

        [Test]
        public void InBattle_WorldStillVisible_DoesNotTeleport()
        {
            // 负对照：门闸还没过、黑幕还没落满（世界看得见）时不闪回去。
            EnterBattleByContact();
            Vector3 contactPoint = bossObject.transform.position;

            world.WorldHidden = false;
            Tick(5, 0.1f);

            Assert.That(bossObject.transform.position, Is.EqualTo(contactPoint));
        }

        [Test]
        public void InBattle_BattleOver_ResetsInitiatorAndDisarms()
        {
            EnterBattleByContact();
            world.WorldHidden = true;
            aggro.Tick(0f);

            world.WorldHidden = false;
            world.FightingBossId = null; // 打完（撤退：BOSS 还在）
            aggro.Tick(0f);

            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Disarmed));
            Assert.That(context.Initiator, Is.EqualTo(BattleInitiator.PlayerAttacked), "战后复位：之后按 E 挑战仍是正面攻击");
            Assert.That(bossObject.transform.position, Is.EqualTo(HomePosition), "撤退后 BOSS 在原位");
        }

        // ────────────────────── 拴绳 ──────────────────────

        [Test]
        public void Charging_BeyondLeash_ReturnsHomeThenIdlesWhenPlayerAway()
        {
            Bind(); // 拴绳 0 = 半径 × 2 = 10 米
            StartCharging();

            PlacePlayer(LureSpot); // 玩家往远处跑：BOSS 一路追
            Tick(3, 1f);
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Charging), "前置：9 米，还在拴绳内");
            aggro.Tick(1f);         // 12 米 > 10 米
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Returning), "出了拴绳就放弃");

            Tick(5, 1f);

            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Idle), "到原位时玩家不在发难半径里 → 待机");
            Assert.That(bossObject.transform.position, Is.EqualTo(HomePosition));
            Assert.That(world.InteractCalls, Is.EqualTo(0));
        }

        [Test]
        public void Charging_BeyondExplicitLeash_PlayerBackInRadius_DisarmsOnArrival()
        {
            // 拴绳填正数就用填的值（这里 4 米）；回到原位时玩家又在发难半径里 → 解除武装，不立刻再警觉。
            Bind(Aggro(leash: 4f));
            StartCharging();
            PlacePlayer(LureSpot);

            Tick(2, 1f); // 3 米 → 6 米 > 4 米
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Returning));

            PlacePlayer(LingerSpot);
            Tick(5, 1f);

            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Disarmed));
            Assert.That(bossObject.transform.position, Is.EqualTo(HomePosition));
        }

        // ────────────────────── ④ 重新武装 ──────────────────────

        [Test]
        public void Disarmed_PlayerStillInAwareness_DoesNotAlertAgain()
        {
            FinishBattleAndDisarm();

            PlacePlayer(ChallengeSpot);
            Tick(10, 0.5f);
            PlacePlayer(OutsideRadiusSpot); // 出了发难半径，但仍在察觉范围里
            Tick(5, 0.5f);
            PlacePlayer(LingerSpot);
            Tick(10, 0.5f);

            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Disarmed));
            Assert.That(bossObject.transform.position, Is.EqualTo(HomePosition));
            Assert.That(world.InteractCalls, Is.EqualTo(1), "只有最初那一次");
        }

        [Test]
        public void Disarmed_PlayerLeavesThenReenters_AlertsAgain()
        {
            FinishBattleAndDisarm();

            PlacePlayer(FarSpot);
            aggro.Tick(0f);
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Idle), "离开察觉范围 → 重新武装");

            PlacePlayer(LingerSpot);
            aggro.Tick(0f);

            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Alerted));
        }

        // ────────────────────── ③ 守卫 ──────────────────────

        [TestCase(BossAggroGuard.Dialogue)]
        [TestCase(BossAggroGuard.Performance)]
        [TestCase(BossAggroGuard.Battle)]
        [TestCase(BossAggroGuard.WorldPaused)]
        [TestCase(BossAggroGuard.NarrativeBusy)]
        [TestCase(BossAggroGuard.NarrativeUnavailable)]
        public void Idle_GuardBlocks_DoesNotAlertOrCharge(BossAggroGuard guard)
        {
            Bind();
            world.Guard = guard;
            PlacePlayer(LingerSpot);

            Tick(10, 0.5f);

            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Idle));
            Assert.That(bossObject.transform.position, Is.EqualTo(HomePosition));
            Assert.That(world.InteractCalls, Is.EqualTo(0));
            Assert.That(context.Initiator, Is.EqualTo(BattleInitiator.PlayerAttacked));
        }

        [Test]
        public void Alerted_GuardBlocks_PausesReactionTimer()
        {
            Bind();
            PlacePlayer(LingerSpot);
            aggro.Tick(0f);
            aggro.Tick(1f);

            world.Guard = BossAggroGuard.Dialogue;
            Tick(10, 1f);
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Alerted));
            Assert.That(aggro.ReactionElapsed, Is.EqualTo(1f), "挡着时计时不走");

            world.Guard = BossAggroGuard.None;
            aggro.Tick(1.5f);
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Alerted), "1 + 1.5 < 3");
            aggro.Tick(0.5f);

            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Charging));
        }

        [Test]
        public void Charging_GuardBlocks_HoldsPositionThenResumes()
        {
            Bind();
            StartCharging();
            aggro.Tick(0.1f);
            Vector3 before = bossObject.transform.position;

            world.Guard = BossAggroGuard.Dialogue;
            Tick(5, 0.1f);
            Assert.That(bossObject.transform.position, Is.EqualTo(before), "被挡住时原地停住");
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Charging), "不回待机（免得停在半路成了新原位）");

            world.Guard = BossAggroGuard.None;
            aggro.Tick(0.1f);

            Assert.That(bossObject.transform.position.z, Is.GreaterThan(before.z), "放行后接着冲");
        }

        [Test]
        public void Tick_BossObjectInactive_DoesNotAlertOrCharge()
        {
            // 被击败：NarrativeFlagVisibility 关掉整个物体。
            Bind();
            bossObject.SetActive(false);
            PlacePlayer(LingerSpot);

            Tick(10, 0.5f);

            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Idle));
            Assert.That(world.InteractCalls, Is.EqualTo(0));
        }

        [Test]
        public void Idle_BattleWithAnotherBoss_OnlyBlocksAsGuard()
        {
            // 别的仗（实时遭遇升级出来的）只是守卫，不算「在跟我打」：不进战斗中、战后也不解除武装。
            Bind();
            world.Guard = BossAggroGuard.Battle;
            world.FightingBossId = "monster_1003";
            PlacePlayer(LingerSpot);

            aggro.Tick(0f);
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Idle));

            world.Guard = BossAggroGuard.None;
            world.FightingBossId = null;
            aggro.Tick(0f);

            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Alerted));
        }

        // ────────────────────── ⑤ 回归 ──────────────────────

        [Test]
        public void Bind_AggroDisabledInRoster_NeverMovesOrInteracts()
        {
            Bind(new BossDefinition(BossId, "测试 BOSS", 12, 0, aggroEnabled: false));
            PlacePlayer(LingerSpot);

            Tick(20, 0.5f);
            PlacePlayer(ChallengeSpot);
            Tick(20, 0.5f);

            Assert.That(aggro.IsArmed, Is.False);
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Idle));
            Assert.That(bossObject.transform.position, Is.EqualTo(HomePosition));
            Assert.That(world.InteractCalls, Is.EqualTo(0));
            Assert.That(context.Initiator, Is.EqualTo(BattleInitiator.PlayerAttacked));
        }

        [Test]
        public void Bind_BossNotInRoster_IsInert()
        {
            Bind(null);
            PlacePlayer(LingerSpot);

            Tick(10, 0.5f);

            Assert.That(aggro.IsBound, Is.True);
            Assert.That(aggro.IsArmed, Is.False);
            Assert.That(bossObject.transform.position, Is.EqualTo(HomePosition));
            Assert.That(LinesContaining("boss_aggro_bound"), Is.EqualTo(1));
            Assert.That(sink.Lines.Single(l => l.Contains("boss_aggro_bound")), Does.Contain("boss_not_found"));
        }

        [Test]
        public void Bind_AggroRadiusWithinInteractRadius_WarnsButStaysArmed()
        {
            // 名册里校验不了交互半径（它在场景的 NarrativeTrigger 上），绑定时告警 + W 级埋点；照样武装。
            LogAssert.Expect(LogType.Warning, new Regex("发难半径 1.5 米不大于交互半径 2 米"));

            Bind(Aggro(radius: 1.5f));

            Assert.That(aggro.IsArmed, Is.True);
            Assert.That(LinesContaining("W battle/boss_aggro_bound"), Is.EqualTo(1));
            Assert.That(sink.Lines.Single(l => l.Contains("boss_aggro_bound")), Does.Contain("radius_within_interact"));
        }

        [Test]
        public void Idle_ChallengedByPlayer_KeepsPlayerInitiatorAndDisarmsAfterBattle()
        {
            // 玩家按 E 挑战开的仗：理由不动（正面攻击、玩家先手），战后一样解除武装，免得撤退后站在半径里马上又被警觉。
            Bind();
            PlacePlayer(ChallengeSpot);
            world.FightingBossId = BossId;

            aggro.Tick(0f);
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.InBattle));
            Assert.That(context.Initiator, Is.EqualTo(BattleInitiator.PlayerAttacked));

            world.FightingBossId = null;
            Tick(10, 0.5f);

            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Disarmed));
            Assert.That(world.InteractCalls, Is.EqualTo(0));
        }

        // ────────────────────── ⑥ 头顶提示 ──────────────────────

        [Test]
        public void Indicator_ShownWhileAlertedAndCharging_HiddenOtherwise()
        {
            var indicator = new GameObject("MarkerAggro");
            indicator.transform.SetParent(bossObject.transform, false);
            SetAggroField("alertIndicator", p => p.objectReferenceValue = indicator);
            var pending = new UniTaskCompletionSource<bool>();
            world.OnInteract = () => pending.Task;

            Bind();
            Assert.That(indicator.activeSelf, Is.False, "绑定时先关上（场景里忘了默认关也不会一直亮）");

            PlacePlayer(LingerSpot);
            aggro.Tick(0f);
            Assert.That(indicator.activeSelf, Is.True, "警觉：亮");
            aggro.Tick(ReactionSeconds);
            Assert.That(indicator.activeSelf, Is.True, "冲刺：亮");
            aggro.Tick(1f);
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Contact));
            Assert.That(indicator.activeSelf, Is.False, "碰到了：关");

            pending.TrySetResult(false);
            aggro.Tick(0f);
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Disarmed));
            Assert.That(indicator.activeSelf, Is.False, "解除武装：关");
        }

        [Test]
        public void Indicator_NotAssigned_FullCycleDoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                Bind();
                PlacePlayer(LingerSpot);
                aggro.Tick(0f);               // 警觉
                aggro.Tick(ReactionSeconds);  // 冲刺
                aggro.Tick(1f);               // 接触（默认受理但没开成仗）
                aggro.Tick(0f);               // 解除武装
                PlacePlayer(FarSpot);
                Tick(5, 1f);                  // 回到待机
            });
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Idle));
        }

        // ────────────────────── ⑦ 埋点 ──────────────────────

        [Test]
        public void Blocked_SameGuardPersisting_TracksOnce_NewBlockTracksAgain()
        {
            Bind();
            PlacePlayer(LingerSpot);
            world.Guard = BossAggroGuard.Dialogue;

            Tick(5, 0.1f);
            Assert.That(LinesContaining("boss_aggro_blocked"), Is.EqualTo(1), "持续挡着只记一条");

            world.Guard = BossAggroGuard.None;
            aggro.Tick(0.1f); // 放行 → 警觉
            world.Guard = BossAggroGuard.Performance;
            Tick(3, 0.1f);

            Assert.That(LinesContaining("boss_aggro_blocked"), Is.EqualTo(2), "放行后又被挡，再记一条");
            Assert.That(sink.Lines.Last(l => l.Contains("boss_aggro_blocked")), Does.Contain("performance"));
        }

        [Test]
        public void StateTransitions_TrackedOncePerTransition_WithReasons()
        {
            Bind();
            PlacePlayer(LingerSpot);

            aggro.Tick(0f);          // idle → alerted
            Tick(3, 0.01f);          // 警觉中、计时没到：没有迁移
            Assert.That(LinesContaining("I battle/boss_aggro_state"), Is.EqualTo(1));
            Assert.That(sink.Lines.Single(l => l.Contains("boss_aggro_state")),
                Does.Contain("\"from\":\"idle\"").And.Contain("\"to\":\"alerted\"").And.Contain("player_in_aggro_radius"));

            aggro.Tick(ReactionSeconds); // alerted → charging

            Assert.That(LinesContaining("I battle/boss_aggro_state"), Is.EqualTo(2));
            Assert.That(sink.Lines.Last(l => l.Contains("boss_aggro_state")),
                Does.Contain("\"to\":\"charging\"").And.Contain("reaction_elapsed"));
        }

        // ────────────────────── ⑧ 串到真实 BattleFlow ──────────────────────

        [Test]
        public void PressE_DuringReaction_ThroughBattleFlow_OpensFrontalAttackAndDoesNotCharge()
        {
            BuildFlowChain();
            Bind();
            PlacePlayer(ChallengeSpot);
            aggro.Tick(0f);
            aggro.Tick(1f);
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Alerted), "前置：警觉、反应时间没到");

            bus.Publish(Stage()); // 玩家按 E 挑战：剧情走完嘲讽对白、停到 Battle 阶段

            BattleEntryDecision entry = presenter.LastOpening.Value.Session.Entry;
            Assert.That(entry.Kind, Is.EqualTo(BattleEntryKind.FrontalAttack), "反应时间内按 E = 正面攻击");
            Assert.That(entry.Initiative, Is.EqualTo(BattleInitiative.Player), "玩家先手");
            aggro.Tick(ReactionSeconds);
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.InBattle), "进战斗中，不再冲");
            Assert.That(world.InteractCalls, Is.EqualTo(0));
            Assert.That(context.Initiator, Is.EqualTo(BattleInitiator.PlayerAttacked));
        }

        [Test]
        public void Linger_PastReaction_ThroughBattleFlow_ChargesAndOpensAmbushedWithBossFirst()
        {
            BuildFlowChain();
            world.OnInteract = () =>
            {
                bus.Publish(Stage()); // 与按 E 同一条入口：假剧情直接发 Battle 阶段
                return UniTask.FromResult(true);
            };
            Bind();
            PlacePlayer(LingerSpot);

            aggro.Tick(0f);              // 警觉
            aggro.Tick(ReactionSeconds); // 磨蹭满 3 秒 → 冲
            aggro.Tick(1f);              // 冲到接触距离 → 置理由 → 交互 → BattleFlow 开仗

            Assert.That(presenter.LastOpening.HasValue, Is.True, "前置：这一仗开了");
            BattleEntryDecision entry = presenter.LastOpening.Value.Session.Entry;
            Assert.That(entry.Kind, Is.EqualTo(BattleEntryKind.Ambushed), "BOSS 冲上来碰到 = 被打");
            Assert.That(entry.Initiative, Is.EqualTo(BattleInitiative.Boss), "BOSS 先手");

            aggro.Tick(0f);
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.InBattle));
            aggro.Tick(0f); // 战斗场景在场（停在等玩家出招）
            Assert.That(bossObject.transform.position, Is.EqualTo(HomePosition), "世界被挡着时回到原位");

            flow.Dispose(); // 收场（这里用取消代替打完）
            aggro.Tick(0f);

            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Disarmed));
            Assert.That(context.Initiator, Is.EqualTo(BattleInitiator.PlayerAttacked));
        }

        // ────────────────────── 夹具 ──────────────────────

        private static BossDefinition Aggro(float radius = AggroRadius, float leash = 0f) =>
            new BossDefinition(BossId, "测试 BOSS", 12, 0, aggroEnabled: true, aggroChargeSpeed: ChargeSpeed,
                aggroRadius: radius, aggroReactionSeconds: ReactionSeconds, aggroLeashDistance: leash);

        private void Bind() => Bind(Aggro());

        private void Bind(BossDefinition definition) => aggro.Bind(world, definition, telemetryService.Scope("battle"));

        private void Tick(int frames, float deltaTime)
        {
            for (int i = 0; i < frames; i++) aggro.Tick(deltaTime);
        }

        /// <summary>玩家逻辑位置（局势按它判档位）与世界位置（BOSS 量半径、朝它冲）一起摆，地面高度同 BOSS。</summary>
        private void PlacePlayer(Vector2 logic)
        {
            player.Restore(new PlayerSaveData { Health = 3, PositionX = logic.x, PositionY = logic.y });
            world.PlayerPosition = new Vector3(logic.x, HomePosition.y, logic.y);
        }

        /// <summary>磨蹭位进半径 → 警觉 → 计满反应时间 → 冲刺。</summary>
        private void StartCharging()
        {
            PlacePlayer(LingerSpot);
            aggro.Tick(0f);
            aggro.Tick(ReactionSeconds);
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Charging), "前置：警觉满反应时间后开始冲刺");
        }

        /// <summary>冲到接触距离（离原位 2.8 米处，玩家在 4 米处）。</summary>
        private void ReachContact()
        {
            StartCharging();
            aggro.Tick(1f);
            Assert.That(world.InteractCalls, Is.EqualTo(1), "前置：已接触并发起交互");
        }

        private void EnterBattleByContact()
        {
            Bind();
            world.OnInteract = () =>
            {
                world.FightingBossId = BossId;
                return UniTask.FromResult(true);
            };
            ReachContact();
            aggro.Tick(0f);
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.InBattle), "前置：进入战斗中");
        }

        private void FinishBattleAndDisarm()
        {
            EnterBattleByContact();
            world.WorldHidden = true;
            aggro.Tick(0f);
            world.WorldHidden = false;
            world.FightingBossId = null;
            aggro.Tick(0f);
            Assert.That(aggro.State, Is.EqualTo(BossAggroState.Disarmed), "前置：打完解除武装");
        }

        private float HorizontalDistanceToPlayer()
        {
            Vector3 delta = world.PlayerPosition - bossObject.transform.position;
            delta.y = 0f;
            return delta.magnitude;
        }

        private int LinesContaining(string fragment) => sink.Lines.Count(line => line.Contains(fragment));

        private static StageEvent Stage() => new StageEvent(3, 7, "sample_boss_npc", "fight", BossId);

        /// <summary>按 Inspector 的接法写 BossAggro 的私有序列化字段（不给生产代码开 setter）。</summary>
        private void SetAggroField(string field, Action<SerializedProperty> write)
        {
            var serialized = new SerializedObject(aggro);
            write(serialized.FindProperty(field));
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>XZ 平面的投影（同 SampleScene：EncounterSceneView.useXZPlane = true，按 Inspector 的接法写私有字段）。</summary>
        private EncounterSceneView XZProjection()
        {
            EncounterSceneView view = bossObject.AddComponent<EncounterSceneView>();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("useXZPlane").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return view;
        }

        /// <summary>
        /// 真实 BattleFlow（其余口用 BattleFakes，同 BattleFlowTests 的装法）：局势来源就是这只 BOSS 的 BossEntryContext，
        /// 表现层停在等玩家出招（仗一直在打）。假外部世界的「在不在跟它打 / 世界是否被挡」改问真流程与真战斗场景宿主。
        /// </summary>
        private void BuildFlowChain()
        {
            bus = new BattleFakes.Bus<StageEvent>();
            presenter = new BattleFakes.Presenter { BlockOnCommand = true };
            input = new InputService();
            input.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            var items = new BattleItemInventory(new BattleFakes.Backpack());
            var roster = new BossRoster(new[] { Aggro() });
            arena = new BattleArena(new BattleFakes.Curtain(), new BattleFakes.Assets(), NullTelemetryScope.Instance);
            flow = new BattleFlow(bus, roster, new BattleFakes.Narrative(), new BattleFakes.Gate(),
                new BattleWorldLock(new BattleFakes.Pause(), input, new BattleFakes.UI()), arena,
                new BattleSetup(BattleSettings.PlaceholderDefault, () => 42UL, items), items, presenter,
                NullTelemetryScope.Instance);
            flow.BindEntryContextProvider(_ => context);
            flow.Start();

            world.FightingProbe = id => flow.IsFightingBoss(id);
            world.HiddenProbe = () => arena.IsLoaded;
        }

        /// <summary>假外部世界：守卫、在不在跟它打、世界是否被挡、玩家位置都由用例直接摆；交互记次数并按脚本回应。</summary>
        private sealed class FakeWorld : IBossAggroWorld
        {
            public bool IsAvailable { get; set; } = true;
            public BossAggroGuard Guard { get; set; }
            public string FightingBossId { get; set; }
            public Func<string, bool> FightingProbe { get; set; }
            public bool WorldHidden { get; set; }
            public Func<bool> HiddenProbe { get; set; }
            public bool HasPlayer { get; set; } = true;
            public Vector3 PlayerPosition { get; set; }
            public bool InteractResult { get; set; } = true;
            public Func<UniTask<bool>> OnInteract { get; set; }
            public int InteractCalls { get; private set; }

            public bool IsWorldHiddenByBattle => HiddenProbe != null ? HiddenProbe() : WorldHidden;

            public BossAggroGuard CheckGuards() => Guard;

            public bool IsFighting(string bossId) =>
                FightingProbe != null ? FightingProbe(bossId) : FightingBossId != null && FightingBossId == bossId;

            public bool TryGetPlayerPosition(out Vector3 position)
            {
                position = PlayerPosition;
                return HasPlayer;
            }

            public UniTask<bool> InteractAsync(NarrativeTrigger trigger, CancellationToken ct)
            {
                InteractCalls++;
                return OnInteract != null ? OnInteract() : UniTask.FromResult(InteractResult);
            }
        }
    }
}
