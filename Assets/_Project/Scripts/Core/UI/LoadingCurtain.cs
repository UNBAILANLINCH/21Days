// 职责：ILoadingCurtain 的实现——首次落幕时经 IUIService 打开常驻的 LoadingView，按 UIConfig 的时长驱动它淡入淡出，
//   记「黑幕在不在屏上」这一个状态。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：NotificationService 是排队提示卡片，时序与语义都不同；UIService 只开关面板，不管跨状态的盖 / 揭。
//   2. 扩展不行：写进 GameFlow 会让状态流直接依赖具体面板类型，EditMode 测试也没法换成假黑幕记调用顺序；
//      写进 LoadingView 又回到「视图持状态」，调用方得先拿到视图实例。照 NotificationService / NotificationView 的分工：
//      服务持状态、视图只管表现。

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Flow;
using Game.Core.UI.Views;

namespace Game.Core.UI
{
    /// <summary>
    /// 加载黑幕。根作用域单例，<b>不是</b> IGameService：第一次 <see cref="CoverAsync"/> 时才开视图，
    /// 那时 UIService 早已初始化完（Boot → Title 不涉及场景，不会落幕）。
    /// <para>
    /// 面板开不出来（预制体 / Addressables 地址缺失、UIService 已释放）时异常照抛给调用方，
    /// <see cref="IsCovered"/> 回到 false——屏上什么都没有，不能让暂停菜单因为它永远打不开。
    /// </para>
    /// </summary>
    public sealed class LoadingCurtain : ILoadingCurtain
    {
        private enum Phase
        {
            Hidden,
            Covering,
            Covered,
            Revealing,
        }

        private readonly IUIService ui;
        private readonly UIConfig config;

        private LoadingView view;
        private Phase phase = Phase.Hidden;

        public LoadingCurtain(IUIService ui, UIConfig config)
        {
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            // UIConfig 是 ScriptableObject，判空只用 ==。
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
        }

        public bool IsCovered => phase != Phase.Hidden;

        public async UniTask CoverAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // 视图被外部销毁（伪空）时相位还停在 Covered，但屏上已经没有黑幕：照常走一遍，重开视图再盖。
            if (phase == Phase.Covered && view != null)
            {
                return;
            }

            phase = Phase.Covering;
            LoadingView target;
            try
            {
                target = await EnsureViewAsync(ct);
            }
            catch
            {
                // 面板都没开出来：屏上什么也没有，IsCovered 不能卡在 true。
                phase = Phase.Hidden;
                throw;
            }

            // 首次打开面板要等异步实例化，这段时间里有人揭幕（相位已离开 Covering）就不再淡入：
            // 面板刚开出来时 OnOpenAsync 已把它摆成隐藏态，这里直接收手，视图保持隐藏。
            // 不收手的话会淡到全黑而 IsCovered 已是 false，之后的揭幕直接返回——永久黑屏。
            if (phase != Phase.Covering)
            {
                return;
            }

            await target.CoverAsync(config.LoadingFadeSeconds, config.LoadingHintDelaySeconds, ct);

            // 淡入途中被 RevealAsync 接手（相位已变）就不覆写。
            if (phase == Phase.Covering)
            {
                phase = Phase.Covered;
            }
        }

        public async UniTask RevealAsync(CancellationToken ct)
        {
            if (phase == Phase.Hidden)
            {
                return;
            }

            phase = Phase.Revealing;
            LoadingView target = view;
            try
            {
                // UIView 是 MonoBehaviour，判空只用 != null（视图随 UIRoot 销毁后是伪空）。
                if (target != null)
                {
                    await target.RevealAsync(config.LoadingFadeSeconds, ct);
                }
            }
            finally
            {
                // 正常走完时这一步是空操作；取消 / 出错时硬收——宁可闪一下，也不留永久黑屏。
                // 揭幕途中又有人落幕（相位已变）就不动，交给那一次。
                if (phase == Phase.Revealing)
                {
                    phase = Phase.Hidden;
                    if (target != null)
                    {
                        target.HideImmediate();
                    }
                }
            }
        }

        private async UniTask<LoadingView> EnsureViewAsync(CancellationToken ct)
        {
            // 视图被外部关掉（伪空）或还没开：重新开。OpenAsync 对已开的面板直接返回同一实例。
            if (view == null)
            {
                view = await ui.OpenAsync<LoadingView>(null, ct);
            }

            return view;
        }
    }
}
