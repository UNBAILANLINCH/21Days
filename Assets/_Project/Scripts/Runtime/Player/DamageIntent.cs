// 职责：玩家与怪物共用的受伤意图。现有 Core 没有战斗伤害规则可复用。
namespace Game.Player
{
    public readonly struct DamageIntent
    {
        public DamageIntent(int amount) => Amount = amount;
        public int Amount { get; }
    }
}
