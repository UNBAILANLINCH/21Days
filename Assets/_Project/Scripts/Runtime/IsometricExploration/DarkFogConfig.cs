// 职责：暗雾原型参数。现有探索配置与镜裂配置不负责空间雾，因此新增独立配置。
using UnityEngine;
using Game.Core.Simulation;

namespace Game.IsometricExploration
{
    [CreateAssetMenu(menuName = "21Days/IsometricExploration/Dark Fog")]
    public sealed class DarkFogConfig : ScriptableObject
    {
        [SerializeField] private Color color = new Color(0.012f, 0.018f, 0.028f, 1f);
        [SerializeField, Range(0f, 1f), Tooltip("归一化雾强度：0 无雾，1 完整雾效果，中间值线性调整。")]
        private float density = 0.65f;
        [SerializeField, Min(0.1f)] private float maximumDistance = 40f;
        [SerializeField, Range(4, 32)] private int steps = 16;
        [SerializeField, Range(0.01f, 1f), Tooltip("减雾区域外缘的过渡比例；内圈清晰，外圈恢复雾。")]
        private float feather = 0.3f;
        [SerializeField, Range(0f, 1f)] private float noiseStrength = 0.2f;
        [SerializeField, Min(0.01f)] private float noiseScale = 0.4f;
        public Color Color => color;
        public float Density => GameMath.Clamp01(density);
        public float MaximumDistance => GameMath.Max(0.1f, maximumDistance);
        public int Steps => GameMath.Clamp(steps, 4, 32);
        public float Feather => GameMath.Clamp(feather, 0.01f, 1f);
        public float NoiseStrength => GameMath.Clamp01(noiseStrength);
        public float NoiseScale => GameMath.Max(0.01f, noiseScale);
    }
}
