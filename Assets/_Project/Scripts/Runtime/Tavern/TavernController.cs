// 职责：酒肆意图处理和结果埋点；回合制内核不拥有场外灌酒、离区与重试。
using Game.Core.Telemetry;
namespace Game.Tavern
{
    public sealed class TavernController
    {
        private readonly ITelemetryScope telemetry;
        public TavernDrunkRules Rules { get; }
        public TavernController(TavernConfig config, ITelemetryScope telemetry)
        { this.telemetry = telemetry; Rules = config.CreateRules(); }
        public string Apply(TavernAction action, string role, string targetId = "customer")
        {
            var intent = new TavernIntent(action, role == "guest", targetId);
            bool ok = Rules.Apply(in intent, out string reason);
            telemetry.Track("intent_resolved", ("action", (int)action), ("ok", ok), ("player", Rules.PlayerDrunk), ("boss", Rules.BossDrunk));
            return !ok ? reason : action == TavernAction.Leave ? "已离开酒肆，场外醉酒清零" : "饮酒完成，观察移动与感知范围变化";
        }
        public void Reset() { Rules.Reset(); telemetry.Track("reset"); }
    }
}
