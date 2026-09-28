// 职责：镜裂的纯规则——击中裂痕由生命推出并夹到 0..3、作用距离与可见范围随裂痕缩减、三道击中裂痕即镜碎；剧情裂痕只缩范围不致碎。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：PlayerRules 只管生命增减，不知道「裂痕」「作用距离」「可见范围」。
//   2. 扩展不行：塞进 PlayerRules 会改确定性内核（PRP/mirror-core 2.3「不改内核」），且让玩家模块认识镜。
//   击中裂痕不另起计数，直接由 maxHealth − health 推出（maxHealth = 3、attackDamage = 1 时恰好一击一裂，PRP 1 上下文快照）。
using Game.Core.Simulation;

namespace Game.Mirror
{
    /// <summary>镜裂规则。全部静态、无分配；负数裂痕按 0 算。</summary>
    public static class MirrorCrackRules
    {
        /// <summary>击中裂痕上限；到这个数即镜碎。</summary>
        public const int MaxHitCracks = 3;

        /// <summary>击中裂痕 = maxHealth − health，夹到 [0, <see cref="MaxHitCracks"/>]。</summary>
        public static int HitCracks(int maxHealth, int health) =>
            GameMath.Clamp(maxHealth - health, 0, MaxHitCracks);

        /// <summary>作用距离 = baseRange − lossPerCrack × (击中裂痕 + 剧情裂痕)，最低 0。</summary>
        public static float EffectiveRange(float baseRange, float lossPerCrack, int hitCracks, int storyCracks) =>
            Shrink(baseRange, lossPerCrack, hitCracks, storyCracks);

        /// <summary>按配置算作用距离；config 为空（未拖资产）时返回 0（照不到），不抛。</summary>
        public static float EffectiveRange(MirrorConfig config, int hitCracks, int storyCracks)
        {
            // MirrorConfig 是 ScriptableObject，判空只用 == null。
            if (config == null) return 0f;
            return EffectiveRange(config.BaseRange, config.RangeLossPerCrack, hitCracks, storyCracks);
        }

        /// <summary>可见范围 = visionBase − lossPerCrack × (击中裂痕 + 剧情裂痕)，最低 0（画布比例）。</summary>
        public static float VisionRadius(float visionBase, float lossPerCrack, int hitCracks, int storyCracks) =>
            Shrink(visionBase, lossPerCrack, hitCracks, storyCracks);

        /// <summary>按配置算可见范围；config 为空时返回 1（不遮），不抛。</summary>
        public static float VisionRadius(MirrorConfig config, int hitCracks, int storyCracks)
        {
            if (config == null) return 1f;
            return VisionRadius(config.VisionBaseRadius, config.VisionLossPerCrack, hitCracks, storyCracks);
        }

        /// <summary>镜碎：击中裂痕 ≥ <see cref="MaxHitCracks"/>。剧情裂痕不参与。</summary>
        public static bool IsShattered(int hitCracks) => hitCracks >= MaxHitCracks;

        private static float Shrink(float baseValue, float lossPerCrack, int hitCracks, int storyCracks)
        {
            int cracks = GameMath.Max(hitCracks, 0) + GameMath.Max(storyCracks, 0);
            return GameMath.Max(0f, baseValue - GameMath.Max(lossPerCrack, 0f) * cracks);
        }
    }
}
