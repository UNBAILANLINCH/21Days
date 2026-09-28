// 职责：框架层通用的文字记录只读面板——把一组「说话者 + 正文」按固定格式拼成一段文字显示，带一个关闭按钮；
//   不回滚、不推进，面板开关与 Esc 由调用方负责（本面板只抛 OnDismiss）。
// 为什么放在 Core（复用 → 扩展 → 新增）：原是某个玩法模块私有的历史面板；另一个玩法模块也要同一个面板、同一种文字格式，
//   而玩法模块之间不能互相引用，所以原样下沉到 Core 复用（类改名，序列化字段名与脚本 / 预制体 GUID 不变，预制体接线不丢），
//   调用方把各自的记录转成 TranscriptLine 交进来。
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Core.UI.Views
{
    /// <summary>
    /// 文字记录面板（台词记录 / 历史）。预制体 <c>Assets/_Project/Prefabs/UI/TranscriptView.prefab</c>，
    /// Addressables 地址 <c>TranscriptView</c>。
    /// <code>
    /// TranscriptView view = await ui.OpenAsync&lt;TranscriptView&gt;(ct: ct);
    /// view.Show(lines, truncated);
    /// view.OnDismiss += RequestClose;   // 点「关闭」：调用方先退订，再 ui.CloseAsync(view)
    /// </code>
    /// </summary>
    public sealed class TranscriptView : UIView
    {
        private const string TruncatedNotice = "更早的记录已省略。\n";
        private const string SpeakerSeparator = "：";

        [Tooltip("记录正文（滚动区里的 TMP 文本）。")]
        [SerializeField] private TMP_Text content;

        [Tooltip("关闭按钮：点击只抛 OnDismiss，由调用方走自己的关闭流程。")]
        [SerializeField] private Button close;

        /// <summary>
        /// Top 层：调用方可能整层隐藏 Popup 层（演出期间就会整层藏 HUD 层与弹窗层），记录面板必须在演出和对白之上都能显示。
        /// Top 层不进 UI 栈、打开 / 关闭都不改 EventSystem 选中，所以 Esc 与关闭都由调用方负责。
        /// </summary>
        public override UILayer Layer => UILayer.Top;

        /// <summary>
        /// 不让通用取消路由直接关：面板开关由调用方持有（覆盖中 / 暂停之类的标记、事件退订）。
        /// Top 层本来也不进栈、路由看不到它；Esc 由调用方读到后走自己的关闭流程。
        /// </summary>
        public override bool CloseOnCancel => false;

        /// <summary>点「关闭」。面板自己不关，调用方收到后先退订、再 <c>CloseAsync</c>。</summary>
        public event Action OnDismiss;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            if (content == null || close == null) throw new InvalidOperationException("记录面板引用不完整");
            close.onClick.RemoveListener(Dismiss);
            close.onClick.AddListener(Dismiss);
            return UniTask.CompletedTask;
        }

        /// <summary>显示一组记录（按 <see cref="Format"/> 的格式）；<paramref name="truncated"/> 为 true 时开头注明更早的记录已省略。</summary>
        public void Show(IReadOnlyList<TranscriptLine> lines, bool truncated)
        {
            content.text = Format(lines, truncated);
        }

        /// <summary>
        /// 记录的文字格式（纯函数，测试与回放拼期望值用同一个方法）：<paramref name="truncated"/> 时开头一行「更早的记录已省略。」再空一行；
        /// 每条一段：说话者非空写「说话者：正文」，说话者为空（旁白）只写正文；每段后空一行。
        /// </summary>
        public static string Format(IReadOnlyList<TranscriptLine> lines, bool truncated)
        {
            var text = new StringBuilder();
            if (truncated) text.AppendLine(TruncatedNotice);
            if (lines == null) return text.ToString();
            for (int i = 0; i < lines.Count; i++)
            {
                TranscriptLine line = lines[i];
                if (!string.IsNullOrEmpty(line.Speaker)) text.Append(line.Speaker).Append(SpeakerSeparator);
                text.AppendLine(line.Text).AppendLine();
            }
            return text.ToString();
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            // Button 是 UnityEngine.Object，判空只用 != null。
            if (close != null) close.onClick.RemoveListener(Dismiss);
            OnDismiss = null;
            return UniTask.CompletedTask;
        }

        private void Dismiss() => OnDismiss?.Invoke();
    }
}
