// 职责：不可变入口权限快照；现有驯服 API 不定义乐师职业，由调用方组合真实角色资格。
namespace Game.Rhythm
{
    public readonly struct RhythmEntryAccess
    {
        public RhythmEntryAccess(bool isContextValid, bool canAccessLibrary, bool canPerform)
        {
            IsContextValid = isContextValid;
            CanAccessLibrary = canAccessLibrary;
            CanPerform = canPerform;
        }

        public bool IsContextValid { get; }
        public bool CanAccessLibrary { get; }
        public bool CanPerform { get; }
    }
}
