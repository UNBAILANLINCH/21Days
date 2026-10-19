// 职责：有所有权的音效播放口；独立于不支持停止的短音效接口，不改变旧调用方契约。
using UnityEngine;
namespace Game.Core.Audio
{
    public interface ISfxVoiceService
    {
        // 调用方持有 clip，必须先释放声部再释放资源。
        SfxVoice PlayVoice(AudioClip clip, bool loop, float volume = 1f);
    }
}
