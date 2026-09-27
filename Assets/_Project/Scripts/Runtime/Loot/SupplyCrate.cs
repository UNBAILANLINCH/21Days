// 职责：场景里的物资箱——声明箱子键与奖励（tbitem id × 数量），按开 / 合切换两套外观。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：DialogueInteractable 是「可对话物体」，带对白 id 与交互回调；QuestLocation 只是地点标记，都没有奖励与开合状态。
//   2. 扩展不行：往它们身上加奖励字段会让对白 / 任务模块认识物品表，职责说不通。
//   Loot 模块首次落地（PRP/exploration-whitebox 3.1），新建场景组件。
using System;
using UnityEngine;

namespace Game.Loot
{
    /// <summary>
    /// 物资箱。由 <see cref="LootSceneBinder"/> 在场景加载时登记（含未激活的），运行时 Instantiate 的不登记。
    /// 开合只由 <see cref="LootService"/> / <see cref="LootSceneBinder"/> 调 <see cref="SetOpened"/>，组件自己不读输入。
    /// </summary>
    public sealed class SupplyCrate : MonoBehaviour
    {
        [Tooltip("箱子键：存档里记录「已开」用，同一时刻已加载场景里应唯一。")]
        [SerializeField] private string crateKey;

        [Tooltip("奖励物品 id（tbitem 主键）。")]
        [SerializeField, Min(1)] private int itemId = 1;

        [Tooltip("奖励数量。")]
        [SerializeField, Min(1)] private int count = 1;

        [Tooltip("未开时显示的外观子物体（可空）。")]
        [SerializeField] private GameObject closedVisual;

        [Tooltip("已开时显示的外观子物体（可空）。")]
        [SerializeField] private GameObject openedVisual;

        private bool opened;

        public string Key => crateKey;
        public int ItemId => itemId;
        public int Count => count;
        public bool IsOpened => opened;
        public Vector3 Position => transform.position;

        /// <summary>开合状态真正变化时触发（参数为本箱子）。同物体的 <see cref="SupplyCrateMarker"/> 据此刷新显隐。</summary>
        public event Action<SupplyCrate> OnOpenedChanged;

        /// <summary>切开 / 合状态并切两套外观的激活；外观字段为空时跳过。</summary>
        public void SetOpened(bool value)
        {
            bool changed = opened != value;
            opened = value;
            // GameObject 是 UnityEngine.Object，判空只用 != null。
            if (closedVisual != null) closedVisual.SetActive(!value);
            if (openedVisual != null) openedVisual.SetActive(value);
            if (changed) OnOpenedChanged?.Invoke(this);
        }
    }
}
