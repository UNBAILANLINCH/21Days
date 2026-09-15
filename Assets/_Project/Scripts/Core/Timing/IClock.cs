// 职责：全框架唯一的时间来源接口，把 UnityEngine.Time 与 DateTime.UtcNow 挡在业务代码之外。
// 为什么新建：工程内还没有任何时间抽象；直接读 Time.time / DateTime.UtcNow 的代码既没法写
// 确定性的 EditMode 测试，将来也没法换成服务器校时（architecture.md 第 7 节的「缝」）。

using System;

namespace Game.Core.Timing
{
    /// <summary>
    /// 时间来源。需要时间的地方一律注入本接口，不直接读 Time / DateTime。
    /// </summary>
    public interface IClock
    {
        /// <summary>UTC 墙上时间，用于存档时间戳、每日刷新这类跨会话判断。</summary>
        DateTime UtcNow { get; }

        /// <summary>受 timeScale 影响的游戏内累计秒数（对应 Time.time）。</summary>
        float GameTime { get; }

        /// <summary>不受 timeScale 影响的累计秒数（对应 Time.unscaledTime）。UI 动效、暂停菜单用它。</summary>
        float UnscaledTime { get; }

        /// <summary>受 timeScale 影响的上一帧时长（对应 Time.deltaTime）。</summary>
        float DeltaTime { get; }

        /// <summary>不受 timeScale 影响的上一帧时长（对应 Time.unscaledDeltaTime）。</summary>
        float UnscaledDeltaTime { get; }
    }
}
