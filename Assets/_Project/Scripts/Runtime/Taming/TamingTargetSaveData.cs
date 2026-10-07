// 职责：保存每个稳定标识对应的驯服归属与怪物快照。
// 为什么新建：旧 MonsterSaveData 只描述个体 AI；扩展它会把控制归属混进独立怪物规则。
using Game.Monster;

namespace Game.Taming
{
    public sealed class TamingTargetSaveData
    {
        public string Id { get; set; }
        public bool IsTamed { get; set; }
        public MonsterSaveData Monster { get; set; }
    }
}
