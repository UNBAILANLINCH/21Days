// 职责：遭遇场景的白盒遮挡碰撞——把玩家纸片本帧的场景位移按「先 X 后 Z」两次胶囊扫掠截断，贴墙时自然滑动。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：EncounterProjection 是只做数值裁决的纯函数（贴地高度 / 翻转），不碰物理；
//   2. 扩展不行：塞进 EncounterSceneView 会让视图同时承担投影、染色、调试界面与扫掠解算，单独成类便于换成正式实现。
// 为什么不进确定性内核（PRP/exploration-whitebox 3.5，本期白盒取舍）：
//   逻辑层（PlayerModel / EncounterStep）只有 XY、没有障碍数据，把障碍搬进内核要先有「关卡碰撞数据 + 定点/确定性几何」，
//   不是本期白盒的范围。这里用 Unity 物理在表现层解算，再经 EncounterSceneView.OnPlayerBlocked 回写逻辑位置：
//   同机同场景可复现，跨机 / 跨平台不保证逐位一致（PhysX 浮点），回放若要跨设备对齐，正式版必须把障碍放进确定性内核。
using UnityEngine;

namespace Game.Monster
{
    public static class EncounterCollision
    {
        /// <summary>
        /// 从 <paramref name="from"/> 走到 <paramref name="to"/>（只看 XZ），撞到 <paramref name="mask"/> 层的碰撞体就停在其前方。
        /// 胶囊竖直覆盖「脚底 + bottomOffset」到「脚底 + topOffset」这一段（端点球心各往里收一个半径），
        /// bottomOffset 取 0.35 时 0.3 的台阶与坡面不算障碍。返回修正后的位置，y 取 from.y（贴地由调用方另做）。
        /// </summary>
        public static Vector3 Slide(Vector3 from, Vector3 to, float radius, float bottomOffset, float topOffset,
            LayerMask mask, float skin = 0.02f)
        {
            if (mask.value == 0 || radius <= 0f)
            {
                return new Vector3(to.x, from.y, to.z);
            }

            Vector3 position = SweepAxis(from, new Vector3(to.x - from.x, 0f, 0f), radius, bottomOffset, topOffset, mask, skin);
            position = SweepAxis(position, new Vector3(0f, 0f, to.z - from.z), radius, bottomOffset, topOffset, mask, skin);
            return position;
        }

        private static Vector3 SweepAxis(Vector3 feet, Vector3 delta, float radius, float bottomOffset, float topOffset,
            LayerMask mask, float skin)
        {
            float distance = delta.magnitude;
            if (distance <= 0f)
            {
                return feet;
            }

            Vector3 direction = delta / distance;
            float bottomCenter = bottomOffset + radius;
            // 竖直范围不足两个半径时退化为球：两端点重合在中间。
            float topCenter = topOffset - radius < bottomCenter ? bottomCenter : topOffset - radius;
            Vector3 point1 = feet + Vector3.up * bottomCenter;
            Vector3 point2 = feet + Vector3.up * topCenter;
            if (Physics.CapsuleCast(point1, point2, radius, direction, out RaycastHit hit, distance + skin, // lint-ok: 表现层白盒碰撞，结果经 OnPlayerBlocked 回写；不进确定性内核的取舍见文件头
                    mask, QueryTriggerInteraction.Ignore))
            {
                float allowed = hit.distance - skin;
                return allowed > 0f ? feet + direction * allowed : feet;
            }

            return feet + delta;
        }
    }
}
