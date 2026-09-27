// 职责：遭遇表现层的纯投影裁决——贴地高度、按位移方向的翻转、两逻辑 tick 之间的渲染插值与碰撞回写的分轴裁决，
//   不依赖 UnityEngine、MonoBehaviour 与场景状态。
// 插值为什么扩展进本类（复用 → 扩展 → 新建）：插值与贴地 / 翻转同属「逻辑位置 → 本帧渲染位置」的纯表现裁决，
//   职责名说得通；GameMath 是确定性内核的数学转发层，不该放表现层专用规则。
// 为什么新建：复用——工程里没有可复用的纯表现投影工具类；扩展——把这两条判断留在 EncounterSceneView 上
// 会被迫暴露成公开实例 API，且 Game.Runtime 没有对 Game.Tests.EditMode 开 InternalsVisibleTo，
// 无法设为 internal 单测，只能新建一个不依赖 MonoBehaviour 的静态类。
namespace Game.Monster
{
    public static class EncounterProjection
    {
        /// <summary>贴地高度裁决：下落不限；上抬不超过 maxStepHeight（含相等）才采用新高度，超过视为墙顶或家具，保持原高度。</summary>
        public static float ResolveGroundY(float currentY, float groundY, float maxStepHeight) =>
            groundY - currentY <= maxStepHeight ? groundY : currentY;

        /// <summary>按场景 X 位移裁决翻转：左移超阈值翻转为 true，右移超阈值还原为 false，阈值内保持当前朝向。</summary>
        public static bool ResolveFlipX(float previousX, float currentX, bool currentFlipX, float threshold)
        {
            if (currentX < previousX - threshold)
            {
                return true;
            }

            if (currentX > previousX + threshold)
            {
                return false;
            }

            return currentFlipX;
        }

        /// <summary>
        /// 插值比例：本帧处在上一 tick 与当前 tick 之间的哪个位置，结果钳在 [0, 1]。
        /// <paramref name="accumulator"/> 取 SimulationRunner.Accumulator（未满一 tick 的余量秒数）。
        /// 步长非正（拿不到推进器）时返回 1，即直接用当前 tick 位置；余量为负或 NaN 时返回 0。
        /// </summary>
        public static float InterpolationAlpha(float accumulator, float fixedDeltaTime)
        {
            if (!(fixedDeltaTime > 0f))
            {
                return 1f;
            }

            float alpha = accumulator / fixedDeltaTime;
            if (!(alpha > 0f))
            {
                return 0f;
            }

            return alpha < 1f ? alpha : 1f;
        }

        /// <summary>
        /// 两 tick 之间的渲染位置：Lerp(previous, current, alpha)。
        /// 两点距离超过 <paramref name="teleportDistance"/> 视为瞬移（读档 / 重置 / 回放挪位漏同步时的兜底），直接取 current。
        /// alpha ≥ 1 精确返回 current、≤ 0 精确返回 previous，不经浮点加减，避免「不插值」时出现舍入偏差。
        /// </summary>
        public static void InterpolatePosition(float previousX, float previousY, float currentX, float currentY,
            float alpha, float teleportDistance, out float x, out float y)
        {
            float dx = currentX - previousX;
            float dy = currentY - previousY;
            if (alpha >= 1f || dx * dx + dy * dy > teleportDistance * teleportDistance)
            {
                x = currentX;
                y = currentY;
                return;
            }

            if (alpha <= 0f)
            {
                x = previousX;
                y = previousY;
                return;
            }

            x = previousX + dx * alpha;
            y = previousY + dy * alpha;
        }

        /// <summary>
        /// 碰撞回写的分轴裁决（视图侧）：扫掠后该轴偏离期望值超过 <paramref name="tolerance"/> 才算被挡。
        /// 被挡的轴取修正值；没被挡的轴保留逻辑值 <paramref name="logic"/>，不把逻辑位置拉回到插值点。
        /// </summary>
        public static float ResolveBlockedAxis(float logic, float desired, float corrected, float tolerance)
        {
            float delta = corrected - desired;
            return delta > tolerance || delta < -tolerance ? corrected : logic;
        }

        /// <summary>
        /// 碰撞回写时上一 tick 位置的分轴裁决（逻辑侧）：该轴被改写（<paramref name="corrected"/> ≠ <paramref name="current"/>）
        /// 就把上一 tick 位置也设成同一值，下一帧不会从墙里倒插回来；没改写的轴保留原值，继续平滑插值。
        /// </summary>
        public static float CorrectPreviousAxis(float previous, float current, float corrected) =>
            corrected != current ? corrected : previous;
    }
}
