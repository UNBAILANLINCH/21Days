// 职责：玩家回合的指令种类。
// 为什么新建：一个类型一个文件；与 BattleCommand 分开，界面按种类分派时不必引用整个指令结构。
namespace Game.Battle
{
    /// <summary>玩家指令种类。</summary>
    public enum BattleCommandKind
    {
        /// <summary>非法值（默认值），流程按「未知指令」拒掉。</summary>
        None = 0,

        /// <summary>施放招式（07:48-54）。</summary>
        CastSkill = 1,

        /// <summary>用一件道具（07:58，出招前）。</summary>
        UseItem = 2,

        /// <summary>结束回合、直接交给敌方（无招可用 / 晕眩时只能用道具的口径，见 BattleSession.SkipPlayerTurn）。</summary>
        EndTurn = 3,
    }
}
