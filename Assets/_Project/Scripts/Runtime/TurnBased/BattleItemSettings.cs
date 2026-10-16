// 职责：战斗内道具效果的配置（纯值）——一张「道具 id → 效果」的表。
//
// 为什么改（不是新建）：原版只描述**一件**治疗道具（`healItemId` + 百分比回血），是 C91 未拍板时的占位。
// 2026-10-16 策划把 10 件道具的效果写全了（`docs/design/spotlight/07_回合制作战文档.md:199-211`），
// 一件道具的形状装不下，于是扩成一张表；**效果的解释与执行在 `PlayerBattleRules` / `BattleSession`**，
// 本类型只回答「这件道具有没有效果、是什么效果」。
//
// 原文没写的（仍是配置 / 待拍板，不写死）：道具从哪来、一场带几件、用掉后还在不在（C99）；
// 「迅击粉末」的额外一招与回合结构怎么并存（C100）；护盾与护心鳞 / 减伤怎么叠（C101）。
// 见 `docs/design/features-spotlight/09_BOSS战.md` Q31–Q33。

using System;
using System.Collections.Generic;

namespace Game.TurnBased
{
    /// <summary>战斗内道具效果表（只读）。查不到 = 这件道具没有配置效果，只记账、只置暗。</summary>
    public readonly struct BattleItemSettings
    {
        private readonly IReadOnlyList<BattleItemEffect> effects;

        /// <summary>用一张效果表构造；null = 空表。</summary>
        public BattleItemSettings(IReadOnlyList<BattleItemEffect> effects)
        {
            this.effects = effects;
        }

        /// <summary>表里的全部效果（可能为空表，永不为 null）。</summary>
        public IReadOnlyList<BattleItemEffect> Effects => effects ?? Array.Empty<BattleItemEffect>();

        /// <summary>这张表是不是空的（一件道具都没挂效果）。</summary>
        public bool IsEmpty => Effects.Count == 0;

        /// <summary>查这件道具的效果；没有就回 false（<paramref name="effect"/> 为默认值）。</summary>
        public bool TryGet(string itemId, out BattleItemEffect effect)
        {
            if (!string.IsNullOrEmpty(itemId))
            {
                IReadOnlyList<BattleItemEffect> list = Effects;
                for (int i = 0; i < list.Count; i++)
                {
                    if (string.Equals(list[i].ItemId, itemId, StringComparison.Ordinal))
                    {
                        effect = list[i];
                        return true;
                    }
                }
            }

            effect = default;
            return false;
        }

        /// <summary>这件道具的效果是不是「回复生命」——旧接口口径，供仍按治疗道具判断的调用方使用。</summary>
        public bool Heals(string itemId) =>
            TryGet(itemId, out BattleItemEffect effect) && effect.Kind == BattleItemEffectKind.HealFixed;

        /// <summary>
        /// 本工程当前的默认值（与 `TurnBasedConfig` 的字段默认一致，由 `TurnBasedKernelTests` 钉住）：
        /// sp07:199–211 的 10 件道具，id 取 `tbitem` 表 1801–1810（表内每类占一个 100 段的惯例，原文没给 id）。
        /// </summary>
        public static BattleItemSettings PlaceholderDefault => new BattleItemSettings(DefaultEffects());

        /// <summary>默认效果表的数组形式（`TurnBasedConfig` 的字段默认也用它，避免两处各写一遍）。</summary>
        public static BattleItemEffect[] DefaultEffects() => new[]
        {
            new BattleItemEffect("1801", BattleItemEffectKind.HealFixed, 50),                       // 活力药剂
            new BattleItemEffect("1802", BattleItemEffectKind.RageToMax, 0),                        // 狂怒结晶
            new BattleItemEffect("1803", BattleItemEffectKind.DamageReduction, 40, rounds: 2),      // 铁壁护符
            new BattleItemEffect("1804", BattleItemEffectKind.AttackBonus, 7, charges: 3),          // 锐锋油膏
            new BattleItemEffect("1805", BattleItemEffectKind.Lifesteal, 12, rounds: 3),            // 浴血药剂
            new BattleItemEffect("1806", BattleItemEffectKind.HealFixed, 70),                       // 大瓶生命药剂
            new BattleItemEffect("1807", BattleItemEffectKind.RageAdd, 2),                          // 瞬怒晶石
            new BattleItemEffect("1808", BattleItemEffectKind.Shield, 0, charges: 1),               // 坚韧斗篷
            new BattleItemEffect("1809", BattleItemEffectKind.ExtraAction, 0),                      // 迅击粉末
            new BattleItemEffect("1810", BattleItemEffectKind.Regen, 25, rounds: 3),                // 固本甘露
        };
    }
}
