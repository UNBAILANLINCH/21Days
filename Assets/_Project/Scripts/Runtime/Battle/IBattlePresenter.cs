// 职责：战斗流程对「战斗场景表现 + 战斗界面」的唯一接缝。流程只管规则推进与时序，看得见的东西全在实现里。
// 为什么新建：PRP/turnbased-battle W1 只做代码与数据，舞台（BattleArena.unity 里的纸片角色、出招位移、相机）与
//   BattleView（UIView）归 W2；流程不能等它们，也不能直接认识它们——否则 EditMode 里测不了流程、W2 改表现要动流程。
//
// 调用时序（BattleFlow，全程世界暂停、timeScale = 0，表现一律走不受缩放的时间，PRP D5 / §7 坑 3）：
//   黑幕落 → 叠加加载 BattleArena → OpenAsync → 黑幕揭
//   → PlayAsync（开场事件，可能为空）
//   → 循环：玩家回合 WaitCommandAsync → （指令被接受）PlayAsync；BOSS 回合 PlayAsync（BOSS 出招 / 跳过回合）
//   → 分出胜负后：黑幕落 → CloseAsync → 卸载 BattleArena → 恢复世界 → 黑幕揭 → 剧情回写
// 任何一步抛异常或被取消，流程都会照样走 CloseAsync（只要 OpenAsync 被调过），所以 CloseAsync 必须容错、可重入。
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.TurnBased;

namespace Game.Battle
{
    /// <summary>
    /// 战斗表现。W2 实现（建议是一个注册进根作用域的普通类：在 <see cref="OpenAsync"/> 里从
    /// <see cref="BattleOpening.Arena"/> 找舞台组件、用 IUIService 打开 BattleView），由 BattleInstaller 注册为本接口。
    /// 没注册时流程开战即报错并放弃，不会半途卡住。
    /// </summary>
    public interface IBattlePresenter
    {
        /// <summary>黑幕盖着时调：战斗相机接管、舞台按 BOSS 外观摆好、打开战斗界面并按 session 初始状态刷新。</summary>
        UniTask OpenAsync(BattleOpening opening, CancellationToken ct);

        /// <summary>
        /// 播一批事件（出招冲上去再退回、受击、飘字、中央闪现提示 <c>BattleEvent.HintText</c>…），播完再按 session 刷新界面。
        /// <paramref name="events"/> 是本次动作的拷贝，可能为空；返回时这批表现已经播完。
        /// </summary>
        UniTask PlayAsync(BattleSession session, IReadOnlyList<BattleEvent> events, CancellationToken ct);

        /// <summary>
        /// 玩家回合：等玩家点一个招式、一件道具或「结束回合」。<see cref="BattleCommandMenu.Rejection"/> 非空表示上一条指令被规则拒了
        /// （界面可提示），流程会再问一次。菜单里的道具列表只在本次调用内有效，不要跨调用持有。
        /// </summary>
        UniTask<BattleCommand> WaitCommandAsync(BattleCommandMenu menu, CancellationToken ct);

        /// <summary>黑幕盖着时调：关战斗界面、交还相机、清舞台。必须容错（OpenAsync 失败一半也会调）。</summary>
        UniTask CloseAsync(CancellationToken ct);
    }
}
