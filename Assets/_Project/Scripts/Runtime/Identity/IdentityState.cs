// 职责：身份系统的运行时状态——当前身份、剩余时限、冷却、是否「生效中」，以及露馅计数 / 记忆 / 死亡标记。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：`PlayerModel.IsDisguised` 是一个布尔（PlayerModel.cs:27），只能表达「伪装中」，
//      表达不了「借的是谁、还剩多久、冷却多少」；`DisguiseRules` 全文 9 行、只有一条判据，
//      按模块文档（disguise-module-guide.md:32）它按设计不带状态。
//   2. 扩展不行：`PlayerModel` 在 `Runtime/Player/`，本波不许改；且身份状态跨模块（Monster 看它、
//      Dialogue / Narrative 读它、存档分区存它），塞进 Player 会让别人只能拿到玩家整份状态。
//   **本类不进回放快照**：见 IdentitySaveData 文件头的「回放 / 存档决定」。

using System;

namespace Game.Identity
{
    /// <summary>
    /// 身份状态。逻辑上只有两种身份位：<b>本体</b>（<see cref="Current"/> 为 <see cref="IdentityId.None"/>）
    /// 与<b>借来的身份</b>。
    /// <para>
    /// <b>「生效中」是这里的核心口径</b>：写法照 <c>docs/design/features-spotlight/00_功能总览.md:225</c>
    /// 术语表（本系列不使用「穿皮」，用皮 / 面具时照原文写「使用」「持有」「生效中」）
    /// 与 <c>01_换皮与附身.md:21</c>。本类严格区分两件事：
    /// <list type="bullet">
    /// <item><see cref="IsBorrowing"/>：借着一个身份（当前身份有效）；</item>
    /// <item><see cref="IsInEffect"/>：这个身份<b>正在生效</b>（借了、且没超时）。</item>
    /// </list>
    /// 剧情事实键 `identity.borrowed` 与 `identity.&lt;id&gt;` 跟 <see cref="IsInEffect"/> 走
    /// （字典 §4.1 写明「退出 / 失效时清」），但「借过哪些身份」进账簿、跟 <see cref="IsBorrowing"/> 走。
    /// </para>
    /// <para>
    /// 状态的改写只有两条路：<see cref="IdentityRules"/>（借 / 退 / 推进 / 露馅），
    /// 或 <see cref="IdentityRules.Capture"/> / <see cref="IdentityRules.Restore"/>（存档）。
    /// 所以本类的 setter 全是 internal，模块外只能读。
    /// </para>
    /// </summary>
    public sealed class IdentityState
    {
        /// <summary>当前身份；本体时为 <see cref="IdentityId.None"/>。</summary>
        public IdentityId Current { get; private set; }

        /// <summary>当前身份的借用来源（<see cref="IdentityOrigin.None"/> = 本体）。</summary>
        public IdentityOrigin CurrentOrigin { get; private set; }

        /// <summary>当前身份是否有时间限制（false = 无时限，持有即生效，如查勘使面具）。</summary>
        public bool HasTimeLimit { get; private set; }

        /// <summary>剩余时限（秒）。无时限时恒为 0，读它之前先看 <see cref="HasTimeLimit"/>。</summary>
        public float RemainingSeconds { get; private set; }

        /// <summary>距离下次可以再借身份还剩多少秒（秒）；0 = 随时可借。</summary>
        public float CooldownLeft { get; private set; }

        /// <summary>本阶段累计露馅次数（档位键 `identity.exposedCount.*` 的来源）。</summary>
        public int ExposureCount { get; private set; }

        /// <summary>本阶段是否已经露过馅（事实键 `identity.exposed`，字典 §4.1）。</summary>
        public bool Exposed { get; private set; }

        /// <summary>是否已因暴露死亡（事实键 `identity.dead`；走死亡分支的原因由策略注入）。</summary>
        public bool Dead { get; private set; }

        /// <summary>是否已经拿到过所借身份的记忆（事实键 `identity.memory`，字典 §4.1 写明跨阶段不清）。</summary>
        public bool MemoryRecorded { get; private set; }

        /// <summary>当前是否借着一个身份（含时限已归零但还没结算退出的一瞬）。</summary>
        public bool IsBorrowing => Current.IsValid;

        /// <summary>
        /// 身份是否<b>生效中</b>：借了身份且没超时。无时限的身份只要借到就一直生效。
        /// 敌人攻击许可、剧情事实键都看这个，不看 <see cref="IsBorrowing"/>。
        /// </summary>
        public bool IsInEffect => IsBorrowing && (!HasTimeLimit || RemainingSeconds > 0f);

        /// <summary>进入一个身份（由 <see cref="IdentityRules.TryEnter"/> 调用）。</summary>
        /// <param name="definition">身份定义，不可为空。</param>
        /// <param name="durationSeconds">时限（秒）；≤ 0 = 无时限。</param>
        internal void Begin(IdentityDefinition definition, float durationSeconds)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            Current = definition.Id;
            CurrentOrigin = definition.Origin;
            HasTimeLimit = durationSeconds > 0f;
            RemainingSeconds = HasTimeLimit ? durationSeconds : 0f;
            MemoryRecorded = true;
        }

        /// <summary>回到本体（由 <see cref="IdentityRules"/> 调用）；冷却取 <paramref name="cooldownSeconds"/> 与当前剩余冷却的较大者。</summary>
        internal void End(float cooldownSeconds)
        {
            Current = IdentityId.None;
            CurrentOrigin = IdentityOrigin.None;
            HasTimeLimit = false;
            RemainingSeconds = 0f;
            if (cooldownSeconds > CooldownLeft)
            {
                CooldownLeft = cooldownSeconds;
            }
        }

        /// <summary>推进时限与冷却。只减不加，减到 0 停住（时限归零后由 <see cref="IdentityRules.AdvanceIdentity"/> 结算退出）。</summary>
        internal void TickTimers(float deltaTime)
        {
            if (HasTimeLimit && RemainingSeconds > 0f)
            {
                RemainingSeconds -= deltaTime;
                if (RemainingSeconds < 0f)
                {
                    RemainingSeconds = 0f;
                }
            }

            if (CooldownLeft > 0f)
            {
                CooldownLeft -= deltaTime;
                if (CooldownLeft < 0f)
                {
                    CooldownLeft = 0f;
                }
            }
        }

        /// <summary>记一次露馅（仅在「规则层」露馅时调用，见 <see cref="IdentityRules.LosesIdentity"/>）。</summary>
        internal void RecordExposure()
        {
            ExposureCount++;
            Exposed = true;
        }

        /// <summary>标记玩家已因暴露死亡（后果由注入策略给出）。</summary>
        internal void MarkDead() => Dead = true;

        /// <summary>
        /// 阶段迁移时清本阶段的量：<b>只清露馅次数</b>（档位键 `identity.exposedCount.*` 字典写明是「本阶段」的）。
        /// <b>不清</b>：
        /// <list type="bullet">
        /// <item><see cref="Exposed"/>——字典 §4.1 只写「露馅判定命中时写」，没给清理时机，固按不清处理；</item>
        /// <item>账簿——`01_换皮与附身.md:192`（身份要跨阶段保留，所以「持有过哪些身份」需要进存档）；</item>
        /// <item>怀疑度——`02_身份暴露与怀疑.md:127` R26（换场景是否保留原文没写，暂按不清 [推断]）；</item>
        /// <item>记忆——字典 §4.1 写明「不清（跨阶段）」，与 `01_换皮与附身.md:134` R14 的「继承记忆」一致。</item>
        /// </list>
        /// </summary>
        public void ResetForNewStage()
        {
            ExposureCount = 0;
        }

        /// <summary>把状态写进存档分区（由 <see cref="IdentityRules.Capture"/> 调用）。</summary>
        internal void Capture(IdentitySaveData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            data.CurrentIdentityId = Current.Value;
            data.CurrentOrigin = (int)CurrentOrigin;
            data.HasTimeLimit = HasTimeLimit;
            data.RemainingSeconds = RemainingSeconds;
            data.CooldownLeft = CooldownLeft;
            data.ExposureCount = ExposureCount;
            data.Exposed = Exposed;
            data.Dead = Dead;
            data.MemoryRecorded = MemoryRecorded;
        }

        /// <summary>从存档分区恢复（由 <see cref="IdentityRules.Restore"/> 调用；当前身份是否可用由规则层校验）。</summary>
        internal void Restore(IdentitySaveData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            // 存档里的 id 可能是旧内容、也可能是损坏文件里的垃圾：格式不合法一律按「本体」读，
            // 不抛异常——读档路径上抛异常会让整个槽位打不开（JsonSaveService 侧只报分区损坏）。
            Current = IdentityId.TryParse(data.CurrentIdentityId, out IdentityId parsed) ? parsed : IdentityId.None;
            CurrentOrigin = (IdentityOrigin)data.CurrentOrigin;
            HasTimeLimit = data.HasTimeLimit;
            RemainingSeconds = data.RemainingSeconds;
            CooldownLeft = data.CooldownLeft;
            ExposureCount = data.ExposureCount;
            Exposed = data.Exposed;
            Dead = data.Dead;
            MemoryRecorded = data.MemoryRecorded;
        }

        /// <summary>读档时丢弃当前身份、回到本体（保留冷却与其余计数）。</summary>
        internal void DropCurrentIdentity()
        {
            Current = IdentityId.None;
            CurrentOrigin = IdentityOrigin.None;
            HasTimeLimit = false;
            RemainingSeconds = 0f;
        }
    }
}
