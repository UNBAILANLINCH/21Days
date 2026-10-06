// 职责：切场景时的加载黑幕契约——落幕（淡入到全黑）、揭幕（淡出）、读回黑幕在不在屏上。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：IUIService 只开关面板，不管「盖住两段状态生命周期」这种跨状态的时序；
//      INotificationService 是排队提示，语义不同。
//   2. 扩展不行：roadmap E4 原写「SceneGameState 前后钩子」，不采用——黑幕要跨两个状态：
//      场景→标题时 Exit 方（SceneGameState）落幕、Enter 方（TitleState，不是场景状态）揭幕，挂在单个状态上收不了尾；
//      连续切换还要全程保持黑屏，只有排队的 GameFlow 知道「后面还有没有请求」。所以由 GameFlow 调，
//      契约放 Core/Flow；实现（LoadingCurtain，要开 UI 面板）放 Core/UI，状态流不认识具体面板。

using System.Threading;
using Cysharp.Threading.Tasks;

namespace Game.Core.Flow
{
    /// <summary>
    /// 加载黑幕。<see cref="GameFlow"/> 在切换涉及场景时调用：Changing 事件 → <see cref="CoverAsync"/> → 前一状态 Exit
    /// → 目标状态 Enter → Changed 事件 → 队列清空时 <see cref="RevealAsync"/>。玩法与状态不需要自己调它。
    /// </summary>
    public interface ILoadingCurtain
    {
        /// <summary>
        /// 黑幕在不在屏上：从开始落幕到揭幕完成都算 true（淡入、淡出途中也算），没落过幕或已完全揭开为 false。
        /// 输入方（暂停菜单）按它判断「正在切场景」——淡入途中开出来的菜单会持着暂停令牌跨进下一个场景。
        /// </summary>
        bool IsCovered { get; }

        /// <summary>
        /// 淡入到全黑。返回时画面已被完全盖住、点击被挡住；已完全盖住时直接返回。
        /// 揭幕途中再调：从当前透明度往回淡入。<paramref name="ct"/> 取消时抛 <see cref="System.OperationCanceledException"/>，
        /// 黑幕可能停在半透明，调用方负责随后 <see cref="RevealAsync"/>。
        /// </summary>
        UniTask CoverAsync(CancellationToken ct);

        /// <summary>
        /// 淡出。返回时黑幕已完全揭开、不再挡点击；没盖着时直接返回。
        /// 中途取消或出错也会把黑幕硬收掉再往外抛——宁可闪一下，也不留永久黑屏。
        /// </summary>
        UniTask RevealAsync(CancellationToken ct);
    }
}
