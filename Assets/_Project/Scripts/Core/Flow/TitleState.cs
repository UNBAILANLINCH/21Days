// 职责：标题界面状态——进入时开标题面板、把面板的「开始」点击转成框架事件、「设置」开设置面板、
//   「退出游戏」走共用退出实现，退出状态时关掉面板。
// 为什么改（不是新建）：波 1 这个类只打一行日志占位，architecture.md 5.1 规定启动以
//   GoToAsync<TitleState>() 收尾；波 3 UI 落地后把占位换成真正的开关面板；波 4 再加一层
//   「面板事件 → 框架事件」的转发，职责始终是「标题这个状态该做什么」，所以一直扩展原文件。
//   标题页转正式后又接了「设置」「退出游戏」两个按钮：设置复用 SettingsController（与暂停菜单同一入口），
//   退出复用 Boot/GameQuit；手机上隐藏退出按钮按 IPlatformService.IsTouchPrimary 判断，都属于「标题该做什么」。
//   存档会话（PRP save-session）再加「继续」「选择存档」两个按钮，照「开始」的样子转成两个框架事件。

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Boot;
using Game.Core.Events;
using Game.Core.Logging;
using Game.Core.Platform;
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
    /// <para>
    /// 「设置」直接开设置面板：它是 Panel 层全屏面板，会把标题暂时隐藏；Esc / 返回关掉后 UIService
    /// 自动把标题显示回来并重选默认按钮，这里不用额外处理。
    /// </para>
    /// </summary>
    public sealed class TitleState : GameState
    {
        private readonly IUIService ui;
        private readonly IPublisher<TitleStartClickedEvent> startClickedPublisher;
        private readonly IPublisher<TitleContinueClickedEvent> continueClickedPublisher;
        private readonly IPublisher<TitleLoadClickedEvent> loadClickedPublisher;
        private readonly SettingsController settings;
        private readonly IPlatformService platform;

        private TitleView view;

        public TitleState(IUIService ui, IPublisher<TitleStartClickedEvent> startClickedPublisher,
            IPublisher<TitleContinueClickedEvent> continueClickedPublisher,
            IPublisher<TitleLoadClickedEvent> loadClickedPublisher,
            SettingsController settings, IPlatformService platform)
        {
            this.ui = ui;
            this.startClickedPublisher = startClickedPublisher;
            this.continueClickedPublisher = continueClickedPublisher;
            this.loadClickedPublisher = loadClickedPublisher;
            this.settings = settings;
            this.platform = platform;
        }

        public override async UniTask EnterAsync(CancellationToken ct)
        {
            view = await ui.OpenAsync<TitleView>(ct: ct);

            // 手机上系统有返回桌面，不给退出按钮（与暂停菜单同一判断）。
            view.SetQuitVisible(!platform.IsTouchPrimary);

            // 订阅与退订成对：Enter 里加、Exit 里摘。面板实例每次打开都是新的，
            // 但 Exit 可能被调两次（见 IGameFlow.Current 的说明），退订必须幂等——
            // C# 的 -= 对没订阅过的委托是空操作，天然幂等。
            view.OnStartClicked += HandleStartClicked;
            view.OnContinueClicked += HandleContinueClicked;
            view.OnLoadClicked += HandleLoadClicked;
            view.OnSettingsClicked += HandleSettingsClicked;
            view.OnQuitClicked += HandleQuitClicked;
            Log.Info("进入 TitleState：标题面板已打开");
        }

        public override async UniTask ExitAsync(CancellationToken ct)
        {
            // UIView 是 MonoBehaviour，判空只用 != null（Unity 重载了 ==，?. 会把已销毁对象当成非空）。
            if (view != null)
            {
                view.OnStartClicked -= HandleStartClicked;
                view.OnContinueClicked -= HandleContinueClicked;
                view.OnLoadClicked -= HandleLoadClicked;
                view.OnSettingsClicked -= HandleSettingsClicked;
                view.OnQuitClicked -= HandleQuitClicked;
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

        /// <summary>「继续」「选择存档」同「开始」：只转成框架事件，去向由玩法层（存档会话）决定，Core 不认识存档槽。</summary>
        private void HandleContinueClicked()
        {
            Log.Info("标题界面：点了继续，发布 TitleContinueClickedEvent");
            continueClickedPublisher.Publish(new TitleContinueClickedEvent());
        }

        private void HandleLoadClicked()
        {
            Log.Info("标题界面：点了选择存档，发布 TitleLoadClickedEvent");
            loadClickedPublisher.Publish(new TitleLoadClickedEvent());
        }

        private void HandleSettingsClicked() => OpenSettingsAsync().Forget();

        private async UniTaskVoid OpenSettingsAsync()
        {
            try
            {
                // 已开或正在开时 SettingsController 自己会直接返回，连点不会叠面板。
                await settings.OpenAsync();
            }
            catch (Exception e)
            {
                Log.Error($"TitleState：打开设置面板失败：{e}");
            }
        }

        private void HandleQuitClicked() => GameQuit.Quit("标题界面");
    }
}
