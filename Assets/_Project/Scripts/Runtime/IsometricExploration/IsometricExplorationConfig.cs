// 职责：保存 2.5D 探索场景需要现场调节的相机与探索 HUD 参数（移动速度归 PlayerConfig）。
// 为什么新建：现有 PlayerConfig 面向战斗规则，不能承载独立场景原型的表现参数。
using UnityEngine;

namespace Game.IsometricExploration
{
    [CreateAssetMenu(menuName = "21Days/IsometricExploration/Config")]
    public sealed class IsometricExplorationConfig : ScriptableObject
    {
        [SerializeField, Min(0.01f), Tooltip("摄像机跟随目标时的缓动时间")]
        private float cameraSmoothTime = 0.2f;

        // —— PRP/exploration-whitebox 波 3 追加：探索 HUD 的控件、万向标与重置文案。
        [SerializeField, Tooltip("开发用开关：桌面平台也显示触屏控件（摇杆 / 三键 / 走跑按钮），默认关；正式只在触屏平台显示")]
        private bool showStickOnDesktop = false;
        [SerializeField, Tooltip("屏幕四周对全部屏外兴趣点画贴边标记；默认关（用户决定：会显得屏幕乱，任务追踪指引由 Quest 负责），"
            + "调试或将来做附近提示时再开")]
        private bool showCompass = false;
        [SerializeField, Min(0f), Tooltip("万向标贴屏幕边时的内缩像素")]
        private float compassEdgeMargin = 48f;
        [SerializeField, Min(0), Tooltip("万向标标签最多显示几个字，超出截断；0 = 不显示标签")]
        private int compassLabelMax = 6;
        [SerializeField, Tooltip("奔跑模式下走跑按钮的标签")]
        private string runLabel = "奔跑";
        [SerializeField, Tooltip("散步模式下走跑按钮的标签")]
        private string walkLabel = "散步";
        [SerializeField, Tooltip("重置进度确认弹窗的正文")]
        private string resetMessage = "重置故事进度？任务与物资箱将回到初始状态。";
        [SerializeField, Tooltip("重置确认弹窗的确认按钮文字")]
        private string resetConfirmText = "重置";
        [SerializeField, Tooltip("重置确认弹窗的取消按钮文字")]
        private string resetCancelText = "取消";

        // —— PRP/exploration-whitebox 波 10 追加：遮挡半透明的探测粗细。
        [SerializeField, Min(0f), Tooltip("遮挡探测的球形扫掠半径（米）：相机 → 玩家胸口扫一根这么粗的「射线」，擦到的 SceneOccluder 都变半透明。"
            + "越大越早淡出、画面越通透；0 退化成细射线（只有正挡住胸口才淡出）")]
        private float occluderProbeRadius = 1f;

        public bool ShowStickOnDesktop => showStickOnDesktop;
        public bool ShowCompass => showCompass;
        public float CompassEdgeMargin => compassEdgeMargin;
        public int CompassLabelMax => compassLabelMax;
        public string RunLabel => runLabel;
        public string WalkLabel => walkLabel;
        public string ResetMessage => resetMessage;
        public string ResetConfirmText => resetConfirmText;
        public string ResetCancelText => resetCancelText;

        public float CameraSmoothTime => cameraSmoothTime;
        public float OccluderProbeRadius => occluderProbeRadius;
    }
}
