// 职责：身份系统写给剧情 / 对白条件的那套事实键——键名、取值形状、保留字。
//   键名**不是本模块自创**：全部照抄字典 `ai-docs/docs/story-facts.md` §4.1 已登记的键，
//   表达式一律 `Fact.StoryFlag` + 点分小写键（同文件 §2 的 C-1/C-2：不加 Fact 枚举值、只有布尔谓词）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：`EncounterContext.Fact`（EncounterContext.cs:9）只有七个内置事实，
//      没有「当前身份」，而字典 §3.1 明写「A 类不要扩」。
//   2. 扩展不行：不能把这些键散在调用点——字典 §1 第 2 条记着现状
//      `NarrativeCatalog.ValidateFacts` 只校验「键非空」，键名拼错完全静默。键必须集中定义。

namespace Game.Identity
{
    /// <summary>
    /// 身份相关的剧情事实键（`Fact.StoryFlag` 的 Key）。逐条对应
    /// <c>ai-docs/docs/story-facts.md</c> §4.1 的登记行，写入方都是 Identity。
    /// <para>
    /// <b>阈值不进表</b>（字典 §6 第 4 条）：`identity.suspicion.*` / `identity.exposedCount.*` 的档位
    /// 由 <see cref="IdentitySettings"/> 的阈值算出来，表里只写谓词。
    /// </para>
    /// <para>
    /// <b>接线方式（本波不接线）</b>：查询时把 <see cref="IdentityFactSnapshot.CollectTrueKeys"/> 收出来的键
    /// 并进 <c>EncounterContext.WithStoryFlags</c>（EncounterContext.cs:33-39 已有这个方法），
    /// 例如在 <c>NarrativeConditionSource.Snapshot</c> 里做一次合并——不需要给 <c>Fact</c> 枚举加值，
    /// 也不需要改表结构。具体改法见交付报告的「建议补丁」。
    /// </para>
    /// </summary>
    public static class IdentityFacts
    {
        /// <summary>命名空间段（字典 §3.2 白名单里的 <c>identity</c>）。</summary>
        public const string Namespace = "identity";

        /// <summary>命名空间前缀，含点号。</summary>
        public const string Prefix = "identity.";

        /// <summary>`identity.borrowed`：当前处于借来的身份（身份生效中）。</summary>
        public const string Borrowed = "identity.borrowed";

        /// <summary>`identity.skin`：正在以「皮」的形式借用身份。</summary>
        public const string Skin = "identity.skin";

        /// <summary>`identity.mask`：正在以「面具」的形式借用身份。</summary>
        public const string Mask = "identity.mask";

        /// <summary>`identity.memory`：已获得所借身份的记忆（跨阶段不清）。</summary>
        public const string Memory = "identity.memory";

        /// <summary>`identity.exposed`：身份已被看穿（露馅已发生）。</summary>
        public const string Exposed = "identity.exposed";

        /// <summary>`identity.ledger.over`：用过的身份超量（触发追逐的开关）。</summary>
        public const string LedgerOver = "identity.ledger.over";

        /// <summary>`identity.dead`：玩家因暴露而死亡（失败态）。</summary>
        public const string Dead = "identity.dead";

        /// <summary>露馅次数档位键的前两段。</summary>
        public const string ExposedCountPrefix = "identity.exposedCount.";

        /// <summary>怀疑度档位键的前两段。</summary>
        public const string SuspicionPrefix = "identity.suspicion.";

        /// <summary>
        /// `identity.&lt;id&gt;`：当前身份是哪一个（一身份一键，如 <c>identity.yuezheng</c>）。
        /// 本体（<see cref="IdentityId.None"/>）返回空串——本体不写任何身份键。
        /// </summary>
        public static string CurrentIdentity(IdentityId id) => id.IsValid ? Prefix + id.Value : string.Empty;

        /// <summary>露馅次数档位键；<see cref="FactTier.None"/> 返回空串（这一档不写）。</summary>
        public static string ExposedCount(FactTier tier)
            => tier == FactTier.None ? string.Empty : ExposedCountPrefix + FactTierMath.SuffixOf(tier);

        /// <summary>怀疑度档位键；<see cref="FactTier.None"/> 返回空串（这一档不写）。</summary>
        public static string Suspicion(FactTier tier)
            => tier == FactTier.None ? string.Empty : SuspicionPrefix + FactTierMath.SuffixOf(tier);

        /// <summary>
        /// 把两组档位键（怀疑度、露馅次数各三个）收进 <paramref name="into"/>，供写方在状态变化时
        /// <b>先清后写</b>——档位三键互斥，只写当前档而不清另外两档，会留下两个同时为真的档位键。
        /// </summary>
        public static void CollectTierKeys(System.Collections.Generic.List<string> into)
        {
            if (into == null)
            {
                return;
            }

            into.Add(ExposedCount(FactTier.Low));
            into.Add(ExposedCount(FactTier.Mid));
            into.Add(ExposedCount(FactTier.High));
            into.Add(Suspicion(FactTier.Low));
            into.Add(Suspicion(FactTier.Mid));
            into.Add(Suspicion(FactTier.High));
        }

        /// <summary>
        /// 这个身份 id 会不会和固定事实键撞名（如 id 取 <c>borrowed</c> 时会写出 `identity.borrowed`）。
        /// 字典 §3.2 要求「同一个键全项目只有一处写入」，所以这种 id 由
        /// <see cref="IdentityCatalog.Register"/> 直接拒收。
        /// </summary>
        public static bool IsReservedName(string identityId)
        {
            switch (identityId)
            {
                case "borrowed":
                case "skin":
                case "mask":
                case "memory":
                case "exposed":
                case "exposedCount":
                case "suspicion":
                case "ledger":
                case "dead":
                    return true;
                default:
                    return false;
            }
        }
    }
}
