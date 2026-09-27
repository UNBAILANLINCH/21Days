// 职责：暂停菜单面板——半透明遮罩铺满 + 继续 / 设置 / 回标题 / 退出游戏四个按钮；只抛事件，不注入服务。
// 为什么新建：复用——TitleView 只有一个「开始」按钮且 CloseOnCancel 为 false，职责是流程起点，不是游戏中途的暂停；
//   扩展——往 TitleView 里塞四个按钮会让「标题」和「暂停」两种生命周期（一个随 TitleState 开关、
//   一个随 Esc / P 开关并持有暂停令牌）混在一个类里，名字和职责都说不通。所以单独一个面板（roadmap E5）。

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Core.UI.Views
{
    /// <summary>
    /// 暂停菜单。预制体 <c>Assets/_Project/Prefabs/UI/PauseMenuView.prefab</c>，Addressables 地址 <c>PauseMenuView</c>。
    /// <para>
    /// 会话（暂停令牌、输入图、打开条件）全在 <see cref="PauseMenuController"/>；本面板只报告「哪个按钮被点了」，
    /// 以及「我被关了」（<see cref="OnClosed"/>：Esc 由 <see cref="UICancelRouter"/> 从外部关掉时，控制器靠它收尾）。
    /// </para>
    /// </summary>
    public sealed class PauseMenuView : UIView
    {
        [Tooltip("「继续」按钮；同时应拖到基类的 Default Selected 上，键盘 / 手柄导航从它开始。")]
        [SerializeField] private Button resumeButton;

        [Tooltip("「设置」按钮。")]
        [SerializeField] private Button settingsButton;

        [Tooltip("「回标题」按钮。")]
        [SerializeField] private Button titleButton;

        [Tooltip("「退出游戏」按钮；触屏为主的平台（手机）由控制器隐藏。")]
        [SerializeField] private Button quitButton;

        /// <summary>「继续」被点。</summary>
        public event Action OnResume;

        /// <summary>「设置」被点。</summary>
        public event Action OnSettings;

        /// <summary>「回标题」被点。</summary>
        public event Action OnTitle;

        /// <summary>「退出游戏」被点。</summary>
        public event Action OnQuit;

        /// <summary>面板已被关闭（不论谁关的），在 <see cref="OnCloseAsync"/> 里触发一次。</summary>
        public event Action OnClosed;

        public override UILayer Layer => UILayer.Panel;

        /// <summary>半透明遮罩铺满整屏，压栈时把下面的 Panel 隐藏。</summary>
        public override bool IsFullScreen => true;

        /// <summary>Esc / 手柄 B 由通用路由关掉，等同「继续」。</summary>
        public override bool CloseOnCancel => true;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            // 先 Remove 再 Add：OpenAsync 对已开面板会再调一次 OnOpenAsync，不叠两份监听。
            Hook(resumeButton, RaiseResume);
            Hook(settingsButton, RaiseSettings);
            Hook(titleButton, RaiseTitle);
            Hook(quitButton, RaiseQuit);
            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            Unhook(resumeButton, RaiseResume);
            Unhook(settingsButton, RaiseSettings);
            Unhook(titleButton, RaiseTitle);
            Unhook(quitButton, RaiseQuit);

            // 面板可能被 UIService 从外部关掉（Esc 走 CloseTopAsync），通知持有者收尾暂停令牌与输入图。
            Action closed = OnClosed;
            OnClosed = null;
            OnResume = null;
            OnSettings = null;
            OnTitle = null;
            OnQuit = null;
            closed?.Invoke();
            return UniTask.CompletedTask;
        }

        /// <summary>显示 / 隐藏「退出游戏」按钮（手机上系统有返回桌面，不给退出按钮）。</summary>
        public void SetQuitVisible(bool visible)
        {
            if (quitButton != null)
            {
                quitButton.gameObject.SetActive(visible);
            }
        }

        private void RaiseResume() => OnResume?.Invoke();

        private void RaiseSettings() => OnSettings?.Invoke();

        private void RaiseTitle() => OnTitle?.Invoke();

        private void RaiseQuit() => OnQuit?.Invoke();
    }
}
