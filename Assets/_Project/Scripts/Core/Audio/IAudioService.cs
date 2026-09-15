// 职责：音频播放与三路音量的唯一入口契约。
// 为什么新建：architecture.md 5.8 定义了这个契约；实现与契约分开放（将来换 FMOD 只换实现）。

using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Game.Core.Audio
{
    /// <summary>
    /// 音频服务。玩法要出声只走这里，不自己建 AudioSource、不自己 <c>AudioSource.PlayClipAtPoint</c>。
    /// <para>
    /// 三路音量：<see cref="MasterVolume"/> 乘在另外两路之上，都是 0～1 的线性值。
    /// setter 会**立刻生效并写回存档分区** <c>SettingsSaveData</c>，但**不落盘**——
    /// 落盘由设置界面在关闭时调 <c>ISaveService.SaveAsync</c>，免得拖动音量滑块时每帧写一次文件。
    /// </para>
    /// </summary>
    public interface IAudioService
    {
        /// <summary>总音量，0～1。越界的值会被夹住。</summary>
        float MasterVolume { get; set; }

        /// <summary>背景音乐音量，0～1。</summary>
        float BgmVolume { get; set; }

        /// <summary>音效音量，0～1。</summary>
        float SfxVolume { get; set; }

        /// <summary>
        /// 播一个已经拿在手上的音效（走 SFX 声部池）。
        /// <paramref name="volume"/> 是这一次的相对音量，最终音量 = 它 × Master × Sfx。
        /// </summary>
        void PlaySfx(AudioClip clip, float volume = 1f);

        /// <summary>
        /// 按 Addressables 地址播一个音效：内部用 <c>IAssetService</c> 加载，播完自动释放句柄。
        /// 返回的 UniTask 在音效播完（或服务销毁）时结束——不关心什么时候播完就直接 <c>.Forget()</c>。
        /// <para>
        /// <paramref name="ct"/> **只管加载阶段**：取消它等于「别再加载了」。
        /// 音效一旦开始响就与调用方脱钩，等它播完再释放句柄这一段只跟着音频服务自己的生命周期走。
        /// 这样「放个音效然后马上切状态」不会把声音掐成半截，也不会提前释放还在播的 clip。
        /// </para>
        /// </summary>
        UniTask PlaySfxAsync(string key, float volume = 1f, CancellationToken ct = default);

        /// <summary>
        /// 按 Addressables 地址切背景音乐。旧曲淡出、新曲淡入；
        /// <paramref name="fadeSeconds"/> 传 0 直接切，传负数用 <c>AudioConfig.DefaultBgmFadeSeconds</c>。
        /// 已经在放同一首时是空操作。
        /// <para>
        /// **只有一路 BGM，所以永远是「最后一次请求说了算」**：还没切完又来一次切曲或 <see cref="StopBgm"/>，
        /// 先来的那次会在下一个等待点悄悄放弃（把自己加载出来的资源还回去），不会去动新请求正在放的曲子。
        /// 被放弃的那次返回的 UniTask 正常结束，不抛异常——连点切曲不是错误。
        /// </para>
        /// </summary>
        UniTask PlayBgmAsync(string key, float fadeSeconds = 0.5f, CancellationToken ct = default);

        /// <summary>停背景音乐并释放它的资源句柄。<paramref name="fadeSeconds"/> 语义同上。不等待淡出完成。</summary>
        void StopBgm(float fadeSeconds = 0.5f);
    }
}
