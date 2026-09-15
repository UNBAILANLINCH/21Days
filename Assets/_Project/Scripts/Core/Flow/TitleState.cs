// 职责：标题界面状态的占位实现——启动流程跑通的终点。
// 为什么新建：architecture.md 5.1 规定启动以 GoToAsync<TitleState>() 收尾，需要有这个类型；
// 波 3 接 UI 后在这里开标题面板，现在只打一行日志证明流程走通。

using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Logging;

namespace Game.Core.Flow
{
    /// <summary>
    /// 标题状态（占位）。波 3 会在 EnterAsync 里 await IUIService.OpenAsync&lt;TitleView&gt;()，
    /// 在 ExitAsync 里关掉它；在那之前只打日志。
    /// </summary>
    public sealed class TitleState : GameState
    {
        public override UniTask EnterAsync(CancellationToken ct)
        {
            Log.Info("进入 TitleState：启动流程完成（标题界面待波 3 接入）");
            return UniTask.CompletedTask;
        }

        public override UniTask ExitAsync(CancellationToken ct)
        {
            Log.Info("离开 TitleState");
            return UniTask.CompletedTask;
        }
    }
}
