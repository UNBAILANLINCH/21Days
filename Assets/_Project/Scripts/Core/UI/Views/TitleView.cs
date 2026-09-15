// 职责：标题界面的占位面板——一个标题文字 + 一个「开始」按钮，用来把 UI 链路（状态 → 服务 → 预制体 → Addressables）跑通。
// 为什么新建：UIView 是抽象基类，得有一个真实面板把「面板该怎么写」立成范例；
//   工程内没有任何 UIView 子类可复用或扩展。玩法定了之后这个面板会被真正的标题界面替换。

using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Logging;
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

        /// <summary>标题是主界面，走 Panel 层（全屏，会盖住下面的面板）。</summary>
        public override UILayer Layer => UILayer.Panel;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            if (startButton != null)
            {
                // 先 Remove 再 Add：面板被复用打开时（OpenAsync 对已开面板会再调一次 OnOpenAsync）不会叠两份监听。
                startButton.onClick.RemoveListener(OnStartClicked);
                startButton.onClick.AddListener(OnStartClicked);
            }

            OnRefresh();
            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            if (startButton != null)
            {
                startButton.onClick.RemoveListener(OnStartClicked);
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

        private void OnStartClicked()
        {
            Log.Info("开始：玩法状态待接入");
        }
    }
}
