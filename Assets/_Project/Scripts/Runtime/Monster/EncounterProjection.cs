// 职责：遭遇表现层的纯投影裁决——贴地高度、按位移方向的翻转，不依赖 MonoBehaviour 与场景状态。
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
    }
}
