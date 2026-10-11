// 职责：玩家状态区的一格——种类、图标字、角标数、悬停详情。
// 为什么新建：BattleHudRules.CollectPlayerStatuses 的输出项；值类型，界面按它画一格，不碰规则对象。一个类型一个文件。
namespace Game.Battle
{
    /// <summary>玩家状态格（值类型，按当次 Render 现算）。</summary>
    public readonly struct BattleStatusEntry
    {
        public BattleStatusEntry(BattleStatusKind kind, string icon, int badge, string tooltip)
        {
            Kind = kind;
            Icon = icon ?? string.Empty;
            Badge = badge;
            Tooltip = tooltip ?? string.Empty;
        }

        /// <summary>状态种类。</summary>
        public BattleStatusKind Kind { get; }

        /// <summary>图标上的字（白盒占位，等美术出图标）。</summary>
        public string Icon { get; }

        /// <summary>角标：剩余回合 / 剩余次数（07 布局图状态格右下角的数字）。</summary>
        public int Badge { get; }

        /// <summary>鼠标悬停弹出的详情（数值取配置）。</summary>
        public string Tooltip { get; }
    }
}
