// 职责：当前存档中一名酒客的历史样本与平滑难度，不保存未确认的拳局。
// 五店进度只负责组合保存；学习器需纯数据深拷贝，故单独给出 DTO。
using System.Collections.Generic;
namespace Game.Tavern
{
    public sealed class TavernLearningData
    {
        public List<int> History { get; set; } = new List<int>();
        public int Performance { get; set; } = 500;
        public int Strength { get; set; }
    }
}
