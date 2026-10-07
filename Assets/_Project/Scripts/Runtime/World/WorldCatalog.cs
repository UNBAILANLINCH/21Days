// 职责：世界结构表（Tables/Defines/world.xml → TbScene / TbRegion / TbPortal）的只读查询——
//   把「场景地址」「所属世界」「区域」「出生点」「传送点」这些列读通，并做白名单与引用完整性校验。
//   **只做查询，不做玩法判定**：读到的值谁用谁判（出生点怎么选是 WorldRules 的事），
//   本类不写存档、不发事件、不碰场景、不认识 Addressables。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：cfg.world.Scene 是 Luban 一次性的行对象，没有「按世界反查」「两界同构对不对」这类查询；
//      而且 TbScene 在配置服务初始化完成前访问会抛，各处自己 try/catch 会把容错散到每一个调用方。
//   2. 扩展不行：YaoCatalog 是妖物表的适配层（Mirror 模块的），世界表与它没有共同列、没有共同消费者；
//      把两张表塞进一个类等于给「妖物」和「场景」造一个假共性。
//   3. 判据同 DialogueCatalog / QuestCatalog / YaoCatalog：表适配单独一层，一个模块一张表的适配一个类。
// 依赖只到 Game.Core.Config（IConfigService）+ 生成代码 cfg，不引用场景、存档、容器、Addressables。
using System;
using System.Collections.Generic;
using Game.Core.Config;

namespace Game.World
{
    /// <summary>
    /// 世界目录（只读）。首次访问任一成员时才读表（惰性）：<see cref="IConfigService.Tables"/>
    /// 在配置服务初始化完成前访问会抛，所以构造时不碰表。
    /// <para>
    /// 列与读者的对应关系写在 <c>Tables/Defines/world.xml</c> 表头。本类把三张表读成三类查询：
    /// <see cref="TryGetScene"/> / <see cref="GetScene"/>（含 <see cref="IsSceneImplemented"/>、<see cref="SceneAddressOf"/>）、
    /// <see cref="RegionsOf"/> / <see cref="MirrorRegionOf"/>、<see cref="TryGetPortal"/> / <see cref="PortalsOf"/>。
    /// </para>
    /// <para>
    /// <b>表没就绪不算异常</b>：<see cref="IsReady"/> 为 false、<see cref="Count"/> 为 0、
    /// <see cref="TryGetScene"/> 一律 false（同 <c>YaoCatalog</c> 的容错）。启动早期要读表就先用
    /// <see cref="IsReady"/> 探一下，别把「配置还没加载完」当成「表里没有这个世界」。
    /// </para>
    /// <para>
    /// <b>取值白名单与引用校验只做一次</b>（同 YaoCatalog 的 B13 收口）：首次读表的那一次对全表跑一遍
    /// <see cref="Validate"/>，之后每次查询都直接读缓存，不再回头重扫全表。数据坏了仍是当场抛
    /// <see cref="ArgumentException"/>（消息带「哪一行、原值、去哪张表改」）。
    /// <see cref="Invalidate"/> 同时清掉缓存与「已校验」标记，下一次访问重新读表并重新校验。
    /// </para>
    /// <para>
    /// <b>场景地址与实装是两件事</b>：<see cref="SceneAddressOf"/> 只给出表里那一列；
    /// 「Addressables 里到底有没有这个地址」不在这里判（那要 UnityEditor / Addressables，Runtime 不许引用）。
    /// 表里用 <c>implemented=false</c> 标明未实装，<see cref="Validate"/> 把它单独收进
    /// <see cref="WorldValidationResult.NotImplementedSceneKeys"/> 并检查「未实装的行不许写地址」。
    /// </para>
    /// </summary>
    public sealed class WorldCatalog
    {
        private readonly IConfigService config;
        private Dictionary<string, global::cfg.world.Scene> scenesByKey;
        private Dictionary<string, global::cfg.world.Region> regionsById;
        private Dictionary<string, global::cfg.world.Portal> portalsById;

        // 「整张表已经读过一遍并校验过」的标记。与三个字典同生共死：要么都在（表已读、校验已过），
        // 要么都为空（还没读，或 Invalidate 刚清过）。留着它才能让「校验只做一次」是个能断言的显式状态。
        private bool validated;

        public WorldCatalog(IConfigService config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>配置表是否已就绪；未就绪时其余成员按「查不到」处理，不抛异常。</summary>
        public bool IsReady
        {
            get
            {
                try
                {
                    EnsureTableRead();
                    return true;
                }
                catch (InvalidOperationException)
                {
                    // 配置服务用它自己的 InvalidOperationException 报「还没初始化完」，这里翻成人话给查询方。
                    return false;
                }
            }
        }

        /// <summary>场景条数；表没就绪时为 0。</summary>
        public int Count => IsReady ? scenesByKey.Count : 0;

        /// <summary>全部场景（表顺序）；表没就绪时为空列表。</summary>
        public IReadOnlyList<global::cfg.world.Scene> AllScenes =>
            IsReady ? (IReadOnlyList<global::cfg.world.Scene>)config.Tables.TbScene.DataList : Array.Empty<global::cfg.world.Scene>();

        /// <summary>按场景键取整条配置；表没就绪或没有这个键时返回 false。</summary>
        public bool TryGetScene(string sceneKey, out global::cfg.world.Scene scene)
        {
            scene = null;
            if (IsReady && sceneKey != null)
            {
                return scenesByKey.TryGetValue(sceneKey, out scene);
            }

            return false;
        }

        /// <summary>按场景键取整条配置；没有这个键时抛 <see cref="KeyNotFoundException"/>。表里查得到用这个。</summary>
        public global::cfg.world.Scene GetScene(string sceneKey)
        {
            if (!TryGetScene(sceneKey, out global::cfg.world.Scene scene))
            {
                throw new KeyNotFoundException($"世界表里没有场景「{sceneKey}」（见 Tables/Defines/world.xml 的 TbScene）。");
            }

            return scene;
        }

        /// <summary>
        /// 场景的 Addressables 地址；未实装（<c>implemented=false</c>）或查不到时返回空串。
        /// 返回空串 = 「现在还不能加载它」，调用方不要拿它去 LoadScene。
        /// </summary>
        public string SceneAddressOf(string sceneKey) =>
            TryGetScene(sceneKey, out global::cfg.world.Scene scene) ? scene.SceneAddress ?? string.Empty : string.Empty;

        /// <summary>这个场景在 Addressables 里是否已实装；查不到按未实装处理。</summary>
        public bool IsSceneImplemented(string sceneKey) =>
            TryGetScene(sceneKey, out global::cfg.world.Scene scene) && scene.Implemented;

        /// <summary>
        /// 场景是否可加载：已实装且地址非空。**这是「能不能真的切过去」的唯一判据**，
        /// 比单看 <see cref="IsSceneImplemented"/> 稳（表里写了 implemented=true 却忘了填地址时会被拦下）。
        /// </summary>
        public bool CanLoadScene(string sceneKey) =>
            TryGetScene(sceneKey, out global::cfg.world.Scene scene)
            && scene.Implemented
            && !string.IsNullOrEmpty(scene.SceneAddress);

        /// <summary>按区域 id 取整条配置；没有这个 id 时返回 false。</summary>
        public bool TryGetRegion(string regionId, out global::cfg.world.Region region)
        {
            region = null;
            if (IsReady && regionId != null)
            {
                return regionsById.TryGetValue(regionId, out region);
            }

            return false;
        }

        /// <summary>本场景的区域（按 TbScene.region_ids 的顺序）；表没就绪或查不到场景时为空列表。</summary>
        public IReadOnlyList<global::cfg.world.Region> RegionsOf(string sceneKey)
        {
            if (!TryGetScene(sceneKey, out global::cfg.world.Scene scene) || scene.RegionIds == null)
            {
                return Array.Empty<global::cfg.world.Region>();
            }

            var result = new List<global::cfg.world.Region>(scene.RegionIds.Count);
            for (int i = 0; i < scene.RegionIds.Count; i++)
            {
                // 引用完整性在 Validate 里已经查过，这里查不到只可能是表被旁路改了；跳过而不是塞 null。
                if (regionsById.TryGetValue(scene.RegionIds[i], out global::cfg.world.Region region))
                {
                    result.Add(region);
                }
            }

            return result;
        }

        /// <summary>对面世界的对应区域 id；没有对面（单场景区域）时返回空串。查不到这个 id 时也返回空串。</summary>
        public string MirrorRegionOf(string regionId) =>
            TryGetRegion(regionId, out global::cfg.world.Region region) ? region.MirrorRegionId ?? string.Empty : string.Empty;

        /// <summary>按传送点 id 取整条配置；没有这个 id 时返回 false。</summary>
        public bool TryGetPortal(string portalId, out global::cfg.world.Portal portal)
        {
            portal = null;
            if (IsReady && portalId != null)
            {
                return portalsById.TryGetValue(portalId, out portal);
            }

            return false;
        }

        /// <summary>本场景里的传送点（按 TbScene.portal_ids 的顺序）；查不到场景时为空列表。</summary>
        public IReadOnlyList<global::cfg.world.Portal> PortalsOf(string sceneKey)
        {
            if (!TryGetScene(sceneKey, out global::cfg.world.Scene scene) || scene.PortalIds == null)
            {
                return Array.Empty<global::cfg.world.Portal>();
            }

            var result = new List<global::cfg.world.Portal>(scene.PortalIds.Count);
            for (int i = 0; i < scene.PortalIds.Count; i++)
            {
                if (portalsById.TryGetValue(scene.PortalIds[i], out global::cfg.world.Portal portal))
                {
                    result.Add(portal);
                }
            }

            return result;
        }

        /// <summary>
        /// 全表校验：取值白名单 + 引用完整性 + 两界同构的成对关系 + 「未实装的场景不许写地址」。
        /// 返回的 <see cref="WorldValidationResult.Problems"/> 为空才算过；
        /// <see cref="WorldValidationResult.NotImplementedSceneKeys"/> 单独给出，它是**状态**不是错误。
        /// </summary>
        public WorldValidationResult Validate()
        {
            // 先确保读表（IsReady 内部会跑一次白名单校验并缓存结果）。
            if (!IsReady)
            {
                return WorldValidationResult.Failed(
                    new[] { new WorldValidationIssue("(全部)", "配置表还没初始化完，读不到 TbScene / TbRegion / TbPortal。") });
            }

            return WorldCatalogValidator.Validate(config.Tables);
        }

        /// <summary>
        /// 清掉惰性缓存与「已校验」标记。语义同 <c>YaoCatalog.Invalidate</c>：只在测试里换过表数据后用得到，
        /// 正常运行期表是只读的；调用之后下一次访问会重新读表、重新校验一遍。
        /// </summary>
        public void Invalidate()
        {
            scenesByKey = null;
            regionsById = null;
            portalsById = null;
            validated = false;
        }

        // 读表 + 全表校验，只做一次：校验与读表绑在同一道门上，门一开就是一个完整的显式状态
        // （三个字典与 validated 同时成立），之后的查询只读缓存。
        // 表里有一行写坏时这份状态不生效：异常当场抛出，缓存不落，下次访问重新校验并报同一个错。
        private void EnsureTableRead()
        {
            if (validated)
            {
                return;
            }

            global::cfg.Tables tables = config.Tables;

            // WorldCatalogValidator 只读这三张表的行，不依赖本类的缓存，所以可以先校验再建索引。
            WorldValidationResult result = WorldCatalogValidator.Validate(tables);
            if (result.Problems.Count > 0)
            {
                throw new ArgumentException(
                    $"世界表校验不通过（{result.Problems.Count} 条）：{result.Describe()}"
                    + " 表定义见 Tables/Defines/world.xml，数据在 Tables/Data/world/ 下。");
            }

            scenesByKey = Index(tables.TbScene.DataList, scene => scene.SceneKey, "TbScene", "scene_key");
            regionsById = Index(tables.TbRegion.DataList, region => region.RegionId, "TbRegion", "region_id");
            portalsById = Index(tables.TbPortal.DataList, portal => portal.PortalId, "TbPortal", "portal_id");
            validated = true;
        }

        private static Dictionary<string, TRow> Index<TRow>(IReadOnlyList<TRow> rows, Func<TRow, string> keyOf,
            string tableName, string columnName)
        {
            var map = new Dictionary<string, TRow>(rows.Count, StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++)
            {
                string key = keyOf(rows[i]);

                // 主键为空 / 重复在 Luban 生成期不一定拦得住（空串能进），这里当成数据坏了当场报。
                if (string.IsNullOrEmpty(key))
                {
                    throw new ArgumentException($"{tableName} 里有一行的 {columnName} 是空的（见 Tables/Defines/world.xml）。");
                }

                if (map.ContainsKey(key))
                {
                    throw new ArgumentException($"{tableName} 的主键「{key}」重复了（见 Tables/Defines/world.xml）。");
                }

                map.Add(key, rows[i]);
            }

            return map;
        }
    }
}
