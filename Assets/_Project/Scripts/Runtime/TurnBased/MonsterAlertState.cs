// 职责：快照量「怪物当前处于什么状态」——进入战斗判定的输入之一。
//
// 为什么另建一个枚举、不用 Monster 模块的怪物状态：
// 本模块是纯规则内核，**不认识 Monster 类型**（`Runtime/Monster/` 是并行任务目录，本次不动、
// 也不引用）。接线侧读怪物状态后翻译成本枚举的三个值即可；这样规则的测试不需要构造怪物。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:22、:27`「怪物正处于警戒状态或敌对状态」。

namespace Game.TurnBased
{
    /// <summary>怪物在接触那一刻的警觉档位（只取进入战斗判定用得上的三档）。</summary>
    public enum MonsterAlertState
    {
        /// <summary>未察觉（既不在警戒也不在敌对）。</summary>
        Unaware = 0,

        /// <summary>警戒（07:22）。</summary>
        Alert = 1,

        /// <summary>敌对（07:22）。</summary>
        Hostile = 2,
    }
}
