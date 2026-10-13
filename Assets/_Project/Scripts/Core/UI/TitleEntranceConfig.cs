// 职责：标题入场各部件的节拍、运动曲线与落定后的呼吸幅度。
// 为什么新建：UIConfig 的整面板过渡没有部件时间轴；将标题专用轨道塞进全局配置会混淆职责。
using System;
using UnityEngine;

namespace Game.Core.UI
{
    [CreateAssetMenu(menuName = "21Days/UI/Title Entrance")]
    public sealed class TitleEntranceConfig : ScriptableObject
    {
        [SerializeField] private TrackSettings[] tracks = Array.Empty<TrackSettings>();
        public TrackSettings[] Tracks => tracks;

        [Serializable]
        public sealed class TrackSettings
        {
            [SerializeField] private string part;
            [SerializeField, Min(0f)] private float delay;
            [SerializeField, Min(0.01f)] private float duration = 1f;
            [SerializeField, Min(0.01f)] private float fadeSeconds = 0.6f;
            [SerializeField] private Vector2 offset;
            [SerializeField, Min(0.01f)] private float scale = 1f;
            [SerializeField] private float rotation;
            [SerializeField] private AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            [SerializeField] private int revealDirection;
            [SerializeField] private Vector2 breathOffset;
            [SerializeField] private float breathScale;
            [SerializeField, Min(0.1f)] private float breathSeconds = 4f;

            public string Part => part;
            public float Delay => delay;
            public float Duration => duration;
            public float FadeSeconds => fadeSeconds;
            public Vector2 Offset => offset;
            public float Scale => scale;
            public float Rotation => rotation;
            public AnimationCurve Curve => curve;
            public int RevealDirection => revealDirection;
            public Vector2 BreathOffset => breathOffset;
            public float BreathScale => breathScale;
            public float BreathSeconds => breathSeconds;
        }
    }
}
