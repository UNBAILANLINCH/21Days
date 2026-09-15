// 职责：标题界面状态——进入时开标题面板，退出时关掉。
// 为什么改（不是新建）：波 1 这个类只打一行日志占位，architecture.md 5.1 规定启动以
//   GoToAsync<TitleState>() 收尾；波 3 UI 落地后把占位换成真正的开关面板，职责没变，所以扩展原文件而不是新建。

using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Logging;
using Game.Core.UI;
using Game.Core.UI.Views;

namespace Game.Core.Flow
{
    /// <summary>
    /// 标题状态。不加载场景（标题只有 UI），所以直接继承 <see cref="GameState"/> 而不是 <see cref="SceneGameState"/>。
    /// 玩法状态要带场景时继承 SceneGameState，把场景地址填进 <c>SceneKey</c>。
    /// </summary>
    public sealed class TitleState : GameState
    {
        private readonly IUIService ui;

        private TitleView view;

        public TitleState(IUIService ui)
        {
            this.ui = ui;
        }

        public override async UniTask EnterAsync(CancellationToken ct)
        {
            view = await ui.OpenAsync<TitleView>(ct: ct);
            Log.Info("进入 TitleState：标题面板已打开");
        }

        public override async UniTask ExitAsync(CancellationToken ct)
        {
            // UIView 是 MonoBehaviour，判空只用 != null（Unity 重载了 ==，?. 会把已销毁对象当成非空）。
            if (view != null)
            {
                await ui.CloseAsync(view, ct);
                view = null;
            }

            Log.Info("离开 TitleState");
        }
    }
}
