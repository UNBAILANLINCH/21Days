// 职责：镜上多了一道击中裂痕这个事实事件。
// 为什么新建：一个事件一个文件（EventConventions.cs 第 3 条）；订阅者（镜图标、音效、后续叙事）与照镜事件不同。
namespace Game.Mirror
{
    /// <summary>击中裂痕增加。发布方：MirrorCrackPresenter（波 2），在检测到 PlayerModel.Health 下降、裂痕数变化时发布。</summary>
    public readonly struct MirrorCrackedEvent
    {
        public MirrorCrackedEvent(int hitCracks, int storyCracks)
        {
            HitCracks = hitCracks;
            StoryCracks = storyCracks;
        }

        /// <summary>变化后的击中裂痕数（0..3）。</summary>
        public int HitCracks { get; }

        /// <summary>当前剧情裂痕数（只缩范围，不计入三裂）。</summary>
        public int StoryCracks { get; }
    }
}
