// 职责：物资箱模块的表现与规则参数（交互半径、任务上报键、提示 / 通知文案、头顶标记抬高）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：QuestConfig / DialogueConfig 各管本模块文案与参数，物资箱的半径与奖励文案不属于它们。
//   2. 扩展不行：塞进 QuestConfig 会让任务模块认识「物资箱」，依赖方向是 Loot → Quest，反过来不行。
//   所以 Loot 模块首次落地（PRP/exploration-whitebox 3.1）新建自己的 SO。
using UnityEngine;

namespace Game.Loot
{
    /// <summary>物资箱配置。资产放 <c>Data/Loot/LootConfig.asset</c>，拖到 Boot 场景 <c>GameBootstrap</c> 的 <see cref="LootInstaller"/> 上。运行时只读。</summary>
    [CreateAssetMenu(menuName = "21Days/Loot/LootConfig", fileName = "LootConfig")]
    public sealed class LootConfig : ScriptableObject
    {
        [Tooltip("交互半径（米）：玩家锚点与未开箱子的距离小于该值时成为焦点。")]
        [SerializeField, Min(0f)] private float crateInteractRadius = 1.5f;

        [Tooltip("开箱成功后向任务系统上报的 Counter 目标键（支线 2002 用 \"crate\"）。")]
        [SerializeField] private string crateQuestKey = "crate";

        [Tooltip("焦点在箱子上时 HUD 显示的交互提示；按键提示随 Gameplay/Interact 的绑定手写，改绑定时同步。")]
        [SerializeField] private string promptText = "E 打开物资箱";

        [Tooltip("开箱通知的标题。")]
        [SerializeField] private string rewardTitle = "获得物资";

        [Tooltip("开箱通知的正文格式：{0} = 物品名，{1} = 数量。")]
        [SerializeField] private string rewardBodyFormat = "{0} ×{1}";

        [Tooltip("头顶标记在箱子顶部之上再抬高的距离（米）。")]
        [SerializeField, Min(0f)] private float markerLift = 0.3f;

        public float CrateInteractRadius => crateInteractRadius;
        public string CrateQuestKey => crateQuestKey;
        public string PromptText => promptText;
        public string RewardTitle => rewardTitle;
        public string RewardBodyFormat => rewardBodyFormat;
        public float MarkerLift => markerLift;
    }
}
