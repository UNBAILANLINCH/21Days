// 职责：选槽面板的两种打开模式——读档（只有可读槽能选）与新游戏（任何槽都能选，非空槽先确认覆盖）。
// 为什么新建：一个类型一个文件；SaveSlotsView 的文案 / 可选判定与 SaveSlotsController 的点击分派都按它分支。

namespace Game.Session
{
    public enum SlotsMode
    {
        /// <summary>标题「选择存档」：点可读槽继续该槽。</summary>
        Load = 0,

        /// <summary>标题「开始」且没有空槽：点空槽直接开局，点非空槽确认覆盖后开局。</summary>
        NewGame = 1,
    }
}
