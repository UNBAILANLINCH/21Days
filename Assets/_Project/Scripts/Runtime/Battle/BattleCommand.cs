// 职责：玩家回合的一条指令（表现层 → 战斗流程）。
// 为什么新建：IBattlePresenter.WaitCommandAsync 的返回值；招式 / 道具 / 结束回合三种合成一个值类型，流程按种类分派。
using Game.TurnBased;

namespace Game.Battle
{
    /// <summary>玩家指令（值类型）。用三个工厂方法构造。</summary>
    public readonly struct BattleCommand
    {
        private BattleCommand(BattleCommandKind kind, PlayerSkill skill, string itemId)
        {
            Kind = kind;
            Skill = skill;
            ItemId = itemId ?? string.Empty;
        }

        /// <summary>指令种类。</summary>
        public BattleCommandKind Kind { get; }

        /// <summary>招式（只有 <see cref="BattleCommandKind.CastSkill"/> 有意义）。</summary>
        public PlayerSkill Skill { get; }

        /// <summary>道具 id（只有 <see cref="BattleCommandKind.UseItem"/> 有意义；与 <see cref="BattleItemSlot.ItemId"/> 同一套）。</summary>
        public string ItemId { get; }

        /// <summary>施放招式。</summary>
        public static BattleCommand CastSkill(PlayerSkill skill) => new BattleCommand(BattleCommandKind.CastSkill, skill, null);

        /// <summary>用道具。</summary>
        public static BattleCommand UseItem(string itemId) => new BattleCommand(BattleCommandKind.UseItem, PlayerSkill.None, itemId);

        /// <summary>结束回合。</summary>
        public static BattleCommand EndTurn() => new BattleCommand(BattleCommandKind.EndTurn, PlayerSkill.None, null);
    }
}
