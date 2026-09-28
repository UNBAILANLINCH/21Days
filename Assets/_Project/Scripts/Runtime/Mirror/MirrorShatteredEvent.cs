// 职责：镜碎（第三道击中裂痕）这个事实事件。
// 为什么新建：一个事件一个文件（EventConventions.cs 第 3 条）；订阅者（镜碎页、后续叙事后果）与裂痕事件不同。
namespace Game.Mirror
{
    /// <summary>镜已碎。发布方：MirrorCrackPresenter（波 2），在 <see cref="MirrorCrackRules.IsShattered"/> 首次为真时发布一次。无载荷。</summary>
    public readonly struct MirrorShatteredEvent
    {
    }
}
