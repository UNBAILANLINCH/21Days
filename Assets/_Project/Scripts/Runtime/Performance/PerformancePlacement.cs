// 职责：世界模式演出的摆放值——把演出预制体实例摆到世界里的哪个位置、朝哪个方向。
// 为什么新建（复用 → 扩展 → 新建）：Unity 的 Pose 没有「未指定」语义，传 default 会把演出摆到原点；
//   PlayAsync 需要一个能区分「不摆」与「摆到原点」的值，塞进 PerformanceViewArgs / PerformancePolicy 都名实不符，只能新建。

using UnityEngine;

namespace Game.Performance
{
    /// <summary>
    /// 演出摆放值。<see cref="None"/>（即 <c>default</c>）= 不摆放，实例保持预制体自身位姿；
    /// 只对 <see cref="PerformanceStageMode.World"/> 模式生效，叠加模式传入会被忽略并告警。
    /// </summary>
    public readonly struct PerformancePlacement
    {
        /// <summary>不摆放。</summary>
        public static readonly PerformancePlacement None = default;

        public PerformancePlacement(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
            HasValue = true;
        }

        /// <summary>世界坐标位置。</summary>
        public Vector3 Position { get; }

        /// <summary>世界朝向。</summary>
        public Quaternion Rotation { get; }

        /// <summary>是否指定了摆放；false 时位置 / 朝向无意义。</summary>
        public bool HasValue { get; }

        /// <summary>取某个锚点物体的世界位姿；传 null 返回 <see cref="None"/>。</summary>
        public static PerformancePlacement FromTransform(Transform anchor)
        {
            // Transform 是 UnityEngine.Object，判空只用 == null。
            return anchor == null ? None : new PerformancePlacement(anchor.position, anchor.rotation);
        }
    }
}
