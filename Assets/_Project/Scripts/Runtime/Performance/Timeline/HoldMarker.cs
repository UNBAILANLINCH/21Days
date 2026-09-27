// 职责：「等待输入」标记——时间轴走到这里时通知舞台暂停，等玩家按确认后继续。
// 为什么新建（复用 → 扩展 → 新建）：Timeline 自带的 SignalEmitter 要在场景里配 SignalReceiver + SignalAsset 再接 UnityEvent，
//   动画师每段演出都要手接一遍、容易漏；用一个专用标记类型，舞台按类型识别即可，零接线。
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Game.Performance.Timeline
{
    /// <summary>
    /// 停顿标记。放在时间轴顶部的 Markers 区（通知发给 PlayableDirector 所在物体，即 <see cref="PerformanceStage"/>）。
    /// 只在播放时触发一次，编辑模式拖时间线不触发（不设 TriggerInEditMode）。
    /// </summary>
    public sealed class HoldMarker : Marker, INotification, INotificationOptionProvider
    {
        [Tooltip("备注（只给动画师看，如「等玩家读完第二句」），运行时不显示。")]
        [SerializeField] private string label = string.Empty;

        public string Label => label;

        public PropertyName id => new PropertyName();

        NotificationFlags INotificationOptionProvider.flags => NotificationFlags.TriggerOnce;
    }
}
