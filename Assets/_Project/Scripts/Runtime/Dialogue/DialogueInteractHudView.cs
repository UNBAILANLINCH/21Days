// 职责：PC 式交互提示——有交互焦点时在屏幕底部居中显示一条胶囊「[E] 对话 · NPC 名」，点击抛 OnInteract（PRP 8.1、roadmap H4）。
// 为什么新建：DialogueView 是对白进行中的主面板（Panel 层、拉起后才开），这条提示在对白之外常驻 Hud 层，
//   生命周期与层级都不同；需要单独的预制体与 Addressables 地址，塞进现有 View 说不通职责。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Dialogue
{
    /// <summary>
    /// 交互 HUD。预制体 Addressables 地址须为 <c>DialogueInteractHudView</c>。
    /// 面板本身常驻打开，显隐只切 <see cref="root"/>，不走 UIService 的开关（避免每次进出范围都实例化 / 淡入淡出）。
    /// 键位徽章文字由焦点系统在打开后经 <see cref="SetKeyText"/> 赋一次（View 不注入输入服务）。
    /// </summary>
    public sealed class DialogueInteractHudView : UIView
    {
        /// <summary>键位显示串取不到时的回退：与 Interact 动作的第一条键盘绑定一致。</summary>
        public const string FallbackKeyText = "E";

        private const string Verb = "对话";
        private const string NameSeparator = " · ";

        [Tooltip("整条胶囊；有焦点时激活。")]
        [SerializeField] private GameObject root;
        [Tooltip("整条胶囊按钮，鼠标点击同样触发交互。")]
        [SerializeField] private Button button;
        [Tooltip("左侧键位徽章里的文字（如「E」）。")]
        [SerializeField] private TMP_Text keyLabel;
        [Tooltip("右侧文字「对话 · NPC 名」。")]
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
            label.text = FormatLabel(null);
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

        /// <summary>显示提示，右侧写「对话 · {npcName}」；名字为空只写「对话」。</summary>
        public void Show(string npcName)
        {
            if (label != null) label.text = FormatLabel(npcName);
            if (root != null && !root.activeSelf) root.SetActive(true);
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

        /// <summary>右侧文字：有名字为「对话 · 名字」，名字为空白只写「对话」。纯函数，供测试。</summary>
        public static string FormatLabel(string npcName)
        {
            if (string.IsNullOrWhiteSpace(npcName)) return Verb;
            return Verb + NameSeparator + npcName.Trim();
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
                throw new InvalidOperationException("DialogueInteractHudView 引用未接线：" + string.Join("、", missing));
        }

        private void RaiseInteract() => OnInteract?.Invoke();
    }
}
