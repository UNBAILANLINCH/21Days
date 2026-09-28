// 职责：镜裂的逐帧状态跟踪（纯 C#）——只在遭遇活动时判定；进入遭遇时取基线不发裂痕；击中裂痕增加才算「新裂」；
//   镜碎每次遭遇只触发一次；剧情裂痕变化只算「要刷新」。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：MirrorCrackRules 是无状态的换算，不记「上一帧是多少、这次遭遇碎过没有」。
//   2. 扩展不行：把状态塞进 MirrorCrackPresenter 就只能在 PlayMode 里验防重入；抽成纯类 EditMode 可穷举（PRP/mirror-core 波 1 定稿 ⑧）。
namespace Game.Mirror
{
    /// <summary>镜裂跟踪器。由 <see cref="MirrorCrackPresenter"/> 每帧喂一次；无分配。</summary>
    public sealed class MirrorCrackTracker
    {
        /// <summary>当前是否在跟踪一场遭遇（上一帧遭遇活动）。</summary>
        public bool Tracking { get; private set; }

        /// <summary>最近一次记下的击中裂痕数。</summary>
        public int HitCracks { get; private set; }

        /// <summary>最近一次记下的剧情裂痕数。</summary>
        public int StoryCracks { get; private set; }

        /// <summary>本次遭遇是否已触发过镜碎。遭遇重新开始时清零。</summary>
        public bool ShatterRaised { get; private set; }

        /// <summary>
        /// 喂一帧。返回值 = 显示需要刷新（进入遭遇、或任一裂痕数变化）。
        /// <paramref name="cracked"/>：击中裂痕比上一帧多（进入遭遇那一帧取基线，不算）。
        /// <paramref name="shattered"/>：本次遭遇首次达到镜碎（进入遭遇时已是三裂也算，例如读到了一份战败时的存档）。
        /// 遭遇不活动时什么都不做（不刷新、不发事件），保持镜碎后的显示直到重开。
        /// </summary>
        public bool Update(bool encounterActive, int hitCracks, int storyCracks, out bool cracked, out bool shattered)
        {
            cracked = false;
            shattered = false;
            if (!encounterActive)
            {
                Tracking = false;
                return false;
            }

            bool changed;
            if (!Tracking)
            {
                Tracking = true;
                ShatterRaised = false;
                changed = true;
            }
            else
            {
                changed = hitCracks != HitCracks || storyCracks != StoryCracks;
                cracked = hitCracks > HitCracks;
            }

            HitCracks = hitCracks;
            StoryCracks = storyCracks;
            if (!ShatterRaised && MirrorCrackRules.IsShattered(hitCracks))
            {
                ShatterRaised = true;
                shattered = true;
            }

            return changed;
        }
    }
}
