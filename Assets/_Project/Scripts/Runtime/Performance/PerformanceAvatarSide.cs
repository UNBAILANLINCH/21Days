// 职责：演出对白面板里头像显示在哪一侧——按说话者在舞台上的站位配，谁说话头像就在谁那一侧。
// 为什么新建（复用 → 扩展 → 新建）：工程里没有「面板左右侧」的枚举可复用；
//   用 bool 塞进 PerformanceCastEntry 读不出语义，将来也没法再加「居中」等取值；一个文件一个类型，所以单独成文件。

namespace Game.Performance
{
    /// <summary>演员名单条目的头像侧：对白面板把该说话者的头像显示在左侧还是右侧头像位。</summary>
    public enum PerformanceAvatarSide
    {
        /// <summary>左侧头像位（默认；旧演出没有该字段，全部按左侧显示）。</summary>
        Left = 0,

        /// <summary>右侧头像位。</summary>
        Right = 1,
    }
}
