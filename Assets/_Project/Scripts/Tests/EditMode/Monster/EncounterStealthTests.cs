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
        /// <summary>挡在 (3,0) 与 (0,0) 之间的那块掩体：0.5 × 2 的轴对齐矩形。</summary>
        private static readonly StealthOccluder[] BlockBetween =
            { StealthOccluder.MakeRectangle(new Vector2(1.5f, 0f), new Vector2(0.5f, 2f), 1) };

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

        [Test]
        public void Step_WhileOccluderBlocksSight_PlayerCountsAsHiddenAndCovered()
        {
            Rig rig = NewRig(Vector2.zero, new Vector2(3f, 0f), new Vector2(0f, 0f));
            Assert.That(rig.MonsterRules.Detects(rig.PlayerModel.Snapshot), Is.True, "距离与夹角上都够得着");

            rig.Step.Sight.SetOccluders(BlockBetween);
            Tick(rig, 0f);

            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.Hidden), Is.True, "视线被挡 = 未被察觉");
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.Cover), Is.True, "处于掩体遮挡下");
            // 边界：本波遮挡只进事实层与追逐的视线输入，**不改** MonsterRules 的红区 / 橙区感知
            //（掩体做不做、要不要连战斗一起挡是 `00_功能总览.md` §8.1 #6 的未定项）。
            Assert.That(rig.MonsterRules.Detects(rig.PlayerModel.Snapshot), Is.True, "感知本身没被本波改动");
        }

        [Test]
        public void Step_WithoutOccluder_PlayerIsSeenAndNotCovered()
        {
            Rig rig = NewRig(Vector2.zero, new Vector2(3f, 0f), new Vector2(0f, 0f));
            rig.Step.Sight.SetOccluders(null);

            Tick(rig, 0f);

            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.Hidden), Is.False, "没遮挡就是被看见");
            Assert.That(rig.Step.Facts.IsTrue(StealthFactKeys.Cover), Is.False);
        }

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
