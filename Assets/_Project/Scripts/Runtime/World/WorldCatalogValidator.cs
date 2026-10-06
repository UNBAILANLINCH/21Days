// 职责：世界表（TbScene / TbRegion / TbPortal）的全表校验——枚举白名单、引用完整性、两界同构的成对关系、
//   「未实装的场景不许写地址」，以及把未实装的场景单独收集出来（它是状态不是错误）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：工程里没有别的表有这种「三张表互相引用 + 成对关系」的校验；QuestTableValidator 是编辑器侧
//      给 Excel 草稿用的，跑在 UnityEditor 里，Runtime 用不了。
//   2. 扩展不行：WorldCatalog 的职责是「表 → 只读查询」，把十几条校验塞进去，改一条规则就要动查询类；
//      而且校验要在 EditMode 里用假表数据逐条测（改坏一行看报不报），单独一份才测得干净。
//   3. 判据同 YaoCatalog 的做法：白名单校验放在读表那道门上（WorldCatalog.EnsureTableRead），本类是那段实现。
using System;
using System.Collections.Generic;

namespace Game.World
{
    /// <summary>
    /// 世界表校验器（静态、无状态）。输入一份 <c>cfg.Tables</c>，输出问题清单与未实装场景清单；
    /// 不抛异常、不读别的表、不碰磁盘。
    /// <para>
    /// 校验规则与真源出处（逐条）：
    /// ① 未实装的场景不许写地址、实装的必须有地址 —— 现状是 Addressables Scenes 组只有地址 IsometricEncounter
    ///    （Assets/AddressableAssetsData/AssetGroups/Scenes.asset:19–24），写了别的地址就是空头支票。
    /// ② region_ids / portal_ids / spawn_points 必须真实存在 —— 三张表的互相引用
    ///    （Tables/Defines/world.xml 的列注释；区域口径见 10_两界与场景结构.md:133 R5、:136 R8）。
    /// ③ 主图区域必须成对互指且指向对面世界 —— 同文 :132 R4「两张俯视图格局完全对应」。
    /// ④ 主图区域必须列在所属场景的 region_ids 里 —— 同文 :133 R5（3 / 2.5 / 2 层都在俯视图上）。
    /// ⑤ 单场景专属区域不许写 scene_key，战斗关延伸区域必须写 —— 同文 :146 R18、:131 R3。
    /// ⑥ 出入口的触发方式与到达方式必须一致 —— 同文 :153「两界切换的触发 [待定]」：进入范围的出口不可能
    ///    产出「交互」这种到达方式。
    /// </para>
    /// </summary>
    public static class WorldCatalogValidator
    {
        /// <summary>世界表数据的校验入口。</summary>
        /// <param name="tables">Luban 生成的表根；构造可参考 ConfigService.BuildTables。</param>
        public static WorldValidationResult Validate(global::cfg.Tables tables)
        {
            if (tables == null)
            {
                throw new ArgumentNullException(nameof(tables));
            }

            var problems = new List<WorldValidationIssue>();
            var notImplemented = new List<string>();

            IReadOnlyList<global::cfg.world.Scene> scenes = tables.TbScene.DataList;
            IReadOnlyList<global::cfg.world.Region> regions = tables.TbRegion.DataList;
            IReadOnlyList<global::cfg.world.Portal> portals = tables.TbPortal.DataList;

            if (scenes.Count == 0)
            {
                problems.Add(new WorldValidationIssue("TbScene", "一张场景都没有；世界表没数据（Tables/Data/world/scene/）。"));
            }

            // 索引：引用完整性要在 O(1) 里查，且主键为空 / 重复本身也是问题。
            Dictionary<string, global::cfg.world.Scene> sceneByKey = SceneIndex(scenes, problems, notImplemented);
            Dictionary<string, global::cfg.world.Region> regionById = RegionIndex(regions, problems);
            Dictionary<string, global::cfg.world.Portal> portalById = PortalIndex(portals, problems);

            ValidateScenes(scenes, regionById, portalById, problems);
            ValidateRegions(regions, sceneByKey, regionById, problems);
            ValidatePortals(portals, sceneByKey, problems);

            return WorldValidationResult.Of(problems, notImplemented);
        }

        private static Dictionary<string, global::cfg.world.Scene> SceneIndex(IReadOnlyList<global::cfg.world.Scene> scenes,
            List<WorldValidationIssue> problems, List<string> notImplemented)
        {
            var map = new Dictionary<string, global::cfg.world.Scene>(scenes.Count, StringComparer.Ordinal);
            for (int i = 0; i < scenes.Count; i++)
            {
                global::cfg.world.Scene scene = scenes[i];
                if (AddIndex(map, scene.SceneKey, scene, "TbScene", "scene_key", problems))
                {
                    if (!scene.Implemented)
                    {
                        notImplemented.Add(scene.SceneKey);
                    }
                }
            }

            return map;
        }

        private static Dictionary<string, global::cfg.world.Region> RegionIndex(IReadOnlyList<global::cfg.world.Region> regions,
            List<WorldValidationIssue> problems)
        {
            var map = new Dictionary<string, global::cfg.world.Region>(regions.Count, StringComparer.Ordinal);
            for (int i = 0; i < regions.Count; i++)
            {
                AddIndex(map, regions[i].RegionId, regions[i], "TbRegion", "region_id", problems);
            }

            return map;
        }

        private static Dictionary<string, global::cfg.world.Portal> PortalIndex(IReadOnlyList<global::cfg.world.Portal> portals,
            List<WorldValidationIssue> problems)
        {
            var map = new Dictionary<string, global::cfg.world.Portal>(portals.Count, StringComparer.Ordinal);
            for (int i = 0; i < portals.Count; i++)
            {
                AddIndex(map, portals[i].PortalId, portals[i], "TbPortal", "portal_id", problems);
            }

            return map;
        }

        private static bool AddIndex<TRow>(Dictionary<string, TRow> map, string key, TRow row,
            string tableName, string columnName, List<WorldValidationIssue> problems)
        {
            string label = tableName + "." + (string.IsNullOrEmpty(key) ? "(空主键第 " + map.Count + " 行)" : key);
            if (string.IsNullOrEmpty(key))
            {
                problems.Add(new WorldValidationIssue(label, $"{columnName} 不能为空（见 Tables/Defines/world.xml）。"));
                return false;
            }

            if (map.ContainsKey(key))
            {
                problems.Add(new WorldValidationIssue(label, $"{columnName}「{key}」重复，主键必须唯一。"));
                return false;
            }

            map.Add(key, row);
            return true;
        }

        private static void ValidateScenes(IReadOnlyList<global::cfg.world.Scene> scenes,
            Dictionary<string, global::cfg.world.Region> regionById,
            Dictionary<string, global::cfg.world.Portal> portalById,
            List<WorldValidationIssue> problems)
        {
            for (int i = 0; i < scenes.Count; i++)
            {
                global::cfg.world.Scene scene = scenes[i];
                string label = "TbScene." + scene.SceneKey;

                if (string.IsNullOrEmpty(scene.SceneKey))
                {
                    continue;
                }

                ValidateSceneAddress(scene, label, problems);
                ValidateSceneLists(scene, label, regionById, portalById, problems);
                ValidateSpawnPoints(scene, label, problems);
            }
        }

        // ① 实装与地址的对应关系。**这是「地址字段允许未实装、但校验要能报出来」那条要求的落点**。
        private static void ValidateSceneAddress(global::cfg.world.Scene scene, string label, List<WorldValidationIssue> problems)
        {
            bool hasAddress = !string.IsNullOrEmpty(scene.SceneAddress);

            // 场景地址写死成现有的那个地址：Addressables 里目前只有它一条（Scenes.asset:19–24），
            // 各场景都填它等于「两张地图都是同一张 demo 场景」，必须报出来而不是放行。
            if (scene.Implemented && !hasAddress)
            {
                problems.Add(new WorldValidationIssue(label,
                    "implemented=true 但 scene_address 是空的——实装的场景必须有 Addressables 地址；"
                    + "地址还没登记就先把它写成 implemented=false。"));
            }

            if (!scene.Implemented && hasAddress)
            {
                problems.Add(new WorldValidationIssue(label,
                    $"implemented=false 却写了 scene_address「{scene.SceneAddress}」——"
                    + "未实装的场景不许写地址（写了就等于宣称 Addressables 里有它，而 Scenes 组里目前只有 IsometricEncounter，"
                    + "见 Assets/AddressableAssetsData/AssetGroups/Scenes.asset:19–24）。场景实装了再把 implemented 改成 true 并填地址。"));
            }
        }

        // ② region_ids / region_names / portal_ids 的引用与长度。
        private static void ValidateSceneLists(global::cfg.world.Scene scene, string label,
            Dictionary<string, global::cfg.world.Region> regionById,
            Dictionary<string, global::cfg.world.Portal> portalById,
            List<WorldValidationIssue> problems)
        {
            List<string> regionIds = scene.RegionIds ?? new List<string>();
            List<string> regionNames = scene.RegionNames ?? new List<string>();

            if (regionIds.Count != regionNames.Count)
            {
                problems.Add(new WorldValidationIssue(label,
                    $"region_ids 有 {regionIds.Count} 项、region_names 有 {regionNames.Count} 项，两个列表必须一一对应。"));
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int j = 0; j < regionIds.Count; j++)
            {
                string regionId = regionIds[j];
                if (string.IsNullOrEmpty(regionId))
                {
                    problems.Add(new WorldValidationIssue(label, $"region_ids 第 {j + 1} 项是空的。"));
                    continue;
                }

                if (!seen.Add(regionId))
                {
                    problems.Add(new WorldValidationIssue(label, $"region_ids 里「{regionId}」出现了两次。"));
                }

                if (!regionById.ContainsKey(regionId))
                {
                    problems.Add(new WorldValidationIssue(label,
                        $"region_ids 里的「{regionId}」在 TbRegion 里不存在（区域要单独建一行，见 Tables/Data/world/region/）。"));
                    continue;
                }

                global::cfg.world.Region region = regionById[regionId];
                if (!string.Equals(region.SceneKey, scene.SceneKey, StringComparison.Ordinal))
                {
                    problems.Add(new WorldValidationIssue("TbRegion." + regionId,
                        $"它写在场景「{scene.SceneKey}」的 region_ids 里，但自己的 scene_key 是「{region.SceneKey}」，两边不一致。"));
                }
            }

            List<string> portalIds = scene.PortalIds ?? new List<string>();
            var seenPortals = new HashSet<string>(StringComparer.Ordinal);
            for (int j = 0; j < portalIds.Count; j++)
            {
                string portalId = portalIds[j];
                if (string.IsNullOrEmpty(portalId))
                {
                    problems.Add(new WorldValidationIssue(label, $"portal_ids 第 {j + 1} 项是空的。"));
                    continue;
                }

                if (!seenPortals.Add(portalId))
                {
                    problems.Add(new WorldValidationIssue(label, $"portal_ids 里「{portalId}」出现了两次。"));
                }

                if (!portalById.TryGetValue(portalId, out global::cfg.world.Portal portal))
                {
                    problems.Add(new WorldValidationIssue(label,
                        $"portal_ids 里的「{portalId}」在 TbPortal 里不存在（传送点要单独建一行，见 Tables/Data/world/portal/）。"));
                    continue;
                }

                if (!string.Equals(portal.SceneKey, scene.SceneKey, StringComparison.Ordinal))
                {
                    problems.Add(new WorldValidationIssue("TbPortal." + portalId,
                        $"它写在场景「{scene.SceneKey}」的 portal_ids 里，但自己的 scene_key 是「{portal.SceneKey}」，两边不一致。"));
                }
            }

            foreach (global::cfg.world.Portal portal in portalById.Values)
            {
                if (string.Equals(portal.SceneKey, scene.SceneKey, StringComparison.Ordinal)
                    && !seenPortals.Contains(portal.PortalId))
                {
                    problems.Add(new WorldValidationIssue(label,
                        $"场景里有传送点「{portal.PortalId}」（TbPortal 的 scene_key 指向本场景），"
                        + "但它没写进本场景的 portal_ids。"));
                }
            }
        }

        // ③ 出生点列表与默认出生点。
        private static void ValidateSpawnPoints(global::cfg.world.Scene scene, string label, List<WorldValidationIssue> problems)
        {
            List<string> spawnPoints = scene.SpawnPoints ?? new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < spawnPoints.Count; i++)
            {
                if (string.IsNullOrEmpty(spawnPoints[i]))
                {
                    problems.Add(new WorldValidationIssue(label, $"spawn_points 第 {i + 1} 项是空的。"));
                }
                else if (!seen.Add(spawnPoints[i]))
                {
                    problems.Add(new WorldValidationIssue(label, $"spawn_points 里「{spawnPoints[i]}」出现了两次。"));
                }
            }

            bool hasDefault = !string.IsNullOrEmpty(scene.DefaultSpawnId);
            if (scene.Implemented)
            {
                // 实装的场景必须有默认出生点，否则「新开局 / 读档恢复」没有落点。
                if (!hasDefault)
                {
                    problems.Add(new WorldValidationIssue(label,
                        "implemented=true 但没有 default_spawn_id——实装的场景必须有一个默认出生点，"
                        + "否则新开局与读档恢复落不下去（10_两界与场景结构.md:72 的可走面是街面）。"));
                }
                else if (!seen.Contains(scene.DefaultSpawnId))
                {
                    problems.Add(new WorldValidationIssue(label,
                        $"default_spawn_id「{scene.DefaultSpawnId}」不在 spawn_points 里。"));
                }

                return;
            }

            // 未实装的场景允许空出生点列表；写了就必须自洽，免得实装时才发现默认出生点写错了。
            if (hasDefault && !seen.Contains(scene.DefaultSpawnId))
            {
                problems.Add(new WorldValidationIssue(label,
                    $"default_spawn_id「{scene.DefaultSpawnId}」不在 spawn_points 里。"));
            }
        }

        private static void ValidateRegions(IReadOnlyList<global::cfg.world.Region> regions,
            Dictionary<string, global::cfg.world.Scene> sceneByKey,
            Dictionary<string, global::cfg.world.Region> regionById,
            List<WorldValidationIssue> problems)
        {
            for (int i = 0; i < regions.Count; i++)
            {
                global::cfg.world.Region region = regions[i];
                if (string.IsNullOrEmpty(region.RegionId))
                {
                    continue;
                }

                string label = "TbRegion." + region.RegionId;
                string sceneKey = region.SceneKey ?? string.Empty;
                string mirrorId = region.MirrorRegionId ?? string.Empty;

                // ⑤ 区域形态与 scene_key 的对应关系。
                switch (region.SceneScope)
                {
                    case global::cfg.world.SceneScope.SingleScene:
                        if (sceneKey.Length > 0)
                        {
                            problems.Add(new WorldValidationIssue(label,
                                $"scene_scope=SingleScene 时 scene_key 必须是空串，现在是「{sceneKey}」"
                                + "（单场景专属的地点只属于一个世界，对面没有对应物）。"));
                        }

                        break;

                    case global::cfg.world.SceneScope.MainMap:
                    case global::cfg.world.SceneScope.StageExtension:
                        if (sceneKey.Length == 0)
                        {
                            problems.Add(new WorldValidationIssue(label,
                                $"scene_scope={region.SceneScope} 必须写 scene_key（区域要落在某张地图上；"
                                + "10_两界与场景结构.md:146 R18 那批战斗关区域属于哪张图还没定，定下来再填）。"));
                        }
                        else if (!sceneByKey.ContainsKey(sceneKey))
                        {
                            problems.Add(new WorldValidationIssue(label, $"scene_key「{sceneKey}」不在 TbScene 里。"));
                        }
                        else if (region.SceneScope == global::cfg.world.SceneScope.MainMap)
                        {
                            // ④ 主图区域必须列在所属场景的 region_ids 里。
                            List<string> declared = sceneByKey[sceneKey].RegionIds;
                            if (declared == null || !declared.Contains(region.RegionId))
                            {
                                problems.Add(new WorldValidationIssue(label,
                                    $"主图区域必须写进场景「{sceneKey}」的 region_ids（10_两界与场景结构.md:133 R5 的 3 / 2.5 / 2 层都在俯视图上）。"));
                            }
                        }

                        break;

                    default:
                        problems.Add(new WorldValidationIssue(label,
                            $"scene_scope 的取值 {region.SceneScope} 不在白名单里（MainMap / StageExtension / SingleScene）。"));
                        break;
                }

                // ③ 主图区域成对互指；其它形态必须留空。
                if (region.SceneScope != global::cfg.world.SceneScope.MainMap)
                {
                    if (mirrorId.Length > 0)
                    {
                        problems.Add(new WorldValidationIssue(label,
                            $"只有 scene_scope=MainMap 的区域才有 mirror_region_id（10_两界与场景结构.md:132 R4 "
                            + "说的是两张主图逐格对齐），现在是「{mirrorId}」。"));
                    }

                    continue;
                }

                if (mirrorId.Length == 0)
                {
                    problems.Add(new WorldValidationIssue(label,
                        "主图区域必须写 mirror_region_id 指向对面世界的对应区域（10_两界与场景结构.md:132 R4）。"));
                    continue;
                }

                if (!regionById.TryGetValue(mirrorId, out global::cfg.world.Region mirror))
                {
                    problems.Add(new WorldValidationIssue(label, $"mirror_region_id「{mirrorId}」在 TbRegion 里不存在。"));
                    continue;
                }

                if (!string.Equals(mirror.MirrorRegionId, region.RegionId, StringComparison.Ordinal))
                {
                    problems.Add(new WorldValidationIssue(label,
                        $"与「{mirrorId}」的对应关系不是双向的（对方写的是「{mirror.MirrorRegionId}」）。"));
                }

                if (sceneByKey.TryGetValue(sceneKey, out global::cfg.world.Scene self)
                    && sceneByKey.TryGetValue(mirror.SceneKey ?? string.Empty, out global::cfg.world.Scene other)
                    && self.World == other.World)
                {
                    problems.Add(new WorldValidationIssue(label,
                        $"mirror_region_id「{mirrorId}」落在同一个世界（{self.World}）里——对应区域必须是**对面**世界的那一个。"));
                }
            }
        }

        private static void ValidatePortals(IReadOnlyList<global::cfg.world.Portal> portals,
            Dictionary<string, global::cfg.world.Scene> sceneByKey,
            List<WorldValidationIssue> problems)
        {
            for (int i = 0; i < portals.Count; i++)
            {
                global::cfg.world.Portal portal = portals[i];
                if (string.IsNullOrEmpty(portal.PortalId))
                {
                    continue;
                }

                string label = "TbPortal." + portal.PortalId;

                if (!sceneByKey.TryGetValue(portal.SceneKey ?? string.Empty, out global::cfg.world.Scene from))
                {
                    problems.Add(new WorldValidationIssue(label, $"scene_key「{portal.SceneKey}」不在 TbScene 里。"));
                    continue;
                }

                if (!sceneByKey.TryGetValue(portal.TargetScene ?? string.Empty, out global::cfg.world.Scene to))
                {
                    problems.Add(new WorldValidationIssue(label, $"target_scene「{portal.TargetScene}」不在 TbScene 里。"));
                    continue;
                }

                // 出口锚点必须在出口场景的出生点列表里（锚点就是「到了这边从哪出发」的对位点）。
                if (string.IsNullOrEmpty(portal.AnchorId) || !HasSpawn(from, portal.AnchorId))
                {
                    problems.Add(new WorldValidationIssue(label,
                        $"anchor_id「{portal.AnchorId}」不在场景「{portal.SceneKey}」的 spawn_points 里"
                        + $"（它有：{JoinSpawns(from)}）。"));
                }

                // 目标出生点：留空 = 交给 WorldRules 回退到默认出生点；写了就必须存在，否则当场报，别等进场景才发现。
                if (!string.IsNullOrEmpty(portal.TargetSpawnId) && !HasSpawn(to, portal.TargetSpawnId))
                {
                    problems.Add(new WorldValidationIssue(label,
                        $"target_spawn_id「{portal.TargetSpawnId}」不在场景「{portal.TargetScene}」的 spawn_points 里"
                        + $"（它有：{JoinSpawns(to)}；留空表示用该场景的 default_spawn_id）。"));
                }

                // 目标场景还没有默认出生点、传送点自己也没指定：解析一定失败，直接在表里报出来。
                if (string.IsNullOrEmpty(portal.TargetSpawnId) && string.IsNullOrEmpty(to.DefaultSpawnId))
                {
                    problems.Add(new WorldValidationIssue(label,
                        $"target_spawn_id 是空的，而目标场景「{portal.TargetScene}」也没有 default_spawn_id，"
                        + "落点解析不出来（见 Game.World.WorldRules.TryResolveSpawn 的回退规则）。"));
                }

                // ⑥ 触发方式与到达方式的一致性。
                switch (portal.TriggerKind)
                {
                    case global::cfg.world.PortalTriggerKind.Interact:
                        if (string.IsNullOrEmpty(portal.ArrivalMethod))
                        {
                            problems.Add(new WorldValidationIssue(label,
                                "trigger_kind=Interact 时 arrival_method 不能为空——交互式出口必须先有到达方式，"
                                + "出生点回退时要靠它说明「从哪来」。"));
                        }

                        break;

                    case global::cfg.world.PortalTriggerKind.EnterRange:
                        if (string.Equals(portal.ArrivalMethod, WorldRules.ArrivalMirror, StringComparison.Ordinal))
                        {
                            problems.Add(new WorldValidationIssue(label,
                                $"trigger_kind=EnterRange 却写 arrival_method=「{WorldRules.ArrivalMirror}」——"
                                + "两界转场原文要求玩家主动过镜（10_两界与场景结构.md:148 R20），"
                                + "进入范围就自动切走会把叙事打断；要自动切就把这一行拆成新的到达方式。"));
                        }

                        break;

                    default:
                        problems.Add(new WorldValidationIssue(label,
                            $"trigger_kind 的取值 {portal.TriggerKind} 不在白名单里（EnterRange / Interact）。"));
                        break;
                }

                // 距离：出口与目标场景相同（同场景内的区域跳转）是允许的，这里只报「原地转圈」。
                if (string.Equals(portal.SceneKey, portal.TargetScene, StringComparison.Ordinal)
                    && string.Equals(portal.AnchorId, portal.TargetSpawnId, StringComparison.Ordinal))
                {
                    problems.Add(new WorldValidationIssue(label, "出口和目标出生点是同一个，玩家会在原地打转。"));
                }
            }
        }

        private static bool HasSpawn(global::cfg.world.Scene scene, string spawnId)
        {
            List<string> points = scene.SpawnPoints;
            return points != null && points.Contains(spawnId);
        }

        private static string JoinSpawns(global::cfg.world.Scene scene)
        {
            List<string> points = scene.SpawnPoints;
            return points == null || points.Count == 0 ? "一个都没有" : string.Join("、", points);
        }
    }
}
