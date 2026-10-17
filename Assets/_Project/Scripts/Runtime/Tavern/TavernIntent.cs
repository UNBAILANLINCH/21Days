// 职责：场外灌酒、被灌与离区的意图；战内继续沿现有TurnBased规则。
namespace Game.Tavern
{
    public enum TavernAction { DrinkPlayer, DrinkBoss, DrinkCustomer, Leave }
    public readonly struct TavernIntent
    {
        public TavernIntent(TavernAction action, bool customerIdentity, string targetId = "customer")
        { Action = action; HasCustomerIdentity = customerIdentity; TargetId = targetId; }
        public TavernAction Action { get; }
        public bool HasCustomerIdentity { get; }
        public string TargetId { get; }
    }
}
