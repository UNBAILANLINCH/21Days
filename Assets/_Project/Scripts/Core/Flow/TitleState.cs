// 职责：标题界面状态——进入时开标题面板、把面板的「开始」点击转成框架事件，退出时关掉面板。
// 为什么改（不是新建）：波 1 这个类只打一行日志占位，architecture.md 5.1 规定启动以
//   GoToAsync<TitleState>() 收尾；波 3 UI 落地后把占位换成真正的开关面板；波 4 再加一层
//   「面板事件 → 框架事件」的转发，职责始终是「标题这个状态该做什么」，所以一直扩展原文件。

using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Events;
using Game.Core.Logging;
using Game.Core.UI;
using Game.Core.UI.Views;
using MessagePipe;

namespace Game.Core.Flow
{
    /// <summary>
    /// 标题状态。不加载场景（标题只有 UI），所以直接继承 <see cref="GameState"/> 而不是 <see cref="SceneGameState"/>。
    /// 玩法状态要带场景时继承 SceneGameState，把场景地址填进 <c>SceneKey</c>。
    /// <para>
    /// 「开始」按钮的去向**不在这里决定**：本状态只把 <see cref="TitleView.OnStartClicked"/> 转发成
    /// <see cref="TitleStartClickedEvent"/>，由玩法层订阅后自己 <c>GoToAsync&lt;玩法状态&gt;()</c>。
    /// 这样 Game.Core 不需要认识任何玩法状态（asmdef 依赖方向：Runtime → Core，反过来不行）。
    /// 范例见 <c>Assets/_Project/Scripts/Runtime/Sample/SampleTitleRouter.cs</c>。
    /// </para>
    /// </summary>
    public sealed class TitleState : GameState
    {
        private readonly IUIService ui;
        private readonly IPublisher<TitleStartClickedEvent> startClickedPublisher;

        private TitleView view;

        public TitleState(IUIService ui, IPublisher<TitleStartClickedEvent> startClickedPublisher)
        {
            this.ui = ui;
            this.startClickedPublisher = startClickedPublisher;
        }

        public override async UniTask EnterAsync(CancellationToken ct)
        {
            view = await ui.OpenAsync<TitleView>(ct: ct);

            // 订阅与退订成对：Enter 里加、Exit 里摘。面板实例每次打开都是新的，
            // 但 Exit 可能被调两次（见 IGameFlow.Current 的说明），退订必须幂等——
            // C# 的 -= 对没订阅过的委托是空操作，天然幂等。
            view.OnStartClicked += HandleStartClicked;
            Log.Info("进入 TitleState：标题面板已打开");
        }

        public override async UniTask ExitAsync(CancellationToken ct)
        {
            // UIView 是 MonoBehaviour，判空只用 != null（Unity 重载了 ==，?. 会把已销毁对象当成非空）。
            if (view != null)
            {
                view.OnStartClicked -= HandleStartClicked;
                await ui.CloseAsync(view, ct);
                view = null;
            }

            Log.Info("离开 TitleState");
        }

        /// <summary>
        /// 把面板的点击变成框架事件。没有玩法层订阅时什么也不会发生——那是「玩法还没接进来」，
        /// 不是错误，所以只留一条日志便于排查，不报 Warn。
        /// </summary>
        private void HandleStartClicked()
        {
            Log.Info("标题界面：点了开始，发布 TitleStartClickedEvent");
            startClickedPublisher.Publish(new TitleStartClickedEvent());
        }
    }
}
