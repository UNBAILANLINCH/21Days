// 职责：结果画面要显示的一份内容（标题 / 名字 / 说明 / 刻痕 / 结果图与观感），以及「按结果种类组文案」的纯函数。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：MirrorResult 是判定结果（种类、下标、妖 id），不带文案与图；DialogueContent 是对白树内容，结构对不上。
//   2. 扩展不行：把文案塞进 MirrorResult 会让纯规则认识配置与 Sprite。
//   结果画面不注入服务（UIView 约定），呈现器组好这一份再作为 OpenAsync 参数递进去；组文案抽成纯函数供 EditMode 测。
using UnityEngine;

namespace Game.Mirror
{
    /// <summary>
    /// 结果画面内容（运行时临时对象）。由 <see cref="MirrorInputPresenter"/> 组装，<see cref="MirrorResultView"/> 只读它渲染。
    /// 规则：照人 / 照物显示标记的名字；模糊轮廓<b>不给任何名字</b>；真形给表里的真形名与描述；照不到 / 自照只有说明。
    /// </summary>
    public sealed class MirrorResultInfo
    {
        private MirrorResultInfo(MirrorResultKind kind, string title, string name, string body, string engraving)
        {
            Kind = kind;
            Title = title ?? string.Empty;
            Name = name ?? string.Empty;
            Body = body ?? string.Empty;
            Engraving = engraving ?? string.Empty;
        }

        public MirrorResultKind Kind { get; }
        public string Title { get; }

        /// <summary>对象名：人 / 物取标记显示名，真形取真形名；模糊、照不到、自照为空串。</summary>
        public string Name { get; }

        public string Body { get; }

        /// <summary>镜缘刻痕：<see cref="MirrorConfig.Engraving"/> 逐行拼接（换行分隔）。</summary>
        public string Engraving { get; }

        /// <summary>结果图；为 null 时结果画面不显示图（自照由视图自己放空镜面图）。</summary>
        public Sprite Image { get; private set; }

        /// <summary>模糊观感：真形图压暗、降透明、略放大，不给名字。</summary>
        public bool Blurred => Kind == MirrorResultKind.Blurry;

        /// <summary>空镜面：自照专用，视图显示自己的空镜面图而不是 <see cref="Image"/>。</summary>
        public bool BlankMirror => Kind == MirrorResultKind.Self;

        /// <summary>
        /// 按结果种类组文案。config 为空（未拖资产）时标题与说明为空串，不抛。
        /// <paramref name="subjectName"/> 只在照人 / 照物时用；<paramref name="trueName"/> / <paramref name="trueDesc"/> 只在真形时用。
        /// </summary>
        public static MirrorResultInfo Compose(MirrorResultKind kind, MirrorConfig config, string subjectName,
            string trueName, string trueDesc)
        {
            // MirrorConfig 是 ScriptableObject，判空只用 == null。
            bool hasConfig = config != null;
            string engraving = hasConfig ? JoinLines(config.Engraving) : string.Empty;
            switch (kind)
            {
                case MirrorResultKind.Human:
                    return new MirrorResultInfo(kind, hasConfig ? config.HumanTitle : null, subjectName, null, engraving);
                case MirrorResultKind.Object:
                    return new MirrorResultInfo(kind, hasConfig ? config.ObjectTitle : null, subjectName, null, engraving);
                case MirrorResultKind.Blurry:
                    return new MirrorResultInfo(kind, hasConfig ? config.BlurryTitle : null, null,
                        hasConfig ? config.BlurryBody : null, engraving);
                case MirrorResultKind.TrueForm:
                    return new MirrorResultInfo(kind, hasConfig ? config.TrueFormTitle : null, trueName, trueDesc, engraving);
                case MirrorResultKind.Self:
                    return new MirrorResultInfo(kind, hasConfig ? config.SelfTitle : null, null,
                        hasConfig ? config.SelfBody : null, engraving);
                default:
                    return new MirrorResultInfo(MirrorResultKind.Nothing, hasConfig ? config.NothingTitle : null, null,
                        hasConfig ? config.NothingBody : null, engraving);
            }
        }

        /// <summary>挂上结果图（照人 / 照物用标记形象，真形 / 模糊用加载到的真形图）。只在打开结果画面之前调。</summary>
        public MirrorResultInfo WithImage(Sprite image)
        {
            Image = image;
            return this;
        }

        private static string JoinLines(System.Collections.Generic.IReadOnlyList<string> lines)
        {
            if (lines == null || lines.Count == 0) return string.Empty;
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (string.IsNullOrEmpty(lines[i])) continue;
                if (builder.Length > 0) builder.Append('\n');
                builder.Append(lines[i]);
            }
            return builder.ToString();
        }
    }
}
