// 职责：只读外部角色及场景资格；音游不直接控制或驯服角色。
namespace Game.Rhythm
{
    public interface IRhythmEntryPermission
    {
        RhythmEntryAccess Capture(string contextId);
    }
}
