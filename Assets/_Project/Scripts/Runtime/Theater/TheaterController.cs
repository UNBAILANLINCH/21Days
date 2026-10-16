// 职责：戏班身份闸、巡逻转向与演奏结果；音游不拥有店内前置条件。
using Game.Core.Telemetry;
namespace Game.Theater
{
    public enum TheaterAction { Verify, Redirect, FinishSuccess, FinishFailure }
    public readonly struct TheaterIntent
    {
        public TheaterIntent(TheaterAction action, string role) { Action = action; Role = role; }
        public TheaterAction Action { get; }
        public string Role { get; }
    }
    public sealed class TheaterController
    {
        private readonly ITelemetryScope telemetry;
        public bool Entered { get; private set; }
        public bool PatrolRedirected { get; private set; }
        public bool Completed { get; private set; }
        public void Restore(bool entered, bool redirected, bool completed)
        {
            if ((redirected && !entered) || (completed && !redirected)) throw new System.ArgumentException("戏班快照非法");
            Entered = entered; PatrolRedirected = redirected; Completed = completed;
        }
        public TheaterController(ITelemetryScope telemetry) { this.telemetry = telemetry; }
        public bool CanPerform(string role) => Entered && PatrolRedirected && role == "musician" && !Completed;
        public string Apply(in TheaterIntent intent)
        {
            string reason = null;
            switch (intent.Action)
            {
                case TheaterAction.Verify:
                    if (intent.Role != "apprentice") reason = "借学徒身份才能通过验身";
                    else Entered = true;
                    break;
                case TheaterAction.Redirect:
                    if (!Entered || intent.Role != "director") reason = "先验身，再借都知身份下令";
                    else PatrolRedirected = true;
                    break;
                case TheaterAction.FinishSuccess:
                case TheaterAction.FinishFailure:
                    if (!CanPerform(intent.Role)) reason = "演奏上下文已失效";
                    else Completed = intent.Action == TheaterAction.FinishSuccess;
                    break;
                default: reason = "无效戏班操作"; break;
            }
            telemetry.Track("intent_resolved", ("action", (int)intent.Action), ("ok", reason == null), ("reason", reason));
            return reason ?? (Completed ? "演奏达标，本玩法完成" : intent.Action == TheaterAction.FinishFailure ? "未达标，可以重试演奏" : "操作完成");
        }
        public void Reset() { Entered = false; PatrolRedirected = false; Completed = false; telemetry.Track("reset"); }
    }
}
