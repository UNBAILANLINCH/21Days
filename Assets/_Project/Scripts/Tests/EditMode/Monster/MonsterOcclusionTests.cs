// 职责：钉住 2026-10-08 的判据变更——**掩体贯通到怪物察觉**。
//   变更前：`MonsterRules.Sense` 是纯视锥，遮挡只写 `stealth.*` 事实键，于是「事实说没看见、怪照样警戒 / 照样追」。
//   变更后：视锥那两档（红区 `MonsterConfig.HostileRadius`、橙区 `ResolveAlertRadius`）要过一道通视判定；
//   **贴身那一档（`ResolveNearSenseRadius`）不受遮挡**——那是近身感知不是视觉，
//   免它的办法是**潜行**（`06_怪物状态与交互设计文档.md:71-73`「玩家需要进入潜行状态（踩静步）才不会被怪物察觉」）。
// 为什么新建（不塞进 `MonsterRulesTests`）：那一条文件覆盖的是巡逻 / 警戒 / 伤害的既有规则；
//   本文件覆盖的是「遮挡这一个输入怎么改变那些规则」，每条正向用例都配一条同位置的负对照
//   （没有遮挡时照旧被发现）——没有负对照就分不清「是遮挡起的作用」还是「规则本来就不触发」。
//
// ⚠️ **夹具的主意（别再退回两点巡逻）**：怪物用**单点巡逻** `Reset(new[] { Vector2.zero })`，
//   `Reset` 在 `waypoints.Length == 1` 那一支把朝向设成 `Vector2.right`，`Step` 里 `MoveTowards(自己的位置)`
//   立即返回——所以怪物**原地不动、朝向恒 +X**，整条用例的几何是静止的。
//   用两点巡逻（原点 → 正东 100 米）写这一组曾踩两个坑：① 巡逻每 tick 漂移、掩体与连线的关系要手算；
//   ② 一旦进警戒它会朝玩家逼近（2.2 m/s），几格之后距离掉进红区，测「警戒」的用例变成在测「红区」。
//   需要它追人的用例（贴身穿透掩体）照样成立：那一档只在怪物动起来之后才转身。
// 出处：`docs/design/spotlight/06_怪物状态与交互设计文档.md:23`（警戒橙区）、`:43`（敌对红区）、`:71-73`（不分朝向的近距察觉区、要潜行才免）。
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Monster;
using Game.Player;
using Game.Stealth;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Monster
{
    /// <summary>
    /// 掩体 → 怪物察觉（视锥两档吃遮挡、贴身一档不吃）。
    /// <para>
    /// 几何全部以「怪物停在原点、朝 +X」为基准（见文件头），所以：
    /// 玩家在 <c>(3,0)</c> = 正面锥内、橙区（2 &lt; 3 ≤ 6）；在 <c>(1.5,0)</c> = 正面锥内、红区（≤ 2）；
    /// 在 <c>(-1,0)</c> = **背后**，只可能命中贴身察觉（1.5 之内）。
    /// </para>
    /// </summary>
    public sealed class MonsterOcclusionTests
    {
        /// <summary>横在 (0,0) 与 (3,0) 之间：x∈[1.4, 2.6]、y∈[-2, 2]（`MakeRectangle` 收的是全长）。</summary>
        private static readonly StealthOccluder[] BlockSightToCone =
            { StealthOccluder.MakeRectangle(new Vector2(2f, 0f), new Vector2(1.2f, 4f), 1) };

        /// <summary>横在 (0,0) 与 (1.8,0) 之间：x∈[0.4, 1.4]（玩家在 1.8，不在掩体上）。</summary>
        private static readonly StealthOccluder[] BlockSightToRedZone =
            { StealthOccluder.MakeRectangle(new Vector2(0.9f, 0f), new Vector2(1f, 4f), 2) };

        /// <summary>
        /// 红区站位：**1.8**，不是 1.5。
        /// <para>
        /// ⚠️ 取 1.5 会踩坑（2026-10-08 主窗口复跑抓到过这条红的）：`MonsterConfig.nearSenseRadius` 默认正好是
        /// **1.5**，而判定写的是 <c>distance &lt;= ResolveNearSenseRadius(kind)</c> —— **1.5 ≤ 1.5 成立**，
        /// 于是贴身那一档生效；它按设计**不吃遮挡**（`06:71-73`「要潜行才免」），后面的链条就成了
        /// 「怪进警戒 → 朝玩家逼近（1.1×）→ 贴上后距离趋 0，`HasSightNonAlloc` 对重合两点判通视 →
        /// 红区成立 → Hostile」。这条用例要测的是「**红区**被遮挡」，站位必须落在
        /// 「贴身半径之外（&gt; 1.5）、红区之内（≤ 2）」这个窄带里。
        /// </para>
        /// </summary>
        private static readonly Vector2 RedZoneSpot = new Vector2(1.8f, 0f);

        /// <summary>横在 (0,0) 与 (-1,0) 之间：x∈[-0.9, -0.1]（玩家在 -1，不在掩体上）。</summary>
        private static readonly StealthOccluder[] BlockSightBehind =
            { StealthOccluder.MakeRectangle(new Vector2(-0.5f, 0f), new Vector2(0.8f, 4f), 3) };

        private MonsterConfig config;

        [SetUp]
        public void SetUp() => config = ScriptableObject.CreateInstance<MonsterConfig>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        // ──────────────────────── 橙区（警戒）：被挡住就不涨警戒 ────────────────────────

        /// <summary>
        /// 正例：3 米（橙区 6 之内、红区 2 之外）隔着掩体 → 没察觉、警戒恒为 0、一直停在巡逻。
        /// 8 格 × 0.1 秒 = 0.8 秒，有遮挡时警戒必须还是 0；负对照里同样几格就已经涨起来了。
        /// </summary>
        [Test]
        public void Cone_WhileOccluderBlocksSight_NoAlertAndNoHostile()
        {
            Rig rig = NewRig(new Vector2(3f, 0f), BlockSightToCone);

            for (int i = 0; i < 8; i++)
            {
                Tick(rig, 0.1f);
            }

            Assert.That(rig.Rules.Detects(rig.Target), Is.False, "视线被切断 = 没看见");
            Assert.That(rig.Rules.Model.Alert, Is.Zero, "警戒不涨");
            Assert.That(rig.Rules.Model.Mode, Is.EqualTo(MonsterMode.PatrolWalk), "不进警戒、更不转敌对");
        }

        /// <summary>
        /// 负对照：同一位置、**没有掩体**时照常被发现（警戒涨起来）。
        /// <para>
        /// **只推 3 格**（0.3 秒）：它一旦进警戒就会朝玩家逼近（2.2 m/s × 0.1 = 0.22/格），
        /// 第 5 格起距离 ≤ 2 会升级成敌对——那时这条负对照测的就变成红区了（红区那条另有专门用例）。
        /// </para>
        /// </summary>
        [Test]
        public void Cone_WithoutOccluder_SamePosition_RaisesAlert()
        {
            Rig rig = NewRig(new Vector2(3f, 0f), null);

            for (int i = 0; i < 3; i++)
            {
                Tick(rig, 0.1f);
            }

            Assert.That(rig.Rules.Detects(rig.Target), Is.True);
            Assert.That(rig.Rules.Model.Alert, Is.GreaterThan(0f), "没有遮挡就该涨警戒（这条证明上一条是遮挡起的作用）");
            Assert.That(rig.Rules.Model.Mode, Is.EqualTo(MonsterMode.Alert));
        }

        // ──────────────────────── 红区（敌对）：被挡住也不进敌对 ────────────────────────

        /// <summary>
        /// 红区被遮挡：**1.8 米**（贴身半径 1.5 之外、红区 2 之内、正面视野锥里）隔着掩体 → 不立刻敌对。
        /// `06_怪物状态与交互设计文档.md:43`「当玩家进入怪物的敌对区域……会进入敌对状态」的前提是**先看见**。
        /// </summary>
        [Test]
        public void RedZone_WhileOccluderBlocksSight_DoesNotTurnHostile()
        {
            Rig rig = NewRig(RedZoneSpot, BlockSightToRedZone);
            Assert.That(rig.Rules.PerceivedBySenses(rig.Target), Is.EqualTo(2),
                "前置：红区那一档确实够得着（不含遮挡），否则这条测的不是红区");

            for (int i = 0; i < 8; i++)
            {
                Tick(rig, 0.1f);
            }

            Assert.That(rig.Rules.Model.Mode, Is.EqualTo(MonsterMode.PatrolWalk), "红区也得先看得见才算进去");
            Assert.That(rig.Rules.Model.Alert, Is.Zero);
        }

        /// <summary>负对照：同一位置没有掩体时红区立刻敌对（`EnterHostile`，不攒警戒）。</summary>
        [Test]
        public void RedZone_WithoutOccluder_SamePosition_TurnsHostileImmediately()
        {
            Rig rig = NewRig(RedZoneSpot, null);

            Tick(rig, 0.1f);

            Assert.That(rig.Rules.Model.Mode, Is.EqualTo(MonsterMode.Hostile), "红区先判定、不需要攒警戒");
        }

        // ──────────────────────── 贴身察觉：不受遮挡（语义不许被改掉） ────────────────────────

        /// <summary>
        /// 贴身那一档**不看遮挡**：不潜行、站在背后 1 米（掩体横在两人连线上、又不在正面锥里）→ 照样被察觉、转身。
        /// 这是 `06_怪物状态与交互设计文档.md:71-73` 的「不分朝向的近距察觉区」。
        /// </summary>
        [Test]
        public void NearSense_ThroughOccluder_WithoutSneak_IsSensed()
        {
            Rig rig = NewRig(new Vector2(-1f, 0f), BlockSightBehind);
            Assert.That(rig.Rules.PerceivedBySenses(rig.Target), Is.EqualTo(1),
                "前置：只有贴身那一档够得着（背后 1 米、正面锥外）");

            Tick(rig, 0.1f);

            Assert.That(rig.Rules.Model.Mode, Is.EqualTo(MonsterMode.Alert), "贴身察觉穿透掩体");
            Assert.That(rig.Rules.Model.Facing, Is.EqualTo(Vector2.left), "察觉到就转身面向玩家");
        }

        /// <summary>
        /// 负对照：同一位置、**没有掩体**时结果一模一样（同样被察觉、同样转身）。
        /// 这一对合起来才说明「遮挡对贴身那一档没有任何影响」——只留正向那条证明不了。
        /// </summary>
        [Test]
        public void NearSense_WithoutOccluder_WithoutSneak_IsSensedToo()
        {
            Rig rig = NewRig(new Vector2(-1f, 0f), null);

            Tick(rig, 0.1f);

            Assert.That(rig.Rules.Model.Mode, Is.EqualTo(MonsterMode.Alert));
            Assert.That(rig.Rules.Model.Facing, Is.EqualTo(Vector2.left));
        }

        /// <summary>
        /// 负对照：**潜行**时贴身在背后 1 米也不被察觉——这正是原文给的免法
        /// （`06_怪物状态与交互设计文档.md:73`「玩家需要进入潜行状态（踩静步）才不会被怪物察觉」）。
        /// 掩体在这里无关紧要，所以同时钉住「潜行免掉的是近身感知，不是被遮挡」。
        /// </summary>
        [Test]
        public void NearSense_WithSneak_IsNotSensed()
        {
            Rig rig = NewRig(new Vector2(-1f, 0f), BlockSightBehind);
            rig.SetSneaking(true);

            Tick(rig, 0.1f);

            Assert.That(rig.Rules.Detects(rig.Target), Is.False, "潜行免掉贴身那一档");
            Assert.That(rig.Rules.Model.Mode, Is.EqualTo(MonsterMode.PatrolWalk));
        }

        /// <summary>负对照：潜行 + **出了贴身半径**（1.8 &gt; 1.5）→ 同样什么都不发生。</summary>
        [Test]
        public void NearSense_SneakingBeyondRadius_IsNotSensed()
        {
            Rig rig = NewRig(new Vector2(-1.8f, 0f), BlockSightBehind);
            rig.SetSneaking(true);

            Tick(rig, 0.1f);

            Assert.That(rig.Rules.Detects(rig.Target), Is.False, "潜行免掉贴身那一档，锥外本来也不看");
            Assert.That(rig.Rules.Model.Mode, Is.EqualTo(MonsterMode.PatrolWalk));
        }

        // ──────────────────────── 两把尺子必须分开（stealth.cover 那条回归的根） ────────────────────────

        /// <summary>
        /// `PerceivedBySenses`（不含遮挡）与 `Detects`（含遮挡）在**同一现场**给出不同答案——
        /// 这正是 `stealth.cover` 需要的那个差。
        /// <para>
        /// 回归的形状（2026-10-08 主窗口复核抓到）：`EncounterStep.SettleStealth` 的
        /// <c>coverBlocks = 够得着 &amp;&amp; !看得见</c> 是**两个布尔相减**。若「够得着」那一半也用了含遮挡的
        /// `Detects`，被挡住时它已经是 false，相减恒为 0 —— `stealth.cover` 在正式流程里永远点不亮，
        /// 「躲进掩体」那条摆脱条件（`04_追逐.md:104` R23）随之静默失效，而 EditMode 里只在不注入遮挡的
        /// 夹具上还亮着。这条用例把两把尺子的差异单独钉住，防止再被合并成一个。
        /// </para>
        /// </summary>
        [Test]
        public void PerceivedBySenses_IgnoresOccluders_WhileDetectsRespectsThem()
        {
            Rig rig = NewRig(new Vector2(3f, 0f), BlockSightToCone);

            Assert.That(rig.Rules.PerceivedBySenses(rig.Target), Is.EqualTo(1),
                "不含遮挡的那把尺子：距离与夹角都够得着（橙区）");
            Assert.That(rig.Rules.Detects(rig.Target), Is.False, "含遮挡的那把尺子：被掩体切断 = 没看见");
        }

        // ──────────────────────── 装配：默认不查遮挡 ────────────────────────

        /// <summary>
        /// 负对照：**没注入遮挡**（`UseOcclusion` 从未被调）时行为与改动前逐位一致——遮挡体列表里有东西也不看。
        /// 这条钉的是「独立原型场景与纯 Monster 测试作用域不受本波影响」。
        /// </summary>
        [Test]
        public void WithoutOcclusionInjection_OccluderListIsIgnored()
        {
            Rig rig = NewRig(new Vector2(3f, 0f), BlockSightToCone, injectOcclusion: false);

            Tick(rig, 0.1f);

            Assert.That(rig.Rules.Detects(rig.Target), Is.True, "没接遮挡就按纯视锥判");
        }

        /// <summary>显式传 null 等于断开遮挡（`UseOcclusion(null)`），行为回到纯视锥。</summary>
        [Test]
        public void UseOcclusion_Null_DisconnectsOcclusion()
        {
            Rig rig = NewRig(new Vector2(3f, 0f), BlockSightToCone);
            Assert.That(rig.Rules.Detects(rig.Target), Is.False);

            rig.Rules.UseOcclusion(null);

            Assert.That(rig.Rules.Detects(rig.Target), Is.True);
        }

        // ──────────────────────── 夹具 ────────────────────────

        private sealed class Rig
        {
            public MonsterRules Rules { get; set; }

            /// <summary>玩家位置（<see cref="Tick"/> 现填快照用）。</summary>
            public Vector2 PlayerPosition { get; set; }

            public bool PlayerSneaking { get; set; }

            public PlayerSnapshot Target => new PlayerSnapshot(PlayerPosition, Vector2.right, PlayerSneaking, false, 3);

            public void SetSneaking(bool sneaking) => PlayerSneaking = sneaking;
        }

        /// <summary>
        /// 造一只**停在原点、朝 +X** 的怪（单点巡逻，见文件头），并按需注入遮挡。
        /// <paramref name="injectOcclusion"/> 为假时用一只空的 <see cref="StealthSight"/>（负对照走那一支）。
        /// <para>
        /// 注入的那一份**当场自检**：每个遮挡体都要真的切断「怪 → 玩家」这条线。
        /// 挡不挡得住不靠脑算——前提不成立就当场红，而不是让后面的断言给出误导性的失败。
        /// </para>
        /// </summary>
        private Rig NewRig(Vector2 playerPosition, StealthOccluder[] occluders, bool injectOcclusion = true)
        {
            var rules = new MonsterRules(config, new MonsterModel(), new RandomService(7ul), NullTelemetryScope.Instance);
            rules.Reset(new[] { Vector2.zero });
            var sight = new StealthSight();
            if (injectOcclusion)
            {
                sight.SetOccluders(occluders);
                rules.UseOcclusion(sight);
                for (int i = 0; i < sight.Count; i++)
                {
                    Assert.That(StealthGeometry.SegmentIntersectsOccluder(rules.Model.Position, playerPosition, sight.GetOccluder(i)),
                        Is.True, $"前置：第 {i} 个遮挡体确实切断了怪 → 玩家这条线");
                }
            }

            return new Rig { Rules = rules, PlayerPosition = playerPosition };
        }

        private static void Tick(Rig rig, float deltaTime)
        {
            PlayerSnapshot target = rig.Target;
            var intent = new MonsterIntent(target, deltaTime);
            rig.Rules.Step(in intent);
        }
    }
}
