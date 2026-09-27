// 职责：存档槽的三态——空、可读、不可用（损坏 / 版本过高 / 缺元数据）。
// 为什么新建：一个类型一个文件；选槽面板按它决定行的文案与能否选中。

namespace Game.Session
{
    public enum SlotState
    {
        /// <summary>没有存档文件。</summary>
        Empty = 0,

        /// <summary>候选读取成功且带 <see cref="SessionSaveData"/>。</summary>
        Available = 1,

        /// <summary>有文件但读不了：损坏、版本高于代码、缺元数据分区。</summary>
        Unavailable = 2,
    }
}
