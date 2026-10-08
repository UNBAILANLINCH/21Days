// 职责：IBattleWorldGate 的真实实现——当前状态是场景状态（SceneGameState）且加载黑幕没盖着，才算世界就绪。
// 为什么新建：见 IBattleWorldGate 文件头；判据只读 Core/Flow 的两个公开接口，不认识任何具体玩法状态（不引用 Monster）。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Flow;

namespace Game.Battle
{
    /// <summary>按状态流与加载黑幕判断的开战门闸（根作用域单例）。</summary>
    public sealed class SceneWorldGate : IBattleWorldGate
    {
        private readonly IGameFlow flow;
        private readonly ILoadingCurtain curtain;

        public SceneWorldGate(IGameFlow flow, ILoadingCurtain curtain)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.curtain = curtain ?? throw new ArgumentNullException(nameof(curtain));
        }

        private bool IsReady => flow.Current is SceneGameState && !curtain.IsCovered;

        public async UniTask WaitUntilReadyAsync(CancellationToken ct)
        {
            // 至少让出一帧：开战通知是在 NarrativeService.DriveAsync 里同步发布的（那时剧情 busy=true），
            // 这里不让一帧，整场要是同步跑完，CompleteBattleAsync 会被剧情的 CanAccept 拒掉。
            // 按帧等、不按时间等：世界暂停时 timeScale = 0，帧照样走。
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            if (!IsReady) await UniTask.WaitUntil(() => IsReady, PlayerLoopTiming.Update, ct);
        }
    }
}
