// 职责：前三阶段的固定光照与局部光源参数；不运行时钟，也不修改剧情。
// 现有 StoryConfig 只管交互提示，无法复用光照参数；独立配置避免扩大它的职责。
using System;
using UnityEngine;

namespace Game.Narrative
{
    [CreateAssetMenu(menuName = "21Days/Narrative/Spotlight Environment")]
    public sealed class SpotlightEnvironmentConfig : ScriptableObject
    {
        [Serializable]
        public sealed class LightingSettings
        {
            [SerializeField] private Color lightColor;
            [SerializeField, Min(0f)] private float lightIntensity;
            [SerializeField] private Vector3 lightEulerAngles;
            [SerializeField] private Color ambientSky;
            [SerializeField] private Color ambientEquator;
            [SerializeField] private Color ambientGround;
            [SerializeField, Min(0f)] private float ambientIntensity = 1f;
            [SerializeField, Min(0f)] private float reflectionIntensity;
            [SerializeField] private bool fogEnabled = true;
            [SerializeField] private Color fogColor;
            [SerializeField, Min(0f)] private float fogStart = 24f;
            [SerializeField, Min(0f)] private float fogEnd = 100f;

            public Color LightColor => lightColor;
            public float LightIntensity => lightIntensity;
            public Vector3 LightEulerAngles => lightEulerAngles;
            public Color AmbientSky => ambientSky;
            public Color AmbientEquator => ambientEquator;
            public Color AmbientGround => ambientGround;
            public float AmbientIntensity => ambientIntensity;
            public float ReflectionIntensity => reflectionIntensity;
            public bool FogEnabled => fogEnabled;
            public Color FogColor => fogColor;
            public float FogStart => fogStart;
            public float FogEnd => fogEnd < fogStart ? fogStart : fogEnd;

            public LightingSettings(Color light, float intensity, Vector3 angles,
                Color sky, Color equator, Color ground, float reflection, Color fog)
            {
                lightColor = light;
                lightIntensity = intensity;
                lightEulerAngles = angles;
                ambientSky = sky;
                ambientEquator = equator;
                ambientGround = ground;
                reflectionIntensity = reflection;
                fogColor = fog;
            }
        }

        [SerializeField] private LightingSettings dawn = new LightingSettings(
            new Color(1f, .79f, .61f), .85f, new Vector3(22f, -35f, 0f),
            new Color(.62f, .66f, .77f), new Color(.51f, .44f, .43f),
            new Color(.24f, .25f, .30f), .55f, new Color(.72f, .72f, .78f));
        [SerializeField] private LightingSettings day = new LightingSettings(
            new Color(1f, .96f, .87f), 1.15f, new Vector3(52f, -35f, 0f),
            new Color(.65f, .76f, .89f), new Color(.58f, .62f, .66f),
            new Color(.30f, .32f, .35f), .8f, new Color(.72f, .81f, .87f));
        [SerializeField] private LightingSettings moonNight = new LightingSettings(
            new Color(.60f, .72f, 1f), .35f, new Vector3(35f, 135f, 0f),
            new Color(.20f, .27f, .43f), new Color(.16f, .20f, .31f),
            new Color(.09f, .11f, .18f), .25f, new Color(.16f, .21f, .32f));
        [SerializeField] private Color flameColor = new Color(1f, .53f, .20f);
        [SerializeField, Min(0f)] private float altarIntensity = 2.2f;
        [SerializeField, Min(0f)] private float candleIntensity = .8f;

        public Color FlameColor => flameColor;
        public float AltarIntensity => altarIntensity;
        public float CandleIntensity => candleIntensity;

        public LightingSettings Get(SpotlightEnvironmentRules.Preset preset)
        {
            switch (preset)
            {
                case SpotlightEnvironmentRules.Preset.Dawn: return dawn;
                case SpotlightEnvironmentRules.Preset.Day: return day;
                case SpotlightEnvironmentRules.Preset.MoonNight: return moonNight;
                default: return null;
            }
        }
    }
}
