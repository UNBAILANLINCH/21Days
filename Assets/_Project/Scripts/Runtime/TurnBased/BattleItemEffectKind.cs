// 职责：战斗内道具的效果种类——sp07「五、道具相关」10 件道具归纳出的 8 种机制。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:199-211`（2026-10-16 新增的两张道具表，逐条对得上）：
//   1 活力药剂 恢复 50 点生命值        → HealFixed(50)
//   2 狂怒结晶 主角怒气直接补满至 3 点  → RageToMax
//   3 铁壁护符 接下来 2 回合，受伤降低 40% → DamageReduction(40, 2 回合)
//   4 锐锋油膏 接下来 3 次攻击，基础攻击力 + 7 → AttackBonus(7, 3 次)
//   5 浴血药剂 接下来 3 回合，每次命中后回复 12 点 → Lifesteal(12, 3 回合)
//   6 大瓶生命药剂 恢复 70 点生命值      → HealFixed(70)
//   7 瞬怒晶石 立刻获得 2 点怒气         → RageAdd(2)
//   8 坚韧斗篷 生成 1 层护盾，抵挡 1 次任意来源伤害 → Shield(1 层)
//   9 迅击粉末 本回合可以额外释放一次招式 → ExtraAction
//  10 固本甘露 接下来 3 回合，回合结束时回复 25 点 → Regen(25, 3 回合)
//
// 原文没写的两处（都留成配置值，不写死）：迅击粉末与回合结构怎么并存（C100）、
// 护盾与护心鳞 / 减伤怎么叠（C101）。见 `docs/design/features-spotlight/09_BOSS战.md` Q31–Q33。

namespace Game.TurnBased
{
    /// <summary>战斗内道具的效果种类。每件道具一条，数值在 <see cref="BattleItemEffect"/> 里。</summary>
    public enum BattleItemEffectKind
    {
        /// <summary>没有效果（只记账、只置暗）。默认值 = 加效果之前的旧行为。</summary>
        None = 0,

        /// <summary>立即回复定值生命（封顶到生命上限，已被打倒不回）。</summary>
        HealFixed = 1,

        /// <summary>怒气直接补满到上限（sp07:200）。</summary>
        RageToMax = 2,

        /// <summary>立即获得若干点怒气（封顶到上限）。</summary>
        RageAdd = 3,

        /// <summary>接下来若干回合，受到的伤害降低若干百分比。</summary>
        DamageReduction = 4,

        /// <summary>接下来若干次攻击，伤害加固定值。</summary>
        AttackBonus = 5,

        /// <summary>接下来若干回合，每次攻击命中后回复固定生命。</summary>
        Lifesteal = 6,

        /// <summary>获得若干层护盾，每层抵挡一次任意来源伤害。</summary>
        Shield = 7,

        /// <summary>本回合可以额外释放一次招式。</summary>
        ExtraAction = 8,

        /// <summary>接下来若干回合，每个回合结束时回复固定生命。</summary>
        Regen = 9,
    }
}
