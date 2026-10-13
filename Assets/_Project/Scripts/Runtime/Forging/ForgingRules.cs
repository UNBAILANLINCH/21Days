// 职责：额度、打造点击、扣料和交付；取消未完成工件释放预留额度。
// 复用ItemCraftRules判材料；扩展它不成立：通用配方不拥有官证额度、点击进度与提交首饰。
using System;
using System.Collections.Generic;
using Game.Inventory;
namespace Game.Forging
{
    public sealed class ForgingRules
    {
        private const int MetalId = 1;
        private readonly int initialQuota, maximumQuota, keyCost, ornamentCost, requiredOrnaments;
        private readonly int[] order;
        private readonly Dictionary<int, int> materials = new Dictionary<int, int>();
        private int currentProduct, reserved;
        public ForgingRules(int initial, int maximum, int key, int ornament, int required, int[] pointOrder)
        {
            if (initial < 0 || maximum < initial || key <= 0 || ornament <= 0 || required < 1 || pointOrder == null || pointOrder.Length == 0 ||
                (long)key + (long)ornament * required > maximum)
                throw new ArgumentException("打造配置不合法");
            foreach (int point in pointOrder) if (point < 0 || point > 3) throw new ArgumentException("打造点位须为0至3");
            initialQuota = initial; maximumQuota = maximum; keyCost = key; ornamentCost = ornament;
            requiredOrnaments = required; order = (int[])pointOrder.Clone(); Reset();
        }
        public int Quota { get; private set; }
        public int Spent { get; private set; }
        public int Available => Quota - Spent - reserved;
        public int Progress { get; private set; }
        public int RequiredStrikes => order.Length;
        public int NextPoint => IsForging ? order[Progress] : -1;
        public bool IsForging => currentProduct != 0;
        public int Keys { get; private set; }
        public int Ornaments { get; private set; }
        public bool Submitted { get; private set; }
        public ForgingProgressData Capture() => new ForgingProgressData
        { Quota = Quota, Spent = Spent, Keys = Keys, Ornaments = Ornaments, Product = currentProduct, Progress = Progress, Submitted = Submitted };
        public void Restore(ForgingProgressData saved)
        {
            if (saved == null) throw new ArgumentNullException(nameof(saved));
            int reservation = saved.Product == 2 ? keyCost : saved.Product == 3 ? ornamentCost : 0;
            long expectedSpent = (long)saved.Keys * keyCost + ((long)saved.Ornaments + (saved.Submitted ? requiredOrnaments : 0)) * ornamentCost;
            if (saved.Quota < 0 || saved.Quota > maximumQuota || saved.Spent < 0 || saved.Keys < 0 || saved.Ornaments < 0 ||
                expectedSpent != saved.Spent || (long)saved.Spent + reservation > saved.Quota ||
                (saved.Product != 0 && saved.Product != 2 && saved.Product != 3) ||
                (saved.Product == 0 ? saved.Progress != 0 : saved.Progress < 0 || saved.Progress >= order.Length) ||
                (saved.Submitted && (saved.Keys < 1 || saved.Product != 0))) throw new ArgumentException("打造快照非法");
            Quota = saved.Quota; Spent = saved.Spent; Keys = saved.Keys; Ornaments = saved.Ornaments;
            currentProduct = saved.Product; reserved = reservation; Progress = saved.Progress; Submitted = saved.Submitted;
        }
        public bool Apply(in ForgingIntent intent, out string reason)
        {
            reason = null;
            if (intent.Action == ForgingAction.SetQuota)
            {
                if (!intent.IsOfficer) reason = "只有官证身份能修改拨料文书";
                else if (intent.Value < Spent + reserved || intent.Value > maximumQuota || intent.Value < 0) reason = "拨料量不足以覆盖已用材料或超出额度";
                else { Quota = intent.Value; return true; }
                return false;
            }
            if (!intent.IsCraftsman) { reason = "只有金银匠身份能打造和提交"; return false; }
            if (Submitted) { reason = "已提交，请重试本玩法"; return false; }
            switch (intent.Action)
            {
                case ForgingAction.StartKey:
                case ForgingAction.StartOrnament:
                    if (IsForging) { reason = "先完成或取消当前工件"; return false; }
                    int product = intent.Action == ForgingAction.StartKey ? 2 : 3;
                    int cost = product == 2 ? keyCost : ornamentCost;
                    materials[MetalId] = Available;
                    var recipe = new ItemRecipe(product == 2 ? "key" : "ornament", new[] { new ItemStack(MetalId, cost) }, product);
                    if (!ItemCraftRules.TryCraft(materials, recipe).IsOk) { reason = "拨料额度不足"; return false; }
                    currentProduct = product; reserved = cost; Progress = 0; return true;
                case ForgingAction.Strike:
                    if (!IsForging) { reason = "先选择要打造的工件"; return false; }
                    if (intent.Value != NextPoint) { reason = "点位不符，按高亮顺序继续"; return false; }
                    Progress++;
                    if (Progress == order.Length)
                    {
                        Spent += reserved;
                        if (currentProduct == 2) Keys++; else Ornaments++;
                        currentProduct = 0; reserved = 0; Progress = 0;
                    }
                    return true;
                case ForgingAction.Cancel:
                    if (!IsForging) { reason = "当前没有工件"; return false; }
                    currentProduct = 0; reserved = 0; Progress = 0; return true;
                case ForgingAction.Submit:
                    if (IsForging || Keys < 1 || Ornaments < requiredOrnaments) { reason = "完成金钥匙和规定首饰后才能提交"; return false; }
                    Ornaments -= requiredOrnaments; Submitted = true; return true;
                default: reason = "无效操作"; return false;
            }
        }
        public void Reset()
        { Quota = initialQuota; Spent = 0; currentProduct = 0; reserved = 0; Progress = 0; Keys = 0; Ornaments = 0; Submitted = false; }
    }
}
