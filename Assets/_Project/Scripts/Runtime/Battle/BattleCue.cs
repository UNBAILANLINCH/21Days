// 职责：一段演出的参数——种类、是不是加重版、数值、要显示的字。
// 为什么新建：BattleCueRules 的输出；值类型。把「事件 → 演出」的判定从带场景的表现层里拆出来，EditMode 才测得到。一个类型一个文件。
namespace Game.Battle
{
    /// <summary>演出参数（值类型）。</summary>
    public readonly struct BattleCue
    {
        public BattleCue(BattleCueKind kind, bool heavy, int amount, int secondaryAmount, string text, string itemId)
        {
            Kind = kind;
            Heavy = heavy;
            Amount = amount;
            SecondaryAmount = secondaryAmount;
            Text = text;
            ItemId = itemId;
        }

        /// <summary>演出种类。</summary>
        public BattleCueKind Kind { get; }

        /// <summary>加重版（玩家招式 3 / BOSS 招式 3：蓄力更久、震屏更重、飘字更大）。</summary>
        public bool Heavy { get; }

        /// <summary>主数值：伤害 / 回血 / 加醉酒 / 晕眩回合数（按种类解释）。</summary>
        public int Amount { get; }

        /// <summary>次数值：BOSS 饮酒的实际回血。</summary>
        public int SecondaryAmount { get; }

        /// <summary>要显示的字：中央提示原文 / 头顶字样；没有时为 null。</summary>
        public string Text { get; }

        /// <summary>道具 id（只有用道具有）。</summary>
        public string ItemId { get; }
    }
}
