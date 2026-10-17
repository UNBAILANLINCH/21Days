// 职责：钉住「存档 / 回放快照按稳定 id 对齐」（PRP/combat-iteration D2）——加怪的前置条件：
//   场景里加了一只怪之后，玩家的旧存档与旧回放不能因为「巡逻者数量与场景不匹配」直接读不进来。
//   三种情况各一组（存档一组、回放快照一组）：存档 / 快照比场景**少**（缺的按场景初始状态开局）、
//   **多**（多出的丢弃、字节照样读掉）、**一致**（逐只恢复、不记对齐埋点）；对不齐时记一条
//   `monster/encounter_restore_aligned` 的 W 级埋点。
// 为什么新建：`EncounterStepTests` 里的两条多目标用例钉的是「数量一致时往返逐字节相同」与「坏存档不部分改写」，
//   对齐口径是这一波的新契约，单列一个文件，失败时一眼能看出是哪一类读档回归。
// 不接种类表：本文件只关心「谁的状态落到谁身上」，全部怪按全局默认值跑（生命 3、可常规击杀），
//   用「挨一刀掉 1 血 / 被驯服」做可区分的状态标记。
using Game.Core.Config;
using Game.Core.Replay;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Mirror;
using Game.Monster;
using Game.Player;
using Game.Tests.EditMode.Core;
using Game.Tests.EditMode.Telemetry;
using Game.Tests.EditMode.World;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Monster
{
    /// <summary>读档与回放快照按稳定 id 对齐：比场景多、少、一致三种。</summary>
    public sealed class EncounterRestoreAlignmentTests
    {
        private const string PlayerId = "player";
        private const string AlignedEvent = "monster/encounter_restore_aligned";
        private static readonly Vector2 FarSpawn = new Vector2(0f, -30f);

        private PlayerConfig playerConfig;
        private MonsterConfig monsterConfig;
        private RecordingTelemetrySink sink;
        private TelemetryService telemetry;

        [SetUp]
        public void SetUp()
        {
            playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
            monsterConfig = ScriptableObject.CreateInstance<MonsterConfig>();
            sink = new RecordingTelemetrySink();
            telemetry = new TelemetryService(TelemetryOptions.Default, new FakeTelemetryClock(), sink);
        }

        [TearDown]
        public void TearDown()
        {
            telemetry.Dispose();
            Object.DestroyImmediate(playerConfig);
            Object.DestroyImmediate(monsterConfig);
        }

        // ──────────────────────── 存档（EncounterSaveData） ────────────────────────

        /// <summary>
        /// 存档比场景少（场景后来加了怪 c，读旧档）：不抛；存档里有的 b 恢复成存档状态，
        /// 场景里多出来的 c 回到场景初始状态（满血、路线起点、未驯服）；记一条对齐 Warn。
        /// </summary>
        [Test]
        public void Restore_SaveHasFewerTargetsThanScene_MissingOnesStartFresh()
        {
            EncounterStep saving = NewStep("a", "b");
            Hit(saving, "b");
            saving.Taming.TryTame("b");
            EncounterSaveData saved = saving.Capture(1);

            EncounterStep scene = NewStep("a", "b", "c");
            Hit(scene, "c");
            scene.Taming.TryTame("c");

            Assert.DoesNotThrow(() => scene.Restore(saved), "存档比场景少是合法存档（旧档读进加了怪的新场景）");

            Assert.That(scene.Taming.GetTarget("b").Model.Health, Is.EqualTo(monsterConfig.MaxHealth - 1), "b 取存档状态");
            Assert.That(scene.Taming.IsTargetTamed("b"), Is.True);
            Assert.That(scene.Taming.GetTarget("c").Model.Health, Is.EqualTo(monsterConfig.MaxHealth), "c 按场景初始状态开局");
            Assert.That(scene.Taming.GetTarget("c").Model.Position, Is.EqualTo(RouteOf("c")[0]));
            Assert.That(scene.Taming.IsTargetTamed("c"), Is.False, "存档里没有它，就没有驯服归属");
            AssertAligned(saved: 2, scene: 3, dropped: 0, fresh: 1);
        }

        /// <summary>
        /// 存档比场景多（场景后来删了怪 c，且存档时正控制着 c）：不抛；c 那一条丢弃，b 照常恢复，
        /// 控制权退回玩家（被控对象已不在场景里）；记一条对齐 Warn。
        /// </summary>
        [Test]
        public void Restore_SaveHasMoreTargetsThanScene_ExtraOnesAreDropped()
        {
            EncounterStep saving = NewStep("a", "b", "c");
            Hit(saving, "b");
            saving.Taming.TryTame("c");
            saving.Taming.TryControl("c");
            EncounterSaveData saved = saving.Capture(1);

            EncounterStep scene = NewStep("a", "b");

            Assert.DoesNotThrow(() => scene.Restore(saved), "存档比场景多也是合法存档（新档读进删了怪的场景）");

            Assert.That(scene.Taming.GetTarget("b").Model.Health, Is.EqualTo(monsterConfig.MaxHealth - 1));
            Assert.That(scene.CurrentControlId, Is.EqualTo(PlayerId), "被控对象不在场景里：控制权退回玩家");
            AssertAligned(saved: 3, scene: 2, dropped: 1, fresh: 0);
        }

        /// <summary>存档与场景一致：逐只恢复，且不记对齐埋点（正常读档不刷屏）。</summary>
        [Test]
        public void Restore_SameTargets_RestoresEachAndTracksNothing()
        {
            EncounterStep saving = NewStep("a", "b", "c");
            Hit(saving, "b");
            Hit(saving, "c");
            Hit(saving, "c");
            saving.Taming.TryTame("b");
            EncounterSaveData saved = saving.Capture(1);

            EncounterStep scene = NewStep("a", "b", "c");
            scene.Restore(saved);

            Assert.That(scene.Taming.GetTarget("b").Model.Health, Is.EqualTo(monsterConfig.MaxHealth - 1));
            Assert.That(scene.Taming.GetTarget("c").Model.Health, Is.EqualTo(monsterConfig.MaxHealth - 2));
            Assert.That(scene.Taming.IsTargetTamed("b"), Is.True);
            Assert.That(LineFor(AlignedEvent), Is.Null, "一一对得上时不记对齐埋点");
        }

        // ──────────────────────── 回放快照（IReplayState） ────────────────────────

        /// <summary>
        /// 快照比场景少：读完不抛、字节恰好读尽；快照里有的 b 与录制时逐字段相同，
        /// 场景里多出来的 c 回到场景初始状态；记一条对齐 Warn。
        /// </summary>
        [Test]
        public void Deserialize_SnapshotHasFewerTargetsThanScene_MissingOnesStartFresh()
        {
            EncounterStep recording = NewStep("a", "b");
            Hit(recording, "b");
            recording.Taming.TryTame("b");
            var bytes = new StateBuffer();
            recording.Serialize(bytes);

            EncounterStep scene = NewStep("a", "b", "c");
            Hit(scene, "c");
            scene.Deserialize(bytes);

            Assert.That(bytes.Remaining, Is.Zero, "快照恰好读尽");
            Assert.That(scene.Taming.GetTarget("b").Capture().HasSameState(recording.Taming.GetTarget("b").Capture()), Is.True);
            Assert.That(scene.Taming.IsTargetTamed("b"), Is.True);
            Assert.That(scene.Taming.GetTarget("c").Model.Health, Is.EqualTo(monsterConfig.MaxHealth), "c 按场景初始状态开局");
            Assert.That(scene.Taming.GetTarget("c").Model.Position, Is.EqualTo(RouteOf("c")[0]));
            AssertAligned(saved: 2, scene: 3, dropped: 0, fresh: 1);
        }

        /// <summary>
        /// 快照比场景多：多出来那只的字节照样读掉（后面的条目才对得上），快照恰好读尽；b 照常恢复；记一条对齐 Warn。
        /// 多出来的那只故意排在 b 前面——读掉的字节数不对，b 就会读出垃圾。
        /// </summary>
        [Test]
        public void Deserialize_SnapshotHasMoreTargetsThanScene_ExtraBytesAreSkipped()
        {
            EncounterStep recording = NewStep("a", "c", "b");
            Hit(recording, "c");
            Hit(recording, "b");
            var bytes = new StateBuffer();
            recording.Serialize(bytes);

            EncounterStep scene = NewStep("a", "b");
            scene.Deserialize(bytes);

            Assert.That(bytes.Remaining, Is.Zero, "多出来那只的字节被完整读掉");
            Assert.That(scene.Taming.GetTarget("b").Capture().HasSameState(recording.Taming.GetTarget("b").Capture()), Is.True,
                "排在被丢弃条目后面的 b 仍然读对");
            AssertAligned(saved: 3, scene: 2, dropped: 1, fresh: 0);
        }

        /// <summary>快照与场景是同一组 id、只是顺序不同：按 id 落到各自身上，不记对齐埋点。</summary>
        [Test]
        public void Deserialize_SameTargetsInDifferentOrder_AlignsByIdWithoutWarning()
        {
            EncounterStep recording = NewStep("a", "c", "b");
            Hit(recording, "b");
            recording.Taming.TryTame("c");
            var bytes = new StateBuffer();
            recording.Serialize(bytes);

            EncounterStep scene = NewStep("a", "b", "c");
            scene.Deserialize(bytes);

            Assert.That(bytes.Remaining, Is.Zero);
            Assert.That(scene.Taming.GetTarget("b").Model.Health, Is.EqualTo(monsterConfig.MaxHealth - 1));
            Assert.That(scene.Taming.GetTarget("c").Model.Health, Is.EqualTo(monsterConfig.MaxHealth));
            Assert.That(scene.Taming.IsTargetTamed("c"), Is.True, "驯服标记跟着 id 走，不跟着位置走");
            Assert.That(scene.Taming.IsTargetTamed("b"), Is.False);
            Assert.That(LineFor(AlignedEvent), Is.Null);
        }

        // ──────────────── 读档两处缺陷（PRP/combat-iteration 执行中修订，2026-10-14 并进 W2） ────────────────
        // 这一组接真实种类表：主怪 1003 市令（killable=false，视野 75°，妖 3），附加完整参与者 1001 巡夜人（killable=false + 暗杀，妖 1；2026-10-15 用户拍板只能暗杀）。
        // 场景侧按正式读档流程造：ConfigureTaming 之后**直接 Restore / Deserialize，不走 Begin**（MonsterEncounterState.OnSceneReadyAsync）。

        /// <summary>读档后主怪重新解析种类：种类 id 与 killable 与 Begin 之后一致（修之前主怪种类为 null、killable 退回 true）。</summary>
        [Test]
        public void Restore_WithoutBegin_PrimaryResolvesKindAndKillable()
        {
            EncounterStep saving = NewKindStep(null);
            saving.Begin(FarSpawn, RouteOf("a"));
            EncounterSaveData saved = saving.Capture(1);

            EncounterStep scene = NewKindStep(null);
            scene.Restore(saved);

            MonsterRules primary = scene.Taming.GetTarget("a");
            Assert.That(primary.Kind, Is.Not.Null, "读档后主怪必须有种类");
            Assert.That(primary.Kind.KindId, Is.EqualTo(ShilingKindId));
            Assert.That(primary.IsKillable, Is.False, "市令 killable=false：读档后照样拒常规伤害");
            Assert.That(scene.Taming.GetTarget("x").Kind.KindId, Is.EqualTo(XunyeKindId), "附加怪（W1 已先 Reset）同样各按各的种类");
        }

        /// <summary>读档后主怪视锥是种类配置值（市令 75°），不是初值 0 余弦对应的 180°：锥外 15° 的点看不见、锥内 15° 的点看得见。</summary>
        [Test]
        public void Restore_WithoutBegin_PrimaryVisionConeIsKindValueNot180()
        {
            EncounterStep saving = NewKindStep(null);
            saving.Begin(FarSpawn, RouteOf("a"));
            EncounterSaveData saved = saving.Capture(1);

            EncounterStep scene = NewKindStep(null);
            scene.Restore(saved);

            AssertConeIsKindValue(scene.Taming.GetTarget("a"));
        }

        /// <summary>快照恢复（回放）同一口径：不走 Begin 的主怪恢复后也有种类、视锥正确。</summary>
        [Test]
        public void Deserialize_WithoutBegin_PrimaryResolvesKindAndVisionCone()
        {
            EncounterStep recording = NewKindStep(null);
            recording.Begin(FarSpawn, RouteOf("a"));
            var bytes = new StateBuffer();
            recording.Serialize(bytes);

            EncounterStep scene = NewKindStep(null);
            scene.Deserialize(bytes);

            MonsterRules primary = scene.Taming.GetTarget("a");
            Assert.That(primary.Kind, Is.Not.Null);
            Assert.That(primary.Kind.KindId, Is.EqualTo(ShilingKindId));
            AssertConeIsKindValue(primary);
        }

        /// <summary>
        /// 存档时已死（且已掉过）的目标，读档后不再掉第二次；存档时活着的目标读档后再死，照常掉一次（正对照）。
        /// </summary>
        [Test]
        public void Restore_TargetDeadInSave_DoesNotDropAgain_AliveOneStillDropsOnce()
        {
            var savingDrops = new EncounterDropTests.RecordingDropSink();
            EncounterStep saving = NewKindStep(savingDrops);
            saving.Begin(FarSpawn, RouteOf("a"));
            Assert.That(saving.Taming.GetTarget("x").ApplyExecution(), Is.True);
            Assert.That(saving.SettleKillDropOnce(), Is.True, "前提：存档那一场已经掉过");
            EncounterSaveData saved = saving.Capture(1);

            var drops = new EncounterDropTests.RecordingDropSink();
            EncounterStep scene = NewKindStep(drops);
            scene.Restore(saved);

            Assert.That(scene.SettleKillDropOnce(), Is.False, "已死目标的闸门在读档时就落下了");
            Assert.That(drops.Drops, Is.Empty);

            Assert.That(scene.Taming.GetTarget("a").ApplyExecution(), Is.True);
            Assert.That(scene.SettleKillDropOnce(), Is.True, "活着读进来的主怪，读档后再死照常掉");
            Assert.That(drops.Drops, Has.Count.EqualTo(1));
            Assert.That(drops.Drops[0].Yao, Is.EqualTo(ShilingYaoId));
        }

        /// <summary>快照恢复同一口径：快照里已死的目标恢复后不再掉。</summary>
        [Test]
        public void Deserialize_TargetDeadInSnapshot_DoesNotDropAgain()
        {
            EncounterStep recording = NewKindStep(null);
            recording.Begin(FarSpawn, RouteOf("a"));
            Assert.That(recording.Taming.GetTarget("x").ApplyExecution(), Is.True);
            var bytes = new StateBuffer();
            recording.Serialize(bytes);

            var drops = new EncounterDropTests.RecordingDropSink();
            EncounterStep scene = NewKindStep(drops);
            scene.Deserialize(bytes);

            Assert.That(scene.Taming.GetTarget("x").Model.Health, Is.Zero, "前提：恢复出来它就是死的");
            Assert.That(scene.SettleKillDropOnce(), Is.False);
            Assert.That(drops.Drops, Is.Empty);
        }

        // ──────────────────────── 夹具 ────────────────────────

        private const int ShilingKindId = 1003;
        private const int ShilingYaoId = 3;
        private const int XunyeKindId = 1001;

        /// <summary>
        /// 两只怪的遭遇：主怪 "a" = 市令（MonsterInstaller 的种类 id 口径），附加 "x" = 巡夜人（种类覆盖 + 完整参与者）。
        /// <b>不 Begin</b>——读档那一侧要的正是「没走过 Begin」的现场；存档 / 录制那一侧由调用方自己 Begin。
        /// </summary>
        private EncounterStep NewKindStep(IMonsterDropSink drops)
        {
            var configService = new WorldTestSupport.StubConfigService(
                ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes()));
            var catalog = new MonsterKindCatalog(configService, new YaoCatalog(configService), monsterConfig.HostileRadius);
            var player = new PlayerRules(playerConfig, new PlayerModel(), NullTelemetryScope.Instance);
            var primary = new MonsterRules(monsterConfig, new MonsterModel(), new RandomService(13ul), NullTelemetryScope.Instance,
                kind: null, kindCatalog: catalog, kindId: ShilingKindId);
            var step = new EncounterStep(player, primary);
            step.UseTelemetry(telemetry.Scope("monster"));
            step.ConfigureTaming(PlayerId, "玩家", new[] { "a", "x" }, new[] { "市令", "巡夜人" },
                new[] { RouteOf("a"), RouteOf("x") }, new[] { 0, XunyeKindId }, new[] { false, true });
            if (drops != null)
            {
                step.BindDropSink(drops);
            }

            return step;
        }

        /// <summary>
        /// 视锥是种类配置值：沿朝向偏「半角 + 15°」、距离 3（在橙区内、贴身半径外）的点看不见；偏「半角 − 15°」的点看得见。
        /// 修之前 coneCosine 停在 0（视野 180°），前一个点也会被看见。
        /// </summary>
        private void AssertConeIsKindValue(MonsterRules rules)
        {
            float half = monsterConfig.ResolveVisionAngle(rules.Kind) * 0.5f;
            Assert.That(half + 15f, Is.LessThan(90f), "前提：种类视野要窄于 150°，否则「锥外」点落不到 180° 视野之外的判别区");
            Vector2 position = rules.Model.Position;
            Vector2 facing = rules.Model.Facing;

            Assert.That(rules.Detects(SnapshotAt(position + Rotate(facing, half + 15f) * 3f)), Is.False,
                "锥外 15° 看不见（视野 180° 时会看见）");
            Assert.That(rules.Detects(SnapshotAt(position + Rotate(facing, half - 15f) * 3f)), Is.True,
                "锥内 15° 看得见（正对照：不是整只怪都瞎了）");
        }

        private static Vector2 Rotate(Vector2 direction, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Vector2(direction.x * cos - direction.y * sin, direction.x * sin + direction.y * cos);
        }

        private static PlayerSnapshot SnapshotAt(Vector2 position) => new PlayerSnapshot(position, Vector2.right, false, false, 3);

        /// <summary>
        /// 按给定 id 顺序造一场遭遇（首位是主怪）并 Begin。路线按 id 取（<see cref="RouteOf"/>），
        /// 所以同一个 id 在不同顺序的场景里落在同一条路线上。埋点接收集型 sink（monster 作用域）。
        /// </summary>
        private EncounterStep NewStep(params string[] ids)
        {
            var player = new PlayerRules(playerConfig, new PlayerModel(), NullTelemetryScope.Instance);
            var monster = new MonsterRules(monsterConfig, new MonsterModel(), new RandomService(13ul), NullTelemetryScope.Instance);
            var step = new EncounterStep(player, monster);
            step.UseTelemetry(telemetry.Scope("monster"));
            var names = new string[ids.Length];
            var routes = new Vector2[ids.Length][];
            for (int i = 0; i < ids.Length; i++)
            {
                names[i] = "巡逻者 " + ids[i];
                routes[i] = RouteOf(ids[i]);
            }

            step.ConfigureTaming(PlayerId, "玩家", ids, names, routes);
            step.Begin(FarSpawn, routes[0]);
            return step;
        }

        /// <summary>每个 id 一条独立路线，彼此相隔 10 米，离玩家出生点都远于橙区。</summary>
        private static Vector2[] RouteOf(string id)
        {
            float x = 10f * (id[0] - 'a' + 1);
            return new[] { new Vector2(x, 0f), new Vector2(x + 5f, 0f) };
        }

        /// <summary>挨一刀（全局默认：可常规击杀，生命 3 → 2），做可区分的状态标记。</summary>
        private static void Hit(EncounterStep step, string id)
        {
            var damage = new DamageIntent(1);
            var attacker = new PlayerSnapshot(Vector2.zero, Vector2.right, false, false, 3);
            Assert.That(step.Taming.GetTarget(id).ApplyDamage(in damage, in attacker), Is.True, "前提：全局默认下可常规击杀");
        }

        private void AssertAligned(int saved, int scene, int dropped, int fresh)
        {
            int index = IndexOf(AlignedEvent);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "对不齐时要记一条 encounter_restore_aligned");
            string line = sink.Lines[index];
            Assert.That(sink.Levels[index], Is.EqualTo(TelemetryLevel.Warn));
            Assert.That(line, Does.Contain("\"saved\":" + saved));
            Assert.That(line, Does.Contain("\"scene\":" + scene));
            Assert.That(line, Does.Contain("\"dropped\":" + dropped));
            Assert.That(line, Does.Contain("\"fresh\":" + fresh));
        }

        private string LineFor(string eventName)
        {
            int index = IndexOf(eventName);
            return index < 0 ? null : sink.Lines[index];
        }

        private int IndexOf(string eventName)
        {
            for (int i = 0; i < sink.Lines.Count; i++)
            {
                if (sink.Lines[i].Contains(eventName))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
