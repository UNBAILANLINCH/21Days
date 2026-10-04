using System;
using Game.Rhythm;
using NUnit.Framework;

namespace Game.Tests.EditMode.Rhythm
{
    public sealed class RhythmExternalSessionTests
    {
        private sealed class Permission : IRhythmEntryPermission
        {
            public bool Valid { get; set; } = true;
            public bool Tamed { get; set; } = true;
            public bool Controlling { get; set; } = true;
            public bool Throws { get; set; }
            public RhythmEntryAccess Capture(string contextId)
            {
                if (Throws) throw new InvalidOperationException("权限失败 fixture");
                return new RhythmEntryAccess(Valid && contextId == "scene", Tamed, Controlling);
            }
        }

        private sealed class Policy : IRhythmCombatPolicy
        {
            public bool Success { get; set; } = true;
            public bool Throws { get; set; }
            public int Calls { get; private set; }
            public bool IsSuccess(RhythmRunResult result)
            {
                Calls++;
                if (Throws) throw new InvalidOperationException("策略失败 fixture");
                return Success;
            }
        }

        [Test]
        public void Entry_TamedButNotControlled_CanBrowseButCannotPerform()
        {
            var permission = new Permission { Tamed = false };
            var session = new RhythmExternalSession(permission, new Policy());
            Assert.That(session.CanAccessLibrary("scene"), Is.False);
            Assert.That(session.TryBegin(Request(), out string reason), Is.False);
            Assert.That(reason, Is.EqualTo("not_tamed"));
            permission.Tamed = true;
            permission.Controlling = false;
            Assert.That(session.CanAccessLibrary("scene"), Is.True);
            Assert.That(session.TryBegin(Request(RhythmPlayMode.Combat), out reason), Is.False);
            Assert.That(reason, Is.EqualTo("not_controlling_performer"));
            Assert.That(session.TryBegin(Request(RhythmPlayMode.FreePlay), out reason), Is.False);
            Assert.That(reason, Is.EqualTo("not_controlling_performer"));
            permission.Controlling = true;
            Assert.That(session.TryBegin(Request(RhythmPlayMode.Combat), out reason), Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CompletedCombat_ValidResult_CallsInjectedOutcomeOnce(bool success)
        {
            var policy = new Policy { Success = success };
            int successes = 0, failures = 0;
            var session = new RhythmExternalSession(new Permission(), policy,
                result => successes++, result => failures++);
            Assert.That(session.TryBegin(Request(RhythmPlayMode.Combat), out _), Is.True);
            Assert.That(session.Consume(Result(RhythmPlayMode.Combat)), Is.True);
            Assert.That(session.Consume(Result(RhythmPlayMode.Combat)), Is.False);
            Assert.That(successes, Is.EqualTo(success ? 1 : 0));
            Assert.That(failures, Is.EqualTo(success ? 0 : 1));
            Assert.That(policy.Calls, Is.EqualTo(1));
            Assert.That(session.TryBegin(Request(RhythmPlayMode.Combat), out string reason), Is.False);
            Assert.That(reason, Is.EqualTo("used_run_id"));
            Assert.That(session.TryBegin(Request(RhythmPlayMode.Combat, "retry"), out _), Is.True);
        }

        [TestCase(RhythmPlayMode.FreePlay)]
        [TestCase(RhythmPlayMode.Practice)]
        public void CompletedNonCombat_NeverCallsCombatPolicyOrCallbacks(RhythmPlayMode mode)
        {
            var policy = new Policy();
            int callbacks = 0;
            var session = new RhythmExternalSession(new Permission(), policy,
                result => callbacks++, result => callbacks++);
            Assert.That(session.TryBegin(Request(mode), out _), Is.True);
            Assert.That(session.Consume(Result(mode)), Is.True);
            Assert.That(policy.Calls, Is.Zero);
            Assert.That(callbacks, Is.Zero);
        }

        [TestCase(RhythmRunCompletion.Aborted)]
        [TestCase(RhythmRunCompletion.TechnicalError)]
        public void InterruptedCombat_UsesIndependentNoticeWithoutOutcome(RhythmRunCompletion completion)
        {
            var policy = new Policy();
            int aborted = 0, technical = 0, outcomes = 0;
            var session = new RhythmExternalSession(new Permission(), policy,
                result => outcomes++, result => outcomes++, result => aborted++, result => technical++);
            Assert.That(session.TryBegin(Request(RhythmPlayMode.Combat), out _), Is.True);
            Assert.That(session.Consume(Result(RhythmPlayMode.Combat, completion)), Is.True);
            Assert.That(session.Consume(Result(RhythmPlayMode.Combat, completion)), Is.False);
            Assert.That(outcomes, Is.Zero);
            Assert.That(policy.Calls, Is.Zero);
            Assert.That(aborted, Is.EqualTo(completion == RhythmRunCompletion.Aborted ? 1 : 0));
            Assert.That(technical, Is.EqualTo(completion == RhythmRunCompletion.TechnicalError ? 1 : 0));
        }

        [Test]
        public void Consume_WrongRunContextSongOrMode_DoesNotConsumeActiveRun()
        {
            var session = new RhythmExternalSession(new Permission(), new Policy());
            Assert.That(session.TryBegin(Request(RhythmPlayMode.Combat), out _), Is.True);
            Assert.That(session.Consume(Result(RhythmPlayMode.Combat, runId: "other")), Is.False);
            Assert.That(session.Consume(Result(RhythmPlayMode.Combat, contextId: "other")), Is.False);
            Assert.That(session.Consume(Result(RhythmPlayMode.Combat, songId: "other")), Is.False);
            Assert.That(session.Consume(Result(RhythmPlayMode.FreePlay)), Is.False);
            Assert.That(session.Active, Is.Not.Null);
            Assert.That(session.Consume(Result(RhythmPlayMode.Combat)), Is.True);
        }

        [Test]
        public void Consume_ExpiredContext_RemainsClosedWhenContextReturns()
        {
            var permission = new Permission();
            var session = new RhythmExternalSession(permission);
            Assert.That(session.TryBegin(Request(), out _), Is.True);
            permission.Valid = false;
            Assert.That(session.Consume(Result()), Is.False);
            permission.Valid = true;
            Assert.That(session.Consume(Result()), Is.False);
            Assert.That(session.TryBegin(Request(), out _), Is.False);
        }

        [Test]
        public void Invalidate_CancelledRun_CannotConsumeLateResultOrReuseId()
        {
            var session = new RhythmExternalSession(new Permission());
            Assert.That(session.TryBegin(Request(), out _), Is.True);
            session.Invalidate();
            Assert.That(session.Consume(Result()), Is.False);
            Assert.That(session.TryBegin(Request(), out _), Is.False);
            Assert.That(session.TryBegin(Request(runId: "retry"), out _), Is.True);
        }

        [Test]
        public void Consume_RevokedControl_RejectsEveryExternalPerformance()
        {
            var permission = new Permission();
            var policy = new Policy();
            var session = new RhythmExternalSession(permission, policy);
            Assert.That(session.TryBegin(Request(RhythmPlayMode.Combat), out _), Is.True);
            permission.Controlling = false;
            Assert.That(session.Consume(Result(RhythmPlayMode.Combat)), Is.False);
            Assert.That(policy.Calls, Is.Zero);
            Assert.That(session.TryBegin(Request(runId: "free"), out _), Is.False);
            permission.Controlling = true;
            Assert.That(session.TryBegin(Request(runId: "free"), out _), Is.True);
            permission.Controlling = false;
            Assert.That(session.Consume(Result(runId: "free")), Is.False);
            Assert.That(policy.Calls, Is.Zero);
        }

        [Test]
        public void Consume_ThrowingPermission_SealsMatchedResultBeforeReadingContext()
        {
            var permission = new Permission();
            var session = new RhythmExternalSession(permission);
            Assert.That(session.TryBegin(Request(), out _), Is.True);
            permission.Throws = true;
            Assert.Throws<InvalidOperationException>(() => session.Consume(Result()));
            permission.Throws = false;
            Assert.That(session.Consume(Result()), Is.False);
            Assert.That(session.Active, Is.Null);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Consume_ThrowingPolicyOrCallback_IsAlreadyConsumed(bool policyThrows)
        {
            var policy = new Policy { Throws = policyThrows };
            int callbacks = 0;
            var session = new RhythmExternalSession(new Permission(), policy, result =>
            {
                callbacks++;
                throw new InvalidOperationException("回调失败 fixture");
            });
            Assert.That(session.TryBegin(Request(RhythmPlayMode.Combat), out _), Is.True);
            Assert.Throws<InvalidOperationException>(() => session.Consume(Result(RhythmPlayMode.Combat)));
            Assert.That(session.Active, Is.Null);
            Assert.That(session.Consume(Result(RhythmPlayMode.Combat)), Is.False);
            Assert.That(policy.Calls, Is.EqualTo(1));
            Assert.That(callbacks, Is.EqualTo(policyThrows ? 0 : 1));
        }

        [Test]
        public void Entry_InvalidContextMissingPolicyOrExistingRun_RejectsWithoutReplacing()
        {
            var permission = new Permission { Valid = false };
            var session = new RhythmExternalSession(permission);
            Assert.That(session.TryBegin(Request(), out string reason), Is.False);
            Assert.That(reason, Is.EqualTo("expired_context"));
            permission.Valid = true;
            Assert.That(session.TryBegin(Request(RhythmPlayMode.Combat), out reason), Is.False);
            Assert.That(reason, Is.EqualTo("missing_combat_policy"));
            Assert.That(session.TryBegin(Request(), out _), Is.True);
            Assert.That(session.TryBegin(Request(runId: "retry"), out reason), Is.False);
            Assert.That(reason, Is.EqualTo("active_run"));
            Assert.That(session.Active.RunId, Is.EqualTo("run"));
        }

        private static RhythmPlayRequest Request(RhythmPlayMode mode = RhythmPlayMode.FreePlay, string runId = "run") =>
            new RhythmPlayRequest(runId, "scene", "song", mode);

        private static RhythmRunResult Result(RhythmPlayMode mode = RhythmPlayMode.FreePlay,
            RhythmRunCompletion completion = RhythmRunCompletion.Completed, string runId = "run",
            string contextId = "scene", string songId = "song") =>
            new RhythmRunResult(runId, contextId, mode, completion, "test", songId, "chart", "v1", "four-lane", "1",
                1, 1, 0, 0, 0, 1000, 1);
    }
}
