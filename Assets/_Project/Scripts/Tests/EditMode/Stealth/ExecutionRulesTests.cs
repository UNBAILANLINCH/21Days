// 职责：钉住背后处决的**门槛**——「位置与察觉」∧「物种允许被处决」的合取，以及四类拒绝原因。
// 为什么新建：`StealthRulesTests.AssassinationRulesTests` 覆盖的是条件 ① 那一半（它连妖物表都不认识），
//   「哪些怪能处决」这一半（`defeat_method = 暗杀`）此前没有任何测试，也没有任何代码在读它。
// 负对照：四类拒绝各一条（不在背后 / 超距 / 目标已察觉 / 物种不可处决），外加「两个条件同时不成立时报哪一个」。
using Game.Core.Config;
using Game.Mirror;
using Game.Monster;
using Game.Stealth;
using Game.Tests.EditMode.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Stealth
{
    public sealed class ExecutionRulesTests
    {
        /// <summary>背后 120°、距离 1.2、不要求潜行——与 `AssassinationSettings.PlaceholderDefault` 同值。</summary>
        private AssassinationRules rules;

        [SetUp]
        public void SetUp() => rules = new AssassinationRules(new AssassinationSettings(120f, 1.2f, false));

        // ──────────────────────── 两个条件的合取（PRP §2.1） ────────────────────────

        [Test]
        public void Evaluate_BehindInRangeUnawareAndExecutableSpecies_IsAllowed()
        {
            // 目标朝 +X、站在原点，攻方在 -X 侧距离 1 = 正后方且在暗杀距离内，物种允许被处决。
            ExecutionVerdict verdict = ExecutionRules.Evaluate(Input(new Vector2(-1f, 0f), speciesExecutable: true), rules);

            Assert.That(verdict.Allowed, Is.True);
            Assert.That(verdict.Reject, Is.EqualTo(ExecutionReject.None));
            Assert.That(verdict.ReasonCode, Is.Empty, "通过时没有原因码");
            Assert.That(verdict.FactKey, Is.EqualTo(StealthFactKeys.Assassinated),
                "通过时给出的是**命中后才该写**的键，不是「此刻能不能下刀」");
        }

        /// <summary>负对照①：站在正面（不在背后锥内）。</summary>
        [Test]
        public void Evaluate_NotBehind_IsRejectedAsNotBehind()
        {
            ExecutionVerdict verdict = ExecutionRules.Evaluate(Input(new Vector2(1f, 0f), speciesExecutable: true), rules);

            Assert.That(verdict.Allowed, Is.False);
            Assert.That(verdict.Reject, Is.EqualTo(ExecutionReject.NotBehind));
            Assert.That(verdict.ReasonCode, Is.EqualTo("not_behind"));
            Assert.That(verdict.FactKey, Is.Empty, "被拒时不许给出事实键");
        }

        /// <summary>负对照②：方向对但超出暗杀距离。</summary>
        [Test]
        public void Evaluate_BehindButTooFar_IsRejectedAsOutOfRange()
        {
            ExecutionVerdict verdict = ExecutionRules.Evaluate(Input(new Vector2(-3f, 0f), speciesExecutable: true), rules);

            Assert.That(verdict.Allowed, Is.False);
            Assert.That(verdict.Reject, Is.EqualTo(ExecutionReject.OutOfRange));
            Assert.That(verdict.ReasonCode, Is.EqualTo("out_of_range"));
        }

        /// <summary>负对照③：目标已经察觉（警戒 / 敌对）。</summary>
        [Test]
        public void Evaluate_TargetAware_IsRejectedAsTargetAware()
        {
            ExecutionVerdict verdict = ExecutionRules.Evaluate(
                Input(new Vector2(-1f, 0f), speciesExecutable: true, targetAware: true), rules);

            Assert.That(verdict.Allowed, Is.False);
            Assert.That(verdict.Reject, Is.EqualTo(ExecutionReject.TargetAware));
            Assert.That(verdict.ReasonCode, Is.EqualTo("target_aware"));
        }

        /// <summary>负对照④：**物种不允许被处决**——其余三条全部成立，只差条件 ②。</summary>
        [Test]
        public void Evaluate_SpeciesNotExecutable_IsRejectedAsSpeciesNotExecutable()
        {
            ExecutionVerdict verdict = ExecutionRules.Evaluate(Input(new Vector2(-1f, 0f), speciesExecutable: false), rules);

            Assert.That(verdict.Allowed, Is.False);
            Assert.That(verdict.Reject, Is.EqualTo(ExecutionReject.SpeciesNotExecutable));
            Assert.That(verdict.ReasonCode, Is.EqualTo("species_not_executable"));
            Assert.That(verdict.FactKey, Is.Empty);
        }

        /// <summary>
        /// 判定顺序：**先 ① 后 ②**。两个条件同时不成立时报位置那一条——
        /// 被拒原因要指向「最先不满足」的那一条，否则玩家会拿到一个离他更远的解释。
        /// </summary>
        [Test]
        public void Evaluate_BothConditionsFail_ReportsThePositionReasonFirst()
        {
            ExecutionVerdict verdict = ExecutionRules.Evaluate(Input(new Vector2(1f, 0f), speciesExecutable: false), rules);

            Assert.That(verdict.Reject, Is.EqualTo(ExecutionReject.NotBehind), "位置不成立时先报位置，不报物种");
        }

        [Test]
        public void Evaluate_TargetAlreadyDead_IsRejectedAsTargetNotAlive()
        {
            // 负对照：目标不处于可处决状态。
            ExecutionVerdict verdict = ExecutionRules.Evaluate(
                Input(new Vector2(-1f, 0f), speciesExecutable: true, targetAlive: false), rules);

            Assert.That(verdict.Reject, Is.EqualTo(ExecutionReject.TargetNotAlive));
        }

        [Test]
        public void Evaluate_NullRules_IsRejected()
        {
            // 负对照：判定内核没接时不许静默放过（那会让「没接线」表现成「处决永远可用」）。
            Assert.Throws<System.ArgumentNullException>(
                () => ExecutionRules.Evaluate(Input(new Vector2(-1f, 0f), speciesExecutable: true), null));
        }

        // ──────────────────────── 条件 ②：物种白名单 ────────────────────────

        [Test]
        public void SpeciesExecutable_OnlyAcceptsTheAssassinationMethodLiteral()
        {
            Assert.That(ExecutionRules.SpeciesExecutable(YaoCatalog.AssassinationMethod), Is.True);
            Assert.That(ExecutionRules.SpeciesExecutable("暗杀"), Is.True, "白名单字面量就是「暗杀」二字（yao.xml）");

            // 负对照：白名单里另外四个值、空串、null、以及带空格 / 大小写漂移都不是「允许处决」。
            Assert.That(ExecutionRules.SpeciesExecutable("可击杀（方式没写）"), Is.False);
            Assert.That(ExecutionRules.SpeciesExecutable("特殊条件"), Is.False);
            Assert.That(ExecutionRules.SpeciesExecutable("需收服"), Is.False);
            Assert.That(ExecutionRules.SpeciesExecutable("不可杀"), Is.False);
            Assert.That(ExecutionRules.SpeciesExecutable("暗杀 "), Is.False, "序号比较，不做裁剪");
            Assert.That(ExecutionRules.SpeciesExecutable(string.Empty), Is.False);
            Assert.That(ExecutionRules.SpeciesExecutable(null), Is.False);
        }

        /// <summary>
        /// PRP §3.2（2026-10-07 扩成两半）：**用当前真表数据**把门槛的两侧各钉一条——
        /// ① `killable = true` + `defeat_method = 可击杀（方式没写）` 的怪（巡夜人 / 井边妇人）**不能**被处决，
        ///    否则绕背变成万能解；
        /// ② `killable = false` + `defeat_method = 暗杀` 的怪（市令）**必须**能被处决——这条是「处决在当前内容里
        ///    真的可达」的正面证据：在它入库之前，真表里一只可处决的怪都没有，那一半只能靠手搓数据测。
        /// 走「真表字节 → 配置服务 → 妖物表 / 种类表」这条真链路，不手搓字符串。
        /// </summary>
        [Test]
        public void SpeciesExecutable_CurrentTableRows_SplitByDefeatMethod()
        {
            var config = ScriptableObject.CreateInstance<MonsterConfig>();
            try
            {
                var stub = new StubConfigService(ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes()));
                var kinds = new MonsterKindCatalog(stub, new YaoCatalog(stub), config.HostileRadius);

                Assert.That(kinds.Count, Is.GreaterThanOrEqualTo(3), "当前表里的种类条数（巡夜人 / 井边妇人 / 市令）");

                int executableKinds = 0;
                for (int i = 0; i < kinds.All.Count; i++)
                {
                    MonsterKind kind = kinds.All[i];
                    bool speciesExecutable = ExecutionRules.SpeciesExecutable(kind.DefeatMethod);

                    if (kind.IsKillable)
                    {
                        // ① 能常规打死的怪绝不该也能被一键处决。
                        Assert.That(kind.DefeatMethod, Is.EqualTo("可击杀（方式没写）"),
                            $"表里这一行 {kind.Name} 是 killable = true，defeat_method 应当是同义的「可击杀（方式没写）」");
                        Assert.That(speciesExecutable, Is.False,
                            $"{kind.Name} 能被常规打死，不该也能被一键处决（PRP §2.1「不要把 killable 当门槛」的反面）");
                    }
                    else
                    {
                        // ② 常规杀不掉的怪必须说清非正面途径（与 YaoTableTests 的真表校验同源，这里从种类侧再看一遍）。
                        Assert.That(kind.DefeatMethod, Is.Not.Empty.And.Not.EqualTo("可击杀（方式没写）"),
                            $"表里这一行 {kind.Name} 是 killable = false，defeat_method 要说清怎么杀");

                        if (speciesExecutable)
                        {
                            executableKinds++;
                        }
                    }

                    // 走过完整门槛：位置与察觉都成立，结论只由条件 ② 决定。
                    ExecutionVerdict verdict = ExecutionRules.Evaluate(
                        new ExecutionInput(
                            new AssassinationInput(new Vector2(-1f, 0f), Vector2.right, Vector2.zero, Vector2.right,
                                true, false, true),
                            speciesExecutable),
                        rules);
                    Assert.That(verdict.Allowed, Is.EqualTo(speciesExecutable),
                        $"{kind.Name}（defeat_method = {kind.DefeatMethod}）的门槛结论与物种条件不一致");
                    Assert.That(verdict.Reject,
                        Is.EqualTo(speciesExecutable ? ExecutionReject.None : ExecutionReject.SpeciesNotExecutable),
                        $"{kind.Name} 的拒绝原因");
                }

                Assert.That(executableKinds, Is.EqualTo(1),
                    "真表里恰有一只只能暗杀的怪（市令）——处决在内容上可达；增删这类怪要同步这条断言");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        // ──────────────────────── 原因码与枚举映射 ────────────────────────

        [Test]
        public void ReasonCode_CoversTheFourRequiredRejections()
        {
            // 埋点里「玩家按了没反应」的四种现场必须各有稳定的码（PRP §2.6）。
            Assert.That(ExecutionRules.ReasonCode(ExecutionReject.NotBehind), Is.EqualTo("not_behind"));
            Assert.That(ExecutionRules.ReasonCode(ExecutionReject.OutOfRange), Is.EqualTo("out_of_range"));
            Assert.That(ExecutionRules.ReasonCode(ExecutionReject.TargetAware), Is.EqualTo("target_aware"));
            Assert.That(ExecutionRules.ReasonCode(ExecutionReject.SpeciesNotExecutable),
                Is.EqualTo(ExecutionRules.ReasonSpeciesNotExecutable));
            Assert.That(ExecutionRules.ReasonCode(ExecutionReject.NoTarget), Is.EqualTo("no_target"));
            Assert.That(ExecutionRules.ReasonCode(ExecutionReject.None), Is.Empty);
        }

        [Test]
        public void Describe_GivesChineseForEveryReason()
        {
            foreach (ExecutionReject reject in System.Enum.GetValues(typeof(ExecutionReject)))
            {
                Assert.That(ExecutionRules.Describe(reject), Is.Not.Null.And.Not.Empty, reject.ToString());
            }
        }

        /// <summary>
        /// 条件 ① 的映射必须**覆盖内核枚举的每一个值**（名字一一对应）。
        /// 内核将来加了新的拒绝原因而这里没跟上时，`FromPosition` 会抛而不是悄悄算成「通过」。
        /// </summary>
        [Test]
        public void FromPosition_MapsEveryKernelReject()
        {
            foreach (AssassinationReject reject in System.Enum.GetValues(typeof(AssassinationReject)))
            {
                Assert.That(ExecutionRules.FromPosition(reject).ToString(), Is.EqualTo(reject.ToString()));
            }

            // 负对照：未登记的值当场抛，不许静默通过。
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => ExecutionRules.FromPosition((AssassinationReject)99));
        }

        private static ExecutionInput Input(Vector2 attackerPosition, bool speciesExecutable,
            bool targetAware = false, bool targetAlive = true)
        {
            // 目标在原点朝 +X；攻方朝向 +X、潜行中（本用例的阈值不要求潜行，潜行只为贴近真实调用方）。
            var snapshot = new AssassinationInput(
                attackerPosition, Vector2.right, Vector2.zero, Vector2.right, targetAlive, targetAware, true);
            return new ExecutionInput(in snapshot, speciesExecutable);
        }

        /// <summary>只递一份现成的 <c>cfg.Tables</c>（同 MonsterRulesTests 的写法）。</summary>
        private sealed class StubConfigService : IConfigService
        {
            public StubConfigService(global::cfg.Tables tables) => Tables = tables;

            public global::cfg.Tables Tables { get; }

            public ulong ContentHash => throw new System.NotSupportedException("假配置服务不提供内容指纹");
        }
    }
}
