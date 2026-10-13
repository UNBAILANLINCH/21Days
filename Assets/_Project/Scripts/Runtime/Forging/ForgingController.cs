// 职责：打造意图处理和结果埋点；配方规则不拥有工位流程或界面。
using Game.Core.Telemetry;
namespace Game.Forging
{
    public sealed class ForgingController
    {
        private readonly ITelemetryScope telemetry;
        public ForgingRules Rules { get; }
        public int MaximumQuota { get; }
        public ForgingController(ForgingConfig config, ITelemetryScope telemetry)
        { this.telemetry = telemetry; Rules = config.CreateRules(); MaximumQuota = config.MaximumQuota; }
        public string Apply(ForgingAction action, int value, string role)
        {
            var intent = new ForgingIntent(action, value, role == "officer", role == "smith");
            bool ok = Rules.Apply(in intent, out string reason);
            telemetry.Track("intent_resolved", ("action", (int)action), ("ok", ok), ("available", Rules.Available), ("reason", reason));
            return !ok ? reason : Rules.Submitted ? "首饰已交付，金钥匙保留；本玩法完成" : "操作完成";
        }
        public void Reset() { Rules.Reset(); telemetry.Track("reset"); }
    }
}
