// 职责：一个身份的形状——身份来自谁、显示名、借用来源、能力、通行权限。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：`MonsterConfig` 是全体怪物共用的一份数值（模块文档写「所有怪物共用一份配置」），
//      没有「按角色区分」的维度，装不下逐身份的能力 / 通行权限表。
//   2. 扩展不行：不能往 `PlayerModel` / `PlayerSnapshot` 里塞——它们只存布尔与数值，
//      而这里是内容（谁、能做什么、能进哪），要进 ScriptableObject 让策划改。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Identity
{
    /// <summary>
    /// 身份定义（内容侧）。形状照 <c>docs/design/features-spotlight/01_换皮与附身.md</c>：
    /// 「身份」= 玩家当前对外呈现的那个角色（`:23` 术语表）；借用后继承身份 / 物品 / 记忆（`:134` R14）；
    /// 每个身份的能力与通行权限见 `:137-147` 的 R15 表；身份本身就是通行凭证（`:152` R19）。
    /// <para>
    /// <b>本类不含任何数值</b>（时限、冷却、阈值都在 <see cref="IdentitySettings"/>）：
    /// 数值整批待拍板，混进内容定义会让每条内容都带一份占位数字。
    /// </para>
    /// <para>
    /// 能力与通行权限都是<b>开放字符串键</b>（如 <c>music_puzzle</c> / <c>backstage</c>）：
    /// 原文的能力表（R15）逐角色不同且还在改，用枚举会把 01 的表格抄进代码、改一次抄一次。
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class IdentityDefinition
    {
        [Tooltip("身份 id。只允许小写字母、数字、下划线，不含点号（会拼进剧情事实键 identity.<id>）。")]
        [SerializeField] private string id = string.Empty;

        [Tooltip("对外显示名（HUD / 对白里给玩家看的名字）。")]
        [SerializeField] private string displayName = string.Empty;

        [Tooltip("身份来自谁：被附身的角色，或掉落这件皮 / 面具的角色（01 R14「继承身份」）。")]
        [SerializeField] private string sourceCharacterId = string.Empty;

        [Tooltip("借用来源：附身 / 皮 / 面具 / 丹。决定写 identity.skin 还是 identity.mask。")]
        [SerializeField] private IdentityOrigin origin = IdentityOrigin.Possession;

        [Tooltip("该身份独有的能力键（01 R15 能力列）。空数组 = 只能对话之类的无特殊能力身份。")]
        [SerializeField] private string[] abilities = Array.Empty<string>();

        [Tooltip("该身份的通行权限键（01 R15 通行权限列、R19 身份即通行凭证）。")]
        [SerializeField] private string[] accessRights = Array.Empty<string>();

        /// <summary>空构造给 Unity 序列化用；代码里请用 <see cref="Create"/>。</summary>
        public IdentityDefinition()
        {
        }

        /// <summary>身份 id。<b>格式不合法时抛异常</b>——内容写错要在这里炸，不要等到条件永远为假。</summary>
        public IdentityId Id => IdentityId.From(id);

        /// <summary>对外显示名。</summary>
        public string DisplayName => displayName ?? string.Empty;

        /// <summary>身份来自哪个角色（被附身者 / 掉落皮、面具的角色）。</summary>
        public string SourceCharacterId => sourceCharacterId ?? string.Empty;

        /// <summary>借用来源。</summary>
        public IdentityOrigin Origin => origin;

        /// <summary>能力键（只读）。</summary>
        public IReadOnlyList<string> Abilities => abilities ?? (IReadOnlyList<string>)Array.Empty<string>();

        /// <summary>通行权限键（只读）。</summary>
        public IReadOnlyList<string> AccessRights => accessRights ?? (IReadOnlyList<string>)Array.Empty<string>();

        /// <summary>是否有某个能力键。</summary>
        public bool HasAbility(string abilityKey)
        {
            if (string.IsNullOrEmpty(abilityKey))
            {
                return false;
            }

            string[] keys = abilities;
            for (int i = 0; keys != null && i < keys.Length; i++)
            {
                if (string.Equals(keys[i], abilityKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>是否有某个通行权限键。</summary>
        public bool HasAccessRight(string accessKey)
        {
            if (string.IsNullOrEmpty(accessKey))
            {
                return false;
            }

            string[] keys = accessRights;
            for (int i = 0; keys != null && i < keys.Length; i++)
            {
                if (string.Equals(keys[i], accessKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>代码 / 测试里造定义用（克隆输入数组，避免调用方后续改动影响定义）。</summary>
        public static IdentityDefinition Create(
            string id,
            string displayName,
            IdentityOrigin origin = IdentityOrigin.Possession,
            string sourceCharacterId = null,
            string[] abilities = null,
            string[] accessRights = null)
        {
            return new IdentityDefinition
            {
                id = id,
                displayName = displayName ?? string.Empty,
                origin = origin,
                sourceCharacterId = sourceCharacterId ?? string.Empty,
                abilities = abilities == null ? Array.Empty<string>() : (string[])abilities.Clone(),
                accessRights = accessRights == null ? Array.Empty<string>() : (string[])accessRights.Clone(),
            };
        }
    }
}
