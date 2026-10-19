// 职责：把剧情环境快照应用到已接线的场景对象，并在退出时还原。
// 现有 StoryFlow 负责推进而非渲染，已有可见性组件只管标记显隐；故新增专用表现组件。
// 每场景一个，放在人妖两界分组之外。局部粒子必须预摆、关闭 Play On Awake；不自动找对象或建材质。
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Game.Narrative
{
    [DisallowMultipleComponent]
    public sealed class SpotlightEnvironmentView : MonoBehaviour
    {
        [SerializeField] private SpotlightEnvironmentConfig config;
        [SerializeField] private Light directionalLight;
        [SerializeField] private ParticleSystem snow;
        [SerializeField] private Light altarFire;
        [SerializeField] private ParticleSystem altarFlame;
        [SerializeField] private Light[] candles = Array.Empty<Light>();
        [SerializeField] private ParticleSystem waterMist;

        private LightingSnapshot originalLighting;
        private LightSnapshot originalDirectional;
        private LightSnapshot originalAltar;
        private LightSnapshot[] originalCandles;
        private bool captured;
        private bool globalsApplied;
        private Scene lightingOwner;

        public bool HasApplied { get; private set; }
        public SpotlightEnvironmentRules.State CurrentState { get; private set; }
        public event Action OnInvalidated;

        private void OnEnable()
        {
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            OnInvalidated?.Invoke();
        }

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            Restore();
            OnInvalidated?.Invoke();
        }

        private void OnDestroy() => Restore();

        public void Apply(SpotlightEnvironmentRules.State state)
        {
            if (state.Lighting == SpotlightEnvironmentRules.Preset.None || config == null || directionalLight == null)
            {
                Restore();
                return;
            }
            if (!isActiveAndEnabled) return;
            if (captured && !OwnsActiveSettings()) { Restore(); return; }
            var settings = config.Get(state.Lighting);
            if (settings == null) { Restore(); return; }
            if (HasApplied && SameState(CurrentState, state)) return;
            CaptureOriginals();
            // 只在预设变化时写全局光照；局部效果变化不重刷 RenderSettings。
            if (!HasApplied || CurrentState.Lighting != state.Lighting)
            {
                ApplyLighting(settings);
                globalsApplied = true;
            }
            SetParticles(snow, state.Snow);
            SetParticles(altarFlame, state.AltarFire);
            SetParticles(waterMist, state.WaterMist);
            SetLocalLight(altarFire, state.AltarFire, config.AltarIntensity);
            if (candles != null)
                foreach (var candle in candles) SetLocalLight(candle, state.Candles, config.CandleIntensity);
            CurrentState = state;
            HasApplied = true;
        }

        public void Restore()
        {
            SetParticles(snow, false);
            SetParticles(altarFlame, false);
            SetParticles(waterMist, false);
            if (captured)
            {
                originalDirectional.Restore();
                originalAltar.Restore();
                foreach (var candle in originalCandles) candle.Restore();
                // RenderSettings 随活动场景切换；不能把旧场景的快照覆盖到标题场景。
                // SampleScene 是附加加载，光照所有者可能是 Boot；必须按首次应用时的活动场景还原。
                if (globalsApplied && OwnsActiveSettings())
                {
                    originalLighting.Restore();
                    globalsApplied = false;
                }
            }
            HasApplied = false;
            CurrentState = default;
        }

        private bool OwnsActiveSettings() => lightingOwner.IsValid()
            && lightingOwner == SceneManager.GetActiveScene();

        private void OnActiveSceneChanged(Scene previous, Scene next)
        {
            if (!captured) return;
            if (previous == lightingOwner || (next == lightingOwner && !HasApplied)) Restore();
        }

        private void CaptureOriginals()
        {
            if (captured) return;
            lightingOwner = SceneManager.GetActiveScene();
            originalLighting = new LightingSnapshot();
            originalDirectional = new LightSnapshot(directionalLight);
            originalAltar = new LightSnapshot(altarFire);
            originalCandles = new LightSnapshot[candles == null ? 0 : candles.Length];
            for (int i = 0; i < originalCandles.Length; i++) originalCandles[i] = new LightSnapshot(candles[i]);
            captured = true;
        }

        private void ApplyLighting(SpotlightEnvironmentConfig.LightingSettings settings)
        {
            if (directionalLight != null)
            {
                directionalLight.enabled = true;
                directionalLight.color = settings.LightColor;
                directionalLight.intensity = settings.LightIntensity;
                directionalLight.transform.rotation = Quaternion.Euler(settings.LightEulerAngles);
                RenderSettings.sun = directionalLight;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = settings.AmbientSky;
            RenderSettings.ambientEquatorColor = settings.AmbientEquator;
            RenderSettings.ambientGroundColor = settings.AmbientGround;
            RenderSettings.ambientIntensity = settings.AmbientIntensity;
            RenderSettings.reflectionIntensity = settings.ReflectionIntensity;
            RenderSettings.fog = settings.FogEnabled;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = settings.FogColor;
            RenderSettings.fogStartDistance = settings.FogStart;
            RenderSettings.fogEndDistance = settings.FogEnd;
        }

        private void SetLocalLight(Light light, bool enabled, float intensity)
        {
            if (light == null) return;
            light.enabled = enabled;
            light.color = config.FlameColor;
            light.intensity = intensity;
        }

        private static void SetParticles(ParticleSystem particles, bool enabled)
        {
            if (particles == null) return;
            if (enabled && particles.gameObject.activeInHierarchy)
            {
                var main = particles.main;
                main.useUnscaledTime = true; // 对白会暂停世界，剧情雪／火／雾仍须推进。
                if (!particles.isPlaying) particles.Play(true);
            }
            else if (particles.isPlaying || particles.particleCount > 0)
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private static bool SameState(SpotlightEnvironmentRules.State a, SpotlightEnvironmentRules.State b)
            => a.Lighting == b.Lighting && a.Snow == b.Snow && a.AltarFire == b.AltarFire
                && a.Candles == b.Candles && a.WaterMist == b.WaterMist;

        private readonly struct LightSnapshot
        {
            private readonly Light light;
            private readonly bool enabled;
            private readonly Color color;
            private readonly float intensity;
            private readonly Quaternion rotation;

            internal LightSnapshot(Light source)
            {
                light = source;
                enabled = source != null && source.enabled;
                color = source != null ? source.color : default;
                intensity = source != null ? source.intensity : default;
                rotation = source != null ? source.transform.rotation : Quaternion.identity;
            }

            internal void Restore()
            {
                if (light == null) return;
                light.enabled = enabled;
                light.color = color;
                light.intensity = intensity;
                light.transform.rotation = rotation;
            }
        }

        private sealed class LightingSnapshot
        {
            private readonly AmbientMode ambientMode = RenderSettings.ambientMode;
            private readonly Color ambientLight = RenderSettings.ambientLight;
            private readonly Color sky = RenderSettings.ambientSkyColor;
            private readonly Color equator = RenderSettings.ambientEquatorColor;
            private readonly Color ground = RenderSettings.ambientGroundColor;
            private readonly float ambientIntensity = RenderSettings.ambientIntensity;
            private readonly float reflectionIntensity = RenderSettings.reflectionIntensity;
            private readonly Material skybox = RenderSettings.skybox;
            private readonly Light sun = RenderSettings.sun;
            private readonly bool fog = RenderSettings.fog;
            private readonly FogMode fogMode = RenderSettings.fogMode;
            private readonly Color fogColor = RenderSettings.fogColor;
            private readonly float fogDensity = RenderSettings.fogDensity;
            private readonly float fogStart = RenderSettings.fogStartDistance;
            private readonly float fogEnd = RenderSettings.fogEndDistance;

            internal void Restore()
            {
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambientLight;
                RenderSettings.ambientSkyColor = sky;
                RenderSettings.ambientEquatorColor = equator;
                RenderSettings.ambientGroundColor = ground;
                RenderSettings.ambientIntensity = ambientIntensity;
                RenderSettings.reflectionIntensity = reflectionIntensity;
                RenderSettings.skybox = skybox;
                RenderSettings.sun = sun;
                RenderSettings.fog = fog;
                RenderSettings.fogMode = fogMode;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogDensity = fogDensity;
                RenderSettings.fogStartDistance = fogStart;
                RenderSettings.fogEndDistance = fogEnd;
            }
        }
    }
}
