// 职责：探索兴趣点的种类（NPC / 物资箱 / 地点），决定万向标的可见规则。
// 为什么新建：一个文件一个类型；ExplorationPointOfInterest 与万向标呈现器共用，放任一文件里都不合适。
namespace Game.IsometricExploration
{
    /// <summary>兴趣点种类。</summary>
    public enum PoiKind
    {
        Npc = 0,
        Crate = 1,
        Location = 2,
    }
}
