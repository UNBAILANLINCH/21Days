// 职责：通用确认弹窗（ConfirmView）的打开参数——正文与两个按钮的文字。
// 为什么新建：ConfirmView 的 OnOpenAsync 只收一个 object 参数，三段文字要打成一包传；
//   不复用字符串参数（ExplorationConfirmView 的做法）是因为按钮文字也要随场景换（「覆盖」「删除」），
//   另开 SetButtonLabels 让调用方分两步调，容易忘掉第二步而沿用上一次的文字。

namespace Game.Core.UI
{
    /// <summary>
    /// 确认弹窗的打开参数。<c>ui.OpenAsync&lt;ConfirmView&gt;(new ConfirmRequest("正文", "确认", "取消"))</c>。
    /// 任一字段为空时用预制体 / 视图的默认文案。
    /// </summary>
    public readonly struct ConfirmRequest
    {
        public ConfirmRequest(string message, string confirmText = null, string cancelText = null)
        {
            Message = message;
            ConfirmText = confirmText;
            CancelText = cancelText;
        }

        /// <summary>正文。</summary>
        public string Message { get; }

        /// <summary>确认按钮文字；空则用默认（「确定」）。</summary>
        public string ConfirmText { get; }

        /// <summary>取消按钮文字；空则用默认（「取消」）。</summary>
        public string CancelText { get; }
    }
}
