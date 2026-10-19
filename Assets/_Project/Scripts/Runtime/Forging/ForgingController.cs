// 职责：打造意图处理和结果埋点；配方规则不拥有工位流程或界面。
using Game.Core.Telemetry;
namespace Game.Forging
{
    public sealed class ForgingController
    {
        private readonly ITelemetryScope telemetry;
        private readonly ForgingConfig config;
        public ForgingRules Rules { get; }
        public int MaximumQuota { get; }
        public ForgingController(ForgingConfig config, ITelemetryScope telemetry)
        { this.config = config; this.telemetry = telemetry; Rules = config.CreateRules(); MaximumQuota = config.MaximumQuota; }
        public string ProductName(int index) => config.ProductName(index);
        public string Apply(ForgingAction action, int value, string role)
        {
            var intent = new ForgingIntent(action, value, role == "officer", role == "smith");
            bool ok = Rules.Apply(in intent, out string reason);
            if (!ok) telemetry.TrackWarn("intent_rejected", TelemetryProps.Of(("action", (int)action), ("value", value), ("available", Rules.Available), ("reason", reason)));
            else telemetry.Track("intent_resolved", ("action", (int)action), ("value", value), ("ok", true), ("available", Rules.Available));
            return !ok ? reason : Rules.Submitted ? "金饰已提交，金襟扣×1、金带钩×2、金环扣×3已存入背包。" : "操作完成";
        }
        public void Reset() { Rules.Reset(); telemetry.Track("reset"); }
    }
}
