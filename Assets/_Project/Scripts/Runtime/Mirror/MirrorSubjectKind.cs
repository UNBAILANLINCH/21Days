// 职责：照镜对象的种类——人、物、妖。
// 为什么新建：Mirror 模块首次落地（PRP/mirror-core 2.1）；既有模块没有「场景对象身份」这一分类可复用，
//   PoiKind 是万向标兴趣点类别，语义不同，塞进去会让探索 HUD 认识妖。

namespace Game.Mirror
{
    /// <summary>场景标记 <see cref="MirrorSubject"/> 声明的对象种类。只有 <see cref="Yao"/> 读妖物表。</summary>
    public enum MirrorSubjectKind
    {
        /// <summary>普通人：照镜得「照人正常」，显示标记自带的名字与形象。</summary>
        Human = 0,

        /// <summary>物件：照镜得「照物正常」。</summary>
        Object = 1,

        /// <summary>妖：按妖物表判线索，得「模糊轮廓」或「真形」；通灵视只对它生效。</summary>
        Yao = 2,
    }
}
