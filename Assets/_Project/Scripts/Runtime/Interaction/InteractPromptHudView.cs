// 职责：统一交互提示——有交互焦点时在屏幕底部居中显示一条胶囊「[E] 动词 · 名字」（如「[E] 对话 · 老者」「[E] 打开 · 物资箱」「[E] 挑战 · BOSS」），
//   点击抛 OnInteract（PRP/interaction D7，原 PRP 8.1、roadmap H4）。
// 为什么迁入并改名（D7）：这条提示原在 Dialogue 模块（类名带 Dialogue 前缀），动词写死「对话」，Loot 另用探索 HUD 的一行文字；统一后全屏只有这一条提示，
//   动词由对象自己给。git mv 连同 .meta 移入 Interaction 模块，GUID 不变，预制体上的脚本引用不断；Addressables 地址随类名改。
// 为什么当初新建：DialogueView 是对白进行中的主面板（Panel 层、拉起后才开），这条提示在对白之外常驻 Hud 层，
//   生命周期与层级都不同；需要单独的预制体与 Addressables 地址，塞进现有 View 说不通职责。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Simulation;
using Game.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Interaction
{
    /// <summary>
    /// 交互提示 HUD。预制体 Addressables 地址须为 <c>InteractPromptHudView</c>（等于类名）。
    /// 面板本身常驻打开，显隐只切 <see cref="root"/>，不走 UIService 的开关（避免每次进出范围都实例化 / 淡入淡出）。
    /// 键位徽章文字由焦点系统在打开后经 <see cref="SetKeyText"/> 赋一次（View 不注入输入服务）。
    /// 胶囊宽度随文字自适应（<see cref="ResolveWidth"/>）：最小 <c>minWidth</c>（320），最大 <c>maxWidth</c>（640，参考分辨率下），超过最大才省略。
    /// </summary>
    public sealed class InteractPromptHudView : UIView
    {
        /// <summary>键位显示串取不到时的回退：与 Interact 动作的第一条键盘绑定一致。</summary>
        public const string FallbackKeyText = "E";

        /// <summary>对象没给动词时的回退。</summary>
        public const string FallbackVerb = "交互";

        private const string NameSeparator = " · ";

        /// <summary>按文字定宽时多留的像素：宽度恰好等于首选宽度时浮点误差会让 TMP 误判放不下而显示省略号。</summary>
        private const float TextSlack = 2f;

        [Tooltip("胶囊最小宽度（参考分辨率下的像素）：短提示也保持这个宽度。")]
        [SerializeField, Min(0f)] private float minWidth = 320f;
        [Tooltip("胶囊最大宽度（参考分辨率下的像素）：文字再长就停在这个宽度，右侧文字显示省略号。")]
        [SerializeField, Min(0f)] private float maxWidth = 640f;

        [Tooltip("整条胶囊；有焦点时激活。")]
        [SerializeField] private GameObject root;
        [Tooltip("整条胶囊按钮，鼠标点击同样触发交互。")]
        [SerializeField] private Button button;
        [Tooltip("左侧键位徽章里的文字（如「E」）。")]
        [SerializeField] private TMP_Text keyLabel;
        [Tooltip("右侧文字「动词 · 名字」。")]
        [SerializeField] private TMP_Text label;
        [Tooltip("图标（占位，可空；当前 PC 式提示不显示）。")]
        [SerializeField] private Image icon;

        public override UILayer Layer => UILayer.Hud;
        public override bool IsFullScreen => false;

        /// <summary>玩家点了交互提示。</summary>
        public event Action OnInteract;

        /// <summary>提示当前是否显示。</summary>
        public bool IsShown => root != null && root.activeSelf;

        /// <summary>键位徽章当前文字（供验证场景 / 测试读取）。</summary>
        public string KeyText => keyLabel != null ? keyLabel.text : string.Empty;

        /// <summary>右侧文字当前内容（供验证场景 / 测试读取）。</summary>
        public string LabelText => label != null ? label.text : string.Empty;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            Validate();
            keyLabel.text = FallbackKeyText;
            label.text = FormatLabel(default(InteractionPrompt));
            button.onClick.RemoveListener(RaiseInteract);
            button.onClick.AddListener(RaiseInteract);
            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            if (button != null) button.onClick.RemoveListener(RaiseInteract);
            OnInteract = null;
            return UniTask.CompletedTask;
        }

        /// <summary>设置键位徽章文字；传入输入系统给的绑定显示串，空则回退「E」。</summary>
        public void SetKeyText(string bindingDisplay)
        {
            if (keyLabel != null) keyLabel.text = FormatKeyText(bindingDisplay);
        }

        /// <summary>显示提示，右侧写「动词 · 名字」；名字为空只写动词。胶囊宽度随文字在 [minWidth, maxWidth] 里伸缩。</summary>
        public void Show(InteractionPrompt prompt)
        {
            if (label != null) label.text = FormatLabel(prompt);
            // 先激活再量宽：Root 在预制体里默认未激活，文字组件没初始化时量出的首选宽度不准，
            // 第一次显示会退回最小宽度把长提示截断（2026-10-08 World 回放截到「前往 · 镜中妖界长安…」）。
            if (root != null && !root.activeSelf) root.SetActive(true);
            FitWidth();
        }

        /// <summary>
        /// 胶囊宽度：内容宽度（内边距 + 徽章 + 间距 + 文字首选宽度）夹在 [<paramref name="min"/>, <paramref name="max"/>] 里；
        /// 最大值小于最小值时按最小值。超过最大值的部分由右侧文字的省略号吃掉。纯函数，供测试。
        /// </summary>
        public static float ResolveWidth(float contentWidth, float min, float max)
        {
            if (max < min) max = min;
            return GameMath.Clamp(contentWidth, min, max);
        }

        // 只在焦点变化时（Show）跑，不在每帧路径上。宽度改了 RectTransform，布局组下一次重建按新宽度分给文字。
        private void FitWidth()
        {
            if (root == null || label == null) return;
            if (!(root.transform is RectTransform rect)) return;
            float labelWidth = label.GetPreferredValues(label.text).x;
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                ResolveWidth(ChromeWidth(rect) + labelWidth + TextSlack, minWidth, maxWidth));
        }

        // 胶囊里除右侧文字外占的宽度：布局组左右内边距 + 其它参与布局的子物体（键位徽章、启用时的图标）的首选宽度 + 间距。
        private float ChromeWidth(RectTransform rect)
        {
            HorizontalOrVerticalLayoutGroup layout = rect.GetComponent<HorizontalOrVerticalLayoutGroup>();
            float width = layout == null ? 0f : layout.padding.horizontal;
            float spacing = layout == null ? 0f : layout.spacing;
            RectTransform labelRect = label.rectTransform;
            int laidOut = 0;
            for (int i = 0; i < rect.childCount; i++)
            {
                if (!(rect.GetChild(i) is RectTransform child) || !child.gameObject.activeSelf) continue;
                if (child.TryGetComponent(out ILayoutIgnorer ignorer) && ignorer.ignoreLayout) continue;
                laidOut++;
                if (child != labelRect) width += LayoutUtility.GetPreferredWidth(child);
            }
            if (laidOut > 1) width += spacing * (laidOut - 1);
            return width;
        }

        public void Hide()
        {
            if (root != null && root.activeSelf) root.SetActive(false);
        }

        /// <summary>键位徽章文字：绑定显示串去首尾空白；为空（无键盘绑定 / 输入未初始化）时回退「E」。纯函数，供测试。</summary>
        public static string FormatKeyText(string bindingDisplay)
        {
            if (string.IsNullOrWhiteSpace(bindingDisplay)) return FallbackKeyText;
            return bindingDisplay.Trim();
        }

        /// <summary>右侧文字：「动词 · 名字」；名字为空白只写动词，动词为空白回退「交互」。纯函数，供测试。</summary>
        public static string FormatLabel(InteractionPrompt prompt) => FormatLabel(prompt.Verb, prompt.Name);

        /// <summary>同 <see cref="FormatLabel(InteractionPrompt)"/>，按动词与名字两个参数拼。</summary>
        public static string FormatLabel(string verb, string name)
        {
            string shownVerb = string.IsNullOrWhiteSpace(verb) ? FallbackVerb : verb.Trim();
            if (string.IsNullOrWhiteSpace(name)) return shownVerb;
            return shownVerb + NameSeparator + name.Trim();
        }

        // 逐个点名缺失字段，预制体按名字接线时一眼看出漏了哪个。icon 是占位，可空。
        private void Validate()
        {
            var missing = new List<string>();
            if (root == null) missing.Add(nameof(root));
            if (button == null) missing.Add(nameof(button));
            if (keyLabel == null) missing.Add(nameof(keyLabel));
            if (label == null) missing.Add(nameof(label));
            if (missing.Count > 0)
                throw new InvalidOperationException("InteractPromptHudView 引用未接线：" + string.Join("、", missing));
        }

        private void RaiseInteract() => OnInteract?.Invoke();
    }
}
