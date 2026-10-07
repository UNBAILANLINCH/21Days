// 职责：战斗界面「玩家状态」区能出现的状态种类（07 画面表现：玩家没有醉酒值，但是有状态）。
// 为什么新建：一个类型一个文件；BattleHudRules 按种类给图标字、悬停文案，界面只认种类不认规则。
namespace Game.Battle
{
    /// <summary>玩家状态种类。</summary>
    public enum BattleStatusKind
    {
        /// <summary>非法值（默认值）。</summary>
        None = 0,

        /// <summary>晕眩（BOSS 招式 3 在薄醉态附带，07:82）。角标 = 还剩几回合不能行动。</summary>
        Stunned = 1,

        /// <summary>招式 3 留下的「接下来 3 次攻击附带额外伤害」（07:54）。角标 = 还剩几次。</summary>
        ExtraDamage = 2,
    }
}
