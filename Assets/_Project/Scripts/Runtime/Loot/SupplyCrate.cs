// 职责：场景里的物资箱——声明箱子键与奖励（tbitem id × 数量），按开 / 合切换两套外观。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：DialogueInteractable 是「可对话物体」，带对白 id 与交互回调；QuestLocation 只是地点标记，都没有奖励与开合状态。
//   2. 扩展不行：往它们身上加奖励字段会让对白 / 任务模块认识物品表，职责说不通。
//   Loot 模块首次落地（PRP/exploration-whitebox 3.1），新建场景组件。
// 统一交互（PRP/interaction D8）：本组件实现 IInteractable，由 LootSceneBinder 配好半径 / 提示 / 开箱回调后登记进 Interaction 的登记表；
//   焦点、交互键、底部提示都由 Game.Interaction.InteractionFocus 统一驱动，组件自己仍不读输入、不认识容器。
using System;
using Game.Interaction;
using UnityEngine;

namespace Game.Loot
{
    /// <summary>
    /// 物资箱。由 <see cref="LootSceneBinder"/> 在场景加载时登记（含未激活的），运行时 Instantiate 的不登记。
    /// 开合只由 <see cref="LootService"/> / <see cref="LootSceneBinder"/> 调 <see cref="SetOpened"/>，组件自己不读输入。
    /// 交互参数由 <see cref="LootSceneBinder"/> 经 <see cref="BindInteraction"/> 下发；没下发过的箱子不可交互（不会成为焦点）。
    /// </summary>
    public sealed class SupplyCrate : MonoBehaviour, IInteractable
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
        private float interactRadius;
        private InteractionPrompt prompt;
        private Func<SupplyCrate, bool> collect;

        public string Key => crateKey;
        public int ItemId => itemId;
        public int Count => count;
        public bool IsOpened => opened;
        public Vector3 Position => transform.position;

        /// <summary>是否是当前交互焦点（由统一焦点经 <see cref="IInteractable.OnFocusChanged"/> 写）。头顶标记不看它：未开就显示（规则不变）。</summary>
        public bool Focused { get; private set; }

        /// <summary>交互半径（来自 LootConfig，经 <see cref="BindInteraction"/> 下发）；小于等于 0 不限距离。</summary>
        public float InteractionRadius => interactRadius;

        /// <summary>未开、且已下发开箱回调才可交互。距离由选择函数判，这里不含。</summary>
        public bool CanInteract => !opened && collect != null;

        /// <summary>现有对象全是 0，只在距离相等时打平。</summary>
        public int InteractionPriority => 0;

        /// <summary>底部交互提示：动词「打开」+ 名字「物资箱」，文案来自 LootConfig。</summary>
        public InteractionPrompt Prompt => prompt;

        /// <summary>开合状态真正变化时触发（参数为本箱子）。同物体的 <see cref="SupplyCrateMarker"/> 据此刷新显隐。</summary>
        public event Action<SupplyCrate> OnOpenedChanged;

        /// <summary>
        /// 下发交互参数（由 <see cref="LootSceneBinder"/> 在场景加载登记时调）：半径、提示、开箱回调（通常是 <c>LootService.TryCollect</c>）。
        /// 传的是回调而不是服务：组件不认识容器，开箱的幂等、存档、通知都留在服务里。
        /// </summary>
        public void BindInteraction(float radius, InteractionPrompt interactionPrompt, Func<SupplyCrate, bool> onCollect)
        {
            interactRadius = radius;
            prompt = interactionPrompt;
            collect = onCollect;
        }

        /// <summary>交互：调下发的开箱回调；已开 / 未下发时忽略（开箱幂等由回调方保证）。</summary>
        public void Interact()
        {
            if (opened || collect == null) return;
            collect(this);
        }

        void IInteractable.OnFocusChanged(bool focused) => Focused = focused;

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
