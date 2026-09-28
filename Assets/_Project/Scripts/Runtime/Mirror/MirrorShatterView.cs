// 职责：镜碎页——全黑底、居中「镜碎」二字，别无文字与按钮；点击任意处（或呈现器转来的确认 / 取消键）后交回。只显示，不注入服务。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：ExplorationConfirmView 有正文 + 确认 / 取消两个按钮，镜碎页明确不许出现别的文字与按钮（PRD V10，不出现「读档」）。
//   2. 扩展不行：给确认框加「无按钮模式」会让通用弹窗背上镜碎的特殊交互。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Mirror
{
    /// <summary>
    /// 镜碎页。预制体 <c>Prefabs/UI/MirrorShatterView.prefab</c>，Addressables 地址 <c>MirrorShatterView</c>。
    /// <para>
    /// 层级取 Popup 而不是 Top：Top 不进栈，Esc 会落到「无面板可关 → 开暂停菜单」（PauseMenuController 第 4 条）；
    /// Popup 且 <see cref="CloseOnCancel"/> = false 时，UICancelRouter 判 Blocked、什么都不做，取消键交给呈现器当「任意键」用。
    /// Popup 层画在 Hud / Panel 之上，全黑底盖住世界、HUD 与暂停菜单。
    /// </para>
    /// <c>OpenAsync&lt;MirrorShatterView&gt;(文字)</c> 打开，<see cref="WaitAsync"/> 等交回；关闭由调用方负责。
    /// </summary>
    public sealed class MirrorShatterView : UIView
    {
        [Tooltip("全黑底 Image（ui_shatter_bg 或纯色），铺满、吃射线。")]
        [SerializeField] private Image background;
        [Tooltip("居中文字「镜碎」。")]
        [SerializeField] private TMP_Text text;
        [Tooltip("铺满的透明点击区（Button，可与 background 同一物体）；点任意处交回。")]
        [SerializeField] private Button tapArea;
        [Tooltip("打开参数不是字符串时显示的文字。")]
        [SerializeField] private string defaultText = "镜碎";

        private UniTaskCompletionSource pending;

        public override UILayer Layer => UILayer.Popup;
        public override bool IsFullScreen => false;
        public override bool CloseOnCancel => false;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            Validate();
            text.text = arg is string value && !string.IsNullOrEmpty(value) ? value : defaultText;
            background.raycastTarget = true;
            tapArea.onClick.RemoveListener(Dismiss);
            tapArea.onClick.AddListener(Dismiss);
            if (pending == null || pending.Task.Status != UniTaskStatus.Pending)
            {
                pending = new UniTaskCompletionSource();
            }

            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            if (tapArea != null) tapArea.onClick.RemoveListener(Dismiss);
            // 被关掉按「已交回」处理：TrySetResult，等待方不会收到异常。
            Dismiss();
            return UniTask.CompletedTask;
        }

        /// <summary>交回（点击、呈现器转来的确认 / 取消键都走这里）。幂等。</summary>
        public void Dismiss()
        {
            if (pending != null) pending.TrySetResult();
        }

        /// <summary>等玩家交回；<paramref name="ct"/> 取消或面板被关也算交回（不抛）。面板没打开过时立即返回。</summary>
        public async UniTask WaitAsync(CancellationToken ct = default)
        {
            UniTaskCompletionSource source = pending;
            if (source == null) return;
            using (ct.Register(() => source.TrySetResult()))
            {
                await source.Task;
            }
        }

        private void Validate()
        {
            var missing = new List<string>();
            if (background == null) missing.Add(nameof(background));
            if (text == null) missing.Add(nameof(text));
            if (tapArea == null) missing.Add(nameof(tapArea));
            if (missing.Count > 0)
                throw new InvalidOperationException("MirrorShatterView 引用未接线：" + string.Join("、", missing));
        }
    }
}
