// 职责：开战前等「世界就绪」——玩法场景已进入、没有在切场景。
// 为什么新建：读档恢复到 Battle 阶段时（PRP D9），剧情在 SessionStartedEvent 里就发出了开战通知，那时还停在标题、
//   紧接着要切进世界场景；这时去叠加加载战斗场景会被 Single 模式的切场景冲掉。流程要等，测试又要能控制「等多久」，
//   所以抽成接口（真实实现 SceneWorldGate）。
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Game.Battle
{
    /// <summary>开战门闸。</summary>
    public interface IBattleWorldGate
    {
        /// <summary>等到可以开战再返回；取消时抛 <see cref="System.OperationCanceledException"/>。</summary>
        UniTask WaitUntilReadyAsync(CancellationToken ct);
    }
}
