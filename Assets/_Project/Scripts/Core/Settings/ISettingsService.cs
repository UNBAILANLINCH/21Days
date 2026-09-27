// 职责：玩家设置（音量 / 显示 / 窗口）的读写与应用契约。
// 为什么新建：roadmap E8 / H8 / H9 要把设置从存档槽里搬出来做成独立档案，并统一「改了怎么生效」；
//   ISaveService 只管落盘、IAudioService 只管音量，都承担不了显示与窗口的应用，工程内没有现成契约可扩展。

using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Save;

namespace Game.Core.Settings
{
    /// <summary>
    /// 设置服务。设置面板的用法：打开时 <see cref="Snapshot"/> 留底 → 直接改 <see cref="Current"/> 的字段
    /// → 按需调 <see cref="ApplyAudio"/>（拖滑条实时听）/ <see cref="ApplyDisplay"/>（点「应用」）
    /// → 确认时 <see cref="SaveAsync"/>，放弃时 <see cref="Restore"/> 回滚。
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>内存中的当前设置，面板直接改它的字段。实例在服务生命周期内不变。</summary>
        SettingsSaveData Current { get; }

        /// <summary>去重、按宽高升序、只留 ≥1280×720 的显示器分辨率；取不到时至少含当前分辨率。</summary>
        IReadOnlyList<DisplayResolution> AvailableResolutions { get; }

        /// <summary>显示器原生分辨率（存档 0×0 时实际用的那一项）。原生分辨率只从这里取，面板不自己读 Unity 显示 API。</summary>
        DisplayResolution NativeResolution { get; }

        /// <summary>把 Current 的显示项应用到 Screen / QualitySettings / Application。</summary>
        void ApplyDisplay();

        /// <summary>把 Current 的三路音量推给 IAudioService（面板拖滑条时实时调）。</summary>
        void ApplyAudio();

        /// <summary>写独立档案 "settings"。</summary>
        UniTask SaveAsync(CancellationToken ct = default);

        /// <summary>深拷贝当前值，面板「返回不应用」时用来回滚。</summary>
        SettingsSaveData Snapshot();

        /// <summary>用快照覆盖 Current 并 ApplyDisplay + ApplyAudio。</summary>
        void Restore(SettingsSaveData snapshot);
    }
}
