// 职责：场景里的通灵视环境区域——一个盒子范围 + 雨 / 夜 / 昏暗三个静态条件；玩家逻辑位置落在范围内且任一条件为真时通灵视生效。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：没有天气 / 昼夜系统（PRD 非目标），既有场景组件（SceneOccluder、QuestLocation）都不带环境条件。
//   2. 扩展不行：往 QuestLocation 加条件会让任务模块认识通灵视。
//   所以新建区域标记。只用 BoxCollider 的尺寸算范围，不走物理回调（玩家不是物理驱动，PRP/mirror-core 2.3）。
using UnityEngine;

namespace Game.Mirror
{
    /// <summary>
    /// 通灵视区域。范围取 <see cref="area"/> 的 center / size 经本物体变换后的包围盒（不读 Collider.bounds：
    /// 未激活或禁用的碰撞体 bounds 为零）。由 <see cref="MirrorSceneBinder"/> 在场景加载时登记。
    /// </summary>
    public sealed class SpiritSightZone : MonoBehaviour
    {
        [Tooltip("区域范围；只读它的 center / size，建议勾 isTrigger，不参与物理判定。为空时取同物体上的 BoxCollider。")]
        [SerializeField] private BoxCollider area;

        [Tooltip("区域内在下雨。")]
        [SerializeField] private bool rain;

        [Tooltip("区域内是夜晚。")]
        [SerializeField] private bool night;

        [Tooltip("区域内昏暗。")]
        [SerializeField] private bool dim = true;

        public bool Rain => rain;
        public bool Night => night;
        public bool Dim => dim;

        /// <summary>三个条件任一为真。</summary>
        public bool IsConditionMet => SpiritSightRules.IsActive(rain, night, dim);

        /// <summary>
        /// 取范围在场景坐标下的轴对齐包围盒两角。没有 BoxCollider 时返回 false。
        /// 只在绑定器判定时调用（场景登记后按需），不在每帧 Update 里跑。
        /// </summary>
        public bool TryGetWorldCorners(out Vector3 min, out Vector3 max)
        {
            // BoxCollider 是 UnityEngine.Object，判空只用 == null。
            if (area == null) area = GetComponent<BoxCollider>();
            if (area == null)
            {
                min = Vector3.zero;
                max = Vector3.zero;
                return false;
            }

            Transform t = area.transform;
            Vector3 half = area.size * 0.5f;
            Vector3 center = area.center;
            min = t.TransformPoint(center + new Vector3(-half.x, -half.y, -half.z));
            max = min;
            for (int i = 1; i < 8; i++)
            {
                var local = new Vector3(
                    (i & 1) == 0 ? -half.x : half.x,
                    (i & 2) == 0 ? -half.y : half.y,
                    (i & 4) == 0 ? -half.z : half.z);
                Vector3 corner = t.TransformPoint(center + local);
                min = Vector3.Min(min, corner);
                max = Vector3.Max(max, corner);
            }

            return true;
        }
    }
}
