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
            var intent = new ClothingIntent(customer, claim, role == "customer" + customer);
            bool ok = Rules.Apply(in intent, out string reason);
            telemetry.Track("intent_resolved", ("customer", customer), ("claim", claim), ("ok", ok), ("reason", reason));
            return !ok ? reason : claim ? config.HandoverReply : TailorReply(role);
        }
        public int CustomerFor(string role)
        { for (int i = 0; i < config.Count; i++) if (role == "customer" + i) return i; return -1; }
        public string CustomerGreeting(int index) => config.ClueAt(index);
        public string TailorReply(string role)
        {
            int customer = CustomerFor(role);
            return customer < 0 ? config.VisitorReply : Rules.Received && Rules.IsCollector(customer)
                ? config.ReceivedReply : config.TailorReplyAt(customer);
        }
        public bool OffersClothes(string role) => !Rules.Received && Rules.IsCollector(CustomerFor(role));
        public void Reset() { Rules.Reset(); telemetry.Track("reset"); }
    }
}
