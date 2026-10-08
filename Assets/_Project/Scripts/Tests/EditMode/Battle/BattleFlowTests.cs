// 职责：钉住 BattleFlow 的编排——按 payload 查 BOSS（查不到报错不开仗）、胜 / 负各用正确出口键回写、
//   暂停令牌 / Gameplay 输入图 / HUD 层 / 自动存档抑制在正常结束、异常、取消三条路上都回到进来前、
//   场景缺失与表现缺失报错不吞、旧身份不开仗、战斗里用道具扣背包、剧情回写在收场黑幕下（恢复世界之后、揭幕之前）完成，
//   回写被拒 / 抛异常 / 迟迟不落定都照样揭幕；开战失败时也先恢复世界再揭幕；门闸未开时换了身份的通知顶掉旧请求；
//   回放测试口换过数值时开打留 W 级埋点。每条都配负对照。
// 为什么新建：Game.Battle 是新模块，流程是它的核心规则；一个被测类一个测试类。剧情 / 表现 / 黑幕 / 资源用 BattleFakes 的假实现，
//   输入用真实 InputService（同 DialogueServiceTests：要验 Gameplay 图的真实开关）；玩家血量按 BOSS 定义的本场玩家生命（占位 10）。
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Game.Battle;
using Game.Core.Input;
using Game.Core.Telemetry;
using Game.Core.UI;
using Game.Tests.EditMode.Telemetry;
using Game.TurnBased;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using StageEvent = Game.Narrative.BattleStageEnteredEvent;

namespace Game.Tests.EditMode.Battle
{
    public sealed class BattleFlowTests
    {
        private const string BossId = "sample_boss";
        private const int Potion = 1004;

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
        private BattleSetup setup;
        private RecordingTelemetrySink sink;
        private TelemetryService telemetryService;
        private BattleFlow flow;

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
        }

        [TearDown]
        public void TearDown()
        {
            flow?.Dispose();
            input?.Dispose();
            telemetryService?.Dispose();
            telemetryService = null; // NUnit 一个夹具实例跑全部用例，别让上一条的服务漏到下一条
            sink = null;
        }

        // ── 查 BOSS ─────────────────────────────────────────────────────────────

        [Test]
        public void StageEntered_KnownPayload_OpensBattleWithThatBoss()
        {
            Build(VictorySettings());
            presenter.Enqueue(BattleCommand.CastSkill(PlayerSkill.Skill1));

            bus.Publish(Stage(BossId));

            Assert.That(presenter.Opens, Is.EqualTo(1));
            Assert.That(presenter.LastOpening.Value.Boss.Id, Is.EqualTo(BossId));
            Assert.That(presenter.LastOpening.Value.Boss.DisplayName, Is.EqualTo("测试 BOSS"));
            Assert.That(presenter.LastOpening.Value.Session.Entry.Kind, Is.EqualTo(BattleEntryKind.FrontalAttack), "走近按 E = 正面攻击（U2）");
            Assert.That(assets.LoadedKeys, Is.EqualTo(new[] { BattleArena.SceneKey }));
            Assert.That(assets.Modes, Is.EqualTo(new[] { LoadSceneMode.Additive }), "叠加加载，不切场景（D4）");
        }

        [Test]
        public void StageEntered_UnknownPayload_LogsErrorAndDoesNotStart()
        {
            // 负对照：payload 在名册里找不到 → 报错、不登记在途、不暂停、不加载、不回写。
            Build(VictorySettings());
            LogAssert.Expect(LogType.Error, new Regex("payload「no_such_boss」在 BossRosterConfig 里找不到"));

            bus.Publish(Stage("no_such_boss"));

            Assert.That(narrative.BeginCalls, Is.EqualTo(0));
            Assert.That(pause.IsPaused, Is.False);
            Assert.That(assets.LoadedKeys, Is.Empty);
            Assert.That(presenter.Opens, Is.EqualTo(0));
            Assert.That(narrative.Completed, Is.Empty);
            Assert.That(flow.IsBattleRunning, Is.False);
        }

        // ── 胜 / 负的出口键 ──────────────────────────────────────────────────────

        [Test]
        public void Victory_CompletesWithVictoryExitKeyOnce()
        {
            Build(VictorySettings());
            presenter.Enqueue(BattleCommand.CastSkill(PlayerSkill.Skill1));

            bus.Publish(Stage(BossId));

            Assert.That(narrative.Completed, Is.EqualTo(new[] { "Victory" }), "BOSS 1 血、招式 1 打 1 → 胜，只回写一次");
            Assert.That(narrative.Completed, Has.No.Member("Downed"));
            Assert.That(flow.IsBattleRunning, Is.False);
        }

        [Test]
        public void Defeat_CompletesWithDownedExitKeyOnce()
        {
            Build(DefeatSettings());
            presenter.Enqueue(BattleCommand.CastSkill(PlayerSkill.Skill1));

            bus.Publish(Stage(BossId));

            Assert.That(narrative.Completed, Is.EqualTo(new[] { "Downed" }), "BOSS 招式 1 打 10、本场玩家 10 血 → 负，只回写一次");
            Assert.That(narrative.Completed, Has.No.Member("Victory"));
        }

        // ── 世界状态的恢复 ──────────────────────────────────────────────────────

        [Test]
        public void NormalEnd_HoldsWorldDuringBattle_ThenRestoresEverything()
        {
            Build(VictorySettings());
            presenter.Enqueue(BattleCommand.CastSkill(PlayerSkill.Skill1));
            bool pausedInside = false, gameplayInside = true, hudInside = true, savingHeldInside = false;
            presenter.OnOpen = () =>
            {
                pausedInside = pause.IsPaused;
                gameplayInside = input.Actions.Gameplay.enabled;
                hudInside = ui.IsLayerVisible(UILayer.Hud);
                savingHeldInside = narrative.Holding;
            };
            Assert.That(input.Actions.Gameplay.enabled, Is.True, "前置：Gameplay 图开着");

            bus.Publish(Stage(BossId));

            Assert.That(pausedInside, Is.True, "战斗中世界暂停");
            Assert.That(gameplayInside, Is.False, "战斗中 Gameplay 图关闭");
            Assert.That(hudInside, Is.False, "战斗中 HUD 层藏起");
            Assert.That(savingHeldInside, Is.True, "战斗中压住自动存档（剧情登记在途）");
            AssertWorldRestored(gameplayEnabled: true, hudVisible: true);
            Assert.That(curtain.Covers, Is.EqualTo(2), "开场、收场各落一次幕（D6）");
            Assert.That(curtain.Reveals, Is.EqualTo(2));
            Assert.That(curtain.IsCovered, Is.False);
            Assert.That(presenter.Closes, Is.EqualTo(1));
            Assert.That(assets.LastHandle.IsDisposed, Is.True, "战斗场景已卸载");
        }

        [Test]
        public void NormalEnd_WhenGameplayAndHudWereAlreadyOff_DoesNotTurnThemOn()
        {
            // 负对照：只恢复进来前的状态——进来前就关着的 Gameplay 图、藏着的 HUD，战后不擅自打开。
            Build(VictorySettings());
            presenter.Enqueue(BattleCommand.CastSkill(PlayerSkill.Skill1));
            input.DisableMap(InputService.GameplayMap);
            ui.SetLayerVisible(UILayer.Hud, false);

            bus.Publish(Stage(BossId));

            Assert.That(narrative.Completed, Is.EqualTo(new[] { "Victory" }));
            AssertWorldRestored(gameplayEnabled: false, hudVisible: false);
        }

        [Test]
        public void PresenterThrows_RestoresWorldReleasesBattleAndDoesNotComplete()
        {
            Build(VictorySettings());
            presenter.ThrowOnPlay = new InvalidOperationException("测试：表现层炸了");
            LogAssert.Expect(LogType.Error, new Regex("这一仗失败.*测试：表现层炸了", RegexOptions.Singleline));

            bus.Publish(Stage(BossId));

            Assert.That(narrative.Completed, Is.Empty, "没打完不回写");
            Assert.That(narrative.ReleaseCalls, Is.EqualTo(1), "放弃在途登记，剧情可重试");
            AssertWorldRestored(gameplayEnabled: true, hudVisible: true);
            Assert.That(presenter.Closes, Is.EqualTo(1), "开过场的表现照样收场");
            Assert.That(assets.LastHandle.IsDisposed, Is.True);
            Assert.That(curtain.IsCovered, Is.False, "不留黑屏");
        }

        [Test]
        public void Cancelled_WhileWaitingForCommand_RestoresWorldAndReleases()
        {
            Build(VictorySettings());
            presenter.BlockOnCommand = true;

            bus.Publish(Stage(BossId));
            Assert.That(flow.IsBattleEngaged, Is.True, "前置：停在等玩家出招");
            Assert.That(pause.IsPaused, Is.True);
            Assert.That(narrative.Holding, Is.True);

            flow.Dispose(); // 作用域销毁（退出游戏 / 退出 Play）= 取消

            Assert.That(narrative.Completed, Is.Empty);
            Assert.That(narrative.ReleaseCalls, Is.EqualTo(1));
            AssertWorldRestored(gameplayEnabled: true, hudVisible: true);
            Assert.That(presenter.Closes, Is.EqualTo(1));
            Assert.That(flow.IsBattleRunning, Is.False);
        }

        [Test]
        public void ArenaSceneMissing_LogsClearErrorAndRestoresWithoutOpening()
        {
            // W2 之前 BattleArena.unity 还不存在：报错要点名地址与修法，不能吞；世界与黑幕都还原。
            Build(VictorySettings());
            assets.Missing = true;
            LogAssert.Expect(LogType.Error, new Regex("战斗场景「BattleArena」叠加加载失败.*AssetGroups/Scenes.asset", RegexOptions.Singleline));

            bus.Publish(Stage(BossId));

            Assert.That(presenter.Opens, Is.EqualTo(0));
            Assert.That(narrative.Completed, Is.Empty);
            Assert.That(narrative.ReleaseCalls, Is.EqualTo(1));
            AssertWorldRestored(gameplayEnabled: true, hudVisible: true);
            Assert.That(curtain.IsCovered, Is.False);
        }

        [Test]
        public void ArenaEnterFails_RestoresWorldBeforeReveal()
        {
            // 开战失败（这里是场景缺失）与正常收场同序：幕下先恢复世界，再揭幕——揭幕那一刻世界已不再暂停，不露出被暂停的画面。
            Build(VictorySettings());
            assets.Missing = true;
            LogAssert.Expect(LogType.Error, new Regex("战斗场景「BattleArena」叠加加载失败.*AssetGroups/Scenes.asset", RegexOptions.Singleline));
            bool pausedAtCover = false, pausedAtReveal = true, gameplayAtReveal = false, hudAtReveal = false;
            curtain.OnCover = () => pausedAtCover = pause.IsPaused;
            curtain.OnReveal = () =>
            {
                pausedAtReveal = pause.IsPaused;
                gameplayAtReveal = input.Actions.Gameplay.enabled;
                hudAtReveal = ui.IsLayerVisible(UILayer.Hud);
            };

            bus.Publish(Stage(BossId));

            Assert.That(pausedAtCover, Is.True, "前置：落幕时世界已被按住");
            Assert.That(curtain.Reveals, Is.EqualTo(1), "只有开场那一次幕，失败后揭开");
            Assert.That(pausedAtReveal, Is.False, "揭幕时暂停令牌已释放");
            Assert.That(gameplayAtReveal, Is.True, "揭幕时 Gameplay 图已恢复");
            Assert.That(hudAtReveal, Is.True, "揭幕时 HUD 层已恢复");
            AssertWorldRestored(gameplayEnabled: true, hudVisible: true);
        }

        [Test]
        public void ArenaEnterSucceeds_OpeningRevealKeepsWorldHeld()
        {
            // 负对照：开场成功时揭的那次幕，世界必须还按着——提前恢复只许发生在开场失败的路上。
            Build(VictorySettings());
            presenter.BlockOnCommand = true;
            bool pausedAtReveal = false, gameplayAtReveal = true, hudAtReveal = true;
            curtain.OnReveal = () =>
            {
                pausedAtReveal = pause.IsPaused;
                gameplayAtReveal = input.Actions.Gameplay.enabled;
                hudAtReveal = ui.IsLayerVisible(UILayer.Hud);
            };

            bus.Publish(Stage(BossId));

            Assert.That(curtain.Reveals, Is.EqualTo(1), "前置：只揭过开场那一次，停在等玩家出招");
            Assert.That(pausedAtReveal, Is.True, "开场揭幕时世界仍暂停");
            Assert.That(gameplayAtReveal, Is.False, "开场揭幕时 Gameplay 图仍关着");
            Assert.That(hudAtReveal, Is.False, "开场揭幕时 HUD 层仍藏着");
        }

        [Test]
        public void PresenterMissing_LogsErrorAndDoesNotTouchNarrativeOrWorld()
        {
            Build(VictorySettings(), withPresenter: false);
            LogAssert.Expect(LogType.Error, new Regex("没有注册战斗表现（IBattlePresenter）"));

            bus.Publish(Stage(BossId));

            Assert.That(narrative.BeginCalls, Is.EqualTo(0), "不登记在途：剧情 Battle 阶段保持可保存、可重试");
            Assert.That(pause.IsPaused, Is.False);
            Assert.That(assets.LoadedKeys, Is.Empty);
        }

        [Test]
        public void StaleStage_NarrativeRefusesBegin_DoesNotStart()
        {
            // 负对照：剧情不认这组身份（读档后的迟到通知）→ 不开仗、不碰世界。
            Build(VictorySettings());
            narrative.AcceptBegin = false;

            bus.Publish(Stage(BossId));

            Assert.That(narrative.BeginCalls, Is.EqualTo(1));
            Assert.That(pause.IsPaused, Is.False);
            Assert.That(presenter.Opens, Is.EqualTo(0));
            Assert.That(narrative.Completed, Is.Empty);
        }

        [Test]
        public void RejectedResult_ReleasesBattleSoStageStaysRetryable()
        {
            Build(VictorySettings());
            presenter.Enqueue(BattleCommand.CastSkill(PlayerSkill.Skill1));
            narrative.AcceptComplete = false;
            bool coveredAtWriteBack = false;
            narrative.OnComplete = () => coveredAtWriteBack = curtain.IsCovered;
            LogAssert.Expect(LogType.Warning, new Regex("剧情没接受战斗结果 Victory"));

            bus.Publish(Stage(BossId));

            Assert.That(narrative.Completed, Is.EqualTo(new[] { "Victory" }));
            Assert.That(narrative.ReleaseCalls, Is.EqualTo(1), "被拒后解除在途，阶段可重打、可保存");
            Assert.That(narrative.Holding, Is.False);
            // 负对照（回写失败①被拒）：回写是在幕下发起的，被拒也照样揭幕、世界照样恢复，不留黑屏。
            Assert.That(coveredAtWriteBack, Is.True, "前置：回写在幕下发起");
            Assert.That(curtain.IsCovered, Is.False, "回写被拒也揭幕");
            Assert.That(curtain.Reveals, Is.EqualTo(2));
            AssertWorldRestored(gameplayEnabled: true, hudVisible: true);
        }

        // ── 回写与揭幕的先后（BOSS 退场在幕下完成）────────────────────────────────

        [TestCase(true, "Victory")]
        [TestCase(false, "Downed")]
        public void Finished_WritesBackUnderCurtainAfterWorldRestored_BeforeReveal(bool victory, string exitKey)
        {
            // 回写排在揭幕之前：胜利时剧情在幕下写「已击败」标记，NarrativeFlagVisibility 当场藏起 BOSS，揭幕后世界里已经没有它。
            // 被击倒那条（retreat，BOSS 还在）走同一条时序，只是结果不同。
            Build(victory ? VictorySettings() : DefeatSettings());
            presenter.Enqueue(BattleCommand.CastSkill(PlayerSkill.Skill1));
            bool coveredAtWriteBack = false, pausedAtWriteBack = true, sceneUnloadedAtWriteBack = false;
            int revealsAtWriteBack = -1;
            narrative.OnComplete = () =>
            {
                coveredAtWriteBack = curtain.IsCovered;
                revealsAtWriteBack = curtain.Reveals;
                pausedAtWriteBack = pause.IsPaused;
                sceneUnloadedAtWriteBack = assets.LastHandle.IsDisposed;
            };

            bus.Publish(Stage(BossId));

            Assert.That(narrative.Completed, Is.EqualTo(new[] { exitKey }));
            Assert.That(coveredAtWriteBack, Is.True, "回写时收场黑幕还盖着");
            Assert.That(revealsAtWriteBack, Is.EqualTo(1), "回写时只揭过开场那一次：收场的揭幕排在回写之后");
            Assert.That(sceneUnloadedAtWriteBack, Is.True, "回写前战斗场景已卸载");
            Assert.That(pausedAtWriteBack, Is.False, "回写前世界已恢复（战后接对白也看到正常世界）");
            Assert.That(curtain.IsCovered, Is.False);
            Assert.That(curtain.Reveals, Is.EqualTo(2));
            Assert.That(narrative.ReleaseCalls, Is.EqualTo(0), "回写被接受，不放弃在途登记");
        }

        [Test]
        public void WriteBackThrows_StillRevealsRestoresWorldAndReleases()
        {
            // 负对照（回写失败②抛异常）：异常要报出来、不吞，但黑幕必须揭开、世界必须恢复、在途登记解除（阶段可重打）。
            Build(VictorySettings());
            presenter.Enqueue(BattleCommand.CastSkill(PlayerSkill.Skill1));
            narrative.ThrowOnComplete = new InvalidOperationException("测试：剧情回写炸了");
            LogAssert.Expect(LogType.Error, new Regex("这一仗失败.*测试：剧情回写炸了", RegexOptions.Singleline));

            bus.Publish(Stage(BossId));

            Assert.That(narrative.Completed, Is.EqualTo(new[] { "Victory" }), "前置：确实发起了回写");
            Assert.That(curtain.IsCovered, Is.False, "回写抛异常也揭幕，不留黑屏");
            Assert.That(curtain.Reveals, Is.EqualTo(2));
            Assert.That(narrative.ReleaseCalls, Is.EqualTo(1));
            AssertWorldRestored(gameplayEnabled: true, hudVisible: true);
            Assert.That(presenter.Closes, Is.EqualTo(1));
            Assert.That(assets.LastHandle.IsDisposed, Is.True);
            Assert.That(flow.IsBattleRunning, Is.False);
        }

        [Test]
        public void WriteBackStillPending_PastCoverLimit_RevealsThenFinishesBehindCurtain()
        {
            // 回写挂着不落定（剧情正忙 / 战后接对白）：到了幕下上限（这里设 0）就先揭幕、世界照样恢复，回写在幕后继续，落定后正常收尾。
            Build(VictorySettings(), coverLimit: TimeSpan.Zero);
            presenter.Enqueue(BattleCommand.CastSkill(PlayerSkill.Skill1));
            narrative.HoldComplete = true;

            bus.Publish(Stage(BossId));

            Assert.That(narrative.Completed, Is.EqualTo(new[] { "Victory" }), "前置：回写已发起");
            Assert.That(curtain.IsCovered, Is.False, "回写没落定也不卡黑屏");
            Assert.That(pause.IsPaused, Is.False, "世界已恢复");
            Assert.That(input.Actions.Gameplay.enabled, Is.True);
            Assert.That(flow.IsBattleRunning, Is.True, "回写还在幕后等结果");
            Assert.That(narrative.ReleaseCalls, Is.EqualTo(0));

            narrative.FinishComplete();

            Assert.That(flow.IsBattleRunning, Is.False);
            Assert.That(narrative.ReleaseCalls, Is.EqualTo(0), "回写最终被接受，不放弃在途登记");
            Assert.That(narrative.Holding, Is.False);
            Assert.That(curtain.Reveals, Is.EqualTo(2), "不再落幕 / 揭幕");
        }

        [Test]
        public void GateClosed_DoesNotHoldWorldUntilReady()
        {
            // 读档恢复（D9）时剧情在切场景前就发通知：门闸没开之前不能按住世界、不能加载战斗场景。
            Build(VictorySettings());
            gate.Hold = true;
            presenter.Enqueue(BattleCommand.CastSkill(PlayerSkill.Skill1));

            bus.Publish(Stage(BossId));

            Assert.That(flow.IsBattleRunning, Is.True);
            Assert.That(flow.IsBattleEngaged, Is.False);
            Assert.That(pause.IsPaused, Is.False);
            Assert.That(assets.LoadedKeys, Is.Empty);

            gate.Open();

            Assert.That(narrative.Completed, Is.EqualTo(new[] { "Victory" }));
            AssertWorldRestored(gameplayEnabled: true, hudVisible: true);
        }

        [Test]
        public void DuplicateStage_WhileRunning_IsIgnored()
        {
            Build(VictorySettings());
            presenter.BlockOnCommand = true;
            StageEvent stage = Stage(BossId);
            bus.Publish(stage);

            bus.Publish(stage);

            Assert.That(narrative.BeginCalls, Is.EqualTo(1), "同一身份的重复通知不再开第二场");
            Assert.That(presenter.Opens, Is.EqualTo(1));
        }

        // ── 门闸未开时换了身份的通知：新的顶掉旧的 ─────────────────────────────────

        [Test]
        public void NewIdentity_WhileWaitingForGate_SupersedesOld_OnlyNewBattleOpens()
        {
            // 读档途中剧情换了代又发一次通知：旧请求还在等世界就绪，让位给新的；门闸开后只开新的那一仗。
            Build(VictorySettings());
            gate.Hold = true;
            presenter.Enqueue(BattleCommand.CastSkill(PlayerSkill.Skill1));
            StageEvent old = Stage(BossId);
            StageEvent fresh = NextGeneration(old);

            bus.Publish(old);
            bus.Publish(fresh);

            Assert.That(narrative.BeginCalls, Is.EqualTo(2), "新身份重新登记在途");
            Assert.That(narrative.ReleasedStages.Select(s => s.Generation), Is.EqualTo(new[] { old.Generation }), "旧请求让位：放弃它的在途登记");
            Assert.That(flow.IsBattleRunning && !flow.IsBattleEngaged, Is.True, "新请求还在等门闸");
            Assert.That(assets.LoadedKeys, Is.Empty, "门闸没开前谁都不加载场景");

            gate.Open();

            Assert.That(presenter.Opens, Is.EqualTo(1), "只开一场");
            Assert.That(narrative.CompletedStages.Select(s => s.Generation), Is.EqualTo(new[] { fresh.Generation }), "只回写新的那一仗，旧的没开");
            Assert.That(narrative.Completed, Is.EqualTo(new[] { "Victory" }));
            Assert.That(narrative.ReleasedStages.Count, Is.EqualTo(1), "新的那一仗被接受，不再放弃登记");
            AssertWorldRestored(gameplayEnabled: true, hudVisible: true);
        }

        [Test]
        public void SameIdentity_WhileWaitingForGate_KeepsOriginalRequest()
        {
            // 负对照①（身份没变）：门闸没开时重发同一身份，原请求不让位、不重新登记，门闸开后照常开这一仗。
            Build(VictorySettings());
            gate.Hold = true;
            presenter.Enqueue(BattleCommand.CastSkill(PlayerSkill.Skill1));
            StageEvent stage = Stage(BossId);

            bus.Publish(stage);
            bus.Publish(stage);

            Assert.That(narrative.BeginCalls, Is.EqualTo(1));
            Assert.That(narrative.ReleasedStages, Is.Empty, "原请求没让位");

            gate.Open();

            Assert.That(presenter.Opens, Is.EqualTo(1));
            Assert.That(narrative.CompletedStages.Select(s => s.Generation), Is.EqualTo(new[] { stage.Generation }));
        }

        [Test]
        public void NewIdentity_WhileEngaged_IsIgnoredAndCurrentBattleContinues()
        {
            // 负对照②（门闸已开、世界已按住）：换了身份的通知只留痕忽略，不顶掉正在打的这一仗。
            Build(VictorySettings());
            presenter.BlockOnCommand = true;
            StageEvent current = Stage(BossId);
            bus.Publish(current);
            Assert.That(flow.IsBattleEngaged, Is.True, "前置：停在等玩家出招");

            bus.Publish(NextGeneration(current));

            Assert.That(narrative.BeginCalls, Is.EqualTo(1), "不为新身份登记在途");
            Assert.That(narrative.ReleasedStages, Is.Empty, "正在打的这一仗没被放弃");
            Assert.That(presenter.Opens, Is.EqualTo(1));
            Assert.That(flow.IsBattleEngaged, Is.True);
            Assert.That(narrative.LastStage.Value.Generation, Is.EqualTo(current.Generation));
        }

        // ── 回放测试口换数值（BattleSetup.OverrideSettings）的埋点 ────────────────────

        [Test]
        public void BattleStarts_WithOverriddenSettings_TracksOverrideWarning()
        {
            Build(VictorySettings(), telemetry: RecordTelemetry());
            presenter.Enqueue(BattleCommand.CastSkill(PlayerSkill.Skill1));
            using (setup.OverrideSettings(VictorySettings()))
                bus.Publish(Stage(BossId));

            Assert.That(narrative.Completed, Is.EqualTo(new[] { "Victory" }), "前置：这一仗照常打完");
            Assert.That(LinesContaining("W battle/settings_overridden"), Is.EqualTo(1), "开打时处于替换状态，留一条 W 级埋点");
        }

        [Test]
        public void BattleStarts_AfterOverrideDisposed_TracksNoOverrideWarning()
        {
            // 负对照：换过又撤销，下一场用回配置数值，不再报「数值被替换」。
            Build(VictorySettings(), telemetry: RecordTelemetry());
            presenter.Enqueue(BattleCommand.CastSkill(PlayerSkill.Skill1));
            setup.OverrideSettings(DefeatSettings()).Dispose();

            bus.Publish(Stage(BossId));

            Assert.That(LinesContaining("I battle/battle_started"), Is.EqualTo(1), "前置：埋点确实录到了开打");
            Assert.That(LinesContaining("battle/settings_overridden"), Is.EqualTo(0));
            Assert.That(narrative.Completed, Is.EqualTo(new[] { "Victory" }), "前置：这一仗照常打完（撤销后用回原值由 BattleSetupTests 钉住）");
        }

        // ── 道具 ────────────────────────────────────────────────────────────────

        [Test]
        public void UseItem_OwnedConsumable_DeductsOneFromBackpack()
        {
            Build(VictorySettings());
            backpack.Add(Potion, 2, consumable: true);
            presenter.Enqueue(BattleCommand.UseItem("1004"), BattleCommand.CastSkill(PlayerSkill.Skill1));

            bus.Publish(Stage(BossId));

            Assert.That(backpack.CountOf(Potion), Is.EqualTo(1), "用一次扣 1（D10）");
            Assert.That(presenter.PlayedEvents.Any(e => e.Kind == BattleEventKind.ItemUsed && e.ItemId == "1004"), Is.True);
            Assert.That(presenter.Menus[0].Items.Single().ItemId, Is.EqualTo("1004"));
            Assert.That(presenter.Menus[1].Items.Single().UsedThisBattle, Is.True, "第二次问指令时这一格已置暗");
            Assert.That(narrative.Completed, Is.EqualTo(new[] { "Victory" }));
        }

        [Test]
        public void UseItem_NotOwned_IsRejectedAndBackpackUntouched()
        {
            // 负对照：背包里没有 → 规则拒绝、提示原因、背包不动，流程再问一次指令。
            Build(VictorySettings());
            presenter.Enqueue(BattleCommand.UseItem("1004"), BattleCommand.CastSkill(PlayerSkill.Skill1));

            bus.Publish(Stage(BossId));

            Assert.That(backpack.CountOf(Potion), Is.EqualTo(0));
            Assert.That(presenter.Menus[1].Rejection, Does.Contain("未拥有"));
            Assert.That(narrative.Completed, Is.EqualTo(new[] { "Victory" }));
        }

        // ── 帮手 ────────────────────────────────────────────────────────────────

        private void Build(in BattleSettings settings, bool withPresenter = true, TimeSpan? coverLimit = null, ITelemetryScope telemetry = null)
        {
            var roster = new BossRoster(new[] { new BossDefinition(BossId, "测试 BOSS", BossHealth(settings), 0) });
            var items = new BattleItemInventory(backpack);
            setup = new BattleSetup(settings, () => 42UL, items);
            flow = new BattleFlow(bus, roster, narrative, gate, new BattleWorldLock(pause, input, ui),
                new BattleArena(curtain, assets, NullTelemetryScope.Instance), setup, items,
                withPresenter ? presenter : null, telemetry ?? NullTelemetryScope.Instance, coverLimit);
            flow.Start();
        }

        // 现有的假埋点：真 TelemetryService + 记录行的 sink（同 NarrativeBattleRejectionTests），模块名与 BattleInstaller 一致。
        private ITelemetryScope RecordTelemetry()
        {
            sink = new RecordingTelemetrySink();
            telemetryService = new TelemetryService(TelemetryOptions.Default, new FakeTelemetryClock(), sink);
            return telemetryService.Scope("battle");
        }

        private int LinesContaining(string fragment) => sink.Lines.Count(line => line.Contains(fragment));

        // 读档换代后剧情重发的同一阶段：只有 Generation 不同（身份变了）。
        private static StageEvent NextGeneration(in StageEvent stage) =>
            new StageEvent(stage.Generation + 1, stage.ActivationId, stage.TargetId, stage.StageId, stage.Payload);

        // 胜：BOSS 1 血（招式 1 打 1 就倒）。负：BOSS 100 血，只放招式 1、一下 10 点（本场玩家生命占位 10，一下倒）。醉酒 0 → 不跳过、不抽随机数。
        private static int BossHealth(in BattleSettings settings) => settings.BossSkills.Skill1Damage >= 10 ? 100 : 1;

        private static BattleSettings VictorySettings() => WithBossSkills(new BossSkillSettings(1, 30, 10, HealthPercentBase.MaxHealth, 30, 3, 1, 1, 0, 0));

        private static BattleSettings DefeatSettings() => WithBossSkills(new BossSkillSettings(10, 30, 10, HealthPercentBase.MaxHealth, 30, 3, 1, 1, 0, 0));

        private static BattleSettings WithBossSkills(in BossSkillSettings boss)
        {
            BattleSettings basis = BattleSettings.PlaceholderDefault;
            return new BattleSettings(basis.Entry, basis.PlayerSkills, boss, basis.Drunk, basis.Flow,
                basis.ItemOncePerBattle, basis.InheritsDrunkValue, basis.Items);
        }

        private static StageEvent Stage(string payload) => new StageEvent(3, 7, "boss_npc", "fight", payload);

        private void AssertWorldRestored(bool gameplayEnabled, bool hudVisible)
        {
            Assert.That(pause.IsPaused, Is.False, "暂停令牌已释放");
            Assert.That(input.Actions.Gameplay.enabled, Is.EqualTo(gameplayEnabled), "Gameplay 图回到进来前");
            Assert.That(ui.IsLayerVisible(UILayer.Hud), Is.EqualTo(hudVisible), "HUD 层回到进来前");
            Assert.That(narrative.Holding, Is.False, "自动存档不再被压住");
        }
    }
}
