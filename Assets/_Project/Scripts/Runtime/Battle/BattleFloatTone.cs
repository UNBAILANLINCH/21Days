// 职责：战斗飘字的色调（伤害红 / 回血绿 / 字样黄）。
// 为什么新建：一个类型一个文件；表现层按演出种类选色调，具体颜色在 BattleView 的 Inspector 里配。
namespace Game.Battle
{
    /// <summary>飘字色调。</summary>
    public enum BattleFloatTone
    {
        /// <summary>伤害（红）。</summary>
        Damage = 0,

        /// <summary>回血（绿）。</summary>
        Heal = 1,

        /// <summary>字样（黄：「饮酒」「晕眩」「使用 …」）。</summary>
        Info = 2,
    }
}
