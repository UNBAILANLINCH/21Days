// 职责：任务表校验结果的等级——Error 拦生成，Warning 只提示。
// 为什么新建：工程里没有现成的「校验消息等级」枚举（AssetAuditWindow 的发现不分级），复用不了；独立成文件方便窗口与生成菜单共用。

namespace Game.Editor.Quest
{
    public enum QuestIssueSeverity
    {
        Error = 0,
        Warning = 1
    }
}
