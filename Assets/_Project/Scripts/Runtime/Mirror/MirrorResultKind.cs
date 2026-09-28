// 职责：一次照镜的结果种类（PRD：照人正常 / 照物正常 / 模糊轮廓 / 真形 / 照不到，外加自照空白）。
// 为什么新建：MirrorResult 需要一个结果分类；MirrorSubjectKind 是「对象是什么」，结果还多出照不到、模糊、自照三种，
//   两者不是一回事，合成一个枚举会让场景标记能选出「照不到」这种无意义的值。

namespace Game.Mirror
{
    /// <summary><see cref="MirrorResult.Kind"/> 的取值。</summary>
    public enum MirrorResultKind
    {
        /// <summary>照不到：扇形内无对象，或对象超出当前作用距离。</summary>
        Nothing = 0,

        /// <summary>照人正常。</summary>
        Human = 1,

        /// <summary>照物正常。</summary>
        Object = 2,

        /// <summary>照妖但线索不齐：只见模糊轮廓，不给真形名。</summary>
        Blurry = 3,

        /// <summary>照妖且线索齐（或该妖无线索要求）：照见真形。</summary>
        TrueForm = 4,

        /// <summary>自照：结果恒为空白镜面。</summary>
        Self = 5,
    }
}
