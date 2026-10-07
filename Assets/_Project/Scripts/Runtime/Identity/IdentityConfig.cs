// 职责：身份系统的配置资产——数值块（IdentitySettings）+ 身份定义表（IdentityDefinition[]）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：工程里没有身份配置；`Disguise` 模块按模块文档（disguise-module-guide.md:32）
//      写明「没有新增配置、存档字段或回放字段」。
//   2. 扩展不行：不能挂到 `PlayerConfig` / `MonsterConfig` 上——身份是独立模块，
//      配置混进别人的资产后，改数值要跨模块找，且本波 Player / Monster 目录都不许改。

using UnityEngine;

namespace Game.Identity
{
    /// <summary>
    /// 身份配置。资产放 <c>Assets/_Project/Data/Identity/IdentityConfig.asset</c>，
    /// 接线时挂到组合根（与 <c>SessionInstaller</c> 的做法一致，见 docs/developer-guide.md）。
    /// <para>
    /// <b>默认资产里所有数值都是占位</b>：聚光灯 01 / 02 只写了规则形状，数值与阈值整批待策划拍板
    /// （<c>docs/design/features-spotlight/00_功能总览.md:373-390</c> §8.1）。
    /// 每个字段的出处与待拍板编号写在 <see cref="IdentitySettings"/> 的字段注释里。
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "21Days/Identity/Identity Config", fileName = "IdentityConfig")]
    public sealed class IdentityConfig : ScriptableObject
    {
        [Tooltip("全部数值（时限、冷却、账簿上限与口径、怀疑度上限与回落开关、露馅惩罚、档位阈值）。占位默认值，出处见 IdentitySettings 字段注释。")]
        [SerializeField] private IdentitySettings settings = new IdentitySettings();

        [Tooltip("身份定义表（身份来自谁、显示名、能力、通行权限）。空表合法：此时任何身份都借不到（TryEnter 返回 UnknownIdentity）。")]
        [SerializeField] private IdentityDefinition[] definitions = new IdentityDefinition[0];

        /// <summary>数值块（只读引用；改数值请改资产）。</summary>
        public IdentitySettings Settings => settings;

        /// <summary>按资产里的定义建一张运行时表。</summary>
        public IdentityCatalog CreateCatalog() => IdentityCatalog.From(definitions);

        /// <summary>定义条数（诊断 / 编辑器工具用）。</summary>
        public int DefinitionCount => definitions == null ? 0 : definitions.Length;
    }
}
