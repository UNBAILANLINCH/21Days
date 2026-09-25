// 职责：解析出的任务目标——测距用的位置（脚底 / 地点点位）与标记 / 屏幕投影用的锚点（头顶）。
// 为什么新建：原先 TryResolveTarget 只给一个 Vector3，测距与头顶标记要的是两个不同的点；
//   复用 QuestGuidance 不对（那是屏幕空间的结果），塞进 QuestSceneBinder 做嵌套类型又会让调用方写长名字。
using UnityEngine;

namespace Game.Quest
{
    /// <summary>任务目标的两个世界坐标。值类型，每帧解析无分配。</summary>
    public readonly struct QuestTarget
    {
        public QuestTarget(Vector3 position, Vector3 anchor)
        {
            Position = position;
            Anchor = anchor;
        }

        /// <summary>测距用：NPC 根物体位置（脚底）或地点位置。</summary>
        public Vector3 Position { get; }

        /// <summary>头顶标记与屏幕投影用：NPC 碰撞体顶部再抬高一点，或地点上方固定高度。</summary>
        public Vector3 Anchor { get; }
    }
}
