// 职责：加载视图的可选盖幕表现；加载时序仍由 LoadingCurtain 和调用方掌握。
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Game.Core.UI
{
    public interface ILoadingTransitionPresentation
    {
        UniTask CoverAsync(CancellationToken ct);
        UniTask RevealAsync(CancellationToken ct);
        void HideImmediate();
    }
}
