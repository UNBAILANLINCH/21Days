// 职责：把真实控制归属转成音游只读权限；音游不应依赖驯服和店内规则。
using System;
using Game.Rhythm;
using Game.Taming;
namespace Game.Theater
{
    public sealed class TheaterEntryPermission : IRhythmEntryPermission
    {
        private readonly TamingRules taming;
        private readonly string performer, context;
        private readonly Func<bool> valid;
        public TheaterEntryPermission(TamingRules taming, string performer, string context, Func<bool> valid)
        {
            this.taming = taming ?? throw new ArgumentNullException(nameof(taming));
            this.performer = performer; this.context = context; this.valid = valid ?? throw new ArgumentNullException(nameof(valid));
        }
        public RhythmEntryAccess Capture(string contextId) => new RhythmEntryAccess(
            contextId == context && valid(), performer == taming.PlayerId ? valid() : taming.IsTargetTamed(performer),
            taming.CanControl(performer) && taming.CurrentControlId == performer);
    }
}
