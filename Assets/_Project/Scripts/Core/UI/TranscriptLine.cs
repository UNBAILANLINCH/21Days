// 职责：一条文字记录（说话者 + 正文），通用记录面板 TranscriptView 的输入单位。
// 为什么新建（复用 → 扩展 → 新增）：
//   1. 复用不行：记录面板下沉到 Core 后不能再收玩法模块自己的记录类型（Core 不认识玩法类型），各玩法的台词来源也各不相同；
//   2. 扩展不行：Core 现有的值类型（ConfirmRequest、NotificationEntry……）各管各的弹窗 / 通知，塞进「说话者 + 正文」职责说不通。
//   所以新建一个只读值类型，调用方把各自的记录转成它（如「选择：」这类前缀由调用方放进 Speaker）。
namespace Game.Core.UI
{
    /// <summary>一条文字记录。只读值类型；传入 null 一律记为空串。说话者为空表示旁白（显示时只写正文）。</summary>
    public readonly struct TranscriptLine
    {
        public TranscriptLine(string speaker, string text)
        {
            Speaker = speaker ?? string.Empty;
            Text = text ?? string.Empty;
        }

        /// <summary>说话者显示名；空串 = 旁白。「选择」这类前缀也放这里，显示为「选择：正文」。</summary>
        public string Speaker { get; }

        /// <summary>正文（可含 TMP 富文本标签，原样显示）。</summary>
        public string Text { get; }
    }
}
