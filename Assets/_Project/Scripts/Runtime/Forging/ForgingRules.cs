// 职责：当前金属库存、ABC打造点位、一次扣料和交付；取消未完成工件释放预留。
// 复用ItemCraftRules判材料；扩展它不成立：通用配方不拥有官证补料、点位进度与订单目标。
using System;
using System.Collections.Generic;
using Game.Inventory;
namespace Game.Forging
{
    public sealed class ForgingRules
    {
        private const int MetalId = 1;
        private readonly int initialQuota, maximumQuota;
        private readonly int[] order, required;
        private readonly int[] products = new int[3];
        private readonly Dictionary<int, int> materials = new Dictionary<int, int>();
        private int currentProduct = -1;
        public ForgingRules(int initial, int maximum, int cost, int[] requiredProducts, int[] pointOrder)
        {
            if (initial < 0 || maximum < initial || cost <= 0 || cost > maximum || requiredProducts == null || requiredProducts.Length != 3 ||
                pointOrder == null || pointOrder.Length == 0) throw new ArgumentException("打造配置不合法");
            long total = 0;
            foreach (int count in requiredProducts) { if (count < 1) throw new ArgumentException("ABC目标须为正数"); total += count; }
            if (total > int.MaxValue / cost) throw new ArgumentException("打造材料总量溢出");
            foreach (int point in pointOrder) if (point < 0 || point > 3) throw new ArgumentException("打造点位须为0至3");
            initialQuota = initial; maximumQuota = maximum; ProductCost = cost;
            required = (int[])requiredProducts.Clone(); order = (int[])pointOrder.Clone(); Reset();
        }
        public int Quota { get; private set; }
        public int Spent { get; private set; }
        public int Reserved => IsForging ? ProductCost : 0;
        public int Available => Quota - Reserved;
        public int ProductCost { get; }
        public int CurrentProduct => currentProduct;
        public int Progress { get; private set; }
        public int RequiredStrikes => order.Length;
        public int NextPoint => IsForging ? order[Progress] : -1;
        public bool IsForging => currentProduct >= 0;
        public bool Submitted { get; private set; }
        public int Count(int product) => products[ValidateProduct(product)];
        public int Required(int product) => required[ValidateProduct(product)];
        public bool CanSubmit => !Submitted && !IsForging && products[0] >= required[0] && products[1] >= required[1] && products[2] >= required[2];
        private static int ValidateProduct(int product)
        { if (product < 0 || product > 2) throw new ArgumentOutOfRangeException(nameof(product)); return product; }
        public ForgingProgressData Capture() => new ForgingProgressData
        { Schema = 2, Quota = Quota, Spent = Spent, Products = (int[])products.Clone(), Product = currentProduct, Progress = Progress, Submitted = Submitted };
        public void Restore(ForgingProgressData saved)
        {
            if (saved == null) throw new ArgumentNullException(nameof(saved));
            if (saved.Schema != 2 || saved.Products == null || saved.Products.Length != 3) throw new ArgumentException("打造快照版本或产物非法");
            long expectedSpent = 0;
            for (int i = 0; i < 3; i++)
            {
                if (saved.Products[i] < 0 || saved.Products[i] > required[i] || (saved.Submitted && saved.Products[i] != 0))
                    throw new ArgumentException("打造数量非法");
                expectedSpent += ((long)saved.Products[i] + (saved.Submitted ? required[i] : 0)) * ProductCost;
            }
            int reservation = saved.Product >= 0 ? ProductCost : 0;
            if (saved.Quota < 0 || saved.Quota > maximumQuota || saved.Spent < 0 || expectedSpent != saved.Spent ||
                reservation > saved.Quota || saved.Product < -1 || saved.Product > 2 ||
                (saved.Product == -1 ? saved.Progress != 0 : saved.Progress < 0 || saved.Progress >= order.Length) ||
                (saved.Product >= 0 && saved.Products[saved.Product] >= required[saved.Product]) ||
                (saved.Submitted && saved.Product != -1)) throw new ArgumentException("打造快照非法");
            Quota = saved.Quota; Spent = saved.Spent; Array.Copy(saved.Products, products, 3);
            currentProduct = saved.Product; Progress = saved.Progress; Submitted = saved.Submitted;
        }
        public bool Apply(in ForgingIntent intent, out string reason)
        {
            reason = null;
            if (Submitted) { reason = "ABC已经提交，不能重复操作"; return false; }
            if (intent.Action == ForgingAction.SetQuota)
            {
                if (!intent.IsOfficer) reason = "只有官证身份能修改拨料文书";
                else if (intent.Value < Quota || intent.Value > maximumQuota) reason = "补料不能减少当前金属或超出库存上限";
                else { Quota = intent.Value; return true; }
                return false;
            }
            if (!intent.IsCraftsman) { reason = "只有金银匠身份能打造和提交"; return false; }
            switch (intent.Action)
            {
                case ForgingAction.StartProduct:
                    if (intent.Value < 0 || intent.Value > 2) { reason = "没有这个工件"; return false; }
                    if (IsForging) { reason = "先完成或取消当前工件"; return false; }
                    if (products[intent.Value] >= required[intent.Value]) { reason = "该工件目标数量已完成"; return false; }
                    materials[MetalId] = Available;
                    var recipe = new ItemRecipe("forge_" + intent.Value, new[] { new ItemStack(MetalId, ProductCost) }, intent.Value + 2);
                    if (!ItemCraftRules.TryCraft(materials, recipe).IsOk) { reason = "金属不足，请找官证补料"; return false; }
                    currentProduct = intent.Value; Progress = 0; return true;
                case ForgingAction.Strike:
                    if (!IsForging) { reason = "先选择要打造的工件"; return false; }
                    if (intent.Value != NextPoint) { reason = "点位不符，按高亮顺序继续"; return false; }
                    Progress++;
                    if (Progress == order.Length)
                    { Quota -= ProductCost; Spent += ProductCost; products[currentProduct]++; currentProduct = -1; Progress = 0; }
                    return true;
                case ForgingAction.Cancel:
                    if (!IsForging) { reason = "当前没有工件"; return false; }
                    currentProduct = -1; Progress = 0; return true;
                case ForgingAction.Submit:
                    if (!CanSubmit) { reason = "完成A/B/C规定数量后才能提交"; return false; }
                    Array.Clear(products, 0, products.Length); Submitted = true; return true;
                default: reason = "无效操作"; return false;
            }
        }
        public void Reset()
        { Quota = initialQuota; Spent = 0; currentProduct = -1; Progress = 0; Array.Clear(products, 0, products.Length); Submitted = false; }
    }
}
