// 职责：Stealth / Chase 侧对「剧情与对白条件」暴露的状态键，以及只活在内存里的瞬时态承载。
//
// 键名不是本模块自创的：全部照抄 `ai-docs/docs/story-facts.md` 第 4.2 / 4.3 节的字典，
// 命名规范见该文件第 3.2 节（`<命名空间>.<状态名>[.<档位>]`，小写点分，白名单命名空间）。
// 本文件是这些键在代码里的**唯一出处**：写入侧只准引用这里的常量，不许再手打字符串
//（手打一次拼错就是一次静默失效，字典第 5 节 V3 专门防这个）。
//
// 瞬时态纪律（字典第 4.2 / 6.2 节）：`stealth.hidden`、`stealth.behind`、`stealth.knockdown`
// 三个键是**瞬时态，不入档**——只活在本文件的 `StealthFactSnapshot` 这类内存快照里，
// 不进 `NarrativeSaveData.StoryFlags`，也不进回放状态；否则读档会把玩家恢复成「正被击倒」。
// 因此本模块的规则类**不实现 IReplayState**，见交付报告「未验证项 / 待接线」。
//
// 出处：`docs/design/features-spotlight/03_潜行与暗杀.md`、`04_追逐.md`、`00_功能总览.md` §5 C4/C5/C6。
namespace Game.Stealth
{
    /// <summary>
    /// 本模块拥有的状态键常量（字典 4.2 / 4.3 节登记项）。命名空间白名单：`stealth` / `chase`。
    /// </summary>
    public static class StealthFactKeys
    {
        /// <summary>`stealth.hidden`：当前未被任何敌人察觉。**瞬时态，不入档**。</summary>
        public const string Hidden = "stealth.hidden";

        /// <summary>`stealth.behind`：目标当前背对玩家且在暗杀距离内。**瞬时态，不入档**。</summary>
        public const string Behind = "stealth.behind";

        /// <summary>`stealth.assassinated`：已用背后暗杀解决过目标（本阶段计数用）。命中即写，持久。</summary>
        public const string Assassinated = "stealth.assassinated";

        /// <summary>`stealth.knockdown`：玩家处于被击倒状态。**瞬时态，不入档**。</summary>
        public const string Knockdown = "stealth.knockdown";

        /// <summary>`stealth.knockdownCount.low`：本阶段被击倒次数处于低档。每次击倒重算三档。</summary>
        public const string KnockdownCountLow = "stealth.knockdownCount.low";

        /// <summary>`stealth.knockdownCount.mid`：本阶段被击倒次数处于中档。</summary>
        public const string KnockdownCountMid = "stealth.knockdownCount.mid";

        /// <summary>`stealth.knockdownCount.high`：本阶段被击倒次数处于高档。</summary>
        public const string KnockdownCountHigh = "stealth.knockdownCount.high";

        /// <summary>`stealth.cover`：当前处于掩体遮挡下。遮挡未做时该键恒不写。</summary>
        public const string Cover = "stealth.cover";

        /// <summary>`stealth.visionmask`：有敌人正在寻找玩家（搜捕态，即警戒但未敌对）。</summary>
        public const string VisionMask = "stealth.visionmask";

        /// <summary>`chase.active`：追逐进行中。**写入方是 Chase / Monster**，本模块只给常量不写。</summary>
        public const string ChaseActive = "chase.active";

        /// <summary>`chase.escaped`：已成功摆脱过追逐（本阶段）。摆脱判定命中时由接线侧写。</summary>
        public const string ChaseEscaped = "chase.escaped";

        /// <summary>`chase.caught`：已被追上（后果由策略注入）。被抓判定命中时由接线侧写。</summary>
        public const string ChaseCaught = "chase.caught";

        /// <summary>`chase.fixed`：当前是脚本化的「固定追逐」。进入固定追逐时写。</summary>
        public const string ChaseFixed = "chase.fixed";
    }

    /// <summary>被击倒次数的档位段。同一时刻只有一个为真（写入侧每次击倒重算三档）。</summary>
    public enum KnockdownTier : byte
    {
        /// <summary>未开始计数（0 次）。此时三档键都不写。</summary>
        None = 0,

        /// <summary>低档 `stealth.knockdownCount.low`。</summary>
        Low = 1,

        /// <summary>中档 `stealth.knockdownCount.mid`。</summary>
        Mid = 2,

        /// <summary>高档 `stealth.knockdownCount.high`。</summary>
        High = 3,
    }

    /// <summary>
    /// 瞬时态的内存承载：本帧结算出来的「现在处于什么状态」，只给接线侧读一帧、写成事实键。
    /// <para>
    /// **不入档、不进回放状态**（字典 §6.2）。默认值 = 什么都没发生，可以当「清空」用。
    /// </para>
    /// </summary>
    public readonly struct StealthFactSnapshot
    {
        public StealthFactSnapshot(
            bool hidden,
            bool behind,
            bool knockdown,
            bool cover,
            bool visionMask,
            bool assassinated,
            int knockdownCount,
            int lowMax,
            int midMax)
        {
            if (knockdownCount < 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(knockdownCount), "击倒次数不可为负");
            }

            if (lowMax < 0 || midMax < lowMax)
            {
                throw new System.ArgumentOutOfRangeException(nameof(lowMax), "档位阈值必须满足 0 <= lowMax <= midMax");
            }

            Hidden = hidden;
            Behind = behind;
            Knockdown = knockdown;
            Cover = cover;
            VisionMask = visionMask;
            Assassinated = assassinated;
            KnockdownCount = knockdownCount;
            Tier = StealthFacts.ResolveKnockdownTier(knockdownCount, lowMax, midMax);
        }

        /// <summary>`stealth.hidden` 的当前值。</summary>
        public bool Hidden { get; }

        /// <summary>`stealth.behind` 的当前值。</summary>
        public bool Behind { get; }

        /// <summary>`stealth.knockdown` 的当前值。</summary>
        public bool Knockdown { get; }

        /// <summary>`stealth.cover` 的当前值。</summary>
        public bool Cover { get; }

        /// <summary>`stealth.visionmask` 的当前值。</summary>
        public bool VisionMask { get; }

        /// <summary>`stealth.assassinated` 的当前值（持久，跨帧保持）。</summary>
        public bool Assassinated { get; }

        /// <summary>本阶段累计被击倒次数（用来算档位，本身不进事实表）。</summary>
        public int KnockdownCount { get; }

        /// <summary>由 <see cref="KnockdownCount"/> 与配置阈值算出的档位。</summary>
        public KnockdownTier Tier { get; }

        /// <summary>档位对应的键名；<see cref="KnockdownTier.None"/> 返回空串（三档都不写）。</summary>
        public string TierKey => StealthFacts.TierKey(Tier);

        /// <summary>档位是否发生过变化。<paramref name="previous"/> 传上一帧的档位，首次写入传 None。</summary>
        public bool TierChanged(KnockdownTier previous) => previous != Tier;

        /// <summary>
        /// 把当前快照按「键 → 值 → 是否写入」交给调用方。只覆盖本模块拥有的 8 个 `stealth.*` 键；
        /// `chase.*` 四个键虽然同属本字典章节，但字典把写入方标成 Chase / Monster，这里不代写。
        /// 档位键按「先清旧档、再写新档」的顺序回调，保证同一时刻只有一个档位为真。
        /// </summary>
        public void Emit(System.Action<string, bool, bool> sink, KnockdownTier previousTier)
        {
            if (sink == null)
            {
                throw new System.ArgumentNullException(nameof(sink));
            }

            sink(StealthFactKeys.Hidden, Hidden, true);
            sink(StealthFactKeys.Behind, Behind, true);
            sink(StealthFactKeys.Knockdown, Knockdown, true);
            sink(StealthFactKeys.Cover, Cover, true);
            sink(StealthFactKeys.VisionMask, VisionMask, true);
            sink(StealthFactKeys.Assassinated, Assassinated, true);

            if (previousTier != Tier)
            {
                if (previousTier != KnockdownTier.None)
                {
                    sink(StealthFacts.TierKey(previousTier), false, true);
                }

                if (Tier != KnockdownTier.None)
                {
                    sink(StealthFacts.TierKey(Tier), true, true);
                }
            }
        }
    }

    /// <summary>事实键的纯函数工具：档位换算与键名拼接。</summary>
    public static class StealthFacts
    {
        /// <summary>
        /// 把击倒次数换成档位。阈值来自 <c>StealthConfig</c>，**不进事实表**（字典 §6.4）：
        /// 表里只有 `stealth.knockdownCount.low|mid|high` 三个布尔键，改阈值改配置不改表。
        /// </summary>
        public static KnockdownTier ResolveKnockdownTier(int count, int lowMax, int midMax)
        {
            if (lowMax < 0 || midMax < lowMax)
            {
                throw new System.ArgumentOutOfRangeException(nameof(lowMax), "档位阈值必须满足 0 <= lowMax <= midMax");
            }

            if (count <= 0)
            {
                return KnockdownTier.None;
            }

            if (count <= lowMax)
            {
                return KnockdownTier.Low;
            }

            return count <= midMax ? KnockdownTier.Mid : KnockdownTier.High;
        }

        /// <summary>档位对应的字典键名；None 返回空串。</summary>
        public static string TierKey(KnockdownTier tier)
        {
            switch (tier)
            {
                case KnockdownTier.Low:
                    return StealthFactKeys.KnockdownCountLow;
                case KnockdownTier.Mid:
                    return StealthFactKeys.KnockdownCountMid;
                case KnockdownTier.High:
                    return StealthFactKeys.KnockdownCountHigh;
                default:
                    return string.Empty;
            }
        }
    }
}
