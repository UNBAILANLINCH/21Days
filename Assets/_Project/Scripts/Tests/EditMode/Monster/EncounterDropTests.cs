// 职责：钉住「击杀掉落」这条调用链的**调用侧**——谁在什么时候调 `IMonsterDropSink.SettleKillDrop`，
//   以及最要紧的那条边界：**一只怪只结一次**（重复结算会让掉落翻倍）。
// 为什么新建：`IMonsterDropSink` 的契约（`Runtime/Monster/IMonsterDropSink.cs`）是主窗口落盘的，
//   实现方在 `Game.Loot`（另一路并行做）；这一侧在接线前一个调用点都没有。本文件覆盖的正是调用侧：
//   `EncounterStep.BindDropSink` + `SettleKillDropOnce` + 死亡分支。
//
// 用例一律用**真实生成的表**（`ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes())` + 真 `YaoCatalog`），
//   不自造假种类。原因（2026-10-08 修一处编译错误时定的）：`MonsterKind` 是 `sealed` 的
//   **构造后不再变的只读视图**（见它的类文档），跨表三列留的口是「`yaoCatalog` 传 null 时降级成空」，
//   **不是「可以子类化」的口**；为了造几个掉落 id 去摘 `sealed`（或给它加 `virtual`）＝为测试放宽已定契约。
//   真表现在恰好有带掉落的种类（种类 1001 巡夜人 → 妖 1 → `drop_items`），用它比假种类更能说明问题。
// 不依赖 Unity 场景与 Addressables。
using System.Collections.Generic;
using Game.Core.Config;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Mirror;
using Game.Monster;
using Game.Player;
using Game.Tests.EditMode.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Monster
{
    /// <summary>击杀掉落的调用侧（`EncounterStep`）。</summary>
    public sealed class EncounterDropTests
    {
        /// <summary>种类 id 1001「巡夜人」。与 `Tables/Data/monster_species/1001.json` 一致，改表要同步改这里。</summary>
        private const int NightWatchmanKindId = 1001;

        /// <summary>它指向的妖物 id（`Tables/Data/monster_species/1001.json` 的 `yao_id`）。</summary>
        private const int NightWatchmanYaoId = 1;

        /// <summary>`Tables/Data/yao/1.json` 的 `drop_items`；改表要同步改这里（同 `MonsterKindCatalogTests` 的口径）。</summary>
        private static readonly int[] NightWatchmanDrops = { 1101 };

        private global::cfg.Tables tables;
        private PlayerConfig playerConfig;
        private MonsterConfig monsterConfig;
        private MonsterKindCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            tables = ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes());
            var configService = new StubConfigService(tables);
            playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
            monsterConfig = ScriptableObject.CreateInstance<MonsterConfig>();
            catalog = new MonsterKindCatalog(configService, new YaoCatalog(configService), monsterConfig.HostileRadius);
            Assert.That(catalog.TryGet(NightWatchmanKindId, out _), Is.True,
                $"真表里应有种类 {NightWatchmanKindId} 且它指向的妖有 drop_items——本文件的用例要它才有东西可掉");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(playerConfig);
            Object.DestroyImmediate(monsterConfig);
        }

        // ──────────────────────── 不接 = 什么都不掉 ────────────────────────

        /// <summary>
        /// 负对照：**没接** `IMonsterDropSink` 时死亡不产生任何掉落结算。
        /// 独立原型场景与纯 Monster 测试作用域走的正是这一支，那里不该凭空多出一条掉落。
        /// </summary>
        [Test]
        public void WithoutSink_DeathSettlesNothing()
        {
            Rig rig = NewRig(out RecordingDropSink sink);
            KillPrimary(rig);

            Assert.That(rig.Step.SettleKillDropOnce(), Is.False, "没接掉落口，什么都不掉");
            Assert.That(sink.Drops, Is.Empty);
        }

        // ──────────────────────── 接了就结一次，且只结一次 ────────────────────────

        /// <summary>接了之后：主怪死亡 → 结算一次，带的是这只怪的 `yaoId` 与表里的掉落 id。</summary>
        [Test]
        public void WithSink_PrimaryDeath_SettlesExactlyOnce()
        {
            Rig rig = NewRig(out RecordingDropSink sink);
            rig.Step.BindDropSink(sink);
            KillPrimary(rig);

            Assert.That(rig.Step.SettleKillDropOnce(), Is.True);

            Assert.That(sink.Drops, Has.Count.EqualTo(1), "只结一次");
            Assert.That(sink.Drops[0].Yao, Is.EqualTo(NightWatchmanYaoId));
            Assert.That(sink.Drops[0].Items, Is.EqualTo(NightWatchmanDrops));
        }

        /// <summary>**一只怪只结一次**：重复调用第二次起返回 false，外部口没被再调一次（掉落不会翻倍）。</summary>
        [Test]
        public void WithSink_RepeatedCalls_SettleOnlyOnce()
        {
            Rig rig = NewRig(out RecordingDropSink sink);
            rig.Step.BindDropSink(sink);
            KillPrimary(rig);

            Assert.That(rig.Step.SettleKillDropOnce(), Is.True);
            Assert.That(rig.Step.SettleKillDropOnce(), Is.False);
            Assert.That(rig.Step.SettleKillDropOnce(), Is.False);

            Assert.That(sink.Drops, Has.Count.EqualTo(1), "重复调用不许让掉落翻倍");
        }

        /// <summary>怪还活着的时候不结（结算口必须什么都不做）。</summary>
        [Test]
        public void WithSink_WhileAlive_SettlesNothing()
        {
            Rig rig = NewRig(out RecordingDropSink sink);
            rig.Step.BindDropSink(sink);

            Assert.That(rig.Step.SettleKillDropOnce(), Is.False);

            Assert.That(sink.Drops, Is.Empty);
        }

        /// <summary>重开一场遭遇（`Begin`）后闸门清零：同一只怪在下一场里能再掉一次。</summary>
        [Test]
        public void Begin_ResetsTheGate_NextEncounterDropsAgain()
        {
            Rig rig = NewRig(out RecordingDropSink sink);
            rig.Step.BindDropSink(sink);
            KillPrimary(rig);
            Assert.That(rig.Step.SettleKillDropOnce(), Is.True);

            rig.Step.Begin(Vector2.zero, new[] { Vector2.zero, new Vector2(10f, 0f) });
            KillPrimary(rig);

            Assert.That(rig.Step.SettleKillDropOnce(), Is.True, "新的一场要能再掉");
            Assert.That(sink.Drops, Has.Count.EqualTo(2));
        }

        // ──────────────────────── 种类为空 = 跳过，不许猜 ────────────────────────

        /// <summary>
        /// `MonsterRules.Kind` 为 null（表没就绪 / 这只怪没接种类）时**跳过掉落**，不猜一个 id 出来。
        /// 这条覆盖的正是「表还没加载」那一段启动窗口——乱猜一个 `yaoId` 会掉出错的物品。
        /// </summary>
        [Test]
        public void WithoutKind_SkipsDropInsteadOfGuessing()
        {
            Rig rig = NewRig(out RecordingDropSink sink, withKind: false);
            rig.Step.BindDropSink(sink);
            Assert.That(rig.Monster.Kind, Is.Null, "前置：这只怪没接种类（夹具不传目录）");
            KillPrimary(rig);

            Assert.That(rig.Step.SettleKillDropOnce(), Is.False);

            Assert.That(sink.Drops, Is.Empty, "没有 yaoId 就不猜，宁可这一只不掉");
        }

        // ──────────────────────── 死亡分支真的会调它 ────────────────────────

        /// <summary>
        /// 端到端的一条：怪被打死 → `Step` 推进完双方规则之后自己调 `SettleKillDropOnce`，
        /// 不需要外部再喊一次。这条钉的是「接线接在了死亡那一处」，而不是「方法写得对但没人调」。
        /// </summary>
        [Test]
        public void Step_WhenPrimaryDiesAlongTheTick_SettlesDropFromTheDeathBranch()
        {
            Rig rig = NewRig(out RecordingDropSink sink);
            rig.Step.BindDropSink(sink);
            KillPrimary(rig);

            Tick(rig, 0.1f);

            Assert.That(sink.Drops, Has.Count.EqualTo(1), "死亡分支自己结了一次");
            Assert.That(sink.Drops[0].Yao, Is.EqualTo(NightWatchmanYaoId));
        }

        /// <summary>同上但负对照：装了 sink、怪没死 → 走几个 tick 一次都不结。</summary>
        [Test]
        public void Step_WhenPrimarySurvives_SettlesNothing()
        {
            Rig rig = NewRig(out RecordingDropSink sink);
            rig.Step.BindDropSink(sink);

            Tick(rig, 0.1f);
            Tick(rig, 0.1f);

            Assert.That(sink.Drops, Is.Empty);
        }

        // ──────────────────────── 跨表：id 与掉落列表确实来自妖物表 ────────────────────────

        /// <summary>
        /// 交给掉落口的那两个值确实来自妖物表那一行，而不是某处硬编码：
        /// `yaoId` == 妖物表主键、掉落列表 == 同一行的 `drop_items`
        /// （同 `MonsterKindCatalogTests.Kind_CrossTableColumns_ComeFromYaoTable` 的口径）。
        /// </summary>
        [Test]
        public void WithRealTables_PassesYaoIdAndDropItemsFromTheYaoRow()
        {
            var yao = new YaoCatalog(new StubConfigService(tables));
            global::cfg.yao.Yao row = yao.Get(NightWatchmanYaoId);

            Rig rig = NewRig(out RecordingDropSink sink);
            rig.Step.BindDropSink(sink);
            KillPrimary(rig);
            Tick(rig, 0.1f);

            Assert.That(sink.Drops, Has.Count.EqualTo(1));
            Assert.That(sink.Drops[0].Yao, Is.EqualTo(row.Id));
            Assert.That(sink.Drops[0].Items, Is.EqualTo(row.DropItems));
        }

        // ──────────────────────── 夹具 ────────────────────────

        private sealed class Rig
        {
            public PlayerModel PlayerModel { get; set; }
            public PlayerRules PlayerRules { get; set; }
            public MonsterModel MonsterModel { get; set; }
            public MonsterRules Monster { get; set; }
            public EncounterStep Step { get; set; }
        }

        /// <summary>
        /// 造一场最小遭遇。
        /// <para>
        /// <paramref name="withKind"/> 为真（默认）时接**真表目录**，`Reset` 会查到种类 1001；
        /// 为假时**一个目录都不传** → `MonsterRules.kindCatalog == null` → `ResolveKind` 直接返回、
        /// `Kind` 恒为 null（表没就绪那一支，见 <see cref="WithoutKind_SkipsDropInsteadOfGuessing"/>）。
        /// </para>
        /// <para>
        /// ⚠️ 这里**不能**写成 `kindCatalog ?? catalog`（2026-10-08 主窗口复跑抓到过）：那样「传 null」会被
        /// 悄悄兜回真表目录，`Kind` 照样解析成 1001，于是「没有种类」那条用例的前提根本不成立——
        /// 而它的全部价值就在「表没就绪时不猜 id」。夹具的开关必须**真的能关掉**那个输入。
        /// </para>
        /// <para>种类是在 <see cref="EncounterStep.Begin"/> 的 `Reset` 里查的，所以断言要放在 `Begin` 之后。</para>
        /// </summary>
        private Rig NewRig(out RecordingDropSink sink, bool withKind = true)
        {
            sink = new RecordingDropSink();
            var rig = new Rig
            {
                PlayerModel = new PlayerModel(),
                MonsterModel = new MonsterModel(),
            };
            rig.PlayerRules = new PlayerRules(playerConfig, rig.PlayerModel, NullTelemetryScope.Instance);
            rig.Monster = new MonsterRules(monsterConfig, rig.MonsterModel, new RandomService(5ul), NullTelemetryScope.Instance,
                kind: null, kindCatalog: withKind ? catalog : null, kindId: NightWatchmanKindId);
            rig.Step = new EncounterStep(rig.PlayerRules, rig.Monster);
            rig.Step.Begin(Vector2.zero, new[] { Vector2.zero, new Vector2(10f, 0f) });
            return rig;
        }

        /// <summary>
        /// 让主怪死亡：走**公开的常规击杀入口**。
        /// <para>
        /// **不能直接写 `MonsterModel.Health` / `.Mode`**：它们是 `internal set`，而 `Game.Runtime` 没有对
        /// `Game.Tests.EditMode` 开 `InternalsVisibleTo`（工程约定，多处源码注释明写），测试里那样写必然 CS0272。
        /// 需要写模型状态时只有两条路：公开规则入口，或反射（同 `DialogueInteractableTests` / `BattleActorTests`）。
        /// </para>
        /// <para>
        /// 伤害取 `ResolveMaxHealth`（种类优先、否则全局）而不写死 3：改表不会让这条无声漂移。
        /// 断言返回值：`killable = false` 的种类会**拒伤**，那时这条前置会直接点名是表的问题。
        /// </para>
        /// </summary>
        private void KillPrimary(Rig rig)
        {
            var damage = new DamageIntent(monsterConfig.ResolveMaxHealth(rig.Monster.Kind));
            var attacker = new PlayerSnapshot(Vector2.zero, Vector2.right, false, false, 3);
            Assert.That(rig.Monster.ApplyDamage(in damage, in attacker), Is.True,
                "前置：这只怪要能被常规击杀打死（真表里种类 1001 的 killable 为 true，且生命为正）");
        }

        private static void Tick(Rig rig, float deltaTime)
        {
            var command = new InputCommand(Vector2.zero, Vector2.zero, 0u, Vector2.zero, 0);
            var context = new SimulationContext(0, deltaTime, in command, new RandomService(5ul));
            rig.Step.Step(in context);
        }

        /// <summary>收集型掉落口：只记调用，不做任何结算（实现方在 Game.Loot，本文件不测它）。</summary>
        private sealed class RecordingDropSink : IMonsterDropSink
        {
            public List<(int Yao, int[] Items)> Drops { get; } = new List<(int, int[])>();

            public void SettleKillDrop(int yaoId, IReadOnlyList<int> dropItemIds)
            {
                var copy = new int[dropItemIds == null ? 0 : dropItemIds.Count];
                for (int i = 0; i < copy.Length; i++)
                {
                    copy[i] = dropItemIds[i];
                }

                Drops.Add((yaoId, copy));
            }
        }

        /// <summary>只递一份现成的 <c>cfg.Tables</c>（同 MonsterKindCatalogTests 的最小假实现）。</summary>
        private sealed class StubConfigService : IConfigService
        {
            public StubConfigService(global::cfg.Tables tables) => Tables = tables;
            public global::cfg.Tables Tables { get; }
            public ulong ContentHash => throw new System.NotSupportedException("假配置服务不提供内容指纹");
        }
    }
}
