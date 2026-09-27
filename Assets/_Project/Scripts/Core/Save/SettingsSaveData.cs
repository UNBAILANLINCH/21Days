// 职责：设置档案（跨存档槽）——音量三档、语言、显示（分辨率 / 全屏模式 / 垂直同步 / 帧率上限）。
// 为什么新建：ISaveData 只是接口，得有一个真实的数据类承载「玩家改得动的设置」；
//   版本 2 起它不再进存档槽，而是由 SettingsService 读写独立档案 "settings"（ISaveService.ReadProfileAsync），
//   换档、删档都不影响设置。仍实现 ISaveData 是为了保留 Version / Migrate 的迁移约定。

namespace Game.Core.Save
{
    /// <summary>
    /// 设置档案（跨存档槽）。音量是 0～1 的线性值（<c>IAudioService</c> 负责换算成 AudioMixer 的分贝）。
    /// <para>
    /// 这是**玩家档案**不是 ScriptableObject 配置：玩家改得动的才放这儿，
    /// 策划配的数值放 <c>Assets/_Project/Data/</c>（后缀规则见 csharp-code.md）。
    /// 读写只走 <c>ISettingsService</c>，不要再 <c>ISaveService.Get&lt;SettingsSaveData&gt;()</c>。
    /// </para>
    /// </summary>
    public sealed class SettingsSaveData : ISaveData
    {
        /// <summary>全屏模式取值：无边框全屏（默认）。</summary>
        public const int FullScreenModeBorderless = 0;

        /// <summary>全屏模式取值：窗口化（可拖拽缩放）。</summary>
        public const int FullScreenModeWindowed = 1;

        /// <summary>
        /// 分区版本。改字段语义时加一，并在 <see cref="Migrate"/> 里处理。
        /// 版本 2（2026-09-26）：新增显示五项，设置从存档槽搬到独立档案。
        /// </summary>
        public int Version => 2;

        /// <summary>总音量，0～1。</summary>
        public float MasterVolume { get; set; } = 1f;

        /// <summary>背景音乐音量，0～1。</summary>
        public float BgmVolume { get; set; } = 1f;

        /// <summary>音效音量，0～1。</summary>
        public float SfxVolume { get; set; } = 1f;

        /// <summary>语言标签（BCP 47），默认简体中文。</summary>
        public string Language { get; set; } = "zh-CN";

        /// <summary>分辨率宽（像素）。0 = 原生（跟随显示器当前分辨率）。</summary>
        public int ResolutionWidth { get; set; }

        /// <summary>分辨率高（像素）。0 = 原生（跟随显示器当前分辨率）。</summary>
        public int ResolutionHeight { get; set; }

        /// <summary>全屏模式：0 = 无边框全屏（默认），1 = 窗口化。见 <see cref="FullScreenModeBorderless"/>。</summary>
        public int FullScreenMode { get; set; } = FullScreenModeBorderless;

        /// <summary>垂直同步，默认开。开着时帧率上限不生效。</summary>
        public bool VSync { get; set; } = true;

        /// <summary>帧率上限。0 = 不限；只允许 0 / 30 / 60 / 120 / 144 / 240（见 DisplaySettingsMath）。</summary>
        public int TargetFrameRate { get; set; }

        /// <summary>
        /// 按 fromVersion 逐级迁移。v1 → v2：补上显示五项的默认值（原生分辨率、无边框全屏、垂直同步开、帧率不限）。
        /// </summary>
        public void Migrate(int fromVersion)
        {
            if (fromVersion < 2)
            {
                ResolutionWidth = 0;
                ResolutionHeight = 0;
                FullScreenMode = FullScreenModeBorderless;
                VSync = true;
                TargetFrameRate = 0;
            }
        }

        /// <summary>深拷贝一份（全是值类型与不可变字符串，逐字段复制即可）。</summary>
        public SettingsSaveData Clone()
        {
            SettingsSaveData copy = new SettingsSaveData();
            copy.CopyFrom(this);
            return copy;
        }

        /// <summary>
        /// 用另一份的值覆盖自己（原地改，不换实例）：音频服务等持有者拿的是同一个对象，换实例会让它们读到旧值。
        /// </summary>
        public void CopyFrom(SettingsSaveData other)
        {
            if (other == null)
            {
                throw new System.ArgumentNullException(nameof(other));
            }

            MasterVolume = other.MasterVolume;
            BgmVolume = other.BgmVolume;
            SfxVolume = other.SfxVolume;
            Language = other.Language;
            ResolutionWidth = other.ResolutionWidth;
            ResolutionHeight = other.ResolutionHeight;
            FullScreenMode = other.FullScreenMode;
            VSync = other.VSync;
            TargetFrameRate = other.TargetFrameRate;
        }
    }
}
