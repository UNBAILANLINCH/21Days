// 职责：BOSS 进战斗那一刻的快照——生命与**战斗外的醉酒值**。
//
// 为什么醉酒值走快照：07:66 写「BOSS在进入战斗时，会继承战斗场景外的醉酒值」，
// 而战斗外的醉酒（酒肆里灌酒）属于关卡 / 怪物模块；本模块只接收这个数，不去读它们的实现。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:66`；生命数值原文没写
// （`docs/design/features-spotlight/09_BOSS战.md:239`），由外部注入。

namespace Game.TurnBased
{
    /// <summary>BOSS 的血量与醉酒值快照。</summary>
    public readonly struct BossBattleSnapshot
    {
        /// <summary>构造并校验。</summary>
        /// <exception cref="System.ArgumentOutOfRangeException">生命上限非正、当前生命越界、醉酒值为负。</exception>
        public BossBattleSnapshot(int maxHealth, int health, int drunkValue)
        {
            if (maxHealth <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(maxHealth), "BOSS 生命上限必须大于 0");
            }

            if (health < 0 || health > maxHealth)
            {
                throw new System.ArgumentOutOfRangeException(nameof(health), "BOSS 当前生命必须落在 0..生命上限 内");
            }

            if (drunkValue < 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(drunkValue), "醉酒值不可为负");
            }

            MaxHealth = maxHealth;
            Health = health;
            DrunkValue = drunkValue;
        }

        /// <summary>生命上限。</summary>
        public int MaxHealth { get; }

        /// <summary>当前生命。</summary>
        public int Health { get; }

        /// <summary>战斗外带进来的醉酒值（07:66）。</summary>
        public int DrunkValue { get; }
    }
}
