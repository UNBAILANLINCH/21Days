// 职责：一条交互提示的内容——动词（「对话」「打开」「挑战」）+ 名字（NPC 名 / 「物资箱」），由可交互对象自己给出（PRP/interaction D5、D10）。
// 为什么新建：统一交互之前提示文字散在两处（对白 HUD 写死「对话」、物资箱用 LootConfig 的整句），没有可复用的类型；
//   做成 readonly struct 放在 Interaction 模块，契约 IInteractable 与提示 HUD 共用，取值无分配。
namespace Game.Interaction
{
    /// <summary>交互提示：动词 + 名字。名字可空（只显示动词）；拼字符串在 <see cref="InteractPromptHudView.FormatLabel(InteractionPrompt)"/>。</summary>
    public readonly struct InteractionPrompt
    {
        public InteractionPrompt(string verb, string name)
        {
            Verb = verb;
            Name = name;
        }

        /// <summary>动词（「对话」「打开」「挑战」）。为空时 HUD 回退为 <see cref="InteractPromptHudView.FallbackVerb"/>。</summary>
        public string Verb { get; }

        /// <summary>对象名（NPC 名 / 「物资箱」）。为空时只显示动词。</summary>
        public string Name { get; }
    }
}
