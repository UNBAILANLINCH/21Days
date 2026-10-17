// 职责：钉住开仗装配——玩家生命取 BOSS 定义的「本场玩家生命」（满血，与探索血量无关，W2a 主窗口定），BOSS 满血 + 继承战斗外醉酒值，
//   正面攻击玩家先手；本场玩家生命非正时拒绝开仗（负对照）；回放测试口 OverrideSettings 撤销后下一场用回原值。
//   2026-10-08 追加：接上 `IBattleEntryContext` 之后三种进入方式（偷袭 / 正面攻击 / 被打）各自开出什么样的仗，
//   以及**没有 context 时退回接线前那句硬编码请求**（三条负对照）。
// 为什么新建：BattleSetup 在 W2a 改了玩家血量来源；一个被测类一个测试类（W1 只经 BattleFlowTests 间接覆盖）。
using System;
using Game.Battle;
using Game.Monster;
using Game.TurnBased;
using NUnit.Framework;

namespace Game.Tests.EditMode.Battle
{
    public sealed class BattleSetupTests
    {
        [Test]
        public void TryCreate_PlayerHealthComesFromBossDefinition_FullHealth()
        {
            var setup = NewSetup();

            Assert.That(setup.TryCreate(new BossDefinition("b", "B", 12, 50), out BattleSession session, out string error), Is.True, error);

            Assert.That(session.Player.MaxHealth, Is.EqualTo(BossDefinition.DefaultPlayerHealth), "默认 120 血（sp07:129-133）");
            Assert.That(session.Player.Health, Is.EqualTo(session.Player.MaxHealth), "开战满血");
            Assert.That(session.Boss.MaxHealth, Is.EqualTo(12));
            Assert.That(session.Boss.Health, Is.EqualTo(12));
            Assert.That(session.Boss.DrunkValue, Is.EqualTo(50), "继承战斗外醉酒值（07:66）");
            Assert.That(session.Phase, Is.EqualTo(BattlePhase.PlayerTurn), "正面攻击玩家先手");
        }

        // ─────────────────── 进战斗的三种方式（2026-10-08，07 §一） ───────────────────

        /// <summary>
        /// **缺省行为的负对照（本波最重要的一条）**：拿不到遭遇侧快照（`context == null`）时，
        /// 开出来的仗必须与接线前逐字一致——正面攻击、玩家先手、BOSS 满血。
        /// 独立原型场景与纯 Battle 测试作用域走的正是这一支，它们的行为不许被本波改掉。
        /// </summary>
        [Test]
        public void TryCreate_WithoutContext_FallsBackToFrontalAttackExactlyAsBefore()
        {
            var setup = NewSetup();

            Assert.That(setup.TryCreate(new BossDefinition("b", "B", 12, 50), (IBattleEntryContext)null,
                out BattleSession session, out string error), Is.True, error);

            Assert.That(session.Entry.Kind, Is.EqualTo(BattleEntryKind.FrontalAttack));
            Assert.That(session.Entry.Initiative, Is.EqualTo(BattleInitiative.Player));
            Assert.That(session.Boss.Health, Is.EqualTo(12), "正面攻击不扣血");
            Assert.That(session.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
        }

        /// <summary>**缺省行为的第二条负对照**：旧签名（不传 context）与显式传 null 开出同一场仗。</summary>
        [Test]
        public void TryCreate_OldOverload_MatchesExplicitNullContext()
        {
            var setup = NewSetup();

            setup.TryCreate(new BossDefinition("b", "B", 12, 50), out BattleSession legacy, out string legacyError);
            setup.TryCreate(new BossDefinition("b", "B", 12, 50), (IBattleEntryContext)null, out BattleSession explicitNull, out string nullError);

            Assert.That(legacyError, Is.EqualTo(nullError).Or.Null);
            Assert.That(legacy.Entry.Kind, Is.EqualTo(explicitNull.Entry.Kind));
            Assert.That(legacy.Entry.Initiative, Is.EqualTo(explicitNull.Entry.Initiative));
            Assert.That(legacy.Boss.Health, Is.EqualTo(explicitNull.Boss.Health));
        }

        /// <summary>
        /// 偷袭（`07:15-18`）：潜行 + 偷袭命中 + 玩家动手 → `SneakAttack`，玩家先手，**BOSS 开战前先扣 20% 生命**。
        /// 这条在接线前根本没有生产入口（`BattleSetup` 那句请求是写死的）。
        /// </summary>
        [Test]
        public void TryCreate_WithSneakContext_OpensSneakAttackAndShavesBossHealth()
        {
            var setup = NewSetup();

            Assert.That(setup.TryCreate(new BossDefinition("b", "B", 12, 0), Context(sneaking: true, sneakLanded: true),
                out BattleSession session, out string error), Is.True, error);

            Assert.That(session.Entry.Kind, Is.EqualTo(BattleEntryKind.SneakAttack));
            Assert.That(session.Entry.Initiative, Is.EqualTo(BattleInitiative.Player), "偷袭也是玩家先手");
            Assert.That(session.Entry.BossHealthLossPercent, Is.EqualTo(20));
            Assert.That(session.Boss.MaxHealth, Is.EqualTo(12));
            Assert.That(session.Boss.Health, Is.EqualTo(10), "12 × 20% = 2（整数向下取整，BossBattleRules.PercentOfHealth）→ 12 − 2");
        }

        /// <summary>
        /// 被打（`07:25-28`）：在警戒 / 敌对区域内，由 BOSS 先动手 → `Ambushed`，**BOSS 先手**。
        /// </summary>
        [Test]
        public void TryCreate_WithAmbushContext_OpensAmbushedWithBossInitiative()
        {
            var setup = NewSetup();

            Assert.That(setup.TryCreate(new BossDefinition("b", "B", 12, 0),
                Context(hostileZone: true, alert: MonsterAlertState.Hostile, initiator: BattleInitiator.BossAttacked),
                out BattleSession session, out string error), Is.True, error);

            Assert.That(session.Entry.Kind, Is.EqualTo(BattleEntryKind.Ambushed));
            Assert.That(session.Entry.Initiative, Is.EqualTo(BattleInitiative.Boss));
            Assert.That(session.Boss.Health, Is.EqualTo(12), "被打不扣血");
            Assert.That(session.Phase, Is.EqualTo(BattlePhase.BossTurn), "BOSS 先手 → 第一回合是它的");
        }

        /// <summary>正面攻击（`07:20-23`）走 context：在警戒区域内、玩家动手 → 玩家先手、不扣血。</summary>
        [Test]
        public void TryCreate_WithAlertContext_OpensFrontalAttack()
        {
            var setup = NewSetup();

            Assert.That(setup.TryCreate(new BossDefinition("b", "B", 12, 0),
                Context(alertZone: true, alert: MonsterAlertState.Alert), out BattleSession session, out string error), Is.True, error);

            Assert.That(session.Entry.Kind, Is.EqualTo(BattleEntryKind.FrontalAttack));
            Assert.That(session.Entry.Initiative, Is.EqualTo(BattleInitiative.Player));
            Assert.That(session.Boss.Health, Is.EqualTo(12));
        }

        /// <summary>
        /// 负对照：潜行了但**偷袭没命中**、又不在警戒 / 敌对区域 → 判定拒绝开战（`SneakStrikeNotLanded`）。
        /// 没有这条，「潜行 = 白拿 20%」就没人挡。
        /// </summary>
        [Test]
        public void TryCreate_SneakingWithoutLandedStrike_IsRefused()
        {
            var setup = NewSetup();

            bool ok = setup.TryCreate(new BossDefinition("b", "B", 12, 0), Context(sneaking: true),
                out BattleSession session, out string error);

            Assert.That(ok, Is.False);
            Assert.That(session, Is.Null);
            Assert.That(error, Does.Contain("偷袭未成功"));
        }

        [Test]
        public void TryCreate_CustomPlayerHealth_IsUsed()
        {
            var setup = NewSetup();

            setup.TryCreate(new BossDefinition("b", "B", 12, 0, playerHealth: 7), out BattleSession session, out _);

            Assert.That(session.Player.MaxHealth, Is.EqualTo(7));
            Assert.That(session.Player.Health, Is.EqualTo(7));
        }

        [Test]
        public void TryCreate_NonPositivePlayerHealth_Refuses()
        {
            var setup = NewSetup();

            bool ok = setup.TryCreate(new BossDefinition("b", "B", 12, 0, playerHealth: 0), out BattleSession session, out string error);

            Assert.That(ok, Is.False);
            Assert.That(session, Is.Null);
            Assert.That(error, Does.Contain("本场玩家生命"));
        }

        [Test]
        public void OverrideSettings_ThenDispose_NextBattleUsesOriginalSettings()
        {
            // 回放测试口：换上的数值只管换着的那段时间，撤销后下一场用回装配时的数值。
            // 观测点选「是否继承战斗外醉酒值」：开仗当场就体现在 BOSS 醉酒值上，不用推回合。
            var setup = NewSetup(); // 占位默认：继承醉酒值
            var boss = new BossDefinition("b", "B", 12, 50);
            BattleSettings basis = setup.Settings;
            IDisposable handle = setup.OverrideSettings(new BattleSettings(basis.Entry, basis.PlayerSkills, basis.BossSkills,
                basis.Drunk, basis.Flow, basis.ItemOncePerBattle, false, basis.Items));

            Assert.That(setup.IsOverridden, Is.True);
            Assert.That(setup.TryCreate(boss, out BattleSession during, out string error), Is.True, error);
            Assert.That(during.Boss.DrunkValue, Is.EqualTo(0), "前置（负对照）：换着的时候用的是换上的数值（不继承醉酒值）");

            handle.Dispose();
            handle.Dispose(); // 幂等

            Assert.That(setup.IsOverridden, Is.False);
            Assert.That(setup.TryCreate(boss, out BattleSession after, out error), Is.True, error);
            Assert.That(after.Boss.DrunkValue, Is.EqualTo(50), "撤销后下一场用回原值（继承醉酒值 50）");
        }

        // ─────────── 实时遭遇升级回合制（PRP/combat-iteration D5）：触发情形 → 开战方式、实时血量 → 战斗血量 ───────────

        /// <summary>翻译表第一行：玩家打中未察觉的怪 → 偷袭（玩家先手，怪物开战前先掉 20%）。</summary>
        [Test]
        public void TryCreate_EscalationPlayerHitUnaware_OpensSneakAttack()
        {
            var setup = NewSetup();

            Assert.That(setup.TryCreate(new BossDefinition("m", "市令", 10, 0), Escalation(EncounterEscalationTrigger.PlayerHitUnaware, 3, 3),
                out BattleSession session, out string error), Is.True, error);

            Assert.That(session.Entry.Kind, Is.EqualTo(BattleEntryKind.SneakAttack));
            Assert.That(session.Entry.Initiative, Is.EqualTo(BattleInitiative.Player));
            Assert.That(session.Boss.Health, Is.EqualTo(8), "10 × 20% = 2 → 怪物先掉 2");
            Assert.That(session.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
        }

        /// <summary>翻译表第二行：玩家打中已警戒 / 敌对的怪 → 正面攻击（玩家先手、不扣血）。</summary>
        [Test]
        public void TryCreate_EscalationPlayerHitAware_OpensFrontalAttack()
        {
            var setup = NewSetup();

            Assert.That(setup.TryCreate(new BossDefinition("m", "市令", 10, 0), Escalation(EncounterEscalationTrigger.PlayerHitAware, 3, 3),
                out BattleSession session, out string error), Is.True, error);

            Assert.That(session.Entry.Kind, Is.EqualTo(BattleEntryKind.FrontalAttack));
            Assert.That(session.Entry.Initiative, Is.EqualTo(BattleInitiative.Player));
            Assert.That(session.Boss.Health, Is.EqualTo(10));
        }

        /// <summary>翻译表第三行：怪打中玩家 → 被打（怪物先手）。</summary>
        [Test]
        public void TryCreate_EscalationMonsterHitPlayer_OpensAmbushedWithMonsterInitiative()
        {
            var setup = NewSetup();

            Assert.That(setup.TryCreate(new BossDefinition("m", "市令", 10, 0), Escalation(EncounterEscalationTrigger.MonsterHitPlayer, 3, 3),
                out BattleSession session, out string error), Is.True, error);

            Assert.That(session.Entry.Kind, Is.EqualTo(BattleEntryKind.Ambushed));
            Assert.That(session.Entry.Initiative, Is.EqualTo(BattleInitiative.Boss));
            Assert.That(session.Phase, Is.EqualTo(BattlePhase.BossTurn));
            Assert.That(session.Boss.Health, Is.EqualTo(10), "被打不扣血");
        }

        /// <summary>翻译出来的三份请求逐个过一遍规则：都能进战斗，且字段正是 PRP D5 的翻译表。</summary>
        [Test]
        public void TranslateEscalation_ThreeTriggers_MatchTheTranslationTable()
        {
            BattleEntryRequest sneak = BattleSetup.TranslateEscalation(EncounterEscalationTrigger.PlayerHitUnaware);
            BattleEntryRequest frontal = BattleSetup.TranslateEscalation(EncounterEscalationTrigger.PlayerHitAware);
            BattleEntryRequest struck = BattleSetup.TranslateEscalation(EncounterEscalationTrigger.MonsterHitPlayer);

            Assert.That(sneak.Initiator, Is.EqualTo(BattleInitiator.PlayerAttacked));
            Assert.That(sneak.SneakStrikeReady, Is.True);
            Assert.That(frontal.Initiator, Is.EqualTo(BattleInitiator.PlayerAttacked));
            Assert.That(frontal.SneakStrikeReady, Is.False);
            Assert.That(frontal.InAlertOrHostileContext, Is.True);
            Assert.That(struck.Initiator, Is.EqualTo(BattleInitiator.BossAttacked));
            Assert.That(struck.InAlertOrHostileContext, Is.True);
            BattleEntrySettings entry = BattleSettings.PlaceholderDefault.Entry;
            Assert.That(BattleEntryRules.Decide(sneak, entry).Kind, Is.EqualTo(BattleEntryKind.SneakAttack));
            Assert.That(BattleEntryRules.Decide(frontal, entry).Kind, Is.EqualTo(BattleEntryKind.FrontalAttack));
            Assert.That(BattleEntryRules.Decide(struck, entry).Kind, Is.EqualTo(BattleEntryKind.Ambushed));
        }

        /// <summary>负对照：「没有触发」不是一种开战方式——翻译直接抛，不静默按正面攻击开。</summary>
        [Test]
        public void TranslateEscalation_NoneTrigger_Throws() =>
            Assert.Throws<ArgumentOutOfRangeException>(() => BattleSetup.TranslateEscalation(EncounterEscalationTrigger.None));

        /// <summary>拒绝：本场玩家生命非正时，升级那一路同样拒绝开仗（端口据此报 rule_rejected）。</summary>
        [Test]
        public void TryCreate_EscalationWithNonPositivePlayerHealth_Refuses()
        {
            var setup = NewSetup();

            bool ok = setup.TryCreate(new BossDefinition("m", "市令", 10, 0, playerHealth: 0),
                Escalation(EncounterEscalationTrigger.PlayerHitAware, 3, 3), out BattleSession session, out string error);

            Assert.That(ok, Is.False);
            Assert.That(session, Is.Null);
            Assert.That(error, Does.Contain("本场玩家生命"));
        }

        /// <summary>
        /// 血量取整边界（用户拍板「按比例互通」：ceil(实时比例 × 战斗上限)，至少 1；战斗上限仍是 BossDefinition.PlayerHealth）。
        /// </summary>
        [TestCase(3, 3, 10, 10, TestName = "TryCreate_EscalationHealth_FullHealth_StartsFull")]
        [TestCase(1, 3, 10, 4, TestName = "TryCreate_EscalationHealth_OneThird_RoundsUp")]
        [TestCase(1, 3, 3, 1, TestName = "TryCreate_EscalationHealth_OneThirdOfThree_IsExactlyOne")]
        [TestCase(2, 3, 10, 7, TestName = "TryCreate_EscalationHealth_TwoThirds_RoundsUp")]
        [TestCase(1, 100, 10, 1, TestName = "TryCreate_EscalationHealth_LowHealth_AtLeastOne")]
        public void TryCreate_EscalationHealth_ScalesByRatio(int live, int liveMax, int battleMax, int expected)
        {
            var setup = NewSetup();

            Assert.That(setup.TryCreate(new BossDefinition("m", "市令", 10, 0, playerHealth: battleMax),
                Escalation(EncounterEscalationTrigger.PlayerHitAware, live, liveMax), out BattleSession session, out string error), Is.True, error);

            Assert.That(session.Player.MaxHealth, Is.EqualTo(battleMax), "战斗上限仍取 BOSS 定义的本场玩家生命");
            Assert.That(session.Player.Health, Is.EqualTo(expected));
        }

        /// <summary>负对照：剧情那两个重载不带实时血量，照旧满血开打（与升级之前逐字一致）。</summary>
        [Test]
        public void TryCreate_NarrativeOverloads_StillStartAtFullHealth()
        {
            var setup = NewSetup();

            setup.TryCreate(new BossDefinition("b", "B", 12, 0, playerHealth: 7), (IBattleEntryContext)null, out BattleSession session, out _);

            Assert.That(session.Player.Health, Is.EqualTo(7));
        }

        private static EncounterEscalationRequest Escalation(EncounterEscalationTrigger trigger, int health, int maxHealth) =>
            new EncounterEscalationRequest("shiling", 1003, trigger, health, maxHealth);

        private static BattleSetup NewSetup() =>
            new BattleSetup(BattleSettings.PlaceholderDefault, () => 3UL, new BattleItemInventory(new BattleFakes.Backpack()));

        // ───────────── 遭遇侧「有没有发言权」（BattleSetup.IsEncounterEngaged） ─────────────
        // 2026-10-09 补：正式流程里唯一的开战触发是剧情 Battle 阶段（demo 是 `Npc_SampleBoss` 的「挑战」），
        // 与巡逻遭遇是两回事。把巡逻怪的平静快照交出去，规则会判「不在警戒 / 敌对区域」而拒掉整场仗
        // ——实测 `BattleShowcase` 两条回放全红。所以要在翻译层加一道闸：遭遇没话说时退回硬编码缺省。

        [Test]
        public void IsEncounterEngaged_NullContext_HasNoSay() =>
            Assert.That(BattleSetup.IsEncounterEngaged(null), Is.False);

        /// <summary>demo 的常态：巡逻怪平静、玩家没潜行、也没在警戒 / 敌对区域——它此刻没有发言权。</summary>
        [Test]
        public void IsEncounterEngaged_CalmPatrol_HasNoSay() =>
            Assert.That(BattleSetup.IsEncounterEngaged(Context()), Is.False);

        /// <summary>
        /// **潜行本身不算发言权**：它说的是「潜行去袭击这只怪」那件事，而剧情 NPC 的「挑战」跟玩家恰好在潜行无关。
        /// 「潜行但没偷袭命中 = 拒绝开战」这条规则仍在规则层（见 <see cref="TryCreate_SneakingWithoutLandedStrike_IsRefused"/>）。
        /// </summary>
        [Test]
        public void IsEncounterEngaged_SneakingAlone_HasNoSay() =>
            Assert.That(BattleSetup.IsEncounterEngaged(Context(sneaking: true)), Is.False);

        /// <summary>偷袭命中（07:15-18）——够得上发言权。</summary>
        [Test]
        public void IsEncounterEngaged_SneakStrikeLanded_HasSay() =>
            Assert.That(BattleSetup.IsEncounterEngaged(Context(sneaking: true, sneakLanded: true)), Is.True);

        /// <summary>
        /// **命中过、但此刻没在潜行 ⇒ 没有发言权**（2026-10-09 实测踩到的漏判）：
        /// `SneakAttackLanded` 读的是持久事实 `stealth.assassinated`，这一局处决过谁之后它就一直是真。
        /// 只认它的话，「暗杀过之后任何一次不潜行的挑战」都会被判成有发言权 → 请求按现场翻出来是
        /// 「没潜行 + 不在警戒/敌对」→ 规则用 `NoTrigger` 把整场仗拒掉（报错与旧回归逐字相同、成因不同）。
        /// </summary>
        [Test]
        public void IsEncounterEngaged_LandedButNotSneakingNow_HasNoSay() =>
            Assert.That(BattleSetup.IsEncounterEngaged(Context(sneaking: false, sneakLanded: true)), Is.False);

        /// <summary>警戒态（07:22 正面攻击那一支）与敌对态（07:25-28 被打那一支）——都够得上发言权。</summary>
        [Test]
        public void IsEncounterEngaged_MonsterAlertOrHostile_HasSay()
        {
            Assert.That(BattleSetup.IsEncounterEngaged(Context(alertZone: true, alert: MonsterAlertState.Alert)), Is.True,
                "警戒：这一仗是从遭遇里打起来的，按现场局势判正面攻击");
            Assert.That(BattleSetup.IsEncounterEngaged(Context(hostileZone: true, alert: MonsterAlertState.Hostile)), Is.True,
                "敌对：按现场局势判被打（BOSS 先手）");
        }

        /// <summary>
        /// **「这只对手察觉到玩家」单独就够发言权**（2026-10-10 ③ 方案 2）：剧情 BOSS 没有状态机，
        /// 它能报的只有「这一刻的感知结果」——如果这条不算数，BOSS 就得靠十几米外巡逻怪的心情决定开战理由。
        /// </summary>
        [Test]
        public void IsEncounterEngaged_MonsterAware_HasSay()
        {
            Assert.That(BattleSetup.IsEncounterEngaged(Context(aware: true)), Is.True,
                "只报「察觉到了」也算：没有状态机的那一侧只有这一个量");
            Assert.That(BattleSetup.IsEncounterEngaged(Context(aware: false)), Is.False,
                "负对照：没察觉就还是没发言权（与巡逻怪平静同一口径）");
        }

        /// <summary>一份现场快照的假实现——本文件测的是「快照怎么翻成请求」，不是快照怎么从现场读出来。</summary>
        private static IBattleEntryContext Context(bool sneaking = false, bool sneakLanded = false, bool alertZone = false,
            bool hostileZone = false, MonsterAlertState alert = MonsterAlertState.Unaware,
            BattleInitiator initiator = BattleInitiator.PlayerAttacked, bool aware = false) =>
            new FakeContext(sneaking, sneakLanded, alertZone, hostileZone, alert, initiator, aware);

        private sealed class FakeContext : IBattleEntryContext
        {
            public FakeContext(bool sneaking, bool sneakLanded, bool alertZone, bool hostileZone,
                MonsterAlertState alert, BattleInitiator initiator, bool aware)
            {
                PlayerSneaking = sneaking;
                SneakAttackLanded = sneakLanded;
                PlayerInAlertZone = alertZone;
                PlayerInHostileZone = hostileZone;
                MonsterState = alert;
                Initiator = initiator;
                MonsterAware = aware;
            }

            public bool PlayerSneaking { get; }
            public bool SneakAttackLanded { get; }
            public bool PlayerInAlertZone { get; }
            public bool PlayerInHostileZone { get; }
            public MonsterAlertState MonsterState { get; }
            public BattleInitiator Initiator { get; }

            /// <summary>
            /// 「这只对手察觉到了玩家」。默认 false 让既有的负对照保持原样（它们给的都是 Unaware）；
            /// 需要它的用例显式传 <c>aware</c>——2026-10-10 新增，给「没有状态机的那一侧」用。
            /// </summary>
            public bool MonsterAware { get; }
        }

        [Test]
        public void LiveDrunkSource_ZeroOverridesDefault_ClearRestores_InvalidRejects()
        {
            var setup = NewSetup(); var boss = new BossDefinition("b", "B", 12, 50);
            setup.BindDrunkSource(id => id == "b" ? (int?)0 : null);
            Assert.That(setup.TryCreate(boss, out var sober, out _), Is.True); Assert.That(sober.Boss.DrunkValue, Is.Zero);
            setup.BindDrunkSource(_ => 80);
            Assert.That(setup.TryCreate(boss, out var drunk, out _), Is.True); Assert.That(drunk.Boss.DrunkValue, Is.EqualTo(80));
            setup.BindDrunkSource(_ => -1); Assert.That(setup.TryCreate(boss, out _, out _), Is.False);
            setup.BindDrunkSource(null); setup.TryCreate(boss, out var restored, out _); Assert.That(restored.Boss.DrunkValue, Is.EqualTo(50));
        }
    }
}

