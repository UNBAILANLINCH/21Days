// 职责：物资箱进度（已开记录 + 背包）已整体清空这个事实事件。
// 为什么新建：一个事件一个文件（EventConventions.cs 第 3 条）；订阅者（场景绑定把箱子全部合上）与 CrateCollectedEvent 的不同。

namespace Game.Loot
{
    /// <summary>物资箱进度已重置。发布方：<see cref="LootService.Reset"/>。无载荷。</summary>
    public readonly struct LootResetEvent
    {
    }
}
