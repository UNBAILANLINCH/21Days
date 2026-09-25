// 职责：世界空间的任务目标头顶标记——目标在画面内时由 QuestHudPresenter 摆到目标头顶显示，否则隐藏。
// 为什么新建：复用 DialogueInteractableMarker 不行，它绑定单个 NPC、按交互焦点切状态，而任务标记全局一个、跟着追踪目标走；
//   扩展进 QuestHudView 也不行，HUD 是屏幕空间 UIView，世界空间物体不能挂在 UI 画布下。
using UnityEngine;

namespace Game.Quest
{
    /// <summary>
    /// 任务目标标记。预制体 Addressables 地址见 <see cref="QuestConfig.TargetMarkerAddress"/>，
    /// 由 <see cref="QuestHudPresenter"/> 实例化并驱动；朝向相机由预制体上的 CameraBillboard 负责（建预制体时挂）。
    /// 首版静止不浮动。
    /// </summary>
    public sealed class QuestTargetMarker : MonoBehaviour
    {
        [Tooltip("标记图标（世界空间 Sprite）。")]
        [SerializeField] private SpriteRenderer icon;

        public SpriteRenderer Icon => icon;

        /// <summary>摆到锚点并显示。每帧调用：只赋位置，未激活时才激活，无分配。</summary>
        public void Show(Vector3 anchor)
        {
            transform.position = anchor;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }
    }
}
