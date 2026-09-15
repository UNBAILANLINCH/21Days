// 职责：音频系统的可调数值——AudioMixer（可空）、SFX 声部数、BGM 默认淡入淡出时长。
// 为什么新建：这几个值要在 Inspector 上调且随平台不同（低端机少开几个声部），
//   按 csharp-code.md「数值配置进 ScriptableObject」不能写死在 AudioService 里；没有现成配置资产可扩展。

using UnityEngine;
using UnityEngine.Audio;

namespace Game.Core.Audio
{
    /// <summary>
    /// 音频配置。资产在 <c>Assets/_Project/Data/Audio/AudioConfig.asset</c>，拖到 Boot 场景的 GameLifetimeScope 上。
    /// <para>
    /// <see cref="Mixer"/> **可以留空**：Unity 没有公开 API 从代码创建 AudioMixer 资产，
    /// 所以本波先用「AudioSource 音量相乘」实现三路音量（Master × 通道）。
    /// 将来手工建好 Mixer 资产、拖到这个字段上，<see cref="AudioVolumeMath"/> 才会走分贝换算那条路。
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "AudioConfig", menuName = "21Days/Core/Audio Config")]
    public sealed class AudioConfig : ScriptableObject
    {
        [Header("混音器（可空）")]
        [Tooltip("留空则用 AudioSource 音量相乘实现三路音量；接了 Mixer 才走分贝换算。")]
        [SerializeField] private AudioMixer mixer;

        [Header("声部与淡入淡出")]
        [Tooltip("SFX 的 AudioSource 池大小。同时最多这么多个音效在响，用满了复用最早的那个。")]
        [Min(1)]
        [SerializeField] private int sfxVoices = 8;

        [Tooltip("PlayBgmAsync / StopBgm 传负数时用的默认淡入淡出秒数。")]
        [Min(0f)]
        [SerializeField] private float defaultBgmFadeSeconds = 0.5f;

        /// <summary>混音器，可空。</summary>
        public AudioMixer Mixer => mixer;

        /// <summary>SFX 声部数。</summary>
        public int SfxVoices => sfxVoices;

        /// <summary>BGM 默认淡入淡出秒数。</summary>
        public float DefaultBgmFadeSeconds => defaultBgmFadeSeconds;
    }
}
