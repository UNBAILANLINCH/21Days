// 职责：实际交谈记录的只读展示；复用 UI 栈，不执行回滚或推进。
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Dialogue
{
    public sealed class DialogueHistoryView : UIView
    {
        [SerializeField] private TMP_Text content;
        [SerializeField] private Button close;
        public override UILayer Layer => UILayer.Popup;
        /// <summary>
        /// 不让通用取消路由直接关：面板开关由 DialogueController 持有（覆盖中标记、事件退订）。
        /// Esc 与「历史」键由 <see cref="DialogueKeyboardInput"/> 翻成关闭，走 Controller 的正常收尾。
        /// </summary>
        public override bool CloseOnCancel => false;
        public event Action OnDismiss;
        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            if (content == null || close == null) throw new InvalidOperationException("历史面板引用不完整");
            close.onClick.RemoveListener(Dismiss);
            close.onClick.AddListener(Dismiss);
            return UniTask.CompletedTask;
        }
        public void Show(IReadOnlyList<DialogueSaveData.HistoryEntry> entries, bool truncated)
        {
            var text = new StringBuilder();
            if (truncated) text.AppendLine("更早的记录已省略。\n");
            foreach (DialogueSaveData.HistoryEntry entry in entries)
            {
                if (entry.IsChoice) text.Append("选择：");
                else if (!string.IsNullOrEmpty(entry.Speaker)) text.Append(entry.Speaker).Append("：");
                text.AppendLine(entry.Text).AppendLine();
            }
            content.text = text.ToString();
        }
        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            close.onClick.RemoveListener(Dismiss);
            OnDismiss = null;
            return UniTask.CompletedTask;
        }
        private void Dismiss() => OnDismiss?.Invoke();
    }
}
