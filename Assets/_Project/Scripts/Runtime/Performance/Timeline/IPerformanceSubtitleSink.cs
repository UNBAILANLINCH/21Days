// 职责：字幕轨道的输出端契约——时间轴只说「现在该显示哪句 / 该收起」，由谁画出来它不关心。
// 为什么新建（复用 → 扩展 → 新建）：字幕轨道在 Timeline 图里运行，拿不到容器也不该认识具体 UI 面板；
//   用接口把「时间轴 → 面板」这条线断开，编辑器预览时没有面板也能静默运行。工程里没有现成的字幕输出抽象。

using UnityEngine;

namespace Game.Performance.Timeline
{
    /// <summary>字幕输出端。运行时由 <see cref="PerformanceView"/> 实现，经 <see cref="PerformanceStage.SetSubtitleSink"/> 挂到舞台上。</summary>
    public interface IPerformanceSubtitleSink
    {
        /// <summary>
        /// 显示一句字幕。<paramref name="speaker"/> 可为空串（旁白）；<paramref name="avatar"/> 为 null 表示不显示头像；
        /// <paramref name="side"/> 是头像显示在面板哪一侧（两者都由字幕轨道按 <see cref="PerformanceStage.TryGetAvatar"/> 从舞台演员名单取）。
        /// </summary>
        void ShowSubtitle(string speaker, string text, Sprite avatar, PerformanceAvatarSide side);

        /// <summary>收起字幕（当前没有活动的字幕片段）。</summary>
        void HideSubtitle();
    }
}
