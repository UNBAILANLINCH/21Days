// 职责：独立打造的动作，角色与点位由调用方从真实试玩状态提供。
namespace Game.Forging
{
    public enum ForgingAction { SetQuota, StartProduct, Strike, Cancel, Submit }
    public readonly struct ForgingIntent
    {
        public ForgingIntent(ForgingAction action, int value, bool officer, bool craftsman)
        { Action = action; Value = value; IsOfficer = officer; IsCraftsman = craftsman; }
        public ForgingAction Action { get; }
        public int Value { get; }
        public bool IsOfficer { get; }
        public bool IsCraftsman { get; }
    }
}
