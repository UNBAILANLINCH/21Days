// 职责：验证玩家意图、生命与快照在固定输入下可恢复。
using Game.Core.Replay;
using Game.Core.Telemetry;
using Game.Player;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Player
{
    public sealed class PlayerRulesTests
    {
        private PlayerConfig config;
        private PlayerModel model;
        private PlayerRules rules;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<PlayerConfig>();
            model = new PlayerModel();
            rules = new PlayerRules(config, model, NullTelemetryScope.Instance);
            rules.Reset(Vector2.zero);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        [Test]
        public void DamageEvents_OnlyRealDamageAndFirstDeath_ArePublished()
        {
            int hits = 0, deaths = 0;
            rules.OnDamaged += () => hits++;
            rules.OnDied += () => deaths++;
            rules.SetHealth(config.MaxHealth);
            rules.ApplyDamage(new DamageIntent(config.MaxHealth));
            rules.ApplyDamage(new DamageIntent(1));
            rules.SetHealth(0);
            Assert.That(hits, Is.EqualTo(1));
            Assert.That(deaths, Is.EqualTo(1));
            rules.Reset(Vector2.zero);
            rules.SetHealth(0);
            Assert.That(deaths, Is.EqualTo(2), "直接判死也必须通知");
        }

        // 意图简写：参数顺序同 PlayerIntent 构造（移动、潜行、伪装、攻击、奔跑键）。
        private static PlayerIntent Intent(Vector2 move, bool sneak = false, bool disguise = false, bool attack = false, bool run = false)
            => new PlayerIntent(move, sneak, disguise, attack, run);

        [Test]
        public void FixedIntent_ControlsMovementActionsDamageAndDeath()
        {
            rules.Step(new PlayerIntent(Vector2.right, true, true, false, false), 1f);
            Assert.That(model.Position.x, Is.EqualTo(config.SneakSpeed).Within(0.001f));
            Assert.That(model.Snapshot.IsSneaking, Is.True);
            Assert.That(model.Snapshot.IsDisguised, Is.True);

            rules.Step(new PlayerIntent(Vector2.zero, false, true, false, false), 0f);
            Assert.That(model.IsDisguised, Is.True, "长按伪装只切换一次");
            rules.Step(new PlayerIntent(Vector2.zero, false, false, true, false), 0f);
            Assert.That(rules.Step(new PlayerIntent(Vector2.zero, false, false, true, false), 0f), Is.False,
                "长按攻击只触发一次");

            rules.ApplyDamage(new DamageIntent(3));
            Assert.That(model.Snapshot.IsAlive, Is.False);
            Assert.That(rules.Step(new PlayerIntent(Vector2.right, false, false, true, false), 1f), Is.False);
            Assert.That(model.Position.x, Is.EqualTo(config.SneakSpeed).Within(0.001f));
        }

        [Test]
        public void Run_TogglesOnPressEdge_HoldDoesNotRetoggle()
        {
            rules.Step(Intent(Vector2.zero, run: true), 0f);
            Assert.That(model.IsRunning, Is.True, "按下沿切到奔跑");

            for (int i = 0; i < 5; i++)
            {
                rules.Step(Intent(Vector2.zero, run: true), 0f);
            }

            Assert.That(model.IsRunning, Is.True, "长按不反复切");

            rules.Step(Intent(Vector2.zero), 0f);
            Assert.That(model.IsRunning, Is.True, "松开不影响奔跑模式");

            rules.Step(Intent(Vector2.zero, run: true), 0f);
            Assert.That(model.IsRunning, Is.False, "再次按下切回步行");
        }

        [Test]
        public void Speed_UsesWalkRunAndSneakTiers()
        {
            rules.Step(Intent(Vector2.right), 1f);
            Assert.That(model.Position.x, Is.EqualTo(config.MoveSpeed).Within(0.001f), "默认步行");

            rules.Reset(Vector2.zero);
            rules.Step(Intent(Vector2.right, run: true), 1f);
            Assert.That(model.Position.x, Is.EqualTo(config.RunSpeed).Within(0.001f), "切到奔跑后按奔跑速度");
            Assert.That(model.Snapshot.IsRunning, Is.True);

            rules.Reset(Vector2.zero);
            rules.Step(Intent(Vector2.right, sneak: true), 1f);
            Assert.That(model.Position.x, Is.EqualTo(config.SneakSpeed).Within(0.001f), "潜行按住按潜行速度");
            Assert.That(config.RunSpeed, Is.GreaterThan(config.MoveSpeed), "默认奔跑速度应快于步行");
        }

        [Test]
        public void Sneak_OverridesRunWhileHeld_ReleaseKeepsRunning()
        {
            rules.Step(Intent(Vector2.zero, run: true), 0f);
            rules.Step(Intent(Vector2.right, sneak: true), 1f);

            Assert.That(model.Position.x, Is.EqualTo(config.SneakSpeed).Within(0.001f), "潜行按住临时压过奔跑");
            Assert.That(model.IsRunning, Is.True, "潜行不改奔跑模式");
            Assert.That(model.Snapshot.IsRunning, Is.False, "潜行时快照不算在跑");

            rules.Step(Intent(Vector2.right), 1f);
            Assert.That(model.Position.x, Is.EqualTo(config.SneakSpeed + config.RunSpeed).Within(0.001f),
                "松开潜行后恢复奔跑速度");
            Assert.That(model.Snapshot.IsRunning, Is.True);
        }

        [Test]
        public void Reset_ClearsRunningState()
        {
            rules.Step(Intent(Vector2.zero, run: true), 0f);
            rules.Reset(Vector2.zero);

            Assert.That(model.IsRunning, Is.False);
            Assert.That(model.PreviousRun, Is.False);
        }

        [Test]
        public void ReplaySnapshot_RestoresActionEdgesAndCooldown()
        {
            rules.Step(new PlayerIntent(Vector2.up, false, true, true, false), 0.25f);
            var buffer = new StateBuffer();
            model.Serialize(buffer);
            Vector2 position = model.Position;
            float cooldown = model.AttackCooldownLeft;

            rules.Reset(Vector2.right * 10f);
            buffer.SeekToStart();
            model.Deserialize(buffer);

            Assert.That(buffer.Remaining, Is.Zero);
            Assert.That(model.Position, Is.EqualTo(position));
            Assert.That(model.IsDisguised, Is.True);
            Assert.That(model.AttackCooldownLeft, Is.EqualTo(cooldown));
            Assert.That(rules.Step(new PlayerIntent(Vector2.zero, false, true, true, false), 1f), Is.False,
                "恢复后的按键边缘不能重复攻击");
            Assert.That(model.IsDisguised, Is.True);
        }

        [Test]
        public void ReplaySnapshot_RoundTripsRunningModeAndRunEdge()
        {
            rules.Step(Intent(Vector2.zero, run: true), 0f);
            var buffer = new StateBuffer();
            model.Serialize(buffer);

            rules.Reset(Vector2.zero);
            buffer.SeekToStart();
            model.Deserialize(buffer);

            Assert.That(buffer.Remaining, Is.Zero, "新增字段必须被完整读回");
            Assert.That(model.IsRunning, Is.True);
            Assert.That(model.PreviousRun, Is.True);

            rules.Step(Intent(Vector2.zero, run: true), 0f);
            Assert.That(model.IsRunning, Is.True, "恢复后继续长按不能被误判成新的一次切换");
        }

        [Test]
        public void SaveData_RoundTripsRunningMode()
        {
            rules.Step(Intent(Vector2.zero, run: true), 0f);
            PlayerSaveData saved = model.Capture();

            rules.Reset(Vector2.zero);
            model.Restore(saved);

            Assert.That(model.IsRunning, Is.True);
            Assert.That(model.PreviousRun, Is.True);
        }

        // 渲染插值：每 tick 推进前记下上一 tick 位置，视图在两者之间 Lerp。
        [Test]
        public void Step_RecordsPreviousPositionBeforeMoving()
        {
            rules.Step(Intent(Vector2.right), 1f);
            Assert.That(model.PreviousPosition, Is.EqualTo(Vector2.zero));
            Vector2 first = model.Position;
            Assert.That(first.x, Is.GreaterThan(0f));

            rules.Step(Intent(Vector2.right), 1f);
            Assert.That(model.PreviousPosition, Is.EqualTo(first));
        }

        // 停下或死亡的 tick 也要对齐，否则视图会在最后一步的两点之间随余量来回插值（抖动）。
        [Test]
        public void Step_WhenIdleOrDead_AlignsPreviousPosition()
        {
            rules.Step(Intent(Vector2.right), 1f);
            rules.Step(Intent(Vector2.zero), 1f);
            Assert.That(model.PreviousPosition, Is.EqualTo(model.Position));

            rules.Step(Intent(Vector2.right), 1f);
            rules.ApplyDamage(new DamageIntent(config.MaxHealth));
            rules.Step(Intent(Vector2.right), 1f);
            Assert.That(model.PreviousPosition, Is.EqualTo(model.Position), "死亡后 Step 提前返回前也要对齐");
        }

        // 出生 / 读档 / 快照恢复都等同瞬移：PreviousPosition 与 Position 对齐，不跨瞬移插值；快照不携带它。
        [Test]
        public void ResetRestoreAndDeserialize_AlignPreviousPosition()
        {
            rules.Step(Intent(Vector2.right), 1f);
            Assert.That(model.PreviousPosition, Is.Not.EqualTo(model.Position));
            PlayerSaveData saved = model.Capture();
            var buffer = new StateBuffer();
            model.Serialize(buffer);

            rules.Reset(new Vector2(5f, 5f));
            Assert.That(model.PreviousPosition, Is.EqualTo(new Vector2(5f, 5f)));

            model.Restore(saved);
            Assert.That(model.PreviousPosition, Is.EqualTo(model.Position));

            var restored = new PlayerModel();
            buffer.SeekToStart();
            restored.Deserialize(buffer);
            Assert.That(buffer.Remaining, Is.Zero, "PreviousPosition 不进快照，字节布局不变");
            Assert.That(restored.PreviousPosition, Is.EqualTo(restored.Position));
        }

        // ── 设定生命口（PRP/combat-iteration D4 / D5：露馅 / 被抓判死置 0、战后按比例写回）──────────────

        /// <summary>MaxHealth 只转读配置上限，不另存一份。</summary>
        [Test]
        public void MaxHealth_ReadsConfigValue()
        {
            Assert.That(rules.MaxHealth, Is.EqualTo(config.MaxHealth));
        }

        /// <summary>设定生命夹在 [0, MaxHealth]：超上限取上限、负数取 0、区间内原样写入；返回值就是实际写入值。</summary>
        [TestCase(1, 1)]
        [TestCase(0, 0)]
        [TestCase(-5, 0)]
        [TestCase(99, -1)]
        public void SetHealth_ClampsToZeroAndMax(int requested, int expected)
        {
            int expectedHealth = expected < 0 ? config.MaxHealth : expected;

            int written = rules.SetHealth(requested);

            Assert.That(written, Is.EqualTo(expectedHealth));
            Assert.That(model.Health, Is.EqualTo(expectedHealth));
        }

        /// <summary>
        /// 设定生命时清掉击倒状态（战后不该还趴着）：挨打进了倒地期 → SetHealth 后阶段回到 None、能攻击、能原速走；
        /// 累计击倒次数保留（与 Reset 同一口径：次数归零由阶段迁移负责）。
        /// </summary>
        [Test]
        public void SetHealth_WhileKnockedDown_ClearsPhaseButKeepsCount()
        {
            var knockdown = new Game.Stealth.KnockdownRules(Game.Stealth.KnockdownSettings.PlaceholderDefault);
            rules.UseKnockdown(knockdown);
            rules.ApplyDamage(new DamageIntent(1));
            Assert.That(knockdown.IsDowned, Is.True, "前提：挨一下进了倒地期");
            int count = knockdown.KnockdownCount;

            rules.SetHealth(config.MaxHealth);

            Assert.That(knockdown.Phase, Is.EqualTo(Game.Stealth.KnockdownPhase.None));
            Assert.That(knockdown.KnockdownCount, Is.EqualTo(count), "累计次数不清");
            Assert.That(rules.Step(Intent(Vector2.zero, attack: true), 0f), Is.True, "不再被击倒压住攻击");
        }

        /// <summary>负对照：不清击倒时（不调 SetHealth），同一处境下攻击被压住——证明上一条的「能攻击」来自 SetHealth。</summary>
        [Test]
        public void KnockedDown_WithoutSetHealth_StillCannotAttack()
        {
            var knockdown = new Game.Stealth.KnockdownRules(Game.Stealth.KnockdownSettings.PlaceholderDefault);
            rules.UseKnockdown(knockdown);
            rules.ApplyDamage(new DamageIntent(1));

            Assert.That(rules.Step(Intent(Vector2.zero, attack: true), 0f), Is.False);
        }

        /// <summary>置 0 = 死亡（与受击归零同一种终态）：之后 Step 不再行动。</summary>
        [Test]
        public void SetHealth_Zero_PlayerIsDead()
        {
            rules.SetHealth(0);

            Assert.That(model.Snapshot.IsAlive, Is.False);
            Assert.That(rules.Step(Intent(Vector2.right, attack: true), 1f), Is.False);
            Assert.That(model.Position, Is.EqualTo(Vector2.zero));
        }
    }
}
