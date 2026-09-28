// 职责：照镜结果画面（Popup 层）——镜框里显示结果图、标题、名字、说明与镜缘刻痕；模糊轮廓压暗放大、自照显示空镜面。只显示，不注入服务。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：DialogueView / PerformanceView 是对白框，NotificationView 是飘窗，都没有镜框、结果图与刻痕位。
//   2. 扩展不行：往对白框里塞镜的结果会破坏对白 / 演出面板的一致性约束（TalkPanelConsistencyTests）。
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
    /// 照镜结果画面。预制体 <c>Prefabs/UI/MirrorResultView.prefab</c>，Addressables 地址 <c>MirrorResultView</c>。
    /// <c>OpenAsync&lt;MirrorResultView&gt;(MirrorResultInfo)</c> 打开；到时 / 确认键由 <see cref="MirrorInputPresenter"/> 关，
    /// 返回键（UI/Cancel）经 UICancelRouter 直接关（<see cref="UIView.CloseOnCancel"/> 默认对 Popup 为 true）。
    /// 任何一条路关掉都会触发 <see cref="OnClosed"/>，呈现器据此释放世界暂停令牌与图片句柄。
    /// </summary>
    public sealed class MirrorResultView : UIView
    {
        [Tooltip("镜框图（ui_mirror_frame），装饰用，可空。")]
        [SerializeField] private Image frame;
        [Tooltip("镜面里的结果图（人 / 物形象、真形图、空镜面）。")]
        [SerializeField] private Image resultImage;
        [Tooltip("自照时显示的空镜面图：拖 Art/Sprites/UI/Mirror/ui_mirror_blank.png。")]
        [SerializeField] private Sprite blankSprite;
        [Tooltip("标题（「是人」「看不真切」「照见真形」…）。")]
        [SerializeField] private TMP_Text title;
        [Tooltip("对象名（人 / 物名、真形名）；模糊轮廓时为空。")]
        [SerializeField] private TMP_Text subjectName;
        [Tooltip("说明（真形描述、模糊 / 照不到 / 自照的说明）。")]
        [SerializeField] private TMP_Text body;
        [Tooltip("镜缘刻痕文字。")]
        [SerializeField] private TMP_Text engraving;
        [Tooltip("半透明遮罩（挡住下层点击，可空）。")]
        [SerializeField] private Image backdrop;
        [Tooltip("模糊轮廓时结果图的颜色（压暗 + 降透明）。")]
        [SerializeField] private Color blurredTint = new Color(0.25f, 0.25f, 0.3f, 0.65f);
        [Tooltip("模糊轮廓时结果图的放大倍数（轻微失焦感）。")]
        [SerializeField, Min(0.01f)] private float blurredScale = 1.08f;

        private Vector3 imageRestScale = Vector3.one;
        private bool restScaleCaptured;

        public override UILayer Layer => UILayer.Popup;
        public override bool IsFullScreen => false;

        /// <summary>面板被关掉（任何一条路）时触发一次，在 OnCloseAsync 里。订阅方自己退订。</summary>
        public event Action OnClosed;

        /// <summary>
        /// 当前显示的内容：打开时记下（参数不是 <see cref="MirrorResultInfo"/> 时为 null），关闭时清空。
        /// 给回放按结果种类 / 名字 / 刻痕 / 结果图判定，不依赖预制体子物体名。
        /// </summary>
        public MirrorResultInfo Current { get; private set; }

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            Validate();
            if (!restScaleCaptured)
            {
                imageRestScale = resultImage.rectTransform.localScale;
                restScaleCaptured = true;
            }
            if (backdrop != null) backdrop.raycastTarget = true;
            Apply(arg as MirrorResultInfo);
            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            // 关闭时摘掉图：图片句柄随后由呈现器释放，不留对已释放 Sprite 的引用。
            if (resultImage != null) resultImage.sprite = null;
            Current = null;
            Action closed = OnClosed;
            OnClosed = null;
            closed?.Invoke();
            return UniTask.CompletedTask;
        }

        private void Apply(MirrorResultInfo info)
        {
            Current = info;
            if (info == null)
            {
                title.text = string.Empty;
                SetOptional(subjectName, string.Empty);
                SetOptional(body, string.Empty);
                SetOptional(engraving, string.Empty);
                ShowImage(null, false);
                return;
            }

            title.text = info.Title;
            SetOptional(subjectName, info.Name);
            SetOptional(body, info.Body);
            SetOptional(engraving, info.Engraving);
            ShowImage(info.BlankMirror ? blankSprite : info.Image, info.Blurred);
        }

        // 模糊轮廓：同一张真形图压暗、降透明、略放大（不引入新 shader，PRP/mirror-core 2.3 占位做法）。
        private void ShowImage(Sprite sprite, bool blurred)
        {
            // Sprite 是 UnityEngine.Object，判空只用 == null。
            bool hasImage = sprite != null;
            resultImage.enabled = hasImage;
            resultImage.sprite = sprite;
            resultImage.color = blurred ? blurredTint : Color.white;
            resultImage.rectTransform.localScale = blurred ? imageRestScale * blurredScale : imageRestScale;
        }

        private static void SetOptional(TMP_Text text, string value)
        {
            if (text == null) return;
            text.text = value ?? string.Empty;
            text.gameObject.SetActive(!string.IsNullOrEmpty(value));
        }

        // 逐个点名缺失字段，预制体按名字接线时一眼看出漏了哪个。frame / backdrop / subjectName / body / engraving 可空。
        private void Validate()
        {
            var missing = new List<string>();
            if (resultImage == null) missing.Add(nameof(resultImage));
            if (title == null) missing.Add(nameof(title));
            if (blankSprite == null) missing.Add(nameof(blankSprite));
            if (missing.Count > 0)
                throw new InvalidOperationException("MirrorResultView 引用未接线：" + string.Join("、", missing));
        }
    }
}
