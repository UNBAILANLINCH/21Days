// 职责：怪物长期行为状态；GameFlow 只表示场景级状态，不能复用来表示单个怪物。
namespace Game.Monster
{
    public enum MonsterMode : byte
    {
        PatrolWalk,
        PatrolPause,
        Alert,
        Hostile,
        Dead,
    }
}
