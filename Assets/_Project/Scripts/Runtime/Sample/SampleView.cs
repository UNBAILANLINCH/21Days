// 职责：示例模块的面板——显示规则类算出来的一行文字 + 一个「返回标题」按钮。
// 为什么新建：UIView 是抽象基类；Core 里的 TitleView 是框架自带的标题面板，
//   1. 复用不行：一个面板对应一个 Addressables 地址与一套控件，复用 TitleView 只能显示标题。
//   2. 扩展不行：面板属于玩法模块，写进 Core/UI/Views/ 会让框架层出现玩法名词
//      （architecture.md 第 3 节硬约束）。

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Sample
{
    /// <summary>
    /// 示例面板。预制体 <c>Assets/_Project/Prefabs/UI/SampleView.prefab</c>，
    /// **Addressables 地址必须等于类名** <c>SampleView</c>（UIService 按 <c>typeof(T).Name</c> 找预制体）。
    /// <para>
    /// 面板只管显示与收集点击，不做任何决策：要显示什么由 <see cref="OnOpenAsync"/> 的
    /// <c>arg</c> 传进来，点了返回之后去哪由订阅 <see cref="OnBackClicked"/> 的
    /// <see cref="SampleState"/> 决定。面板里不注入服务——它是 Addressables 实例化出来的
    /// MonoBehaviour，不经过容器，拿不到构造注入。
    /// </para>
    /// <para>
    /// 监听只写在 <see cref="OnOpenAsync"/> / <see cref="OnCloseAsync"/>，不写 OnEnable / OnDisable：
    /// 面板被全屏面板盖住时会 SetActive(false)，那两个回调会重复触发。
    /// </para>
    /// </summary>
    public sealed class SampleView : UIView
    {
        [Tooltip("显示规则类算出来的那行文字。")]
        [SerializeField] private TMP_Text lineLabel;

        [Tooltip("「返回标题」按钮。")]
        [SerializeField] private Button backButton;

        private string line;

        /// <summary>「返回标题」被点了。面板不知道标题状态是谁，由 <see cref="SampleState"/> 接住。</summary>
        public event Action OnBackClicked;

        /// <summary>示例面板是全屏主界面，走 Panel 层。</summary>
        public override UILayer Layer => UILayer.Panel;

        /// <param name="arg">要显示的那行文字（<see cref="string"/>）。传别的类型或 null 时显示占位提示。</param>
        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            line = arg as string;
            if (string.IsNullOrEmpty(line))
            {
                line = "（SampleState 没有传入要显示的文字）";
            }

            if (backButton != null)
            {
                // 先 Remove 再 Add：面板被复用打开时 OnOpenAsync 会再走一遍，不能叠两份监听。
                backButton.onClick.RemoveListener(HandleBackClicked);
                backButton.onClick.AddListener(HandleBackClicked);
            }

            OnRefresh();
            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            if (backButton != null)
            {
                backButton.onClick.RemoveListener(HandleBackClicked);
            }

            return UniTask.CompletedTask;
        }

        /// <summary>把 <c>line</c> 画到界面上。数据变了由持有方调一次，UIService 不主动调。</summary>
        public override void OnRefresh()
        {
            if (lineLabel != null)
            {
                lineLabel.text = line;
            }
        }

        private void HandleBackClicked()
        {
            OnBackClicked?.Invoke();
        }
    }
}
