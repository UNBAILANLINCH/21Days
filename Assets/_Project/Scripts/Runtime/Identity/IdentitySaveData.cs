// 职责：身份状态的存档分区——当前身份 / 时限 / 冷却 / 账簿用过的身份 / 怀疑度 / 露馅计数 / 记忆 / 死亡。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：`PlayerSaveData` 是玩家本体的存档（位置、朝向、伪装布尔），装不下身份；
//      `NarrativeSaveData.StoryFlags` 是剧情标记集合，只存布尔谓词，存不了「还剩几秒」这类数值。
//   2. 扩展不行：往上述任一分区加字段都会让别的模块的存档语义跟着变；身份是独立模块，按 ISaveData 分区自持。
// **回放决定（本波）**：只做存档分区，**不注册 IReplayState**，因此**不升 ReplayFormat.CurrentFormatVersion**。
//   理由：身份本波没有接进任何 tick 路径（没有任何玩法代码会改它），注册进 ReplayStateRegistry 只会让
//   快照字节布局变长——按 Core/Replay/ReplayFormat.cs:36-48 的判据第 ④ 条，那必须同时升 CurrentFormatVersion
//   并改 Tests/EditMode/Replay/ 的用例；等身份真的由 tick 驱动（接线波）再注册 + 升版一次做完，代价更小。

using System.Collections.Generic;
using Game.Core.Save;

namespace Game.Identity
{
    /// <summary>
    /// 身份分区（纯 DTO）。<b>只存原始量</b>：剧情事实键（<c>identity.*</c>）是这些量的派生投影
    /// （见 <see cref="IdentityFactSnapshot"/>），每次查询现算，不进存档——否则「档位键」会出现
    /// 三档同时为真这类没法自证的状态。
    /// <para>
    /// 跨阶段保留的依据：`docs/design/features-spotlight/01_换皮与附身.md:192`（身份要跨阶段保留（R18），
    /// 所以「持有过哪些身份」需要进存档）；记忆不清的依据见
    /// `ai-docs/docs/story-facts.md` §4.1 的 <c>identity.memory</c> 行与 §6 第 3 条。
    /// </para>
    /// </summary>
    public sealed class IdentitySaveData : ISaveData
    {
        public int Version => 1;

        /// <summary>当前身份 id；本体为空字符串。</summary>
        public string CurrentIdentityId { get; set; } = string.Empty;

        /// <summary>当前身份的借用来源（<see cref="IdentityOrigin"/> 的整数值）。</summary>
        public int CurrentOrigin { get; set; }

        /// <summary>当前身份是否有时间限制。</summary>
        public bool HasTimeLimit { get; set; }

        /// <summary>剩余时限（秒）；无时限时为 0。</summary>
        public float RemainingSeconds { get; set; }

        /// <summary>距离下次可借身份还剩多少秒。</summary>
        public float CooldownLeft { get; set; }

        /// <summary>本阶段累计露馅次数。</summary>
        public int ExposureCount { get; set; }

        /// <summary>是否已经露过馅（字典 §4.1 的 <c>identity.exposed</c>，未给清理时机，故一并存）。</summary>
        public bool Exposed { get; set; }

        /// <summary>是否已因暴露死亡（<c>identity.dead</c>）。</summary>
        public bool Dead { get; set; }

        /// <summary>是否已获得所借身份的记忆（<c>identity.memory</c>，跨阶段不清）。</summary>
        public bool MemoryRecorded { get; set; }

        /// <summary>用过的不同身份 id（按第一次出现的顺序）。</summary>
        public List<string> UsedIdentityIds { get; set; } = new List<string>();

        /// <summary>以身份行动的总次数（账簿的另一种口径，见 <see cref="LedgerCountingMode"/>）。</summary>
        public int UsedIdentityTotalUses { get; set; }

        /// <summary>怀疑度当前值。</summary>
        public float Suspicion { get; set; }

        /// <summary>
        /// 加字段不用动 <see cref="Version"/>（老档缺的字段保留属性初始化器的默认值）；
        /// 改语义（改名、换单位、值域变了）才升版本并在这里逐级迁移。
        /// </summary>
        public void Migrate(int fromVersion)
        {
        }
    }
}
