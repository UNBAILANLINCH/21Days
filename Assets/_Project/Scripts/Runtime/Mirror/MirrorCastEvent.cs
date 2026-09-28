// 职责：一次照镜（含自照）已完成、结果已写进辨认记录这个事实事件。
// 为什么新建：一个事件一个文件（EventConventions.cs 第 3 条）；Mirror 模块首次落地，没有可复用的事件。
namespace Game.Mirror
{
    /// <summary>
    /// 照镜已完成。发布方：<see cref="MirrorService.Cast"/> / <see cref="MirrorService.CastAt"/> / <see cref="MirrorService.LookSelf"/>，
    /// 在写分区（与首次照见的保存请求）之后发布。订阅方：结果画面、后续收押模块。
    /// </summary>
    public readonly struct MirrorCastEvent
    {
        public MirrorCastEvent(MirrorResult result, MirrorSubject subject, float range)
        {
            Result = result;
            Subject = subject;
            Range = range;
        }

        public MirrorResult Result { get; }

        /// <summary>命中的场景标记；照不到、自照、或经 <see cref="MirrorService.CastAt"/> 直接给候选时为 null（用 == null 判）。</summary>
        public MirrorSubject Subject { get; }

        /// <summary>本次照镜时的作用距离（已计入裂痕）。</summary>
        public float Range { get; }
    }
}
