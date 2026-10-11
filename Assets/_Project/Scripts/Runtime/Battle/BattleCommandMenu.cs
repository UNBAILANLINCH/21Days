// 职责：玩家回合交给表现层的「可选项」——会话（招式 / 怒气 / 晕眩从这里读）、道具格、上一条指令被拒的原因。
// 为什么新建：IBattlePresenter.WaitCommandAsync 的参数打包成一个值，W2 往里加东西不改接口签名；一个类型一个文件。
using System.Collections.Generic;
using Game.TurnBased;

namespace Game.Battle
{
    /// <summary>玩家回合菜单（只在一次 <see cref="IBattlePresenter.WaitCommandAsync"/> 调用内有效）。</summary>
    public readonly struct BattleCommandMenu
    {
        public BattleCommandMenu(BattleSession session, IReadOnlyList<BattleItemSlot> items, string rejection)
        {
            Session = session;
            Items = items;
            Rejection = rejection;
        }

        /// <summary>这一场的会话。</summary>
        public BattleSession Session { get; }

        /// <summary>道具格：背包里的消耗品 + 本场用过的（置暗），按 id 升序。</summary>
        public IReadOnlyList<BattleItemSlot> Items { get; }

        /// <summary>上一条指令被拒的原因（中文）；null = 没被拒。</summary>
        public string Rejection { get; }
    }
}
