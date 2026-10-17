// 职责：独立酒肆场外醉酒与移动/感知倍率；不修改玩家或BOSS配置资产。
// 复用战内BossDrunkRules不成立：它按回合推进且没有探索玩家；扩展不行：场外离区清理不属于战斗内核。
using System;
using System.Collections.Generic;
namespace Game.Tavern
{
    public sealed class TavernDrunkRules
    {
        private readonly int step, playerMaximum, bossMaximum, customerMaximum, playerThreshold;
        private readonly float slowMultiplier, minimumAwareness;
        private readonly Dictionary<string, int> customers = new Dictionary<string, int>(StringComparer.Ordinal);
        public TavernDrunkRules(int perDrink, int playerMax, int bossMax, int customerMax, int threshold, float speed, float awareness)
        {
            if (perDrink <= 0 || playerMax <= 0 || bossMax <= 0 || customerMax <= 0 || threshold <= 0 || threshold > playerMax ||
                speed <= 0f || speed > 1f || float.IsNaN(speed) || awareness <= 0f || awareness > 1f || float.IsNaN(awareness))
                throw new ArgumentException("酒肆场外醉酒配置不合法");
            step = perDrink; playerMaximum = playerMax; bossMaximum = bossMax; customerMaximum = customerMax;
            playerThreshold = threshold; slowMultiplier = speed; minimumAwareness = awareness;
        }
        public int PlayerDrunk { get; private set; }
        public int BossDrunk { get; private set; }
        public int CustomerDrunk => CustomerDrunkFor("customer");
        public int CustomerDrunkFor(string id) => id != null && customers.TryGetValue(id, out int value) ? value : 0;
        public float CustomerAwareness(string id) => Awareness(CustomerDrunkFor(id), customerMaximum);
        public float BossSpeedMultiplier => BossDrunk > 0 ? slowMultiplier : 1f;
        public float CustomerSpeed(string id) => CustomerDrunkFor(id) > 0 ? slowMultiplier : 1f;
        public Dictionary<string, int> CaptureCustomers() => new Dictionary<string, int>(customers, StringComparer.Ordinal);
        public void Restore(int player, int boss, Dictionary<string, int> savedCustomers)
        {
            if (player < 0 || player > playerMaximum || boss < 0 || boss > bossMaximum || savedCustomers == null || savedCustomers.Count > 128)
                throw new ArgumentException("酒肆快照非法");
            foreach (var entry in savedCustomers)
                if (string.IsNullOrWhiteSpace(entry.Key) || entry.Key.Length > 128 || entry.Value < 0 || entry.Value > customerMaximum)
                    throw new ArgumentException("酒客快照非法");
            PlayerDrunk = player; BossDrunk = boss; customers.Clear();
            foreach (var entry in savedCustomers) customers.Add(entry.Key, entry.Value);
        }
        public bool CanSneak => PlayerDrunk < playerThreshold;
        public float SpeedMultiplier => CanSneak ? 1f : slowMultiplier;
        public float BossAwarenessMultiplier => Awareness(BossDrunk, bossMaximum);
        public float CustomerAwarenessMultiplier => Awareness(CustomerDrunk, customerMaximum);
        public bool Apply(in TavernIntent intent, out string reason)
        {
            reason = null;
            if (intent.Action == TavernAction.Leave) { Reset(); return true; }
            if (!intent.HasCustomerIdentity) { reason = "只有酒客身份能饮酒与灌酒"; return false; }
            switch (intent.Action)
            {
                case TavernAction.DrinkPlayer: PlayerDrunk = Add(PlayerDrunk, playerMaximum); return true;
                case TavernAction.DrinkBoss: BossDrunk = Add(BossDrunk, bossMaximum); return true;
                case TavernAction.DrinkCustomer:
                    if (string.IsNullOrWhiteSpace(intent.TargetId)) { reason = "灌酒目标缺少稳定ID"; return false; }
                    customers[intent.TargetId] = Add(CustomerDrunkFor(intent.TargetId), customerMaximum); return true;
                default: reason = "无效饮酒操作"; return false;
            }
        }
        private int Add(int value, int maximum) => maximum - value < step ? maximum : value + step;
        private float Awareness(int value, int maximum) => 1f - (1f - minimumAwareness) * value / maximum;
        public void Reset() { PlayerDrunk = 0; BossDrunk = 0; customers.Clear(); }
    }
}
