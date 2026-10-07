// 职责：身份系统的全部数值——时限、冷却、账簿上限与口径、怀疑度上限与回落开关、露馅惩罚、档位阈值。
//   默认值一律是占位，逐字段注释写清「出处 / 待拍板编号」，策划拍板后只改这个类与资产，不改规则代码。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：工程里没有一份身份数值可复用；`Disguise` 模块按模块文档（disguise-module-guide.md:32）
//      明写「没有新增配置」，它连开关都没有，装不下这些数。
//   2. 扩展不行：不能塞进 `PlayerConfig`（身份不属于 Player，且 Player 目录本波不许改），
//      也不该塞进 `IdentityConfig` 的字段散列——嵌套块让资产 Inspector 可折叠，测试也能直接 new。

using System;
using UnityEngine;

namespace Game.Identity
{
    /// <summary>
    /// 身份系统数值块（可序列化，嵌在 <see cref="IdentityConfig"/> 里）。
    /// <para>
    /// <b>所有默认值都是占位</b>：聚光灯 01 / 02 只写规则形状，数值与阈值整批待策划拍板
    /// （<c>docs/design/features-spotlight/00_功能总览.md:373-390</c> §8.1）。属性带 setter 是为了
    /// 让测试与将来的内容管线能直接改，不必去动资产。
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class IdentitySettings
    {
        // ── 身份本身 ───────────────────────────────────────────────────────────────

        /// <summary>
        /// 借来身份的默认时限（秒）。<b>≤ 0 = 无时限</b>（「持有即生效」型，如查勘使面具、泾龙面具）。
        /// 出处：`01_换皮与附身.md:166` R27（执事皮有「生效中」时段，说明至少有的皮有生效时段）
        /// 与 `:188` 状态表（一次性用完即失效 / 执事皮有生效中时段）；时长多少原文没写。
        /// 待拍板：`00_功能总览.md:380` §8.1 #2（附身条件与退出）、`01_换皮与附身.md:166` R27。
        /// </summary>
        [Tooltip("借来身份的默认时限（秒）。≤ 0 = 无时限（持有即生效）。占位，出处见字段注释。")]
        [SerializeField] private float defaultDurationSeconds = 0f;

        /// <summary>
        /// 主动退出 / 时限归零之后，重新借身份的冷却（秒）。
        /// 出处：`01_换皮与附身.md:130` R13（附身有没有次数、冷却、距离限制，原文没写）。
        /// 待拍板：`00_功能总览.md:380` §8.1 #2。
        /// </summary>
        [Tooltip("退出或时限归零后重新借身份的冷却（秒）。占位：原文没写冷却，见 01 R13。")]
        [SerializeField] private float cooldownSeconds = 0f;

        // ── 账簿（阶段3）────────────────────────────────────────────────────────────

        /// <summary>
        /// 账簿上限。<b>≤ 0 = 关闭超量判定</b>（永不触发）。默认 3 只是占位。
        /// 出处：`02_身份暴露与怀疑.md:114-115` R16/R17（「一定数量」是多少、数的是什么，原文都没写）。
        /// 待拍板：`00_功能总览.md:387` §8.1 #9（账簿、怀疑度、揭露怎么计）。
        /// </summary>
        [Tooltip("账簿上限；≤ 0 = 关闭超量判定。占位：原文写「超过一定数量」，没给数（02 R17）。")]
        [SerializeField] private int ledgerLimit = 3;

        /// <summary>
        /// 账簿口径：数不同身份个数，还是数以身份行动的总次数。
        /// 出处：`02_身份暴露与怀疑.md:115` R17。待拍板：`00_功能总览.md:387` §8.1 #9。
        /// </summary>
        [Tooltip("账簿口径：数不同身份个数，还是数以身份行动的总次数。原文两说，见 02 R17。")]
        [SerializeField] private LedgerCountingMode ledgerMode = LedgerCountingMode.DistinctIdentities;

        // ── 怀疑度（阶段六）──────────────────────────────────────────────────────────

        /// <summary>
        /// 怀疑度上限。<b>≤ 0 = 关闭判定</b>。默认 100 只是占位。
        /// 出处：`02_身份暴露与怀疑.md:126-127` R25/R26（「到达上限」有、上限数值原文没写）。
        /// 待拍板：`00_功能总览.md:387` §8.1 #9。
        /// </summary>
        [Tooltip("怀疑度上限；≤ 0 = 关闭判定。占位：原文写「到达上限」，没给数（02 R26）。")]
        [SerializeField] private float suspicionLimit = 100f;

        /// <summary>
        /// 怀疑度回落开关：false = <b>只增口径</b>（只涨不落），true = 可回落。
        /// 出处：`02_身份暴露与怀疑.md:127` R26（「会不会自然回落，原文都没写」）；
        /// 待拍板：`00_功能总览.md:387` §8.1 #9。两种口径都留在代码里，由这个开关选。
        /// </summary>
        [Tooltip("怀疑度是否可自然回落。false = 只增不落。原文没定，见 02 R26 与 00 §8.1 #9。")]
        [SerializeField] private bool suspicionCanDecay = false;

        /// <summary>怀疑度回落速率（点 / 秒），只在 <see cref="SuspicionCanDecay"/> 为 true 时生效。占位。</summary>
        [Tooltip("怀疑度回落速率（点/秒），只在「可回落」开启时生效。占位：原文没写。")]
        [SerializeField] private float suspicionDecayPerSecond = 0f;

        // ── 露馅惩罚 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 露馅之后重新借身份的锁定时间（秒）。
        /// 原文没写露馅后的冷却；本字段是「露馅代价」的落点，占位 0（不影响任何行为）。待拍板：`00_功能总览.md:381` §8.1 #3。
        /// </summary>
        [Tooltip("露馅后重新借身份的锁定时间（秒）。占位：原文没写，见 00 §8.1 #3。")]
        [SerializeField] private float exposureLockoutSeconds = 0f;

        /// <summary>
        /// 后果为「追逐」时这次追逐的时长（秒）。
        /// 出处：`02_身份暴露与怀疑.md:117` R19（击杀查勘使后「消失一段时间」不触发追逐）、
        /// `:131` R27（揭露后召唤巡逻队）；具体时长原文都没写。待拍板：`00_功能总览.md:381` §8.1 #3。
        /// </summary>
        [Tooltip("露馅→追逐的持续时长（秒）。占位：原文只写「一段时间」，见 02 R19/R27。")]
        [SerializeField] private float exposureChaseDurationSeconds = 0f;

        /// <summary>
        /// 后果为「死亡」时从判定到失败的过渡时长（秒），供表现层留演出时间。
        /// 出处：`02_身份暴露与怀疑.md:230` Q1 / `:232` Q3（死后从哪里重来，原文没写）。待拍板：`00_功能总览.md:381` §8.1 #3。
        /// </summary>
        [Tooltip("露馅→死亡的过渡时长（秒）。占位：原文没写，见 02 Q1/Q3。")]
        [SerializeField] private float exposureDeathDelaySeconds = 0f;

        // ── 档位阈值（阈值不进表：字典 §6 第 4 条）───────────────────────────────────

        /// <summary>
        /// 怀疑度三档阈值。<b>阈值不进剧情事实表</b>（`ai-docs/docs/story-facts.md` §6 第 4 条 / C-2）：
        /// 表里只写 `identity.suspicion.low|mid|high`，档位由这里算出来。
        /// 计算口径：从高档往低档判，命中即停；阈值为 0 或负表示该档不启用；未达 low 时三档都不写。
        /// 数值出处：`02_身份暴露与怀疑.md:127` R26（涨多快、上限，原文都没写）。待拍板：`00_功能总览.md:387` §8.1 #9。
        /// </summary>
        [Tooltip("怀疑度低档阈值。占位：原文没写，见 02 R26 / 00 §8.1 #9。")]
        [SerializeField] private float suspicionLowThreshold = 1f;

        /// <summary>怀疑度中档阈值。占位，同 <see cref="SuspicionLowThreshold"/> 的出处。</summary>
        [Tooltip("怀疑度中档阈值。占位：同上。")]
        [SerializeField] private float suspicionMidThreshold = 34f;

        /// <summary>怀疑度高档阈值。占位，同 <see cref="SuspicionLowThreshold"/> 的出处。</summary>
        [Tooltip("怀疑度高档阈值。占位：同上。")]
        [SerializeField] private float suspicionHighThreshold = 67f;

        /// <summary>露馅次数低档阈值。占位：`02_身份暴露与怀疑.md:127` R26 与 `00_功能总览.md:387` §8.1 #9 未定。</summary>
        [Tooltip("露馅次数低档阈值。占位：原文没写，见 00 §8.1 #9。")]
        [SerializeField] private int exposedCountLowThreshold = 1;

        /// <summary>露馅次数中档阈值。占位，同 <see cref="ExposedCountLowThreshold"/>。</summary>
        [Tooltip("露馅次数中档阈值。占位：同上。")]
        [SerializeField] private int exposedCountMidThreshold = 2;

        /// <summary>露馅次数高档阈值。占位，同 <see cref="ExposedCountLowThreshold"/>。</summary>
        [Tooltip("露馅次数高档阈值。占位：同上。")]
        [SerializeField] private int exposedCountHighThreshold = 3;

        public float DefaultDurationSeconds
        {
            get => defaultDurationSeconds;
            set => defaultDurationSeconds = value;
        }

        public float CooldownSeconds
        {
            get => cooldownSeconds;
            set => cooldownSeconds = value;
        }

        public int LedgerLimit
        {
            get => ledgerLimit;
            set => ledgerLimit = value;
        }

        public LedgerCountingMode LedgerMode
        {
            get => ledgerMode;
            set => ledgerMode = value;
        }

        public float SuspicionLimit
        {
            get => suspicionLimit;
            set => suspicionLimit = value;
        }

        public bool SuspicionCanDecay
        {
            get => suspicionCanDecay;
            set => suspicionCanDecay = value;
        }

        public float SuspicionDecayPerSecond
        {
            get => suspicionDecayPerSecond;
            set => suspicionDecayPerSecond = value;
        }

        public float ExposureLockoutSeconds
        {
            get => exposureLockoutSeconds;
            set => exposureLockoutSeconds = value;
        }

        public float ExposureChaseDurationSeconds
        {
            get => exposureChaseDurationSeconds;
            set => exposureChaseDurationSeconds = value;
        }

        public float ExposureDeathDelaySeconds
        {
            get => exposureDeathDelaySeconds;
            set => exposureDeathDelaySeconds = value;
        }

        public float SuspicionLowThreshold
        {
            get => suspicionLowThreshold;
            set => suspicionLowThreshold = value;
        }

        public float SuspicionMidThreshold
        {
            get => suspicionMidThreshold;
            set => suspicionMidThreshold = value;
        }

        public float SuspicionHighThreshold
        {
            get => suspicionHighThreshold;
            set => suspicionHighThreshold = value;
        }

        public int ExposedCountLowThreshold
        {
            get => exposedCountLowThreshold;
            set => exposedCountLowThreshold = value;
        }

        public int ExposedCountMidThreshold
        {
            get => exposedCountMidThreshold;
            set => exposedCountMidThreshold = value;
        }

        public int ExposedCountHighThreshold
        {
            get => exposedCountHighThreshold;
            set => exposedCountHighThreshold = value;
        }
    }
}
