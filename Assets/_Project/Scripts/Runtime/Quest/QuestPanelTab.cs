// 职责：任务面板的两个标签页（进行中 / 已完成），View 抛事件、Controller 记当前页都用它。
// 为什么新建：枚举要被 View 与 Controller 同时引用，塞进任一方都会让另一方依赖它的文件；一文件一类型。
namespace Game.Quest
{
    public enum QuestPanelTab
    {
        InProgress = 0,
        Completed = 1,
    }
}
