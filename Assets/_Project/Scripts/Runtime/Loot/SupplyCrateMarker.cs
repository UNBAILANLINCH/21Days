// 职责：物资箱头顶的世界空间标记——箱子未开且不在沉浸模式时显示，否则隐藏。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：DialogueInteractableMarker 绑定 DialogueInteractable、按对白焦点切状态；QuestTargetMarker 全局一个、跟追踪目标走。
//   2. 扩展不行：往 SupplyCrate 里塞标记逻辑会让箱子组件同时管外观开合与 HUD 沉浸状态，拆开后各自单一。
//   朝向相机由标记子物体上的 CameraBillboard 负责（建场景时挂），本组件不引用探索模块。
using UnityEngine;

namespace Game.Loot
{
    /// <summary>
    /// 物资箱头顶标记。挂在 <see cref="SupplyCrate"/> 同一物体上，<c>marker</c> 拖头顶标记子物体。
    /// 不注入服务：沉浸状态由 <see cref="LootSceneBinder"/> 订阅事件后逐个调 <see cref="SetHudHidden"/>，
    /// 开合变化经 <see cref="SupplyCrate.OnOpenedChanged"/> 自动刷新（OnEnable / OnDisable 成对订阅）。
    /// </summary>
    public sealed class SupplyCrateMarker : MonoBehaviour
    {
        [Tooltip("头顶标记子物体（SpriteRenderer + CameraBillboard）。")]
        [SerializeField] private GameObject marker;

        private SupplyCrate crate;
        private bool hudHidden;

        public SupplyCrate Crate => crate;

        private void Awake()
        {
            crate = GetComponent<SupplyCrate>();
        }

        private void OnEnable()
        {
            if (crate != null) crate.OnOpenedChanged += HandleOpenedChanged;
            Refresh();
        }

        private void OnDisable()
        {
            if (crate != null) crate.OnOpenedChanged -= HandleOpenedChanged;
        }

        /// <summary>设置沉浸状态并刷新显隐。</summary>
        public void SetHudHidden(bool hidden)
        {
            hudHidden = hidden;
            Refresh();
        }

        /// <summary>未开且未沉浸时显示标记；标记或箱子缺失时容错。</summary>
        public void Refresh()
        {
            // 未激活物体上 Awake 还没跑过：此时补取一次，只在事件驱动路径上发生。
            if (crate == null) crate = GetComponent<SupplyCrate>();
            if (marker == null) return;
            bool visible = !hudHidden && crate != null && !crate.IsOpened;
            if (marker.activeSelf != visible) marker.SetActive(visible);
        }

        private void HandleOpenedChanged(SupplyCrate source) => Refresh();
    }
}
