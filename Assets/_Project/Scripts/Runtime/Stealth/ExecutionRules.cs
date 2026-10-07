// 职责：背后处决的**门槛判定**——「位置与察觉条件成立」∧「这个物种允许被处决」的合取。
//   纯逻辑：不碰场景、不碰输入、不认识 MonsterModel / PlayerModel（与 AssassinationRules 同一层）。
//
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：`AssassinationRules.Evaluate` 只答后半个问题里的一半——位置（背后 + 距离）与察觉，
//      它连妖物表都不认识；拿它的 Allowed 直接当「能不能处决」会漏掉「**对于部分怪物**」这个限定，
//      绕背就变成万能解（`docs/design/spotlight/06_怪物状态与交互设计文档.md:69` 的原文限定）。
//   2. 扩展不行：把物种门槛塞进 `AssassinationRules` 会让那个纯几何内核认识配置表语义；而塞进 Monster 侧
//      （`MonsterRules`）会让「怎么算背后」这种潜行语义反向落到怪物模块，方向是反的
//      （`ai-docs/docs/modules/stealth/stealth-module-guide.md` 依赖方向一节）。
//   3. 新建：以上两条都不成立，故新建一个只做「两个条件合取」的兄弟类型。
//
// 出处（真源）：
//   - `docs/design/spotlight/06_怪物状态与交互设计文档.md:69`「对于部分怪物。玩家可以在怪物背后按F处决」
//     ——「部分」就是条件 ②。
//   - `Tables/Defines/yao.xml` 的 defeat_method 列注释：白名单五值里的「暗杀」原文写着
//     「**只能绕背处决，走不了常规击杀**」，所以对这类怪来说处决不是「多一种打法」而是**唯一一种**。
//   - `PRP/stealth-execution/prp.md` §2.1（合取）、§2.5（能力 ≠ 事实）、§2.6（拒绝原因要进埋点）。
namespace Game.Stealth
{
    /// <summary>
    /// 一次处决被拒的原因。前七个是条件 ①（位置与察觉）的分支，最后一个是条件 ②（物种门槛）；
    /// <see cref="None"/> 表示通过。埋点里的 <c>reason</c> 用 <see cref="ExecutionRules.ReasonCode"/>。
    /// </summary>
    public enum ExecutionReject : byte
    {
        /// <summary>通过了（两个条件都成立）。</summary>
        None = 0,

        /// <summary>附近一个候选目标都没有（交互层用：判定层不会产出这个值）。</summary>
        NoTarget = 1,

        /// <summary>不在目标背后的锥内。</summary>
        NotBehind = 2,

        /// <summary>超出暗杀距离。</summary>
        OutOfRange = 3,

        /// <summary>目标已经察觉（警戒或敌对）。</summary>
        TargetAware = 4,

        /// <summary>目标不处于可处决状态（已经死了）。</summary>
        TargetNotAlive = 5,

        /// <summary>配置要求潜行而攻方没有潜行。</summary>
        NotSneaking = 6,

        /// <summary>攻方与目标重合，朝向没有意义。</summary>
        SamePosition = 7,

        /// <summary>**条件 ②**：这个物种不允许被处决（<c>defeat_method != 暗杀</c>）。</summary>
        SpeciesNotExecutable = 8,
    }

    /// <summary>
    /// 处决门槛要吃的东西：一份目标快照（转手给 <see cref="AssassinationRules.Evaluate"/>）
    /// + 条件 ② 的结论。
    /// <para>
    /// 条件 ② 收成布尔而不是收 <c>yaoId</c>：物种门槛查表是调用方的事（表可能在启动早期还没就绪），
    /// 判定本身保持纯函数——测试不必造表就能把两个条件的四种组合跑遍。
    /// </para>
    /// </summary>
    public readonly struct ExecutionInput
    {
        public ExecutionInput(in AssassinationInput target, bool speciesExecutable)
        {
            Target = target;
            SpeciesExecutable = speciesExecutable;
        }

        /// <summary>条件 ① 的快照（位置、朝向、存活、察觉、潜行）。</summary>
        public AssassinationInput Target { get; }

        /// <summary>
        /// 条件 ② 的结论：<c>defeat_method == 暗杀</c>（用 <see cref="ExecutionRules.SpeciesExecutable"/> 算）。
        /// </summary>
        public bool SpeciesExecutable { get; }
    }

    /// <summary>处决门槛的结果。<see cref="Allowed"/> 为 false 时看 <see cref="Reject"/> 知道差在哪一条。</summary>
    public readonly struct ExecutionVerdict
    {
        public ExecutionVerdict(bool allowed, ExecutionReject reject)
        {
            Allowed = allowed;
            Reject = reject;
        }

        /// <summary>这一刀能不能下（两个条件都成立）。</summary>
        public bool Allowed { get; }

        /// <summary>被拒原因；<see cref="ExecutionReject.None"/> 表示通过。</summary>
        public ExecutionReject Reject { get; }

        /// <summary>
        /// 通过时**该在命中之后写**的事实键（`stealth.assassinated`），被拒时空串。
        /// <para>
        /// ⚠️ 这个键是「**已经**用暗杀解决过目标」（字典 §4.2，命中即写、持久），**不是**「此刻能不能下刀」。
        /// 拿 <see cref="Allowed"/> 直接点亮它，会让「站在守卫背后」等于「已经杀过他」——
        /// `StealthDecisionGate` 的接线波刻意没写它，就是为了避免这一处（PRP §2.5）。
        /// 写入时机由调用方（<see cref="ExecutionResolver"/>）在**执行真的成功之后**把关。
        /// </para>
        /// </summary>
        public string FactKey => Allowed ? StealthFactKeys.Assassinated : string.Empty;

        /// <summary>埋点用的英文原因码（<c>snake_case</c>）；通过时空串。</summary>
        public string ReasonCode => ExecutionRules.ReasonCode(Reject);

        /// <summary>给人看的中文一句（调试面板 / 测试失败信息）。</summary>
        public string Describe() => ExecutionRules.Describe(Reject);
    }

    /// <summary>
    /// 处决门槛的纯函数集合。两个条件分开可调，方便策划逐条对参数、也方便测试造负对照。
    /// </summary>
    public static class ExecutionRules
    {
        /// <summary>埋点里「物种不可处决」的原因码。</summary>
        public const string ReasonSpeciesNotExecutable = "species_not_executable";

        /// <summary>
        /// **条件 ②**：这个物种允不允许被处决。判据是 <c>defeat_method</c> 列等于白名单里的「暗杀」。
        /// <para>
        /// 比的是 <see cref="Game.Mirror.YaoCatalog.AssassinationMethod"/> 而不是再手打一遍「暗杀」：
        /// 白名单的唯一权威在妖物表查询层，两处各写一份就会在改表时静默失效。
        /// </para>
        /// <para>
        /// ⚠️ **不要改用 <c>killable</c> 当门槛**（PRP §2.1）：<c>killable = true</c> 且
        /// <c>defeat_method = 可击杀（方式没写）</c> 的怪**不该**能被一键处决（否则绕背变万能解），
        /// 而 <c>killable = false</c> 也可能是「特殊条件 / 需收服 / 不可杀」，那些同样不该走处决。
        /// 两列答的是两个问题：killable「能不能常规杀」，defeat_method「怎么杀 / 有没有替代途径」
        /// （依据见 `Game.Monster.MonsterKind.cs:101-106` 与 `Tables/Defines/yao.xml` 的列注释）。
        /// </para>
        /// </summary>
        public static bool SpeciesExecutable(string defeatMethod) =>
            string.Equals(defeatMethod, Game.Mirror.YaoCatalog.AssassinationMethod, System.StringComparison.Ordinal);

        /// <summary>
        /// **门槛合取**：① 位置与察觉（<see cref="AssassinationRules.Evaluate"/>）∧ ② 物种允许被处决。
        /// <para>
        /// 判定顺序固定为「先 ① 后 ②」：被拒原因永远指向**最先**不满足的那一条，与
        /// <see cref="AssassinationRules.Evaluate"/> 内部「活着 → 未察觉 → 同位置 → 距离 → 背后 → 潜行」
        /// 的顺序一致，测试与调试都能指名道姓。
        /// </para>
        /// </summary>
        public static ExecutionVerdict Evaluate(in ExecutionInput input, AssassinationRules assassination)
        {
            if (assassination == null)
            {
                throw new System.ArgumentNullException(nameof(assassination));
            }

            // `input` 是 `in` 参数，而 `input.Target` 是只读属性——属性不是变量，不能按引用传进 `Evaluate`，
            // 所以先落到局部再传（AssassinationInput 是 readonly struct，拷贝很便宜）。
            AssassinationInput target = input.Target;
            AssassinationVerdict position = assassination.Evaluate(in target);
            if (!position.Allowed)
            {
                return new ExecutionVerdict(false, FromPosition(position.Reject));
            }

            if (!input.SpeciesExecutable)
            {
                return new ExecutionVerdict(false, ExecutionReject.SpeciesNotExecutable);
            }

            return new ExecutionVerdict(true, ExecutionReject.None);
        }

        /// <summary>把条件 ① 的被拒原因翻成处决侧的原因（两套枚举一一对应，None 只会在通过时出现）。</summary>
        public static ExecutionReject FromPosition(AssassinationReject reject)
        {
            switch (reject)
            {
                case AssassinationReject.None:
                    return ExecutionReject.None;
                case AssassinationReject.NotBehind:
                    return ExecutionReject.NotBehind;
                case AssassinationReject.OutOfRange:
                    return ExecutionReject.OutOfRange;
                case AssassinationReject.TargetAware:
                    return ExecutionReject.TargetAware;
                case AssassinationReject.TargetNotAlive:
                    return ExecutionReject.TargetNotAlive;
                case AssassinationReject.NotSneaking:
                    return ExecutionReject.NotSneaking;
                case AssassinationReject.SamePosition:
                    return ExecutionReject.SamePosition;
                default:
                    // 内核加了新的拒绝原因而这里没跟上时，当场炸而不是悄悄算成「通过」。
                    throw new System.ArgumentOutOfRangeException(nameof(reject),
                        $"暗杀判定新增了未登记的拒绝原因 {(byte)reject}，ExecutionRules.FromPosition 要同步。");
            }
        }

        /// <summary>
        /// 埋点用的英文原因码。**拒绝路径必须有点**（PRP §2.6）——它是「玩家按了没反应」的唯一现场，
        /// 所以四个最常见的原因各有一个稳定的码：不在背后 / 超距 / 目标已察觉 / 物种不可处决。
        /// </summary>
        public static string ReasonCode(ExecutionReject reject)
        {
            switch (reject)
            {
                case ExecutionReject.None:
                    return string.Empty;
                case ExecutionReject.NoTarget:
                    return "no_target";
                case ExecutionReject.NotBehind:
                    return "not_behind";
                case ExecutionReject.OutOfRange:
                    return "out_of_range";
                case ExecutionReject.TargetAware:
                    return "target_aware";
                case ExecutionReject.TargetNotAlive:
                    return "target_not_alive";
                case ExecutionReject.NotSneaking:
                    return "not_sneaking";
                case ExecutionReject.SamePosition:
                    return "same_position";
                case ExecutionReject.SpeciesNotExecutable:
                    return ReasonSpeciesNotExecutable;
                default:
                    return "unknown";
            }
        }

        /// <summary>把被拒原因翻成一句中文（调试面板 / 测试失败信息用）。</summary>
        public static string Describe(ExecutionReject reject)
        {
            switch (reject)
            {
                case ExecutionReject.None:
                    return "可以处决";
                case ExecutionReject.NoTarget:
                    return "附近没有可处决的目标";
                case ExecutionReject.NotBehind:
                    return "不在目标背后";
                case ExecutionReject.OutOfRange:
                    return "超出暗杀距离";
                case ExecutionReject.TargetAware:
                    return "目标已经察觉";
                case ExecutionReject.TargetNotAlive:
                    return "目标不处于可处决状态";
                case ExecutionReject.NotSneaking:
                    return "未潜行";
                case ExecutionReject.SamePosition:
                    return "与目标重合，朝向无意义";
                case ExecutionReject.SpeciesNotExecutable:
                    return "这个物种不允许被处决";
                default:
                    return "未知原因";
            }
        }
    }
}
