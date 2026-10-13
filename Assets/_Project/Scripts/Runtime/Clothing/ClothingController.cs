// 职责：衣肆意图处理和结果埋点；规则不持有界面，通用对白不判领衣资格。
using Game.Core.Telemetry;
namespace Game.Clothing
{
    public sealed class ClothingController
    {
        private readonly ClothingConfig config;
        private readonly ITelemetryScope telemetry;
        public ClothingRules Rules { get; }
        public ClothingController(ClothingConfig config, ITelemetryScope telemetry)
        { this.config = config; this.telemetry = telemetry; Rules = config.CreateRules(); }
        public string Apply(int customer, bool claim, string role)
        {
            if (!claim && customer >= 0 && customer < config.Count && Rules.HasInvestigated(customer))
                return config.NameAt(customer) + "（已调查）：" + config.ClueAt(customer) + "\n掌柜：" + config.TailorClue;
            var intent = new ClothingIntent(customer, claim, role == "customer" + customer);
            bool ok = Rules.Apply(in intent, out string reason);
            telemetry.Track("intent_resolved", ("customer", customer), ("claim", claim), ("ok", ok), ("reason", reason));
            return !ok ? reason : claim ? "已领取衣物，本玩法完成" : config.NameAt(customer) + "：" + config.ClueAt(customer) + "\n掌柜：" + config.TailorClue;
        }
        public void Reset() { Rules.Reset(); telemetry.Track("reset"); }
    }
}
