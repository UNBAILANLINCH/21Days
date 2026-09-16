// 职责：标题界面的占位面板——一个标题文字 + 一个「开始」按钮，用来把 UI 链路（状态 → 服务 → 预制体 → Addressables）跑通。
// 为什么新建：UIView 是抽象基类，得有一个真实面板把「面板该怎么写」立成范例；
//   工程内没有任何 UIView 子类可复用或扩展。玩法定了之后这个面板会被真正的标题界面替换。

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Core.UI.Views
{
    /// <summary>
    /// 标题面板（占位）。预制体 <c>Assets/_Project/Prefabs/UI/TitleView.prefab</c>，
    /// Addressables 地址 <c>TitleView</c>（等于类名，UIService 按类名找）。
    /// <para>
    /// 按钮监听在 <see cref="OnOpenAsync"/> 里加、<see cref="OnCloseAsync"/> 里摘——成对，
    /// 不写在 OnEnable/OnDisable：面板被全屏面板盖住时会 SetActive(false)，那两个回调会重复触发。
    /// </para>
    /// </summary>
    public sealed class TitleView : UIView
    {
        [Tooltip("标题文字。")]
        [SerializeField] private TMP_Text titleLabel;

        [Tooltip("「开始」按钮。")]
        [SerializeField] private Button startButton;

        /// <summary>
        /// 「开始」被点了。面板只报告**按钮被点了**这件事，不知道点完该去哪——
        /// 那是玩法层的决定，由持有本面板的 <see cref="Flow.TitleState"/> 转成
        /// <c>TitleStartClickedEvent</c> 发出去，玩法层订阅后自己切状态。
        /// <para>订阅方自己负责退订；面板关闭时实例会被释放，订阅随之失效。</para>
        /// </summary>
        public event Action OnStartClicked;

        /// <summary>标题是主界面，走 Panel 层（全屏，会盖住下面的面板）。</summary>
        public override UILayer Layer => UILayer.Panel;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            if (startButton != null)
            {
                // 先 Remove 再 Add：面板被复用打开时（OpenAsync 对已开面板会再调一次 OnOpenAsync）不会叠两份监听。
                startButton.onClick.RemoveListener(HandleStartClicked);
                startButton.onClick.AddListener(HandleStartClicked);
            }

            OnRefresh();
            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            if (startButton != null)
            {
                startButton.onClick.RemoveListener(HandleStartClicked);
            }

            return UniTask.CompletedTask;
        }

        /// <summary>按当前语言 / 版本号刷新标题。现在只是占位，玩法接入后从配置或本地化取。</summary>
        public override void OnRefresh()
        {
            if (titleLabel != null)
            {
                titleLabel.text = "21Days";
            }
        }

        private void HandleStartClicked()
        {
            OnStartClicked?.Invoke();
        }
    }
}
