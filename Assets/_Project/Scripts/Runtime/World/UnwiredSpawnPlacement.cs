// 职责：ISpawnPlacement 的默认实现——把落点报出来并**明确说没摆成**，不做任何假装成功的事。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：见 ISpawnPlacement 的类注释（工程里没有这个能力）。
//   2. 扩展不行：不给状态一个「找不到锚点就静默返回 true」的默认值——那会让「玩家没被摆到出生点」
//      表现成「一切正常但人不在场上」，比崩更难查（PRP §2.1 对「悄悄进错图」的同一口径）。
//   3. 所以默认实现只做一件事：**说清缺什么**。等场景波把锚点摆进场景，写一个真实现替换它即可
//      （替换点只有 WorldInstaller 里那一行注册）。
using Game.Core.Logging;

namespace Game.World
{
    /// <summary>
    /// 未接线的默认放置策略：一定返回 false 并把「该摆到哪、还缺什么」记进日志与原因。
    /// <para>
    /// 换掉它的条件（场景波）：场景里按约定摆好了出生点锚点物体、且有一个实现能拿到玩家与相机的引用。
    /// </para>
    /// </summary>
    public sealed class UnwiredSpawnPlacement : ISpawnPlacement
    {
        public bool TryPlace(WorldSpawnTarget spawn, WorldTransitionRequest request, out string reason)
        {
            string where = spawn == null ? "(没有落点)" : $"{spawn.SceneKey}::{spawn.SpawnId}";
            string arrival = request == null ? WorldRules.ArrivalDefault : request.ArrivalMethod;

            reason = $"出生点放置还没接线（默认实现 UnwiredSpawnPlacement）：应该把玩家摆到「{where}」"
                     + $"（到达方式「{arrival}」）并把相机对准，但场景侧没有可用的实现。"
                     + "要做的：① 在世界场景里按出生点 id 摆锚点物体；② 写一个 ISpawnPlacement 实现"
                     + "（拿玩家与相机的引用、按 id 找锚点）；③ 在 WorldInstaller 里把注册换成它。";

            Log.Warn($"出生点放置未接线：目标「{where}」，{nameof(UnwiredSpawnPlacement)} 不做任何摆放。");
            return false;
        }
    }
}
