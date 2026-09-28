// 职责：右上角镜图标 + 0..3 道击中裂痕叠图（Hud 层）；沉浸模式下随 HUD 隐藏。只显示，不注入服务。不显示任何数字或血条。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：QuestHudView 只管任务栏，ExplorationHudView 沉浸时必须留下（VisibleWhenHudHidden = true），镜图标沉浸时要藏，
//      两种显隐语义不能放在同一个 View。
//   2. 扩展不行：塞进 QuestHudView 会让任务模块认识镜。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Mirror
{
    /// <summary>
    /// 镜图标 HUD。预制体 <c>Prefabs/UI/MirrorHudView.prefab</c>，Addressables 地址 <c>MirrorHudView</c>；
    /// 根 RectTransform 铺满画布、根上不放 Graphic（不挡世界点击）。由 <see cref="MirrorCrackPresenter"/> 在启动完成后打开并驱动。
    /// </summary>
    public sealed class MirrorHudView : UIView
    {
        [Tooltip("镜图标（ui_mirror_icon），锚在右上角。")]
        [SerializeField] private Image icon;
        [Tooltip("裂痕叠图，与 icon 同位同大、叠在其上；无裂痕时隐藏。")]
        [SerializeField] private Image crackOverlay;
        [Tooltip("按裂痕数取图：长度 3，依次拖 ui_mirror_crack_1 / _2 / _3（累积式）。")]
        [SerializeField] private Sprite[] crackSprites = new Sprite[MirrorCrackRules.MaxHitCracks];

        private int shownCracks = -1;

        public override UILayer Layer => UILayer.Hud;
        public override bool IsFullScreen => false;

        /// <summary>镜图标上当前显示的裂痕级数（0..3，已按图数夹取）；还没打开过时为 0。</summary>
        public int ShownCracks => shownCracks < 0 ? 0 : shownCracks;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            Validate();
            if (crackOverlay != null) crackOverlay.raycastTarget = false;
            icon.raycastTarget = false;
            shownCracks = -1;
            SetCracks(0);
            return UniTask.CompletedTask;
        }

        /// <summary>显示 hitCracks 道裂痕（夹到 0..3）；与当前相同时不动。事件驱动调用，不在每帧路径上。</summary>
        public void SetCracks(int hitCracks)
        {
            int cracks = SelectCrackIndex(hitCracks, crackSprites == null ? 0 : crackSprites.Length);
            if (cracks == shownCracks) return;
            shownCracks = cracks;
            if (crackOverlay == null) return;
            bool visible = cracks > 0 && crackSprites[cracks - 1] != null;
            crackOverlay.enabled = visible;
            if (visible) crackOverlay.sprite = crackSprites[cracks - 1];
        }

        /// <summary>裂痕数 → 要显示的叠图级数（0 = 不显示），夹到 [0, min(3, 图数)]。纯函数，供视图与测试共用。</summary>
        public static int SelectCrackIndex(int hitCracks, int spriteCount)
        {
            int max = spriteCount < MirrorCrackRules.MaxHitCracks ? spriteCount : MirrorCrackRules.MaxHitCracks;
            if (hitCracks <= 0 || max <= 0) return 0;
            return hitCracks > max ? max : hitCracks;
        }

        private void Validate()
        {
            var missing = new List<string>();
            if (icon == null) missing.Add(nameof(icon));
            if (crackOverlay == null) missing.Add(nameof(crackOverlay));
            if (missing.Count > 0)
                throw new InvalidOperationException("MirrorHudView 引用未接线：" + string.Join("、", missing));
        }
    }
}
