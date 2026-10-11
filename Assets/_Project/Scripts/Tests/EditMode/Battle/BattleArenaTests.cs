// 职责：钉住战斗场景宿主——叠加加载正确地址、黑幕落揭成对；场景缺失时抛出带地址与修法的异常（不吞）且黑幕揭开；
//   表现开场失败时收场、卸场景；收场时在黑幕下执行「恢复世界」回调；开场失败时也在黑幕下先恢复世界、再揭幕（成功时不恢复，负对照）。
// 为什么新建：BattleArena 是本波新增；BattleFlowTests 只经流程间接覆盖它，这里直接钉住异常与时序契约。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Battle;
using Game.Core.Telemetry;
using Game.TurnBased;
using NUnit.Framework;

namespace Game.Tests.EditMode.Battle
{
    public sealed class BattleArenaTests
    {
        private BattleFakes.Curtain curtain;
        private BattleFakes.Assets assets;
        private BattleArena arena;
        private readonly BossDefinition boss = new BossDefinition("sample_boss", "测试 BOSS", 6, 50);

        [SetUp]
        public void SetUp()
        {
            curtain = new BattleFakes.Curtain();
            assets = new BattleFakes.Assets();
            arena = new BattleArena(curtain, assets, NullTelemetryScope.Instance);
        }

        [Test]
        public void EnterThenExit_LoadsAdditiveAndRestoresWorldUnderCurtain()
        {
            var presenter = new BattleFakes.Presenter();
            bool coveredWhileRestoring = false;

            arena.EnterAsync(presenter, boss, null, null, CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(arena.IsLoaded, Is.True);
            Assert.That(curtain.IsCovered, Is.False, "开场后揭幕");
            arena.ExitAsync(presenter, () => coveredWhileRestoring = curtain.IsCovered).GetAwaiter().GetResult();

            Assert.That(assets.LoadedKeys, Is.EqualTo(new[] { "BattleArena" }));
            Assert.That(presenter.Opens, Is.EqualTo(1));
            Assert.That(presenter.Closes, Is.EqualTo(1));
            Assert.That(coveredWhileRestoring, Is.True, "恢复世界发生在黑幕盖着时（D6）");
            Assert.That(curtain.IsCovered, Is.False);
            Assert.That(arena.IsLoaded, Is.False);
            Assert.That(assets.LastHandle.IsDisposed, Is.True);
        }

        [Test]
        public void Enter_SceneMissing_ThrowsClearErrorAndRevealsCurtain()
        {
            var presenter = new BattleFakes.Presenter();
            assets.Missing = true;

            var error = Assert.Throws<InvalidOperationException>(
                () => arena.EnterAsync(presenter, boss, null, null, CancellationToken.None).GetAwaiter().GetResult());

            Assert.That(error.Message, Does.Contain("BattleArena"));
            Assert.That(error.Message, Does.Contain("AssetGroups/Scenes.asset"), "报错要带修法");
            Assert.That(error.InnerException, Is.Not.Null, "原始错误保留");
            Assert.That(presenter.Opens, Is.EqualTo(0));
            Assert.That(presenter.Closes, Is.EqualTo(0), "没开过场就不收场");
            Assert.That(curtain.IsCovered, Is.False, "不留黑屏");
            Assert.That(arena.IsLoaded, Is.False);
        }

        [Test]
        public void Enter_PresenterOpenFails_ClosesUnloadsAndRethrows()
        {
            var presenter = new ThrowingPresenter();

            Assert.Throws<InvalidOperationException>(
                () => arena.EnterAsync(presenter, boss, null, null, CancellationToken.None).GetAwaiter().GetResult());

            Assert.That(presenter.Closes, Is.EqualTo(1), "开场做了一半也要收场（IBattlePresenter 契约）");
            Assert.That(assets.LastHandle.IsDisposed, Is.True);
            Assert.That(curtain.IsCovered, Is.False);
        }

        [Test]
        public void Enter_Fails_RestoresWorldUnderCurtainBeforeReveal()
        {
            // 开场失败的收场顺序与 ExitAsync 一致：收表现 → 卸场景 → 恢复世界（黑幕还盖着）→ 揭幕。
            var presenter = new ThrowingPresenter();
            bool coveredAtRestore = false, unloadedAtRestore = false;
            int revealsAtRestore = -1, closesAtRestore = -1, restored = 0;

            Assert.Throws<InvalidOperationException>(() => arena.EnterAsync(presenter, boss, null, () =>
            {
                restored++;
                coveredAtRestore = curtain.IsCovered;
                revealsAtRestore = curtain.Reveals;
                closesAtRestore = presenter.Closes;
                unloadedAtRestore = assets.LastHandle.IsDisposed;
            }, CancellationToken.None).GetAwaiter().GetResult());

            Assert.That(restored, Is.EqualTo(1), "开场失败恢复一次世界");
            Assert.That(coveredAtRestore, Is.True, "恢复世界时黑幕还盖着");
            Assert.That(revealsAtRestore, Is.EqualTo(0), "恢复排在揭幕之前");
            Assert.That(closesAtRestore, Is.EqualTo(1), "先收表现");
            Assert.That(unloadedAtRestore, Is.True, "先卸场景");
            Assert.That(curtain.Reveals, Is.EqualTo(1));
            Assert.That(curtain.IsCovered, Is.False, "最后照样揭幕");
        }

        [Test]
        public void Enter_Succeeds_DoesNotRunFailureRestore()
        {
            // 负对照：开场成功时世界要一直按住到收场，失败恢复回调一次都不能跑。
            var presenter = new BattleFakes.Presenter();
            int restored = 0;

            arena.EnterAsync(presenter, boss, null, () => restored++, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(restored, Is.EqualTo(0));
            Assert.That(arena.IsLoaded, Is.True);
            Assert.That(curtain.Reveals, Is.EqualTo(1), "前置：开场照常揭幕");
        }

        [Test]
        public void Exit_WhenNothingLoaded_OnlyRunsRestoreCallback()
        {
            // 负对照：没进过场就不落幕、不收表现，只把世界恢复。
            var presenter = new BattleFakes.Presenter();
            int restored = 0;

            arena.ExitAsync(presenter, () => restored++).GetAwaiter().GetResult();

            Assert.That(restored, Is.EqualTo(1));
            Assert.That(curtain.Covers, Is.EqualTo(0));
            Assert.That(presenter.Closes, Is.EqualTo(0));
        }

        private sealed class ThrowingPresenter : IBattlePresenter
        {
            public int Closes { get; private set; }
            public UniTask OpenAsync(BattleOpening opening, CancellationToken ct) => UniTask.FromException(new InvalidOperationException("测试：开场失败"));
            public UniTask PlayAsync(BattleSession session, IReadOnlyList<BattleEvent> events, CancellationToken ct) => UniTask.CompletedTask;
            public UniTask<BattleCommand> WaitCommandAsync(BattleCommandMenu menu, CancellationToken ct) => UniTask.FromResult(BattleCommand.EndTurn());

            public UniTask CloseAsync(CancellationToken ct)
            {
                Closes++;
                return UniTask.CompletedTask;
            }
        }
    }
}
