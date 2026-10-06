// 职责：把「潜行 / 暗杀 / 击倒 / 追逐」四件事算出来的结果，汇总成一组可写进剧情条件的事实键值——
// 也就是 `ai-docs/docs/story-facts.md` §4.2 / §4.3 里本模块拥有的那些键的**唯一出口**。
//
// 为什么新建：
// - 复用：Monster 与 Player 各自的规则类只算自己的状态，谁都不负责「对剧情暴露什么」；
//   工程现有的事实写入方散在 Narrative 侧，本模块需要自己的一个收口点，避免键名在多个地方手打。
// - 扩展：键的写入必须遵守「一个键一个写入方」（字典 §6.1），塞进 MonsterRules 会与
//   Monster 自己的埋点/状态耦合，而本模块的事实是四件事的合集。
// - 新建：以上两条都不成立，故新建。
//
// 瞬时态纪律（字典 §4.2 / §6.2）：`stealth.hidden`、`stealth.behind`、`stealth.knockdown`
// 是**瞬时态，不入档**——本类只把它们算出来交给内存快照，绝不写进存档 / 回放状态。
//
// 出处（真源）：
// - `ai-docs/docs/story-facts.md` §3.2（键名规范）、§4.2（S3 键）、§4.3（S4 键）、§6.2（瞬时态不入档）
// - `docs/design/features-spotlight/03_潜行与暗杀.md`（潜行与暗杀）
// - `docs/design/features-spotlight/04_追逐.md`（追逐）
// - `docs/design/features-spotlight/00_功能总览.md` §5 C4 / C5 / C6
using UnityEngine;

namespace Game.Stealth
{
    /// <summary>聚光灯设计文档的出处锚点。带上它，代码与策划文档之间不用靠记忆对编号。</summary>
    public static class StealthDocRefs
    {
        /// <summary>03·潜行与暗杀（文档内路径）。</summary>
        public const string DocStealth = "docs/design/features-spotlight/03_潜行与暗杀.md";

        /// <summary>04·追逐（文档内路径）。</summary>
        public const string DocChase = "docs/design/features-spotlight/04_追逐.md";

        /// <summary>00·功能总览（文档内路径）。</summary>
        public const string DocOverview = "docs/design/features-spotlight/00_功能总览.md";
    }

    /// <summary>一条判定对剧情的贡献：键名 + 值 + 是不是瞬时态。</summary>
    public readonly struct StealthFact
    {
        public StealthFact(string key, bool value, bool transient)
        {
            Key = key;
            Value = value;
            Transient = transient;
        }

        /// <summary>字典里的点分键名。</summary>
        public string Key { get; }

        /// <summary>该键当前的值。</summary>
        public bool Value { get; }

        /// <summary>是不是瞬时态（true = 不许入档，字典 §6.2）。</summary>
        public bool Transient { get; }
    }

    /// <summary>
    /// 潜行判定门的输入。都是接线侧已经算好的标量 / 布尔，门自己不查场景、不查物理。
    /// </summary>
    public readonly struct StealthGateInput
    {
        public StealthGateInput(
            bool anyEnemyPerceives,
            bool behindTarget,
            bool assassinationAllowed,
            KnockdownPhase knockdownPhase,
            bool coverBlocks,
            bool alreadyAssassinated = false)
        {
            AnyEnemyPerceives = anyEnemyPerceives;
            BehindTarget = behindTarget;
            AssassinationAllowed = assassinationAllowed;
            KnockdownPhase = knockdownPhase;
            CoverBlocks = coverBlocks;
            AlreadyAssassinated = alreadyAssassinated;
        }

        /// <summary>当前有没有任何敌人感知到玩家（接线侧按各自的 `MonsterRules.Detects` 汇总）。</summary>
        public bool AnyEnemyPerceives { get; }

        /// <summary>玩家是否正贴在某个目标的背后（<c>AssassinationRules.IsBehindAndInRange</c> 的结果）。</summary>
        public bool BehindTarget { get; }

        /// <summary>这一刀能不能下（<c>AssassinationRules.Evaluate</c> 的结果）。</summary>
        public bool AssassinationAllowed { get; }

        /// <summary>击倒状态机的当前阶段。</summary>
        public KnockdownPhase KnockdownPhase { get; }

        /// <summary>玩家与「正盯着他的敌人」之间是否被掩体挡住（<c>StealthSight</c> 的结果）。</summary>
        public bool CoverBlocks { get; }

        /// <summary>
        /// **已经用暗杀解决过目标**（即 `stealth.assassinated` 的当前值），由持有该持久状态的调用方传入。
        /// 刻意与 <see cref="AssassinationAllowed"/> 分开：前者是「发生过的事实」，后者是「此刻能不能做」。
        /// **不要用后者推前者**——那会把「站在守卫背后」写成「已经杀过他」。
        /// </summary>
        public bool AlreadyAssassinated { get; }
    }

    /// <summary>潜行判定门的结果：三个瞬时键的值 + 两条与暗杀有关的事实。</summary>
    public readonly struct StealthGateVerdict
    {
        public StealthGateVerdict(
            bool hidden,
            bool behind,
            bool knockdown,
            bool cover,
            bool alreadyAssassinated,
            KnockdownTier knockdownTier)
        {
            Hidden = hidden;
            Behind = behind;
            Knockdown = knockdown;
            Cover = cover;
            AlreadyAssassinated = alreadyAssassinated;
            KnockdownTier = knockdownTier;
        }

        /// <summary>`stealth.hidden`：未被任何敌人察觉。**瞬时态**。</summary>
        public bool Hidden { get; }

        /// <summary>`stealth.behind`：目标背对玩家且在暗杀距离内。**瞬时态**。</summary>
        public bool Behind { get; }

        /// <summary>`stealth.knockdown`：玩家被击倒。**瞬时态**。</summary>
        public bool Knockdown { get; }

        /// <summary>`stealth.cover`：处于掩体遮挡下。</summary>
        public bool Cover { get; }

        /// <summary>
        /// `stealth.assassinated` 的当前值：**「已经用暗杀解决过目标」**（持久，命中即写）。
        /// 与 <see cref="AssassinationAllowed"/>（「此刻这一刀能不能下」）是两件事，名字刻意不同——
        /// 拿后者点前者会把「站在守卫背后」写成「已经杀过他」，内容条件随之失真。
        /// </summary>
        public bool AlreadyAssassinated { get; }

        /// <summary>击倒次数档位，用来决定写哪个 `stealth.knockdownCount.*` 键。</summary>
        public KnockdownTier KnockdownTier { get; }

        /// <summary>`stealth.knockdownCount` 档位键名；None 为空串。</summary>
        public string KnockdownTierKey => StealthFacts.TierKey(KnockdownTier);
    }

    /// <summary>
    /// 潜行判定门：把四件事的结果合成一组事实键。
    /// <para>
    /// 不做感知计算——感知仍由 Monster 的 `<c>MonsterRules.Detects</c>` 说了算，
    /// 本门只把「有敌人察觉」「被掩体挡住」这类结论翻译成对剧情暴露的键。
    /// 这样 Monster 侧一行都不用改就能接上（接线建议见交付报告）。
    /// </para>
    /// </summary>
    public static class StealthDecisionGate
    {
        /// <summary>算这一帧的判定。</summary>
        public static StealthGateVerdict Evaluate(in StealthGateInput input, in SightSettings sight)
        {
            bool knockedDown = input.KnockdownPhase == KnockdownPhase.Downed
                || input.KnockdownPhase == KnockdownPhase.Crawling;

            // 「未被察觉」= 没有敌人感知到，或者被掩体挡住了视线（掩体是否等于隐身由配置决定）。
            bool hidden = !input.AnyEnemyPerceives || (input.CoverBlocks && sight.CoverImpliesHidden);

            bool cover = input.CoverBlocks && sight.WritesCoverFact;

            return new StealthGateVerdict(
                hidden,
                input.BehindTarget,
                knockedDown,
                cover,
                // 「已暗杀过」是**发生过的事实**，由调用方传入；不能拿 AssassinationAllowed（能不能做）去推。
                input.AlreadyAssassinated,
                KnockdownTier.None);
        }

        /// <summary>算这一帧的判定，并带上击倒档位。</summary>
        public static StealthGateVerdict Evaluate(in StealthGateInput input, in SightSettings sight, KnockdownTier tier)
        {
            StealthGateVerdict baseVerdict = Evaluate(in input, in sight);
            return new StealthGateVerdict(
                baseVerdict.Hidden,
                baseVerdict.Behind,
                baseVerdict.Knockdown,
                baseVerdict.Cover,
                baseVerdict.AlreadyAssassinated,
                tier);
        }

        /// <summary>
        /// 判定「某个敌人能不能看见玩家」：先在距离 / 角度上被感知到（调用方给的
        /// <paramref name="perceivesByCone"/>），再看两点之间通不通视。
        /// 这条判据对应 `03_潜行与暗杀.md:129-136`（R24–R28：借掩体潜行）。
        /// </summary>
        public static bool PerceivesThroughCover(in StealthSight sight, Vector2 enemyEye, Vector2 playerPosition, bool perceivesByCone)
        {
            if (!perceivesByCone)
            {
                return false;
            }

            if (sight == null)
            {
                // 没接遮挡时行为与现状一致：只看距离与角度。
                return true;
            }

            return sight.HasSight(enemyEye, playerPosition);
        }

        /// <summary>
        /// 把判定写成一组事实（键 + 值 + 是否瞬时态）。顺序固定，便于快照比对。
        /// 档位为 <see cref="KnockdownTier.None"/> 时那个槽位的键是空串——调用方按「键非空才写」处理，
        /// 不要拿空键去写表（字典 §3.2 的键名规范不接受空键）。
        /// </summary>
        public static StealthFact[] ToFacts(in StealthGateVerdict verdict)
        {
            var facts = new StealthFact[6];
            facts[0] = new StealthFact(StealthFactKeys.Hidden, verdict.Hidden, true);
            facts[1] = new StealthFact(StealthFactKeys.Behind, verdict.Behind, true);
            facts[2] = new StealthFact(StealthFactKeys.Knockdown, verdict.Knockdown, true);
            facts[3] = new StealthFact(StealthFactKeys.Cover, verdict.Cover, false);
            facts[4] = new StealthFact(StealthFactKeys.Assassinated, verdict.AlreadyAssassinated, false);
            facts[5] = new StealthFact(verdict.KnockdownTierKey, verdict.KnockdownTier != KnockdownTier.None, false);
            return facts;
        }
    }
}
