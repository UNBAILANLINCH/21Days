// 职责：由玩法调用方定义战斗成功条件；音游不假定伤害量或失败惩罚。
namespace Game.Rhythm
{
    public interface IRhythmCombatPolicy
    {
        bool IsSuccess(RhythmRunResult result);
    }
}
