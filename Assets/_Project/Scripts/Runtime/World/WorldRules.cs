// 职责：出生点选择的纯规则——给定「目标场景 + 目标出生点 id + 到达方式」，算出该用哪个出生点，
//   并给出可判定的结果（成功 / 落在哪个出生点 / 失败原因），不抛异常、不碰场景、不读表以外的任何状态。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：工程里没有任何「按场景选落点」的规则；MonsterEncounterState 的落点是遭遇快照里记死的坐标。
//   2. 扩展不行：WorldCatalog 是「表 → 只读查询」，把选择规则塞进去，改一条规则就要动表适配层；
//      而且规则要在 EditMode 里用假表数据测各种组合，和「读真表」分开才测得全。
//   3. 所以规则单独一份，表只提供「这个场景有哪些出生点、哪一个是默认」。
using System;

namespace Game.World
{
    /// <summary>
    /// 出生点选择规则。**纯函数**：输入是 <see cref="WorldCatalog"/> 的只读查询结果，输出是一个
    /// <see cref="SpawnResolution"/>；不抛异常、不写状态。
    /// <para>
    /// 缺失时的行为（唯一口径，测试钉住）：**先回退、回退不了才失败**。
    /// ① 指定的出生点 id 在目标场景里存在 → 用它（<see cref="SpawnResolution.UsedFallback"/> = false）；
    /// ② 指定为空或不存在 → 用目标场景的 <c>default_spawn_id</c>（<see cref="SpawnResolution.UsedFallback"/> = true）；
    /// ③ 目标场景在表里查不到、或它连默认出生点也没有 → 失败，
    ///    <see cref="SpawnResolution.Error"/> 说清是哪一种、差值是多少。
    /// 调用方拿 <see cref="SpawnResolution.Success"/> 判，要抛异常自己抛（<see cref="Resolve"/> 就是替它抛）。
    /// </para>
    /// <para>
    /// 「到达方式」是 <c>TbPortal.arrival_method</c> 那一列的原文。本类**不把它当查询条件**：
    /// 目标出生点由传送点那一行显式指定（<c>target_spawn_id</c>），到达方式只用于回退到默认出生点时的
    /// 事件与埋点。理由：真源里两界转场只有一条（10_两界与场景结构.md:148 R20），
    /// 「从哪个传送点来」决定落点这件事原文没有依据，先按「表里写什么就是什么」，
    /// 等策划拍板再往规则里加分支。
    /// </para>
    /// </summary>
    public static class WorldRules
    {
        /// <summary>到达方式：新开局 / 读档恢复，没有经由任何传送点。</summary>
        public const string ArrivalDefault = "default";

        /// <summary>到达方式：经由两界转场（TbPortal.arrival_method 的取值）。</summary>
        public const string ArrivalMirror = "mirror";

        /// <summary>
        /// 算出生点；失败时抛 <see cref="WorldResolveException"/>（消息里带上原因与差值）。
        /// 调用方要把失败当流程分支处理时用非抛异常的 <see cref="TryResolveSpawn"/>。
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="catalog"/> 为 null。</exception>
        /// <exception cref="WorldResolveException">目标场景或出生点解析不出来。</exception>
        public static WorldSpawnTarget Resolve(WorldCatalog catalog, string targetSceneKey, string targetSpawnId, string arrivalMethod)
        {
            SpawnResolution resolution = TryResolveSpawn(catalog, targetSceneKey, targetSpawnId, arrivalMethod);
            if (!resolution.Success)
            {
                throw new WorldResolveException(resolution.Error);
            }

            return resolution.Target;
        }

        /// <summary>
        /// 算出生点，不抛异常。失败原因写在 <see cref="SpawnResolution.Error"/>。
        /// </summary>
        /// <param name="catalog">世界表只读查询。</param>
        /// <param name="targetSceneKey">目标场景键（TbScene.scene_key）。</param>
        /// <param name="targetSpawnId">目标出生点 id；空串 = 直接用默认出生点。</param>
        /// <param name="arrivalMethod">到达方式原文（<c>TbPortal.arrival_method</c>），只用于回退路径的说明。</param>
        public static SpawnResolution TryResolveSpawn(WorldCatalog catalog, string targetSceneKey,
            string targetSpawnId, string arrivalMethod)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (string.IsNullOrEmpty(targetSceneKey))
            {
                return SpawnResolution.Failed("没有给出目标场景键（TbScene.scene_key）。");
            }

            if (!catalog.TryGetScene(targetSceneKey, out global::cfg.world.Scene scene))
            {
                return SpawnResolution.Failed(
                    $"目标场景「{targetSceneKey}」不在 TbScene 里，没有出生点可选"
                    + "（表：Tables/Defines/world.xml 的 TbScene；数据：Tables/Data/world/scene/）。");
            }

            string arrival = string.IsNullOrEmpty(arrivalMethod) ? ArrivalDefault : arrivalMethod;

            // ① 指名的出生点存在就用它。
            if (!string.IsNullOrEmpty(targetSpawnId) && Contains(scene, targetSpawnId))
            {
                return SpawnResolution.Resolved(targetSceneKey, targetSpawnId, false);
            }

            // ② 回退到本场景的默认出生点。
            string fallback = scene.DefaultSpawnId;
            if (!string.IsNullOrEmpty(fallback))
            {
                return SpawnResolution.Resolved(targetSceneKey, fallback, true);
            }

            // ③ 回退不了：说清是「指名的那个不存在」还是「根本是空手来的」。
            //    两个分支**必须都带上同一句「没有默认出生点，回退不了」**：调用方按这句话判断「是没人可回退」，
            //    两种说法（「没有默认出生点」/「默认出生点也是空的」）等于没有口径，改一边就会漏判另一边。
            return string.IsNullOrEmpty(targetSpawnId)
                ? SpawnResolution.Failed(
                    $"场景「{targetSceneKey}」没有默认出生点，回退不了（TbScene.default_spawn_id 为空），"
                    + $"到达方式「{arrival}」也没有指定出生点，落不下去。")
                : SpawnResolution.Failed(
                    $"场景「{targetSceneKey}」里没有出生点「{targetSpawnId}」（它有：{JoinSpawns(scene)}），"
                    + $"而它没有默认出生点，回退不了。到达方式「{arrival}」。");
        }

        private static bool Contains(global::cfg.world.Scene scene, string spawnId)
        {
            System.Collections.Generic.List<string> points = scene.SpawnPoints;
            return points != null && points.Contains(spawnId);
        }

        private static string JoinSpawns(global::cfg.world.Scene scene)
        {
            System.Collections.Generic.List<string> points = scene.SpawnPoints;
            return points == null || points.Count == 0 ? "一个都没有" : string.Join("、", points);
        }
    }
}
