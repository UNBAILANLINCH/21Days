// 职责：场外灌酒、被灌与离区的意图；战内继续沿现有TurnBased规则。
namespace Game.Tavern
{
    public enum TavernAction { DrinkPlayer, DrinkBoss, DrinkCustomer, Leave, Guess }
    public readonly struct TavernIntent
    {
        public TavernIntent(TavernAction action, bool customerIdentity, string targetId = "customer")
        { Action = action; HasCustomerIdentity = customerIdentity; TargetId = targetId; RoundId = 0; Fingers = Call = 0; }
        public TavernIntent(long roundId, string targetId, bool customerIdentity, int fingers, int call)
        { Action = TavernAction.Guess; HasCustomerIdentity = customerIdentity; TargetId = targetId; RoundId = roundId; Fingers = fingers; Call = call; }
        public TavernAction Action { get; }
        public bool HasCustomerIdentity { get; }
        public string TargetId { get; }
        public long RoundId { get; }
        public int Fingers { get; }
        public int Call { get; }
    }
}
