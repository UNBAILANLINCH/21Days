// 职责：音量换算——总音量与通道音量怎么合成、线性音量怎么变成 AudioMixer 的分贝。
// 为什么新建：这两个换算是音频里唯一会算错又看不出来的地方（夹取漏了、log(0) 得 -∞），
//   必须能 EditMode 测；写在 AudioService（要建 GameObject、要读存档）里就只能靠耳朵验证。

using UnityEngine;

namespace Game.Core.Audio
{
    /// <summary>
    /// 音量换算。纯函数，不碰任何 Unity 对象与存档。
    /// </summary>
    public static class AudioVolumeMath
    {
        /// <summary>低于这个线性音量就算静音。0.0001 换算成分贝正好是 -80。</summary>
        public const float MinLinear = 0.0001f;

        /// <summary>静音对应的分贝。AudioMixer 的音量参数下限也是 -80。</summary>
        public const float MinDecibels = -80f;

        /// <summary>
        /// 合成有效音量：总音量 × 通道音量，两者各自先夹到 0～1，结果也夹到 0～1。
        /// 存档里的值是玩家改的，越界（负数、大于 1）必须挡在这儿，不能带到 AudioSource 上。
        /// </summary>
        public static float Effective(float master, float channel)
        {
            return Mathf.Clamp01(Mathf.Clamp01(master) * Mathf.Clamp01(channel));
        }

        /// <summary>
        /// 线性音量（0～1）换算成分贝（-80～0）。接了 AudioMixer 之后才用得上——
        /// Mixer 的音量参数是分贝，直接把 0～1 赋进去会得到几乎听不见的结果。
        /// <para>0 → -80（而不是 log10(0) 的负无穷），1 → 0。</para>
        /// </summary>
        public static float ToDecibels(float linear)
        {
            float clamped = Mathf.Clamp01(linear);
            return clamped <= MinLinear ? MinDecibels : Mathf.Log10(clamped) * 20f;
        }
    }
}
