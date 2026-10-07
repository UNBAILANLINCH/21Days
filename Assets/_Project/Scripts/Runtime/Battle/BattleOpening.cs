// 职责：开场时交给表现层的东西——哪只 BOSS、这一场的会话、已叠加加载的战斗场景。
// 为什么新建：IBattlePresenter.OpenAsync 的参数打包成一个值，W2 往里加字段时不必改接口签名；一个类型一个文件。
using Game.TurnBased;
using UnityEngine.SceneManagement;

namespace Game.Battle
{
    /// <summary>开场参数（只在 <see cref="IBattlePresenter.OpenAsync"/> 调用内有效，场景卸载后 <see cref="Arena"/> 失效）。</summary>
    public readonly struct BattleOpening
    {
        public BattleOpening(BossDefinition boss, BattleSession session, Scene arena)
        {
            Boss = boss;
            Session = session;
            Arena = arena;
        }

        /// <summary>BOSS 定义（显示名、外观预制体）。</summary>
        public BossDefinition Boss { get; }

        /// <summary>这一场的会话（血量、怒气、醉酒、阶段都从这里读）。</summary>
        public BattleSession Session { get; }

        /// <summary>叠加加载的战斗场景（<c>BattleArena</c>）；没切成 ActiveScene（PRP D4）。</summary>
        public Scene Arena { get; }
    }
}
