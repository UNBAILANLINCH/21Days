// 职责：玩家进战斗那一刻的血量快照（值注入，不从 Player 模块读）。
//
// 为什么用快照：本模块是纯规则内核，不引用 `Runtime/Player/`（并行任务目录）。
// 玩家生命上限与当前生命由接线侧从 PlayerConfig / 玩家状态翻译进来。
//
// 出处：玩家血量原文没写（`docs/design/features-spotlight/09_BOSS战.md:239`「血量、伤害数值……原文都没写」），
// 等 C91；本模块不设占位血量，一律由外部注入。

namespace Game.TurnBased
{
    /// <summary>玩家的血量快照。</summary>
    public readonly struct PlayerBattleSnapshot
    {
        /// <summary>构造并校验。</summary>
        /// <exception cref="System.ArgumentOutOfRangeException">生命上限非正，或当前生命不在 [0, 上限] 内。</exception>
        public PlayerBattleSnapshot(int maxHealth, int health)
        {
            if (maxHealth <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(maxHealth), "玩家生命上限必须大于 0");
            }

            if (health < 0 || health > maxHealth)
            {
                throw new System.ArgumentOutOfRangeException(nameof(health), "玩家当前生命必须落在 0..生命上限 内");
            }

            MaxHealth = maxHealth;
            Health = health;
        }

        /// <summary>生命上限。</summary>
        public int MaxHealth { get; }

        /// <summary>当前生命。</summary>
        public int Health { get; }
    }
}
