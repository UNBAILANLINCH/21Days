// 职责：示例模块的玩法规则——按配置表算道具的折后价与订单总价。纯 C# 类，不继承 MonoBehaviour。
// 为什么新建：architecture.md 第 7 节要求「玩法规则写成纯 C# 类，可 EditMode 测试、将来能搬服务端」。
//   1. 复用不行：Game.Core 里全是框架服务（资源、UI、存档……），没有任何装玩法规则的地方，
//      而且把玩法规则写进 Core 就违反了「Core 不出现任何玩法名词」。
//   2. 扩展不行：这是本工程第一个玩法模块，Runtime/ 下原本只有 asmdef，没有文件可扩展。

using System;
using Game.Core.Config;

namespace Game.Sample
{
    /// <summary>
    /// 示例规则：拿 <see cref="IConfigService"/> 的 <c>TbItem</c> 算价格。
    /// <para>
    /// **这个类是整个模块的样板重点**：它不继承 MonoBehaviour、不碰 <c>UnityEngine.Time</c>、
    /// 不碰任何单例，输入输出全是普通值，所以一条 EditMode 测试就能钉住
    /// （见 <c>Scripts/Tests/EditMode/Sample/SampleRulesTests.cs</c>），
    /// 将来真要联网也能整体搬到服务端。玩法规则都照这个形状写。
    /// </para>
    /// <para>
    /// 配置只读：表里的字段都是 <c>readonly</c>，运行期状态不要往配置对象上挂，
    /// 要状态就复制到自己的普通类里（architecture.md 5.4）。
    /// </para>
    /// </summary>
    public sealed class SampleRules
    {
        private readonly IConfigService config;

        public SampleRules(IConfigService config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// 按 id 取道具配置。
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// id 不在 <c>TbItem</c> 里。**不用生成代码自带的 <c>Get</c>**：它抛的是
        /// <c>KeyNotFoundException</c>，报错里只有一句「给定关键字不在字典中」，
        /// 既看不出是哪张表也看不出是哪个 id，线上排查等于重查一遍。
        /// </exception>
        public global::cfg.Item GetItem(int itemId)
        {
            global::cfg.Item item = config.Tables.TbItem.GetOrDefault(itemId);
            if (item == null)
            {
                throw new ArgumentOutOfRangeException(nameof(itemId), itemId,
                    $"配置表 TbItem 里没有 id 为 {itemId} 的道具。"
                    + "确认 Tables/Data/item.xlsx 里有这一行，改完跑一次 scripts/gen-tables.ps1。");
            }

            return item;
        }

        /// <summary>
        /// 算折后单价。<paramref name="discount"/> 是**减免比例**：0 表示原价，1 表示免费，0.2 表示减两成。
        /// 结果四舍五入到整数（逢半进位，不用银行家舍入——玩家看到的价格要和「口算」一致）。
        /// 中间计算走 <see cref="decimal"/>，理由见方法体里的注释。
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">id 非法，或折扣不在 0～1 之间（含 NaN）。</exception>
        public int GetDiscountedPrice(int itemId, float discount)
        {
            ValidateDiscount(discount);

            int price = GetItem(itemId).Price;

            // 用 decimal 而不是 float / double 算钱：float 的 0.15 其实是 0.150000005960464…，
            // 按 double 直算 50 × (1 - 0.15f) = 42.4999997，四舍五入成 42，
            // 而策划口算的是 42.5 → 43，对不上账。decimal 从 float 转换时按 7 位有效数字取整，
            // 拿到的就是 0.15，算出来 42.5，再逢半进位得 43。钱的计算一律走这条路。
            decimal discounted = price * (1m - (decimal)discount);
            int rounded = (int)decimal.Round(discounted, 0, MidpointRounding.AwayFromZero);

            // 表里的价格理论上不会是负数，但配置是人填的；夹一下总比卖出负价格好。
            return rounded < 0 ? 0 : rounded;
        }

        /// <summary>
        /// 把一个购买意图算成折后总价 = 折后单价 × 数量。
        /// 意图的合法性在这里判，不在意图的构造函数里判（<see cref="BuyItemIntent"/> 有说明）。
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">数量不是正数，或 id / 折扣非法。</exception>
        public int GetOrderTotal(BuyItemIntent intent, float discount)
        {
            if (intent.Count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(intent), intent.Count,
                    $"购买数量必须是正数，收到 {intent.Count}（意图：{intent}）。");
            }

            return GetDiscountedPrice(intent.ItemId, discount) * intent.Count;
        }

        /// <summary>折扣校验单独抽出来：两个公开方法都要用，报错文案也得一致。</summary>
        private static void ValidateDiscount(float discount)
        {
            // float.NaN 比较起来两边都是 false，所以显式判一次——否则 NaN 会一路算成 NaN 再转成 int，
            // 得到一个看不懂的数字而不是报错。
            if (float.IsNaN(discount) || discount < 0f || discount > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(discount), discount,
                    "折扣是减免比例，取值必须在 0～1 之间：0 = 原价，1 = 免费。");
            }
        }
    }
}
