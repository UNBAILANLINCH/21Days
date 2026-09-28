// 职责：通灵视的纯规则——环境条件任一为真即生效、只有妖且在半径内才可见、逻辑点是否落在区域矩形内。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：MonsterRules 的感知是「怪看玩家」，方向相反，且带警戒值与视线，不适用。
//   2. 扩展不行：塞进 MirrorRules 会把「有没有」和「是谁」两件事混进一个类（PRD：通灵视回答有没有，镜回答是谁）。
//   与照镜共用 MirrorSubject 登记与模块（PRP/mirror-core 2.1），规则单独一个类，EditMode 可测。
using Game.Core.Simulation;
using UnityEngine;

namespace Game.Mirror
{
    /// <summary>通灵视规则。全部静态、无分配。</summary>
    public static class SpiritSightRules
    {
        /// <summary>雨 / 夜 / 昏暗任一为真即生效（PRD Q4：「需要镜面」不作条件）。</summary>
        public static bool IsActive(bool rain, bool night, bool dim) => rain || night || dim;

        /// <summary>只有妖、且与玩家的逻辑距离 ≤ radius（含边界）才出现影子提示；radius ≤ 0 时谁都不可见。</summary>
        public static bool Visible(MirrorSubjectKind kind, Vector2 subjectPosition, Vector2 playerPosition, float radius)
        {
            if (kind != MirrorSubjectKind.Yao || radius <= 0f) return false;
            return GameMath.SqrMagnitude(subjectPosition - playerPosition) <= radius * radius;
        }

        /// <summary>逻辑点是否在两角围成的轴对齐矩形内（含边界）；两角先后顺序任意。</summary>
        public static bool InZone(Vector2 point, Vector2 cornerA, Vector2 cornerB)
        {
            float minX = GameMath.Min(cornerA.x, cornerB.x);
            float maxX = GameMath.Max(cornerA.x, cornerB.x);
            float minY = GameMath.Min(cornerA.y, cornerB.y);
            float maxY = GameMath.Max(cornerA.y, cornerB.y);
            return point.x >= minX && point.x <= maxX && point.y >= minY && point.y <= maxY;
        }
    }
}
