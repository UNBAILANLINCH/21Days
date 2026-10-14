// 职责：根据证词确认唯一取衣人，保证领取只生效一次。
// 复用不行：Dialogue只呈现证词，Inventory只管物品；扩展不行：二者都不拥有顾客答案与领取判据。
using System;
namespace Game.Clothing
{
    public sealed class ClothingRules
    {
        private readonly bool[] investigated;
        private readonly int collector;
        public ClothingRules(int customerCount, int collectorIndex)
        {
            if (customerCount < 2 || collectorIndex < 0 || collectorIndex >= customerCount)
                throw new ArgumentOutOfRangeException(nameof(collectorIndex));
            investigated = new bool[customerCount]; collector = collectorIndex;
        }
        public bool Received { get; private set; }
        public bool[] CaptureInvestigations() => (bool[])investigated.Clone();
        public void Restore(bool[] seen, bool received)
        {
            if (seen == null || seen.Length != investigated.Length || (received && !seen[collector]))
                throw new ArgumentException("衣肆快照非法");
            Array.Copy(seen, investigated, seen.Length); Received = received;
        }
        public bool HasInvestigated(int index) => index >= 0 && index < investigated.Length && investigated[index];
        public bool Apply(in ClothingIntent intent, out string reason)
        {
            reason = null;
            if (intent.Customer < 0 || intent.Customer >= investigated.Length) reason = "没有这位顾客";
            else if (!intent.HasIdentity) reason = "请先借用这位顾客的身份";
            else if (!intent.Claim) { investigated[intent.Customer] = true; return true; }
            else if (Received) reason = "衣服已经领取过了，不能重复领取";
            else if (!HasInvestigated(intent.Customer)) reason = "先以该顾客身份询问证词";
            else if (intent.Customer != collector) reason = "这不是来取衣服的人，可以继续调查";
            else { Received = true; return true; }
            return false;
        }
        public void Reset() { Array.Clear(investigated, 0, investigated.Length); Received = false; }
    }
}
