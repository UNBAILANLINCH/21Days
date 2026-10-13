// 职责：钉住 EncounterStep 里新接的潜行结算（S3/S4）——遮挡 → 感知、击倒 → 行动限制、追逐 → chase.* 四键。
// 为什么新建：`EncounterStepTests` 覆盖的是攻击距离 / 朝向 / 碰撞回写；本波新接的三段（StealthDecisionGate、
//   击倒状态机、ChaseRules）此前只有内核自身的单测，没有一条走过「固定 tick 里的实际结算」。
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Monster;
using Game.Player;
using Game.Stealth;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Monster
{
    public sealed class EncounterStealthTests
    {
        /// <summary>挡在 (3,0) 与 (0,0) 之间的那块掩体：0.5 × 2 的轴对齐矩形（`MakeRectangle` 收全长）。</summary>
        private static readonly StealthOccluder[] BlockBetween =
            { StealthOccluder.MakeRectangle(new Vector2(1.5f, 0f), new Vector2(0.5f, 2f), 1) };

        /// <summary>
        /// 挡在 (0,0) 与 (-1,0) 之间的那块掩体：x∈[-0.9, -0.1]。
        /// 近距察觉那条用例要它——**必须真的横在连线上**，否则测的是「没有遮挡」，证明不了遮挡无关。
        /// </summary>
        private static readonly StealthOccluder[] BlockBehind =
            { StealthOccluder.MakeRectangle(new Vector2(-0.5f, 0f), new Vector2(0.8f, 4f), 2) };

        private PlayerConfig playerConfig;
        private MonsterConfig monsterConfig;

        [SetUp]
        public void SetUp()
        {
            playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
            monsterConfig = ScriptableObject.CreateInstance<MonsterConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(playerConfig);
            Object.DestroyImmediate(monsterConfig);
        }

        // ──────────────────────────── 遮挡 → 感知 ────────────────────────────

        /// <summary>
        /// 遮挡 → 事实键，且**感知本身也被挡住**（2026-10-08 判据变更，见下）。
        /// <para>
        /// 变更前这条用例最后一句断言的是 `Detects == true`（「感知本身没被本波改动」）——那时遮挡只进事实层，
        /// 结果就是「`stealth.hidden` 为真、怪照样警戒照样追」这条自相矛盾的缺陷。变更后视锥两档吃遮挡，
        /// 所以这里反过来钉「感知确实看不见了」。
        /// </para>
        /// <para>
        /// 夹具按正式流程接法走：遮挡体喂进 `step.Sight` 之后 `BindOcclusion` 分发给每一只怪，
        /// 与 `MonsterEncounterState` / `MonsterInstaller` 的接线一致。
        /// </para>
        /// </summary>
        [Test]
        public void Step_WhileOccluderBlocksSight_PlayerCountsAsHiddenAndCovered()
        {
            Rig rig = NewRig(Vector2.zero, new Vector2(3f, 0f), new Vector2(0f, 0f));
            Assert.That(rig.MonsterRules.Detects(rig.PlayerModel.Snapshot), Is.True, "没遮挡时距离与夹角上都够得着");

            rig.Step.Sight.SetOccluders(BlockBetween);
            rig.Step.BindOcclusion(rig.Step.Sight);
            Tick(rig, 0f);

            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.Hidden), Is.True, "视线被挡 = 未被察觉");
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.Cover), Is.True, "处于掩体遮挡下");
            Assert.That(rig.MonsterRules.Detects(rig.PlayerModel.Snapshot), Is.False,
                "2026-10-08 判据变更：怪物的视锥也吃遮挡——事实层与行为层从此是同一个答案");
        }

        [Test]
        public void Step_WithoutOccluder_PlayerIsSeenAndNotCovered()
        {
            Rig rig = NewRig(Vector2.zero, new Vector2(3f, 0f), new Vector2(0f, 0f));
            rig.Step.Sight.SetOccluders(null);
            rig.Step.BindOcclusion(rig.Step.Sight);

            Tick(rig, 0f);

            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.Hidden), Is.False, "没遮挡就是被看见");
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.Cover), Is.False);
        }

        /// <summary>
        /// 正式接法下 `stealth.cover` 必须真的点亮（2026-10-08 主窗口复核抓到的一处回归）。
        /// <para>
        /// 回归的形状：`SettleStealth` 的 `coverBlocks = perceivesByCone &amp;&amp; !perceives` 是**两个布尔相减**。
        /// 若第一层 `perceivesByCone` 用的是含遮挡的 `MonsterRules.Detects`，被挡住时它已经是 false，
        /// 相减恒为 0 → `stealth.cover` 在正式流程里永远点不亮，只剩「不注入遮挡的夹具」还亮着。
        /// 修法是第一层改走不含遮挡的 `MonsterRules.PerceivedBySenses`（行为层与事实层分层，
        /// 见 `EncounterStep.SettleStealth` 的注释）。
        /// </para>
        /// <para>
        /// **这条用例钉的就是那个相减**：光断言 `Detects == false`（上面那条）证明不了 `cover` 还活着。
        /// </para>
        /// </summary>
        [Test]
        public void Step_WithOccluderAndInSightCone_WritesCoverFact()
        {
            Rig rig = NewRig(Vector2.zero, new Vector2(3f, 0f), new Vector2(0f, 0f));
            Assert.That(rig.MonsterRules.PerceivedBySenses(rig.PlayerModel.Snapshot), Is.GreaterThan(0),
                "前置：这把尺子（不含遮挡）在 3 米橙区里够得着——它就是「视线本来够得着」那一半");

            rig.Step.Sight.SetOccluders(BlockBetween);
            rig.Step.BindOcclusion(rig.Step.Sight);
            Tick(rig, 0f);

            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.Cover), Is.True, "视线够得着却被切断 = 掩体遮挡下");
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.Hidden), Is.True, "同一 tick 里就是未被察觉");
            Assert.That(rig.MonsterRules.Detects(rig.PlayerModel.Snapshot), Is.False, "行为层也看不见（怪不追）");
        }

        /// <summary>
        /// 负对照：同一位置**没有掩体**时 `stealth.cover` 必须为假——证明它不是恒真。
        /// 与上一条配对，缺一条就分不清「修好了」与「`cover` 被写成了常量 true」。
        /// </summary>
        [Test]
        public void Step_WithoutOccluderInSightCone_DoesNotWriteCoverFact()
        {
            Rig rig = NewRig(Vector2.zero, new Vector2(3f, 0f), new Vector2(0f, 0f));

            rig.Step.Sight.SetOccluders(null);
            rig.Step.BindOcclusion(rig.Step.Sight);
            Tick(rig, 0f);

            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.Cover), Is.False, "没有遮挡就不是掩体遮挡下");
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.Hidden), Is.False, "而且确实被看见了");
        }

        /// <summary>
        /// 贴身那一档确实没被遮挡改掉（走的是 `EncounterStep` 那条完整链，不只是 `MonsterRules` 直接调）：
        /// **不潜行**、背后 1 米、掩体**真的横在两人连线上** → 照样被发现，怪进警戒并转身。
        /// 出处 `docs/design/spotlight/06_怪物状态与交互设计文档.md:71-73`（不分朝向的近距察觉区、要潜行才免）。
        /// <para>
        /// 用的是 <see cref="BlockBehind"/> 而不是 <see cref="BlockBetween"/>：后者横在 (3,0)–(0,0) 上，
        /// 与「怪 (0,0) → 玩家 (-1,0)」这条线**没有交点**，拿它来测「遮挡不影响贴身」等于在测「没有遮挡」。
        /// 所以下面先断言这块掩体确实切断了连线。
        /// </para>
        /// <para>
        /// 这条**故意不断言 `stealth.cover`**：贴身察觉与视锥是两档，1 米处的玩家没进正面锥，
        /// 「视线够得着」那一半不成立，所以几何上就不算「处于掩体遮挡下」。
        /// `stealth.cover` 的语义与正例由上面那对 `Step_WithOccluderAndInSightCone_*` 用例钉住。
        /// </para>
        /// </summary>
        [Test]
        public void Step_NearSenseThroughOccluder_WithoutSneak_StillAlerts()
        {
            Rig rig = NewRig(new Vector2(-1f, 0f), Vector2.zero, new Vector2(5f, 0f));
            rig.Step.Sight.SetOccluders(BlockBehind);
            rig.Step.BindOcclusion(rig.Step.Sight);
            Assert.That(rig.MonsterRules.PerceivedBySenses(rig.PlayerModel.Snapshot), Is.EqualTo(1),
                "前置：只有贴身那一档够得着（背后 1 米、正面锥外）");
            Assert.That(rig.Step.Sight.IsBlocked(rig.MonsterModel.Position, rig.PlayerModel.Position), Is.True,
                "前置：掩体确实切断了怪 → 玩家这条线");

            Tick(rig, 0.1f);

            Assert.That(rig.MonsterModel.Mode, Is.EqualTo(MonsterMode.Alert), "贴身察觉穿透掩体");
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.Hidden), Is.False, "被发现了就不是 hidden");
        }

        // ──────────────────────────── 事实层与行为层必须同向 ────────────────────────────

        /// <summary>
        /// **事实层与行为层同向（贴身侧）**——2026-10-08 主窗口复跑抓到的**真产品缺陷**，方向与缺陷①相反。
        /// <para>
        /// 现场：玩家在怪背后 1 米、不潜行、掩体真的横在连线上。行为层（`MonsterRules.Sense`）按设计
        /// **穿过掩体**发现他（贴身那一档不吃遮挡，`06:71-73`「要潜行才免」），于是 `stealth.hidden` 不许为真。
        /// </para>
        /// <para>
        /// 修前的形状：`SettleStealth` 拿**整支**感知（含贴身那一档）去喂
        /// <c>StealthDecisionGate.PerceivesThroughCover</c>，那个门用「对视线的遮挡」把它掐掉了 →
        /// 怪已经进警戒、`stealth.hidden` 却是真。修法：事实层直接抄行为层的答案
        /// （<c>MonsterRules.Detects</c>），两层从结构上不可能再分叉。
        /// </para>
        /// </summary>
        [Test]
        public void Step_WhenNearSensePiercesOccluder_FactLayerAgreesWithBehaviour()
        {
            Rig rig = NewRig(new Vector2(-1f, 0f), Vector2.zero, new Vector2(5f, 0f));
            rig.Step.Sight.SetOccluders(BlockBehind);
            rig.Step.BindOcclusion(rig.Step.Sight);

            Tick(rig, 0.1f);

            bool aware = IsAware(rig);
            Assert.That(aware, Is.True, "前置：贴身察觉穿透掩体，怪进了警戒");
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.Hidden), Is.EqualTo(!aware),
                "事实层与行为层同向：它已经发现你了，stealth.hidden 就不许为真");
        }

        /// <summary>
        /// **事实层与行为层同向（视锥侧）**：与上一条配对，覆盖相反的方向。
        /// 玩家在正面锥内 3 米、掩体切断视线 → 行为层看不见（警戒不涨），那么 `stealth.hidden` 必须为真。
        /// 两条合起来才说明「同向」不是碰巧——只留一条时，把 `hidden` 写死成常量也能过。
        /// </summary>
        [Test]
        public void Step_WhenConeBlockedByOccluder_FactLayerAgreesWithBehaviour()
        {
            Rig rig = NewRig(Vector2.zero, new Vector2(3f, 0f), new Vector2(0f, 0f));
            rig.Step.Sight.SetOccluders(BlockBetween);
            rig.Step.BindOcclusion(rig.Step.Sight);

            Tick(rig, 0f);

            bool aware = IsAware(rig);
            Assert.That(aware, Is.False, "前置：视线被切断，怪没察觉");
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.Hidden), Is.EqualTo(!aware),
                "事实层与行为层同向：它确实没看见你，stealth.hidden 就该为真");
        }

        /// <summary>行为层的「察觉」口径：不在巡逻两态就是被惊动了（同 `EncounterStep.SettleStealth` 里的 <c>aware</c>）。</summary>
        private static bool IsAware(Rig rig) =>
            rig.MonsterModel.Mode != MonsterMode.PatrolWalk && rig.MonsterModel.Mode != MonsterMode.PatrolPause;

        /// <summary>
        /// 绕背（`stealth.behind`）：**潜行**贴到怪物背后且在暗杀距离内。
        /// 潜行不是可选项：不潜行时背后近距（1.5 米）照样被察觉，怪物会在同一个 tick 里转身（见下一条负对照）。
        /// </summary>
        [Test]
        public void Step_SneakingPlayerBehindMonsterInRange_WritesBehindFact()
        {
            Rig behind = NewRig(new Vector2(-0.5f, 0f), Vector2.zero, new Vector2(5f, 0f));
            Tick(behind, 0f, sneak: true);

            Assert.That(behind.MonsterModel.Facing, Is.EqualTo(Vector2.right), "没察觉就不会转身，朝向仍是巡逻方向");
            Assert.That(behind.Step.Facts.IsTrue(StealthFactKeys.Behind), Is.True);
        }

        /// <summary>负对照：站在正面不算绕背。</summary>
        [Test]
        public void Step_SneakingPlayerInFrontOfMonster_DoesNotWriteBehindFact()
        {
            Rig front = NewRig(new Vector2(0.5f, 0f), Vector2.zero, new Vector2(5f, 0f));
            Tick(front, 0f, sneak: true);

            Assert.That(front.Step.Facts.IsTrue(StealthFactKeys.Behind), Is.False, "正面不算绕背");
        }

        /// <summary>
        /// 负对照（这条用例自己踩过的坑）：**不潜行**时背后近距（`MonsterConfig.nearSenseRadius` 1.5）
        /// 照样察觉，`MonsterRules` 在同一个 tick 里转身面向玩家，绕背当场不成立。
        /// 也就是说 `stealth.behind` 的前提是「潜行 + 站位」，不只是站位。
        /// </summary>
        [Test]
        public void Step_NonSneakingPlayerBehind_IsSensedAndTurnsAround()
        {
            Rig rig = NewRig(new Vector2(-0.5f, 0f), Vector2.zero, new Vector2(5f, 0f));

            Tick(rig, 0f);

            Assert.That(rig.MonsterModel.Mode, Is.EqualTo(MonsterMode.Alert), "背后近距察觉命中");
            Assert.That(rig.MonsterModel.Facing, Is.EqualTo(Vector2.left), "被发现 → 转身面向玩家");
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.Behind), Is.False);
        }

        // ──────────────────────────── 击倒 → 行动限制 ────────────────────────────

        /// <summary>倒地期：不能攻击、也挪不动（`03_潜行与暗杀.md:85` R3 的「只能缓慢移动」第一阶段是 0 速）。</summary>
        [Test]
        public void Knockdown_WhileDowned_BlocksAttackAndMovement()
        {
            Rig rig = NewRig(Vector2.zero, new Vector2(10f, 0f), new Vector2(20f, 0f));
            Hit(rig, 1);

            Tick(rig, 0.1f, Vector2.right, attack: true);

            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.Knockdown), Is.True, "stealth.knockdown 应为真");
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.KnockdownCountLow), Is.True,
                "击倒 1 次 = 低档（阈值 1/3，与 StealthConfig 占位值一致）");
            Assert.That(rig.PlayerModel.AttackCooldownLeft, Is.Zero, "倒地期间攻击被压住（冷却没被点亮）");
            Assert.That(rig.PlayerModel.Position.x, Is.EqualTo(0f), "倒地期间按 0 倍速移动");
        }

        /// <summary>负对照：没被击倒时同一份输入照常攻击、按原速移动。</summary>
        [Test]
        public void WithoutKnockdown_SameInput_AttacksAndMovesAtFullSpeed()
        {
            Rig rig = NewRig(Vector2.zero, new Vector2(10f, 0f), new Vector2(20f, 0f));

            Tick(rig, 0.1f, Vector2.right, attack: true);

            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.Knockdown), Is.False);
            Assert.That(rig.PlayerModel.AttackCooldownLeft, Is.GreaterThan(0f), "照常攻击");
            Assert.That(rig.PlayerModel.Position.x, Is.EqualTo(playerConfig.MoveSpeed * 0.1f).Within(0.0001f));
        }

        /// <summary>挣扎期：能走但只能慢走（倍率来自 KnockdownSettings，占位 0.35）。</summary>
        [Test]
        public void Knockdown_WhileCrawling_MovesAtCrawlSpeedOnly()
        {
            Rig rig = NewRig(Vector2.zero, new Vector2(10f, 0f), new Vector2(20f, 0f));
            Hit(rig, 1);

            // 一次走完倒地期（占位 1.5 秒）：这一 tick 末态进入挣扎期，随后按挣扎倍率移动。
            Tick(rig, 1.5f, Vector2.right, attack: true);

            Assert.That(rig.PlayerModel.AttackCooldownLeft, Is.Zero, "挣扎期仍然不能攻击");
            float expected = playerConfig.MoveSpeed * 0.35f * 1.5f;
            Assert.That(rig.PlayerModel.Position.x, Is.EqualTo(expected).Within(0.0001f));
            Assert.That(rig.PlayerModel.Position.x, Is.LessThan(playerConfig.MoveSpeed * 1.5f), "比步行慢");
        }

        // ──────────────────────────── 追逐 → chase.* ────────────────────────────

        [Test]
        public void Chase_WhenMonsterTurnsHostile_WritesActiveFact()
        {
            Rig rig = NewRig(Vector2.zero, new Vector2(1.5f, 0f), new Vector2(0f, 0f));

            Tick(rig, 0.1f);

            Assert.That(rig.MonsterModel.Mode, Is.EqualTo(MonsterMode.Hostile), "红区内转敌对追击");
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.ChaseActive), Is.True);
        }

        /// <summary>负对照：怪物还没察觉玩家时不起追（`chase.active` 不写）。</summary>
        [Test]
        public void Chase_WhenMonsterUnaware_DoesNotActivate()
        {
            Rig rig = NewRig(Vector2.zero, new Vector2(10f, 0f), new Vector2(20f, 0f));

            Tick(rig, 0.1f);

            Assert.That(rig.MonsterModel.Mode, Is.Not.EqualTo(MonsterMode.Hostile));
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.ChaseActive), Is.False);
        }

        [Test]
        public void Chase_AfterLosingSightFarAway_WritesEscapedAndClearsActive()
        {
            Rig rig = NewRig(Vector2.zero, new Vector2(1.5f, 0f), new Vector2(0f, 0f));
            Tick(rig, 0.1f);
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.ChaseActive), Is.True);

            // 玩家跑远（> 摆脱距离 8）：断视线 2 秒（跟丢宽限，`04_追逐.md:104` R23 的原文值）后算摆脱。
            rig.Step.CorrectPlayerPosition(new Vector2(30f, 0f));
            for (int i = 0; i < 25; i++)
            {
                Tick(rig, 0.1f);
            }

            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.ChaseEscaped), Is.True);
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.ChaseActive), Is.False);
        }

        /// <summary>负对照：躲起来了但**没拉开距离**（5 米 &lt; 摆脱距离 8）不算摆脱。</summary>
        [Test]
        public void Chase_HiddenButNotFarEnough_DoesNotEscape()
        {
            Rig rig = NewRig(Vector2.zero, new Vector2(1.5f, 0f), new Vector2(0f, 0f));
            Tick(rig, 0.1f);
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.ChaseActive), Is.True);

            // 挪到怪物背后 5 米：断视线（锥外、超出背后近距 1.5），但没到摆脱距离 8。
            rig.Step.CorrectPlayerPosition(new Vector2(5f, 0f));
            for (int i = 0; i < 25; i++)
            {
                Tick(rig, 0.1f);
            }

            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.ChaseEscaped), Is.False, "距离不够不算摆脱");
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.ChaseActive), Is.True);
        }

        // ──────────────────────────── 夹具 ────────────────────────────

        private sealed class Rig
        {
            public PlayerModel PlayerModel { get; set; }
            public PlayerRules PlayerRules { get; set; }
            public MonsterModel MonsterModel { get; set; }
            public MonsterRules MonsterRules { get; set; }
            public EncounterStep Step { get; set; }
            public RandomService Random { get; set; }
            public int Tick { get; set; }
        }

        private Rig NewRig(Vector2 playerSpawn, params Vector2[] patrolPoints)
        {
            var rig = new Rig
            {
                PlayerModel = new PlayerModel(),
                MonsterModel = new MonsterModel(),
                Random = new RandomService(5ul),
            };
            rig.PlayerRules = new PlayerRules(playerConfig, rig.PlayerModel, NullTelemetryScope.Instance);
            rig.MonsterRules = new MonsterRules(monsterConfig, rig.MonsterModel, new RandomService(5ul), NullTelemetryScope.Instance);
            rig.Step = new EncounterStep(rig.PlayerRules, rig.MonsterRules);
            rig.Step.Begin(playerSpawn, patrolPoints);
            return rig;
        }

        /// <summary>推进一个固定 tick；<paramref name="move"/> 是摇杆输入，<paramref name="attack"/> 是攻击键。</summary>
        private static void Tick(Rig rig, float deltaTime, Vector2 move = default, bool attack = false, bool sneak = false)
        {
            uint buttons = attack ? InputCommand.ButtonAttack : sneak ? InputCommand.ButtonSneak : 0u;
            var command = new InputCommand(move, Vector2.zero, buttons, Vector2.zero, 0);
            var context = new SimulationContext(rig.Tick, deltaTime, in command, rig.Random);
            rig.Tick++;
            rig.Step.Step(in context);
        }

        private static void Hit(Rig rig, int amount)
        {
            var damage = new DamageIntent(amount);
            rig.PlayerRules.ApplyDamage(in damage);
        }
    }
}
