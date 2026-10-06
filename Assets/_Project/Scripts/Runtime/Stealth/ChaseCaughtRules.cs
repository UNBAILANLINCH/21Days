// 职责：追逐「被抓」的后果——把 `04_追逐.md` 里对不上的三处原文做成可注入策略，不硬编一种。
//
// 为什么新建：
// - 复用：工程里现有的失败流程只有 Mirror 的「镜裂三次 → 镜碎页 → 重开本场」（旧版照镜 demo，
//   随旧版冻结），与 S3 / S4 要的口径不是一回事；`EncounterStep.Result` 只有
//   `None / Victory / Defeat / Aborted`（`Assets/_Project/Scripts/Runtime/Monster/EncounterStep.cs:12`），
//   表达不了「击倒」「暴露」这类结果（`docs/roadmap.md:223` 已记这条）。
// - 扩展：`EncounterStep` 本次任务明令不许改，且「被抓之后怎么办」本身没有拍板
//   （`04:190` Q3、`00:382` §8.1 #4）。
// - 新建：以上两条都不成立，故新建策略类型，接线侧将来把它的结果翻译成 `EncounterStep.Result` 或别的流程。
//
// 出处（真源）：
// - `docs/design/features-spotlight/04_追逐.md:108-112`（3.4 被抓的后果：R26 三处原文、
//   R27「这三处哪一条管追逐失败，原文没写」）
// - `04:148`（§5：本体被打会击倒（03 R3），这是追逐失败的候选后果）
// - `04:190`（Q3 追逐失败的后果：击倒、变回原主后死亡、扣血死亡，还是别的）
// - `04:188`（Q1 原文矛盾：「被发现」一处直接死亡、一处进入追逐）
// - `03_潜行与暗杀.md:146`（R33：触发规则会被发现，「从而变回原主，死亡」）
// - `03:214`（约束 2：被打不是直接死，是留给玩家的逃生窗口）
// - `00_功能总览.md:286`（§5 C1 被发现之后：死亡还是追逐）、`00:382`（§8.1 #3）
namespace Game.Stealth
{
    /// <summary>
    /// 被追上的后果口径。**这是策略枚举，不是数值**：四项分别对应原文里能读出的一种说法。
    /// </summary>
    public enum ChaseCaughtPolicy : byte
    {
        /// <summary>什么都没发生（占位 / 关掉追逐惩罚时用）。</summary>
        None = 0,

        /// <summary>
        /// sp00 口径 A（`04:110` R26）：怪物攻击把主角击倒，之后只能缓慢移动、不能攻击。
        /// 交给 <c>KnockdownRules</c> 处理，不写 `chase.caught`（没死也没结束）。
        /// </summary>
        Knockdown = 1,

        /// <summary>
        /// sp00 口径 B（`03:146` R33）：触发规则被发现，「变回原主，死亡」。
        /// 写 `chase.caught`，最终态。
        /// </summary>
        Death = 2,

        /// <summary>
        /// mai 口径（`04:45` 验收 5）：生命归零即死亡、停止行动。
        /// 写 `chase.caught`，最终态。
        /// </summary>
        HealthDeath = 3,

        /// <summary>
        /// 脚本化「固定追逐」的结局（`04:112` R28）：抓到就进下一段预设演出 / 剧情节点，
        /// 由接线侧的注入回调决定具体做什么。写 `chase.caught`，最终态。
        /// </summary>
        ScriptedSetpiece = 4,

        /// <summary>
        /// 折中口径：**先按击倒处理，若生命已经归零才判死**。
        /// 与 `KnockdownRules` 的 `KnockdownThenDeath` 同一个思路——策划拍板前用这个不把任何一侧做死。
        /// </summary>
        KnockdownThenDeath = 5,
    }

    /// <summary>被抓后果的可调项。</summary>
    public readonly struct ChaseOutcomeSettings
    {
        /// <summary>[待拍板] 后果口径。占位 <see cref="ChaseCaughtPolicy.KnockdownThenDeath"/>，等 §8.1 #3 / #4。</summary>
        public ChaseCaughtPolicy Policy { get; }

        /// <summary>被抓时玩家当前生命，用来判「先击倒还是直接死」。</summary>
        public int PlayerHealth { get; }

        /// <summary>
        /// [待拍板] 同一个追逐里被抓几次算最终失败。占位 2：
        /// 第一次抓只击倒（留逃生窗口，`03:214`），第二次才判死。
        /// 等 `04:190` Q3 与 `03:86` R4 一起拍。
        /// </summary>
        public int FailAfterCaughtCount { get; }

        public ChaseOutcomeSettings(ChaseCaughtPolicy policy, int playerHealth, int failAfterCaughtCount)
        {
            if (playerHealth < 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(playerHealth), "生命不可为负");
            }

            if (failAfterCaughtCount <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(failAfterCaughtCount), "失败所需被抓次数必须为正数");
            }

            Policy = policy;
            PlayerHealth = playerHealth;
            FailAfterCaughtCount = failAfterCaughtCount;
        }

        /// <summary>占位默认：折中口径、生命按满血 3、被抓 2 次算失败。</summary>
        public static ChaseOutcomeSettings PlaceholderDefault =>
            new ChaseOutcomeSettings(ChaseCaughtPolicy.KnockdownThenDeath, 3, 2);
    }

    /// <summary>被抓之后的裁决结果。</summary>
    public readonly struct ChaseOutcome
    {
        public ChaseOutcome(ChaseCaughtPolicy policy, bool isFailure, bool entersKnockdown, bool writesCaughtFact, string describe)
        {
            Policy = policy;
            IsFailure = isFailure;
            EntersKnockdown = entersKnockdown;
            WritesCaughtFact = writesCaughtFact;
            Describe = describe;
        }

        /// <summary>实际生效的口径（折中口径会被解析成具体一种）。</summary>
        public ChaseCaughtPolicy Policy { get; }

        /// <summary>这次是不是最终失败（要接死亡 / 重开流程）。</summary>
        public bool IsFailure { get; }

        /// <summary>这次要不要进击倒状态机。</summary>
        public bool EntersKnockdown { get; }

        /// <summary>这次要不要写 `chase.caught`。</summary>
        public bool WritesCaughtFact { get; }

        /// <summary>一句中文说明，给日志与策划对口径用。</summary>
        public string Describe { get; }

        /// <summary>`chase.caught` 的键名（该写时非空）。</summary>
        public string FactKey => WritesCaughtFact ? StealthFactKeys.ChaseCaught : string.Empty;
    }

    /// <summary>
    /// 把「被抓」翻译成后果。纯函数：不写键、不改状态，只回答「该发生什么」，
    /// 写键与走流程由接线侧做（键的 owner 见 `ai-docs/docs/story-facts.md` §4.3）。
    /// </summary>
    public static class ChaseCaughtRules
    {
        /// <summary>裁决一次被抓。</summary>
        public static ChaseOutcome Resolve(in ChaseOutcomeSettings settings, int caughtCountSoFar)
        {
            if (caughtCountSoFar < 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(caughtCountSoFar), "被抓次数不可为负");
            }

            switch (settings.Policy)
            {
                case ChaseCaughtPolicy.None:
                    return new ChaseOutcome(ChaseCaughtPolicy.None, false, false, false, "被抓无后果（策略 None）");

                case ChaseCaughtPolicy.Knockdown:
                    return new ChaseOutcome(ChaseCaughtPolicy.Knockdown, false, true, false, "被抓 → 击倒（缓慢移动、不能攻击）");

                case ChaseCaughtPolicy.Death:
                    return new ChaseOutcome(ChaseCaughtPolicy.Death, true, false, true, "被抓 → 变回原主后死亡");

                case ChaseCaughtPolicy.HealthDeath:
                    return new ChaseOutcome(ChaseCaughtPolicy.HealthDeath, true, false, true, "被抓 → 扣血致死");

                case ChaseCaughtPolicy.ScriptedSetpiece:
                    return new ChaseOutcome(ChaseCaughtPolicy.ScriptedSetpiece, true, false, true, "被抓 → 进入脚本化固定追逐的预设结局");

                case ChaseCaughtPolicy.KnockdownThenDeath:
                    if (settings.PlayerHealth <= 0 || caughtCountSoFar + 1 >= settings.FailAfterCaughtCount)
                    {
                        return new ChaseOutcome(ChaseCaughtPolicy.HealthDeath, true, false, true,
                            $"被抓第 {caughtCountSoFar + 1} 次（或已无血）→ 判死");
                    }

                    return new ChaseOutcome(ChaseCaughtPolicy.Knockdown, false, true, false,
                        $"被抓第 {caughtCountSoFar + 1} 次 → 击倒，还有 {settings.FailAfterCaughtCount - caughtCountSoFar - 1} 次机会");

                default:
                    throw new System.ArgumentOutOfRangeException(nameof(settings), "未知的被抓后果口径");
            }
        }
    }
}
