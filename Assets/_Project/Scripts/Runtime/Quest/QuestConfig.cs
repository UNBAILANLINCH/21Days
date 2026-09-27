// 职责：任务系统的表现参数（屏幕边缘指引留白、悬浮偏移、距离刷新间隔、头顶标记高度与预制体地址、HUD 与面板的固定文案、接取 / 完成通知文案）。
// 为什么新建：任务系统首次落地（PRP/quest-system）；DialogueConfig / UIConfig 管的是对白与通用 UI，职责不同不能塞。
using UnityEngine;

namespace Game.Quest
{
    [CreateAssetMenu(menuName = "21Days/Quest/QuestConfig")]
    public sealed class QuestConfig : ScriptableObject
    {
        [Tooltip("目标在屏幕外时，指引标识中心贴屏幕边缘的留白（参考分辨率像素）。取值依据：须 ≥ 箭头公转半径 48 + 箭头半宽 14 + 余量，" +
                 "否则箭头转到朝外一侧时被屏幕边裁掉；距离文本总在箭头对面（朝屏内），不参与这个下限。")]
        [SerializeField, Min(0)] private float edgeMargin = 72f;

        [Tooltip("目标在屏幕内时，指引标记相对目标屏幕点向上的偏移（参考分辨率像素）。")]
        [SerializeField] private float hoverOffset = 80f;

        [Tooltip("指引上距离数字的刷新间隔（秒），避免每帧改文本。")]
        [SerializeField, Min(0.02f)] private float distanceRefreshInterval = 0.2f;

        [Tooltip("NPC 目标的头顶标记在其碰撞体顶部之上再抬高多少（米）。")]
        [SerializeField, Min(0)] private float markerLift = 0.3f;

        [Tooltip("地点目标（或没有碰撞体的 NPC）的头顶标记离地高度（米）。")]
        [SerializeField, Min(0)] private float locationMarkerHeight = 1.5f;

        [Tooltip("世界空间任务目标标记预制体的 Addressables 地址（根上挂 QuestTargetMarker）。")]
        [SerializeField] private string targetMarkerAddress = "QuestTargetMarker";

        [Tooltip("没有追踪任务时 HUD 显示的文案。")]
        [SerializeField] private string untrackedLabel = "未追踪任务";

        [Tooltip("主线任务的类别标签文案。")]
        [SerializeField] private string mainKindLabel = "主线";

        [Tooltip("支线任务的类别标签文案。")]
        [SerializeField] private string sideKindLabel = "支线";

        [Tooltip("接取任务时通知的标题格式，{0} 为任务标题。")]
        [SerializeField] private string activatedNotificationFormat = "接取任务：{0}";

        [Tooltip("完成任务时通知的标题格式，{0} 为任务标题。")]
        [SerializeField] private string completedNotificationFormat = "任务完成：{0}";

        public float EdgeMargin => edgeMargin;
        public float HoverOffset => hoverOffset;
        public float DistanceRefreshInterval => distanceRefreshInterval;
        public float MarkerLift => markerLift;
        public float LocationMarkerHeight => locationMarkerHeight;
        public string TargetMarkerAddress => targetMarkerAddress;
        public string UntrackedLabel => untrackedLabel;
        public string MainKindLabel => mainKindLabel;
        public string SideKindLabel => sideKindLabel;
        public string ActivatedNotificationFormat => activatedNotificationFormat;
        public string CompletedNotificationFormat => completedNotificationFormat;
    }
}
