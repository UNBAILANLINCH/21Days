// 职责：出生点放置策略的契约——把「玩家该站哪、相机该看哪」这一步从状态里抽出来，做成可注入的一步。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：工程里没有「按 id 找锚点把人摆过去」的能力。EncounterSceneView.PlayerStart 是遭遇场景专用的
//      固定起点（不是按 id 查锚点），QuestSceneBinder 解析的是任务指引锚点，两者都不是出生点。
//   2. 扩展不行：把「找锚点变换、设玩家位置、把相机对准」写进 WorldSceneState 会让状态依赖具体场景物体，
//      而**本波场景里根本没有出生点锚点物体**（下一波做场景时才有）——那样状态在 EditMode 里一步都测不了。
//   3. 所以做成接口：状态只负责把落点交出去并处理「没摆成」这个分支，真正的摆人留给场景侧的实现。
namespace Game.World
{
    /// <summary>
    /// 出生点放置策略。实现在场景侧（要找锚点物体、要持有玩家与相机的引用），状态只调它。
    /// <para>
    /// <b>没摆成是正常分支</b>：本波场景里还没有出生点锚点，默认实现（<see cref="UnwiredSpawnPlacement"/>）
    /// 一定返回 false 并在 <c>reason</c> 里说清缺什么；状态会把它当失败报出来（不静默继续）。
    /// </para>
    /// </summary>
    public interface ISpawnPlacement
    {
        /// <summary>
        /// 把玩家摆到 <paramref name="spawn"/>、让相机对准。返回 false 表示没摆成。
        /// </summary>
        /// <param name="spawn">选中的落点（场景键 + 出生点 id），由 <see cref="WorldRules"/> 算出。</param>
        /// <param name="request">本次转场的来源与到达方式（两界转场要演黑幕 / 过渡文字时用它）。</param>
        /// <param name="reason">没摆成时的原因（人话，指到缺什么）；摆成了写空串。</param>
        bool TryPlace(WorldSpawnTarget spawn, WorldTransitionRequest request, out string reason);
    }
}
