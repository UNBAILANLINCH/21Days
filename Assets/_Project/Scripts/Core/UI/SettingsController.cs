// 职责：设置面板的会话控制——开面板、把面板事件转给 SettingsEditSession、「应用」存盘、「返回」/ Esc / 外部关闭时按未应用回滚。
// 为什么新建：SettingsView 只显示不注入服务；ISettingsService 是纯数据 + 应用入口，不该依赖 UI；
//   暂停菜单、将来的标题「设置」按钮都要一行调用打开设置，所以单独一个根作用域单例（roadmap E3）。

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Logging;
using Game.Core.Platform;
using Game.Core.Save;
using Game.Core.Settings;
using Game.Core.Telemetry;
using Game.Core.UI.Views;

namespace Game.Core.UI
{
    /// <summary>
    /// 设置面板控制器（根作用域单例，不是入口点：没人调 <see cref="OpenAsync"/> 就什么都不做）。
    /// <para>回滚规则见 <see cref="SettingsEditSession"/>。</para>
    /// </summary>
    public sealed class SettingsController : IDisposable
    {
        private const string AppliedEvent = "settings_applied";
        private const string ClosedEvent = "settings_closed";
        private const string ReasonBack = "back";
        private const string ReasonExternal = "external";
        private const string ReasonDispose = "dispose";

        private readonly IUIService ui;
        private readonly ISettingsService settings;
        private readonly IPlatformService platform;
        private readonly ITelemetryScope telemetry;
        private readonly SettingsEditSession session;

        private SettingsView view;
        private bool opening;
        private bool closing;
        private bool applying;
        private bool disposed;

        /// <param name="telemetry">可为 null（EditMode 测试），此时埋点为空操作。</param>
        public SettingsController(IUIService ui, ISettingsService settings, IPlatformService platform,
            ITelemetryService telemetry)
        {
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
            this.telemetry = telemetry == null
                ? (ITelemetryScope)NullTelemetryScope.Instance
                : telemetry.Scope(TelemetryKeys.Ui);
            session = new SettingsEditSession(settings);
        }

        /// <summary>设置面板是否开着。</summary>
        public bool IsOpen => view != null;

        /// <summary>打开设置面板：取回滚点 → 开面板 → 摆上当前值。已开或正在开时直接返回。</summary>
        public async UniTask OpenAsync(CancellationToken ct = default)
        {
            if (disposed || view != null || opening || closing) return;
            opening = true;
            try
            {
                session.Begin();
                SettingsView opened;
                try
                {
                    opened = await ui.OpenAsync<SettingsView>(ct: ct);
                }
                catch
                {
                    session.End();
                    throw;
                }

                // await 期间 Dispose 可能已执行：不收尾就会留下没人关的面板。
                if (disposed)
                {
                    session.End();
                    await ui.CloseAsync(opened);
                    return;
                }

                view = opened;
                view.Bind(settings.Current, settings.AvailableResolutions, settings.NativeResolution);
                view.SetDisplaySectionVisible(!platform.IsTouchPrimary);

                view.OnMasterVolumeChanged += session.SetMasterVolume;
                view.OnBgmVolumeChanged += session.SetBgmVolume;
                view.OnSfxVolumeChanged += session.SetSfxVolume;
                view.OnResolutionChanged += HandleResolutionChanged;
                view.OnFullScreenModeChanged += session.SetFullScreenMode;
                view.OnVSyncChanged += session.SetVSync;
                view.OnFrameRateChanged += session.SetTargetFrameRate;
                view.OnApply += HandleApply;
                view.OnBack += HandleBack;
                view.OnClosed += HandleViewClosed;
            }
            finally
            {
                opening = false;
            }
        }

        /// <summary>关闭设置面板（等同「返回」：未应用的改动回滚）。没开时空操作。</summary>
        public async UniTask CloseAsync()
        {
            if (view == null || closing) return;
            closing = true;
            SettingsView target = view;
            Detach(target);
            try
            {
                await ui.CloseAsync(target);
            }
            finally
            {
                EndSession(ReasonBack);
                closing = false;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            SettingsView target = view;
            if (target == null) return;
            Detach(target);
            EndSession(ReasonDispose);
            CloseViewAsync(target).Forget();
        }

        private void HandleResolutionChanged(DisplayResolution resolution)
        {
            session.SetResolution(resolution.Width, resolution.Height);
        }

        private void HandleApply() => ApplyAsync().Forget();

        private async UniTaskVoid ApplyAsync()
        {
            if (applying) return;
            applying = true;
            try
            {
                await session.ApplyAsync();
                TrackApplied();
            }
            catch (Exception e)
            {
                telemetry.TrackError(AppliedEvent, e);
                Log.Error($"SettingsController：应用设置失败：{e}");
            }
            finally
            {
                applying = false;
            }
        }

        // 埋一条 settings_applied：玩家最终选了什么，排查「换了分辨率就黑屏 / 掉帧」时直接对得上。
        private void TrackApplied()
        {
            SettingsSaveData current = settings.Current;
            telemetry.Track(
                AppliedEvent,
                ("res", current.ResolutionWidth + "x" + current.ResolutionHeight),
                ("mode", current.FullScreenMode),
                ("vsync", current.VSync),
                ("fps", current.TargetFrameRate));
        }

        private void HandleBack() => CloseRequestedAsync().Forget();

        private async UniTaskVoid CloseRequestedAsync()
        {
            try
            {
                await CloseAsync();
            }
            catch (Exception e)
            {
                Log.Error($"SettingsController：关闭设置面板失败：{e}");
            }
        }

        // 面板被 UIService 从外部关掉（Esc 走 CloseTopAsync）：只收尾会话，不再调 ui.CloseAsync。
        private void HandleViewClosed()
        {
            if (closing || view == null) return;
            Detach(view);
            EndSession(ReasonExternal);
        }

        private void Detach(SettingsView target)
        {
            target.OnMasterVolumeChanged -= session.SetMasterVolume;
            target.OnBgmVolumeChanged -= session.SetBgmVolume;
            target.OnSfxVolumeChanged -= session.SetSfxVolume;
            target.OnResolutionChanged -= HandleResolutionChanged;
            target.OnFullScreenModeChanged -= session.SetFullScreenMode;
            target.OnVSyncChanged -= session.SetVSync;
            target.OnFrameRateChanged -= session.SetTargetFrameRate;
            target.OnApply -= HandleApply;
            target.OnBack -= HandleBack;
            target.OnClosed -= HandleViewClosed;
        }

        // 会话收尾（同步、幂等）：未应用则回滚（音量一并回滚）。
        private void EndSession(string reason)
        {
            if (!session.IsActive) return;
            bool reverted = session.End();
            view = null;
            telemetry.Track(ClosedEvent, (TelemetryKeys.Props.Reason, reason), ("reverted", reverted));
        }

        // 作用域销毁时 UIService 往往已先释放，关面板会抛 ObjectDisposedException——面板已随 UIRoot 销毁，静默即可。
        private async UniTaskVoid CloseViewAsync(SettingsView target)
        {
            try
            {
                await ui.CloseAsync(target);
            }
            catch (ObjectDisposedException)
            {
                // 见方法注释。
            }
            catch (Exception e)
            {
                Log.Warn($"SettingsController：关闭设置面板失败：{e.Message}");
            }
        }
    }
}
