// 职责：只读查询状态流队列是否仍在处理；Changing/Changed 不涵盖失败与取消的终态。
// 不扩展 IGameFlow 的命令接口，避免让既有测试替身承担未使用的队列状态。
namespace Game.Core.Flow
{
    public interface IGameFlowTransitionStatus
    {
        bool IsTransitioning { get; }
    }
}
