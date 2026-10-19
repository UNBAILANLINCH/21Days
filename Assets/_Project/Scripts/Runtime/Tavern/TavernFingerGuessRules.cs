// 职责：划拳输入、隐藏选择及一次性结算，不负责醉意、界面或随机取数。
// 不能复用醉酒规则判拳；扩展醉酒规则会混入局身份，故保留独立纯规则。
using System;
namespace Game.Tavern
{
    public sealed class TavernFingerGuessRules
    {
        public enum Outcome { Tie, PlayerWins, OpponentWins }
        public readonly struct Result
        {
            public Result(int fingers, int call, int opponentFingers, int opponentCall, Outcome outcome)
            { Fingers = fingers; Call = call; OpponentFingers = opponentFingers; OpponentCall = opponentCall; Value = outcome; }
            public int Fingers { get; }
            public int Call { get; }
            public int OpponentFingers { get; }
            public int OpponentCall { get; }
            public int Total => Fingers + OpponentFingers;
            public Outcome Value { get; }
        }
        private long ordinal;
        private int opponentFingers, opponentCall;
        public long RoundId { get; private set; }
        public string TargetId { get; private set; } = "";
        public bool IsPending => RoundId != 0;
        public static bool IsLegal(int fingers, int call) => fingers >= 0 && fingers <= 5 && call >= fingers && call <= fingers + 5;
        public long Begin(string targetId, int fingers, int call)
        {
            if (string.IsNullOrWhiteSpace(targetId) || !IsLegal(fingers, call)) throw new ArgumentException("划拳目标或出指喊数不合法");
            TargetId = targetId; opponentFingers = fingers; opponentCall = call;
            RoundId = checked(++ordinal); return RoundId;
        }
        public bool TryResolve(in TavernIntent intent, out Result result, out string reason)
        {
            result = default; reason = "这一局已结束，请重新入席";
            if (!IsPending || intent.Action != TavernAction.Guess || intent.RoundId != RoundId || intent.TargetId != TargetId) return false;
            if (!intent.HasCustomerIdentity) { reason = "需要酒客身份"; return false; }
            if (!IsLegal(intent.Fingers, intent.Call)) { reason = "出 0～5 指，喊自己的出指数至该数加五"; return false; }
            int total = intent.Fingers + opponentFingers;
            bool playerCorrect = intent.Call == total, opponentCorrect = opponentCall == total;
            var outcome = playerCorrect == opponentCorrect ? Outcome.Tie : playerCorrect ? Outcome.PlayerWins : Outcome.OpponentWins;
            result = new Result(intent.Fingers, intent.Call, opponentFingers, opponentCall, outcome);
            Cancel(); reason = ""; return true;
        }
        public void Cancel() { RoundId = 0; TargetId = ""; }
    }
}
