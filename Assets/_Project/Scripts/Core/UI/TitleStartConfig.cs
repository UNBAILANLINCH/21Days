// 职责：点击开始后的镜转、水波、模糊和速度线节拍。
// 新建原因：入场轨道没有全屏转场参数；全局 UIConfig 只管面板淡入，不承载标题专用效果。
using UnityEngine;

namespace Game.Core.UI
{
    [CreateAssetMenu(menuName = "21Days/UI/Title Start")]
    public sealed class TitleStartConfig : ScriptableObject
    {
        [SerializeField, Min(0.01f)] private float mirrorSeconds = 1.15f;
        [SerializeField] private float mirrorDegrees = -32f;
        [SerializeField, Min(0.01f)] private float waveSeconds = 2.2f;
        [SerializeField, Min(0.01f)] private float loadMirrorSeconds = 0.35f;
        [SerializeField, Min(0.01f)] private float loadWaveSeconds = 1.2f;
        [SerializeField, Range(0f, 0.1f)] private float waveStrength = 0.024f;
        [SerializeField, Range(0f, 0.1f)] private float blurStrength = 0.018f;
        [SerializeField, Range(0f, 1f)] private float lineStrength = 0.3f;

        public float MirrorSeconds => mirrorSeconds;
        public float MirrorDegrees => mirrorDegrees;
        public float WaveSeconds => waveSeconds;
        public float LoadMirrorSeconds => loadMirrorSeconds;
        public float LoadWaveSeconds => loadWaveSeconds;
        public float WaveStrength => waveStrength;
        public float BlurStrength => blurStrength;
        public float LineStrength => lineStrength;
    }
}
