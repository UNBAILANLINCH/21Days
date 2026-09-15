// 职责：设置分区——音量三档与语言，第一个真实的 ISaveData 实现。
// 为什么新建：ISaveData 只是接口，得有一个真实分区把「分区怎么写」立成范例，并且波 3 的 IAudioService
// 要从这里读音量；没有现成的数据类能承担设置的职责。

namespace Game.Core.Save
{
    /// <summary>
    /// 设置分区。音量是 0～1 的线性值（波 3 的 <c>IAudioService</c> 负责换算成 AudioMixer 的分贝）。
    /// <para>
    /// 这是**存档分区**不是 ScriptableObject 配置：玩家改得动的才放这儿，
    /// 策划配的数值放 <c>Assets/_Project/Data/</c>（后缀规则见 csharp-code.md）。
    /// </para>
    /// </summary>
    public sealed class SettingsSaveData : ISaveData
    {
        /// <summary>分区版本。改字段语义时加一，并在 <see cref="Migrate"/> 里处理。</summary>
        public int Version => 1;

        /// <summary>总音量，0～1。</summary>
        public float MasterVolume { get; set; } = 1f;

        /// <summary>背景音乐音量，0～1。</summary>
        public float BgmVolume { get; set; } = 1f;

        /// <summary>音效音量，0～1。</summary>
        public float SfxVolume { get; set; } = 1f;

        /// <summary>语言标签（BCP 47），默认简体中文。</summary>
        public string Language { get; set; } = "zh-CN";

        /// <summary>目前只有版本 1，没有要迁的东西。加版本时在这里按 fromVersion 逐级处理。</summary>
        public void Migrate(int fromVersion)
        {
        }
    }
}
