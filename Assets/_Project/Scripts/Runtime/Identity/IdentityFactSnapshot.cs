// 职责：把身份状态投影成一组「为真的剧情事实键」——查询期现算，不落盘、不进存档。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：`EncounterContext` 是不可变快照、只存布尔（EncounterContext.cs:40-55 的 Read 返回 bool），
//      没有「按身份 id 现算」的位置；`NarrativeSaveData.StoryFlags` 是持久集合，把派生档位写进去
//      会出现「三档同时为真」这类无法自证的状态。
//   2. 扩展不行：不能把投影写进 `IdentityState`——状态是数据，投影是「给剧情看的视图」，
//      两者混在一起后，将来加键就要动存档结构。

using System;
using System.Collections.Generic;

namespace Game.Identity
{
    /// <summary>
    /// 身份事实快照（只读视图）。键名全部来自 <see cref="IdentityFacts"/>（= 字典 §4.1 登记的那套），
    /// 取值只有「真 / 假」——`Fact.StoryFlag` 的语义就是「键在不在集合里」。
    /// <para>
    /// <b>为什么是投影而不是写入</b>：字典 §6 第 2 条要求瞬时态不入档；身份相关的量（还剩几秒、
    /// 冷却多少）也是瞬时的，只有「借过哪些身份」「怀疑度」这类原始量进存档。
    /// 所以本类<b>每次查询重算</b>，档位键因此天然互斥（同一时刻只有一个为真）。
    /// </para>
    /// <para>
    /// <b>档位口径</b>：以 <see cref="IdentitySettings"/> 的阈值为准（配置是唯一真源）；
    /// <see cref="SuspicionState.Tier"/> 只用于埋点。用 <see cref="SuspicionState.FromSettings"/> 建状态时两者一致。
    /// </para>
    /// </summary>
    public readonly struct IdentityFactSnapshot
    {
        private readonly IdentityState state;
        private readonly IdentityLedger ledger;
        private readonly SuspicionState suspicion;
        private readonly IdentitySettings settings;

        /// <param name="state">身份状态，不可为空。</param>
        /// <param name="ledger">身份账簿，不可为空。</param>
        /// <param name="suspicion">怀疑度，不可为空。</param>
        /// <param name="settings">数值块（提供档位阈值与账簿口径），不可为空。</param>
        public IdentityFactSnapshot(
            IdentityState state,
            IdentityLedger ledger,
            SuspicionState suspicion,
            IdentitySettings settings)
        {
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            this.suspicion = suspicion ?? throw new ArgumentNullException(nameof(suspicion));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>怀疑度当前档位（阈值取自 <see cref="IdentitySettings"/>）。</summary>
        public FactTier SuspicionTier => FactTierMath.TierOf(
            suspicion.Value, settings.SuspicionLowThreshold, settings.SuspicionMidThreshold, settings.SuspicionHighThreshold);

        /// <summary>本阶段露馅次数当前档位（阈值取自 <see cref="IdentitySettings"/>）。</summary>
        public FactTier ExposedCountTier => FactTierMath.TierOf(
            state.ExposureCount, settings.ExposedCountLowThreshold, settings.ExposedCountMidThreshold, settings.ExposedCountHighThreshold);

        /// <summary>账簿是否超量（键 `identity.ledger.over`）。</summary>
        public bool LedgerOver => ledger.IsOverLimit(settings.LedgerMode, settings.LedgerLimit);

        /// <summary>
        /// 这个键此刻是否成立。未知键一律 false（键名拼错由 `NarrativeCatalog.ValidateFacts` 的
        /// V1–V3 校验拦，见字典 §5，本类不替它兜底、也不抛异常）。
        /// </summary>
        public bool IsTrue(string key)
        {
            if (string.IsNullOrEmpty(key) || !key.StartsWith(IdentityFacts.Prefix, StringComparison.Ordinal))
            {
                return false;
            }

            switch (key)
            {
                // `identity.borrowed` 与 `identity.<id>` 跟「生效中」走：字典 §4.1 写明「退出 / 失效时清」。
                case IdentityFacts.Borrowed: return state.IsInEffect;
                case IdentityFacts.Skin: return state.IsInEffect && state.CurrentOrigin == IdentityOrigin.Pelt;
                case IdentityFacts.Mask: return state.IsInEffect && state.CurrentOrigin == IdentityOrigin.Mask;
                case IdentityFacts.Memory: return state.MemoryRecorded;
                case IdentityFacts.Exposed: return state.Exposed;
                case IdentityFacts.Dead: return state.Dead;
                case IdentityFacts.LedgerOver: return LedgerOver;
            }

            if (key.StartsWith(IdentityFacts.ExposedCountPrefix, StringComparison.Ordinal))
            {
                return ExposedCountTier != FactTier.None
                    && key == IdentityFacts.ExposedCount(ExposedCountTier);
            }

            if (key.StartsWith(IdentityFacts.SuspicionPrefix, StringComparison.Ordinal))
            {
                return SuspicionTier != FactTier.None
                    && key == IdentityFacts.Suspicion(SuspicionTier);
            }

            // 剩下的形态是 `identity.<id>`：id 段里不允许再有点号（IdentityId 的格式约束）。
            string id = key.Substring(IdentityFacts.Prefix.Length);
            return id.IndexOf('.') < 0
                && state.IsInEffect
                && string.Equals(state.Current.Value, id, StringComparison.Ordinal);
        }

        /// <summary>
        /// 把此刻为真的键收进 <paramref name="into"/>（调用方复用同一个 List，稳态零分配）。
        /// <b>调用方注意</b>：档位键三档互斥，写进持久集合时要用
        /// <see cref="IdentityFacts.CollectTierKeys"/> 先把六个档位键清掉再写，
        /// 否则会出现两个档位同时为真。
        /// </summary>
        public void CollectTrueKeys(List<string> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            if (state.IsInEffect)
            {
                into.Add(IdentityFacts.Borrowed);
                string currentKey = IdentityFacts.CurrentIdentity(state.Current);
                if (currentKey.Length > 0)
                {
                    into.Add(currentKey);
                }

                if (state.CurrentOrigin == IdentityOrigin.Pelt)
                {
                    into.Add(IdentityFacts.Skin);
                }
                else if (state.CurrentOrigin == IdentityOrigin.Mask)
                {
                    into.Add(IdentityFacts.Mask);
                }
            }

            if (state.MemoryRecorded)
            {
                into.Add(IdentityFacts.Memory);
            }

            if (state.Exposed)
            {
                into.Add(IdentityFacts.Exposed);
            }

            if (state.Dead)
            {
                into.Add(IdentityFacts.Dead);
            }

            if (LedgerOver)
            {
                into.Add(IdentityFacts.LedgerOver);
            }

            string exposedTierKey = IdentityFacts.ExposedCount(ExposedCountTier);
            if (exposedTierKey.Length > 0)
            {
                into.Add(exposedTierKey);
            }

            string suspicionTierKey = IdentityFacts.Suspicion(SuspicionTier);
            if (suspicionTierKey.Length > 0)
            {
                into.Add(suspicionTierKey);
            }
        }
    }
}
