// 职责：标在 string 字段上，标记该字段是一个演出 id，供编辑器画成下拉选择而不是手打。
// 为什么新建（复用 → 扩展 → 新建）：工程里没有任何「按时间轴编排的演出」概念可复用；
// Dialogue 是内容驱动推进机、Core 不许出现玩法名词，扩展不了，只能新建。
// 用法：标在 string 字段上，编辑器 PropertyDrawer（Game.Editor.Performance.PerformanceIdDrawer，
// 后续任务建）会把它画成 Addressables `Performance` 组地址的下拉。

namespace Game.Performance
{
    /// <summary>标在 string 字段上，表示该字段的值是一个演出 id（Addressables `Performance` 组地址）。</summary>
    public sealed class PerformanceIdAttribute : UnityEngine.PropertyAttribute
    {
    }
}
