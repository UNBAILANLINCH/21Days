// 职责：正式的登录 / 标题页（当前工程唯一的正式场景界面）——全屏背景 + Logo + 「开始游戏 / 继续 / 选择存档 / 设置 / 退出游戏」
//   五个按钮 + 右下角版本号；只报告「哪个按钮被点了」，不注入服务，去向由 TitleState 决定。
// 美术：背景、Logo 底板、按钮底图都是占位图，功能是正式的；美术在 Art/Sprites/UI/Title/ 下同名替换
//   （ui_title_bg / ui_title_logo / ui_btn_menu_normal）即生效，正式 logo 带字时把 TitleLabel 隐藏。
// 为什么改（不是新建）：原本是跑通 UI 链路的占位面板（标题文字 + 开始按钮），职责一直是「标题界面」，
//   这次把它做成正式页，扩展原文件；Addressables 地址、回放场景点 StartButton 的路径都保持不变。
// 存档会话（PRP save-session D5）：预制体 Buttons 布局组追加「继续」「选择存档」两个按钮（顺序 开始 / 继续 / 选择存档 / 设置 / 退出），
//   这里加两个字段、两个事件与 SetContinueEnabled；两个新字段可空，没接线的旧预制体照常能开。
// 用户反馈（2026-09-26）：没有存档时「继续」置灰仍占一行，改为隐藏——加 SetContinueVisible，存档会话改调它。

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Core.UI.Views
{
    /// <summary>
    /// 标题面板。预制体 <c>Assets/_Project/Prefabs/UI/TitleView.prefab</c>，
    /// Addressables 地址 <c>TitleView</c>（等于类名，UIService 按类名找）。
    /// <para>
    /// 按钮监听在 <see cref="OnOpenAsync"/> 里加、<see cref="OnCloseAsync"/> 里摘——成对，
    /// 不写在 OnEnable/OnDisable：面板被全屏面板（如设置面板）盖住时会 SetActive(false)，那两个回调会重复触发。
    /// </para>
    /// </summary>
    public sealed class TitleView : UIView
    {
        [Tooltip("标题文字（叠在 Logo 占位底板上）。正式 logo 图带字后可隐藏。")]
        [SerializeField] private TMP_Text titleLabel;

        [Tooltip("「开始游戏」按钮；同时应拖到基类的 Default Selected 上，键盘 / 手柄导航从它开始。")]
        [SerializeField] private Button startButton;

        [Tooltip("「继续」按钮（继续最近一次的存档）；没有可用存档时由存档会话调 SetContinueVisible(false) 隐藏。可空。")]
        [SerializeField] private Button continueButton;

        [Tooltip("「选择存档」按钮（打开选槽面板）。可空。")]
        [SerializeField] private Button loadButton;

        [Tooltip("「设置」按钮。")]
        [SerializeField] private Button settingsButton;

        [Tooltip("「退出游戏」按钮；触屏为主的平台（手机）由 TitleState 隐藏。")]
        [SerializeField] private Button quitButton;

        [Tooltip("右下角版本号文字，由代码填 \"v\" + Application.version。")]
        [SerializeField] private TMP_Text versionLabel;

        /// <summary>
        /// 「开始」被点了。面板只报告**按钮被点了**这件事，不知道点完该去哪——
        /// 那是玩法层的决定，由持有本面板的 <see cref="Flow.TitleState"/> 转成
        /// <c>TitleStartClickedEvent</c> 发出去，玩法层订阅后自己切状态。
        /// <para>订阅方自己负责退订；面板关闭时实例会被释放，订阅随之失效。</para>
        /// </summary>
        public event Action OnStartClicked;

        /// <summary>「继续」被点了。由 <see cref="Flow.TitleState"/> 转成 <c>TitleContinueClickedEvent</c>，继续哪个槽由玩法层决定。</summary>
        public event Action OnContinueClicked;

        /// <summary>「选择存档」被点了。由 <see cref="Flow.TitleState"/> 转成 <c>TitleLoadClickedEvent</c>，选槽面板归玩法层。</summary>
        public event Action OnLoadClicked;

        /// <summary>「设置」被点了。由 <see cref="Flow.TitleState"/> 打开设置面板。</summary>
        public event Action OnSettingsClicked;

        /// <summary>「退出游戏」被点了。由 <see cref="Flow.TitleState"/> 调共用的退出实现。</summary>
        public event Action OnQuitClicked;

        /// <summary>标题是主界面，走 Panel 层（全屏，会盖住下面的面板）。</summary>
        public override UILayer Layer => UILayer.Panel;

        /// <summary>标题是流程起点，Esc 关掉它玩家就卡在空屏上，所以不让 UICancelRouter 关。</summary>
        public override bool CloseOnCancel => false;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            Hook(startButton, HandleStartClicked);
            Hook(continueButton, HandleContinueClicked);
            Hook(loadButton, HandleLoadClicked);
            Hook(settingsButton, HandleSettingsClicked);
            Hook(quitButton, HandleQuitClicked);

            OnRefresh();
            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            Unhook(startButton, HandleStartClicked);
            Unhook(continueButton, HandleContinueClicked);
            Unhook(loadButton, HandleLoadClicked);
            Unhook(settingsButton, HandleSettingsClicked);
            Unhook(quitButton, HandleQuitClicked);
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

        /// <summary>
        /// 显示 / 隐藏「继续」按钮（没有可用存档时隐藏）。<c>Buttons</c> 是竖向布局组，隐藏后其余按钮自动上移；
        /// 导航为 Automatic，键盘 / 手柄会跳过隐藏物体。显示时顺带恢复可点，避免残留置灰。按钮没接线时是空操作。
        /// </summary>
        public void SetContinueVisible(bool visible)
        {
            if (continueButton != null)
            {
                continueButton.gameObject.SetActive(visible);
                if (visible) continueButton.interactable = true;
            }
        }

        /// <summary>
        /// 「继续」可不可点（只改 <c>interactable</c>，按钮仍占位）。现在主用 <see cref="SetContinueVisible"/>（没存档时隐藏），
        /// 这个保留给「看得见但暂时不可点」的场合。按钮没接线时是空操作。
        /// </summary>
        public void SetContinueEnabled(bool enabled)
        {
            if (continueButton != null)
            {
                continueButton.interactable = enabled;
            }
        }

        /// <summary>刷新标题与版本号。标题文字暂时写死，接本地化后从本地化表取。</summary>
        public override void OnRefresh()
        {
            if (titleLabel != null)
            {
                titleLabel.text = "21Days";
            }

            if (versionLabel != null)
            {
                versionLabel.text = "v" + Application.version;
            }
        }

        private void HandleStartClicked() => OnStartClicked?.Invoke();

        private void HandleContinueClicked() => OnContinueClicked?.Invoke();

        private void HandleLoadClicked() => OnLoadClicked?.Invoke();

        private void HandleSettingsClicked() => OnSettingsClicked?.Invoke();

        private void HandleQuitClicked() => OnQuitClicked?.Invoke();
    }
}
