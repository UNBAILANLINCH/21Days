// 职责：钉住 BattleFlow 的**遭遇版**（PRP/combat-iteration D5「实时遭遇升级回合制」）：
//   ① 遭遇版不触碰剧情（TryBeginBattle / CompleteBattleAsync / ReleaseBattle 零调用）；
//   ② 已有一场仗在途时拒绝升级（Monster 侧逐字走原路径）；只认名册里配了数据的种类；
//   ③ 打完在收场黑幕下回调遭遇的 CompleteEscalation **恰好一次**（胜：怪死 + 血量按比例写回；负：生命归零）；
//   ④ 三种开战方式的先手与开局扣血（偷袭 / 正面攻击 / 被打）；中途取消（作用域销毁）让遭遇退出在途、交还暂停，
//     不碰 UI / 输入（UI 服务已先释放时也不抛，2026-10-15 W6a 补修）。
// 为什么新建：`BattleFlowTests` 钉的是剧情版（那一组原样保留作回归），遭遇版是本波新加的第二个外壳；
//   一个外壳一组用例，混在一个文件里会让「剧情版逐字不变」这件事看不清。
// 夹具：剧情 / 门闸 / 表现 / 黑幕 / 资源 / UI / 暂停用 BattleFakes（同步完成——整场仗在发起升级的那次调用里就同步打完）；
//   遭遇用真 EncounterStep，主怪接**真表**的种类 1003「市令」（名册按这个种类配一行）；埋点 battle / monster 两个作用域记进同一个 sink。
using System;
using System.Linq;
using System.Threading;
using Game.Battle;
using Game.Core.Config;
using Game.Core.Input;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Core.UI;
using Game.Mirror;
using Game.Monster;
using Game.Player;
using Game.Tests.EditMode.Core;
using Game.Tests.EditMode.Telemetry;
using Game.TurnBased;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using StageEvent = Game.Narrative.BattleStageEnteredEvent;

namespace Game.Tests.EditMode.Battle
{
    public sealed class BattleFlowEncounterTests
    {
        private const string NarrativeBossId = "sample_boss";
        private const string MonsterRowId = "shiling";
        private const int ShilingKindId = 1003;

        /// <summary>怪在玩家右侧 0.5 米、背对玩家：巡逻态 = 未察觉，玩家面朝右一刀打得到。</summary>
        private static readonly Vector2[] AheadUnaware = { new Vector2(0.5f, 0f), new Vector2(10f, 0f) };

        /// <summary>怪在玩家右侧 0.5 米、朝玩家（红区）：第一 tick 就转敌对并出手。</summary>
        private static readonly Vector2[] AdjacentFacing = { new Vector2(0.5f, 0f), new Vector2(-10f, 0f) };

        private BattleFakes.Bus<StageEvent> bus;
        private BattleFakes.Narrative narrative;
        private BattleFakes.Gate gate;
        private BattleFakes.Presenter presenter;
        private BattleFakes.Curtain curtain;
        private BattleFakes.Assets assets;
        private BattleFakes.UI ui;
        private BattleFakes.Pause pause;
        private BattleFakes.Backpack backpack;
        private InputService input;
        private RecordingTelemetrySink sink;
        private TelemetryService telemetry;
        private BattleFlow flow;

        private PlayerConfig playerConfig;
        private MonsterConfig monsterConfig;
        private PlayerRules player;
        private MonsterRules monster;
        private EncounterStep step;

        [SetUp]
        public void SetUp()
        {
            bus = new BattleFakes.Bus<StageEvent>();
            narrative = new BattleFakes.Narrative();
            gate = new BattleFakes.Gate();
            presenter = new BattleFakes.Presenter();
            curtain = new BattleFakes.Curtain();
            assets = new BattleFakes.Assets();
            ui = new BattleFakes.UI();
            pause = new BattleFakes.Pause();
            backpack = new BattleFakes.Backpack();
            input = new InputService();
            input.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            sink = new RecordingTelemetrySink();
            telemetry = new TelemetryService(TelemetryOptions.Default, new FakeTelemetryClock(), sink);
            playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
            monsterConfig = ScriptableObject.CreateInstance<MonsterConfig>();
            var serialized = new SerializedObject(monsterConfig);
            serialized.FindProperty("escalateOnSneakStrike").boolValue = true;
            serialized.FindProperty("escalateOnFrontalStrike").boolValue = true;
            serialized.FindProperty("escalateWhenStruck").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            flow?.Dispose();
            flow = null;
            input?.Dispose();
            telemetry?.Dispose();
            telemetry = null;
            UnityEngine.Object.DestroyImmediate(playerConfig);
            UnityEngine.Object.DestroyImmediate(monsterConfig);
        }

        // ──────────────────────── ① 不碰剧情 + ③ 回调恰好一次 ────────────────────────

        /// <summary>
        /// 打赢：遭遇版不调剧情三方法；收场黑幕下回调遭遇恰好一次——怪（killable = false 的市令）死亡、
        /// 实时血量按战斗剩余比例写回（10 / 10 → 3）；世界还原、在途清掉。
        /// </summary>
        [Test]
        public void EscalationVictory_LeavesNarrativeUntouched_AndCompletesTheEncounterOnce()
        {
            Build(VictorySettings(), monsterHealth: 1);
            NewEncounter(AheadUnaware);
            presenter.Enqueue(BattleCommand.CastSkill(PlayerSkill.Skill1));

            Tick(InputCommand.ButtonAttack);

            Assert.That(narrative.BeginCalls, Is.Zero, "不登记剧情在途");
            Assert.That(narrative.ReleaseCalls, Is.Zero);
            Assert.That(narrative.Completed, Is.Empty, "不回写剧情");
            Assert.That(presenter.Opens, Is.EqualTo(1));
            Assert.That(presenter.Closes, Is.EqualTo(1));
            Assert.That(monster.Model.Health, Is.Zero, "打赢视同击杀，无视 killable");
            Assert.That(player.Model.Health, Is.EqualTo(playerConfig.MaxHealth), "满血打赢 → 满血写回");
            Assert.That(step.IsEscalating, Is.False);
            Assert.That(flow.IsBattleRunning, Is.False);
            Assert.That(CountOf("monster/escalation_completed"), Is.EqualTo(1), "恰好回调一次");
            Assert.That(CountOf("monster/escalation_completion_ignored"), Is.Zero);
            Assert.That(CountOf("monster/escalation_aborted"), Is.Zero);
            Assert.That(LastLineFor("battle/battle_entry"), Does.Contain("\"source\":\"escalation\""));
            AssertWorldRestored();
        }

        /// <summary>打输：遭遇的实时生命置 0（走既有的失败流程），剧情照样一个字都不碰。</summary>
        [Test]
        public void EscalationDefeat_ZeroesLiveHealth_AndLeavesNarrativeUntouched()
        {
            Build(DefeatSettings(), monsterHealth: 100);
            NewEncounter(AdjacentFacing);

            Tick(); // 怪出手 → 被打（怪物先手），它第一招就打掉本场玩家 120 血

            Assert.That(presenter.LastOpening.Value.Session.Entry.Kind, Is.EqualTo(BattleEntryKind.Ambushed));
            Assert.That(player.Model.Health, Is.Zero);
            Assert.That(monster.Model.Health, Is.GreaterThan(0));
            Assert.That(narrative.BeginCalls + narrative.ReleaseCalls + narrative.Completed.Count, Is.Zero);
            Assert.That(CountOf("monster/escalation_completed"), Is.EqualTo(1));
            AssertWorldRestored();
        }

        // ──────────────────────── ② 拒绝 ────────────────────────

        /// <summary>
        /// 已有一场剧情 BOSS 战在途（停在等出招）：升级被拒（battle_busy），遭遇不进在途、照原路径走；
        /// 正在打的那一仗不受影响。直接问端口也是同一个答案。
        /// </summary>
        [Test]
        public void EscalationWhileBattleRunning_IsRejectedAsBusy()
        {
            Build(VictorySettings(), monsterHealth: 1);
            NewEncounter(AheadUnaware);
            presenter.BlockOnCommand = true;
            bus.Publish(new StageEvent(3, 7, "boss_npc", "fight", NarrativeBossId));
            Assert.That(flow.IsBattleEngaged, Is.True, "前提：剧情 BOSS 战停在等出招");

            Tick(InputCommand.ButtonAttack);

            Assert.That(step.IsEscalating, Is.False);
            Assert.That(LastLineFor("monster/escalation_requested"), Does.Contain("\"reject\":\"battle_busy\""));
            Assert.That(presenter.Opens, Is.EqualTo(1), "没有开第二场");
            Assert.That(narrative.BeginCalls, Is.EqualTo(1));
            Assert.That(narrative.ReleasedStages, Is.Empty, "正在打的那一仗没被放弃");

            var request = new EncounterEscalationRequest("enemy", ShilingKindId, EncounterEscalationTrigger.PlayerHitAware, 3, 3);
            Assert.That(flow.TryEscalate(step, in request, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo(BattleFlow.EscalationRejectBusy));
        }

        /// <summary>只认名册里配了数据的种类：0（没接种类表）与没配的种类都答「不能升级」，直接发起也被拒。</summary>
        [Test]
        public void CanEscalate_OnlyKindsWithBattleData()
        {
            Build(VictorySettings(), monsterHealth: 1);
            NewEncounter(AheadUnaware);

            Assert.That(flow.CanEscalate(ShilingKindId), Is.True);
            Assert.That(flow.CanEscalate(1001), Is.False, "巡夜人不配");
            Assert.That(flow.CanEscalate(0), Is.False);
            var request = new EncounterEscalationRequest("enemy", 1001, EncounterEscalationTrigger.PlayerHitAware, 3, 3);
            Assert.That(flow.TryEscalate(step, in request, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo(BattleFlow.EscalationRejectNotConfigured));
            Assert.That(flow.IsBattleRunning, Is.False);
        }

        /// <summary>没有战斗表现（纯测试作用域）：答「不可用」，不开仗。</summary>
        [Test]
        public void TryEscalate_WithoutPresenter_IsUnavailable()
        {
            Build(VictorySettings(), monsterHealth: 1, withPresenter: false);
            NewEncounter(AheadUnaware);
            var request = new EncounterEscalationRequest("enemy", ShilingKindId, EncounterEscalationTrigger.PlayerHitAware, 3, 3);

            Assert.That(flow.TryEscalate(step, in request, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo(BattleFlow.EscalationRejectUnavailable));
            Assert.That(pause.IsPaused, Is.False);
        }

        // ──────────────────────── ④ 三种开战方式 ────────────────────────

        /// <summary>玩家打中未察觉的怪 → 偷袭：玩家先手，怪物开战前先掉 20%（10 → 8）。</summary>
        [Test]
        public void SneakStrike_OpensSneakAttack_PlayerFirst_MonsterLosesTwentyPercent()
        {
            Build(VictorySettings(), monsterHealth: 10);
            NewEncounter(AheadUnaware);
            presenter.BlockOnCommand = true;

            Tick(InputCommand.ButtonAttack);

            BattleSession session = presenter.LastOpening.Value.Session;
            Assert.That(session.Entry.Kind, Is.EqualTo(BattleEntryKind.SneakAttack));
            Assert.That(session.Entry.Initiative, Is.EqualTo(BattleInitiative.Player));
            Assert.That(session.Boss.Health, Is.EqualTo(8));
            Assert.That(session.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            Assert.That(session.Player.Health, Is.EqualTo(120), "实时 3 / 3 → 战斗 120 / 120（本场玩家生命，sp07:129-133）");
            Assert.That(step.IsEscalating, Is.True, "还在打：遭遇冻结在途");
        }

        /// <summary>玩家打中已敌对的怪 → 正面攻击：玩家先手、不扣血。</summary>
        [Test]
        public void FrontalStrike_OpensFrontalAttack_PlayerFirst()
        {
            Build(VictorySettings(), monsterHealth: 10);
            NewEncounter(AheadUnaware);
            monster.Provoke(Vector2.zero);
            presenter.BlockOnCommand = true;

            Tick(InputCommand.ButtonAttack);

            BattleSession session = presenter.LastOpening.Value.Session;
            Assert.That(session.Entry.Kind, Is.EqualTo(BattleEntryKind.FrontalAttack));
            Assert.That(session.Entry.Initiative, Is.EqualTo(BattleInitiative.Player));
            Assert.That(session.Boss.Health, Is.EqualTo(10));
        }

        /// <summary>怪打中玩家 → 被打：怪物先手（开场先出一招），实时那一下没扣血。</summary>
        [Test]
        public void Struck_OpensAmbushed_MonsterFirst()
        {
            Build(VictorySettings(), monsterHealth: 10);
            NewEncounter(AdjacentFacing);
            presenter.BlockOnCommand = true;

            Tick();

            BattleSession session = presenter.LastOpening.Value.Session;
            Assert.That(session.Entry.Kind, Is.EqualTo(BattleEntryKind.Ambushed));
            Assert.That(session.Entry.Initiative, Is.EqualTo(BattleInitiative.Boss));
            Assert.That(session.Boss.Health, Is.EqualTo(10), "被打不扣怪物的血");
            Assert.That(session.Player.Health, Is.EqualTo(119), "怪物先手打了一招（本例把 BOSS 招式 1 调成 1 点），轮到玩家时剩 119");
            Assert.That(player.Model.Health, Is.EqualTo(playerConfig.MaxHealth), "实时那一下交给了回合制");
        }

        /// <summary>升级时实时血量按比例进战斗：实时 1 / 3 → 战斗 ceil(120 / 3) = 40。</summary>
        [Test]
        public void Escalation_LowLiveHealth_EntersBattleByRatio()
        {
            Build(VictorySettings(), monsterHealth: 10);
            NewEncounter(AheadUnaware);
            player.SetHealth(1);
            presenter.BlockOnCommand = true;

            Tick(InputCommand.ButtonAttack);

            Assert.That(presenter.LastOpening.Value.Session.Player.Health, Is.EqualTo(40));
            Assert.That(presenter.LastOpening.Value.Session.Player.MaxHealth, Is.EqualTo(BossDefinition.DefaultPlayerHealth));
        }

        /// <summary>
        /// 打到一半被取消（作用域销毁）：遭遇退出在途、血量不动，暂停交还、不留黑屏，剧情照样没碰；
        /// **不再去还原 HUD 层与 Gameplay 图**（2026-10-15 规格变更，W6a 补修：那时 UI / 输入服务也在销毁，见 BattleFlow 文件头）。
        /// </summary>
        [Test]
        public void Cancelled_MidBattle_AbortsEscalation_ReleasesPause_LeavesUiAlone()
        {
            Build(VictorySettings(), monsterHealth: 10);
            NewEncounter(AheadUnaware);
            presenter.BlockOnCommand = true;
            Tick(InputCommand.ButtonAttack);
            Assert.That(pause.IsPaused, Is.True, "前提：世界按住");
            int monsterHealth = monster.Model.Health;
            int layerCallsBefore = ui.LayerCalls;

            flow.Dispose();

            Assert.That(step.IsEscalating, Is.False);
            Assert.That(player.Model.Health, Is.EqualTo(playerConfig.MaxHealth));
            Assert.That(monster.Model.Health, Is.EqualTo(monsterHealth));
            Assert.That(CountOf("monster/escalation_aborted"), Is.EqualTo(1));
            Assert.That(CountOf("monster/escalation_completed"), Is.Zero);
            Assert.That(narrative.ReleaseCalls, Is.Zero);
            Assert.That(pause.IsPaused, Is.False, "暂停令牌已交还");
            Assert.That(curtain.IsCovered, Is.False, "不留黑屏");
            Assert.That(ui.LayerCalls, Is.EqualTo(layerCallsBefore), "销毁那一刻起不再碰 UI 层");
        }

        /// <summary>
        /// 实机的那一种释放顺序（W6a 回放实测：UI 服务先释放，收场里 SetLayerVisible 抛 ObjectDisposedException，被记成 battle_failed）：
        /// 先模拟 UI 服务已销毁，再销毁 BattleFlow——收场不抛、不记 battle_failed、不再调 UI，遭遇照样退出在途、暂停交还。
        /// </summary>
        [Test]
        public void Cancelled_AfterUiServiceDisposed_AbortsEscalationWithoutThrowingOrTouchingUi()
        {
            Build(VictorySettings(), monsterHealth: 10);
            NewEncounter(AheadUnaware);
            presenter.BlockOnCommand = true;
            Tick(InputCommand.ButtonAttack);
            Assert.That(flow.IsBattleEngaged, Is.True, "前提：停在等玩家出招");
            ui.SimulateDisposed();
            int layerCallsBefore = ui.LayerCalls;

            Assert.DoesNotThrow(() => flow.Dispose());

            Assert.That(ui.LayerCalls, Is.EqualTo(layerCallsBefore), "UI 服务已销毁：收场一次都没再调它");
            Assert.That(step.IsEscalating, Is.False, "遭遇退出在途");
            Assert.That(CountOf("monster/escalation_aborted"), Is.EqualTo(1));
            Assert.That(CountOf("battle/battle_failed"), Is.Zero, "收场没出错");
            Assert.That(CountOf("battle/battle_cancelled"), Is.EqualTo(1), "记的是取消");
            Assert.That(pause.IsPaused, Is.False, "暂停令牌已交还");
            Assert.That(flow.IsBattleRunning, Is.False);
        }

        // ──────────────────────── 夹具 ────────────────────────

        private void Build(in BattleSettings settings, int monsterHealth, bool withPresenter = true)
        {
            var roster = new BossRoster(new[]
            {
                new BossDefinition(NarrativeBossId, "测试 BOSS", 1, 0),
                new BossDefinition(MonsterRowId, "市令", monsterHealth, 0, monsterKindId: ShilingKindId),
            });
            var items = new BattleItemInventory(backpack);
            var setup = new BattleSetup(settings, () => 42UL, items);
            flow = new BattleFlow(bus, roster, narrative, gate, new BattleWorldLock(pause, input, ui),
                new BattleArena(curtain, assets, NullTelemetryScope.Instance), setup, items,
                withPresenter ? presenter : null, telemetry.Scope("battle"));
            flow.Start();
        }

        private void NewEncounter(Vector2[] route)
        {
            var config = new StubConfigService(ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes()));
            var catalog = new MonsterKindCatalog(config, new YaoCatalog(config), monsterConfig.HostileRadius);
            player = new PlayerRules(playerConfig, new PlayerModel(), NullTelemetryScope.Instance);
            monster = new MonsterRules(monsterConfig, new MonsterModel(), new RandomService(3ul), NullTelemetryScope.Instance,
                kind: null, kindCatalog: catalog, kindId: ShilingKindId);
            step = new EncounterStep(player, monster);
            step.UseTelemetry(telemetry.Scope("monster"));
            step.BindEscalation(flow);
            step.Begin(Vector2.zero, route);
            Assert.That(monster.Kind, Is.Not.Null, "前提：真表里查到了种类 1003");
        }

        private void Tick(uint buttons = 0u)
        {
            var command = new InputCommand(Vector2.zero, Vector2.zero, buttons, Vector2.zero, 0);
            var context = new SimulationContext(0, 0.1f, in command, new RandomService(3ul));
            step.Step(in context);
        }

        // 胜：玩家招式 1 打 18（sp07:139），怪物生命按用例给（1 / 10 都是一下就倒）。
        // 负：怪物招式 1 一下 120 点（= 本场玩家生命，sp07:129-133），一下倒。
        // 醉酒 0、只放招式 1 → 不跳过、不抽随机数（同 BattleFlowTests 的口径）。
        private static BattleSettings VictorySettings() => WithBossSkills(new BossSkillSettings(1, 30, 10, HealthPercentBase.MaxHealth, 30, 3, 1, 1, 0, 0));

        private static BattleSettings DefeatSettings() => WithBossSkills(new BossSkillSettings(120, 30, 10, HealthPercentBase.MaxHealth, 30, 3, 1, 1, 0, 0));

        private static BattleSettings WithBossSkills(in BossSkillSettings boss)
        {
            BattleSettings basis = BattleSettings.PlaceholderDefault;
            return new BattleSettings(basis.Entry, basis.PlayerSkills, boss, basis.Drunk, basis.Flow,
                basis.ItemOncePerBattle, basis.InheritsDrunkValue, basis.Items);
        }

        private void AssertWorldRestored()
        {
            Assert.That(pause.IsPaused, Is.False, "暂停令牌已释放");
            Assert.That(input.Actions.Gameplay.enabled, Is.True, "Gameplay 图回到进来前");
            Assert.That(ui.IsLayerVisible(UILayer.Hud), Is.True, "HUD 层回到进来前");
            Assert.That(curtain.IsCovered, Is.False, "不留黑屏");
        }

        private string LastLineFor(string eventName) => sink.Lines.LastOrDefault(line => line.Contains(eventName));

        private int CountOf(string eventName) => sink.Lines.Count(line => line.Contains(eventName));

        /// <summary>只递一份现成的 <c>cfg.Tables</c>（同 EncounterDropTests 的最小假实现）。</summary>
        private sealed class StubConfigService : IConfigService
        {
            public StubConfigService(global::cfg.Tables tables) => Tables = tables;
            public global::cfg.Tables Tables { get; }
            public ulong ContentHash => throw new NotSupportedException("假配置服务不提供内容指纹");
        }
    }
}
