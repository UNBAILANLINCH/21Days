// 职责：妖物表（Tables/Defines/yao.xml）的只读查询——把「整条配置」「怪物层级 A/B/C」「能否常规击杀」
//   「掉落」「能否收押」「能否制面具」「族属」这些列读通，供收押、画皮、账簿、调查面板与怪物分层按 id 查。
//   **只做查询，不做玩法判定**：读到的值谁用谁判，本类不写存档、不发事件、不碰场景。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：cfg.yao.Yao 是 Luban 一次性的行对象，没有「按 clan / sealable / mask 反查」这类查询，
//      而且 TbYao 在配置服务初始化完成前访问会抛，各处自己 try/catch 会把容错散到每一个调用方。
//   2. 扩展不行：MirrorService 是照镜门面（带存档、事件、玩家与背包依赖，还要 EncounterStep 判活动），
//      收押 / 画皮 / 账簿只想要一张表的只读视图，不该被迫认识这些运行时状态；MirrorSceneBinder / MirrorRules
//      同样不是「表 → 查询」的职责。判据同 DialogueCatalog / QuestCatalog：表适配单独一层。
// 依赖只到 Game.Core.Config（IConfigService）+ 生成代码 cfg，不引用场景、存档、容器。
using System;
using System.Collections.Generic;
using Game.Core.Config;

namespace Game.Mirror
{
    /// <summary>
    /// 妖物目录（根作用域单例，只读）。首次访问任一成员时才读表（惰性）：<see cref="IConfigService.Tables"/>
    /// 在配置服务初始化完成前访问会抛，所以构造时不碰表。
    /// <para>
    /// 列与读者的对应关系写在 <c>Tables/Defines/yao.xml</c> 表头。本类只把列读出来：
    /// 收押读 <see cref="IsSealable"/>、画皮读 <see cref="CanMask"/>、账簿读 <see cref="ClanOf"/>、
    /// 怪物分层读 <see cref="TierOf"/> / <see cref="IsKillable"/> / <see cref="DropItemsOf"/>。
    /// 调查面板要的破绽 / 执念直接经 <see cref="TryGet"/> 取行对象上的字段。
    /// </para>
    /// <para>
    /// <b>表没就绪不算异常</b>：<see cref="IsReady"/> 为 false、<see cref="Count"/> 为 0、<see cref="TryGet"/>
    /// 一律 false（同 <c>MirrorService.TryGetYao</c> 的容错）。启动早期要读表就先用 <see cref="IsReady"/> 探一下，
    /// 别把「配置还没加载完」当成「表里没有这只妖」。
    /// </para>
    /// <para>
    /// <b>层级只认 A / B / C</b>：<see cref="TierOf"/> 直接给 tier 列的原文（<c>"A"</c> / <c>"B"</c> / <c>"C"</c>），
    /// 不给自定义枚举——分层眼下只有「怪物按层配数值」这一个消费者场景，多一层类型等于多一处要同步的地方。
    /// 别的取值（含文档里作排版记号用的「A·下 / B·下」）一律抛 <see cref="ArgumentException"/>：
    /// 数据坏了要当场看见，不能悄悄退化成某一层。
    /// </para>
    /// </summary>
    public sealed class YaoCatalog
    {
        private readonly IConfigService config;
        private Dictionary<int, global::cfg.yao.Yao> byId;
        private List<int> sealableIds;
        private List<int> maskIds;
        private List<int> killableIds;

        public YaoCatalog(IConfigService config)
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
                    EnsureBuilt();
                    return true;
                }
                catch (InvalidOperationException)
                {
                    // 配置服务用它自己的 InvalidOperationException 报「还没初始化完」，这里翻成人话给查询方。
                    return false;
                }
            }
        }

        /// <summary>表里的妖物条数；表没就绪时为 0。</summary>
        public int Count => IsReady ? byId.Count : 0;

        /// <summary>全部妖（表顺序）；表没就绪时为空列表。</summary>
        public IReadOnlyList<global::cfg.yao.Yao> All => IsReady ? (IReadOnlyList<global::cfg.yao.Yao>)config.Tables.TbYao.DataList : Array.Empty<global::cfg.yao.Yao>();

        /// <summary>
        /// 按 <c>yaoId</c> 取整条配置（表里的行对象本身，字段只读）；表没就绪或没有这个 id 时返回 false。
        /// 这是「取整条配置」的入口：破绽 / 执念 = <c>yao.Flaw</c> / <c>yao.Obsession</c>，
        /// 真形 = <c>yao.TrueName</c> / <c>yao.TrueDesc</c> / <c>yao.TrueImage</c>。
        /// </summary>
        public bool TryGet(int yaoId, out global::cfg.yao.Yao yao)
        {
            yao = null;
            if (!IsReady)
            {
                return false;
            }

            return byId.TryGetValue(yaoId, out yao);
        }

        /// <summary>按 <c>yaoId</c> 取整条配置；没有这个 id 时抛 <see cref="KeyNotFoundException"/>。表里查得到用这个。</summary>
        public global::cfg.yao.Yao Get(int yaoId)
        {
            if (!TryGet(yaoId, out global::cfg.yao.Yao yao))
            {
                throw new KeyNotFoundException($"妖物表里没有 id {yaoId}。");
            }

            return yao;
        }

        /// <summary>
        /// 这只妖的怪物层级原文：<c>"A"</c> 底层 / <c>"B"</c> 特定 / <c>"C"</c> 关键
        /// （docs/design/features-spotlight/06_怪物分层.md:113 R1；表 3.2–3.5 的「层级」列）；表里没有这只妖返回 null。
        /// 取值不是 A / B / C 时抛 <see cref="ArgumentException"/>，见类文档。
        /// </summary>
        public string TierOf(int yaoId)
        {
            if (!TryGet(yaoId, out global::cfg.yao.Yao yao))
            {
                return null;
            }

            ValidateTier(yao);
            return yao.Tier;
        }

        /// <summary>能否常规击杀（[06] R9）：只能暗杀、不可击杀、持有篮子时不可击杀都是 false；查不到也是 false。</summary>
        public bool IsKillable(int yaoId) => TryGet(yaoId, out global::cfg.yao.Yao yao) && yao.Killable;

        /// <summary>掉落（tbitem id，表顺序）；没定或查不到时为空列表。调用方只读，不要改这个列表。</summary>
        // 主窗口代修（2026-10-07）：原文是 yao.DropItems ?? Array.Empty<int>()，`??` 两边是 List<int> 与
        // int[]、两向都不能隐式转换，Unity 报 CS0019 且卡住整个工程编译；外层的 (IReadOnlyList<int>)
        // 转换救不了它（转换发生在 `??` 求值之后）。改成显式判空。
        public IReadOnlyList<int> DropItemsOf(int yaoId)
        {
            if (!TryGet(yaoId, out global::cfg.yao.Yao yao) || yao.DropItems == null)
            {
                return Array.Empty<int>();
            }

            return yao.DropItems;
        }

        /// <summary>能否收押（[04] 收押的前提之一）；查不到这只妖时为 false。</summary>
        public bool IsSealable(int yaoId) => TryGet(yaoId, out global::cfg.yao.Yao yao) && yao.Sealable;

        /// <summary>能否制面具（[06] 画皮傩演）；查不到这只妖时为 false。</summary>
        public bool CanMask(int yaoId) => TryGet(yaoId, out global::cfg.yao.Yao yao) && yao.Mask;

        /// <summary>族属（[07] 两界之账按它记账）；没有这只妖返回 null，有妖但族属留空返回空串——两者含义不同，别混用。</summary>
        public string ClanOf(int yaoId)
        {
            if (!TryGet(yaoId, out global::cfg.yao.Yao yao))
            {
                return null;
            }

            return yao.Clan ?? string.Empty;
        }

        /// <summary>全部可收押的妖 id（表顺序）；收押面板一次取够，别按 id 反复问。</summary>
        public IReadOnlyList<int> SealableIds => Collect(ref sealableIds, yao => yao.Sealable);

        /// <summary>全部可制面具的妖 id（表顺序）；画皮一次取够。</summary>
        public IReadOnlyList<int> MaskIds => Collect(ref maskIds, yao => yao.Mask);

        /// <summary>全部可常规击杀的妖 id（表顺序）；供怪物分层 / 掉落结算一次取够。</summary>
        public IReadOnlyList<int> KillableIds => Collect(ref killableIds, yao => yao.Killable);

        /// <summary>清掉惰性缓存。只在测试里换过表数据后用得到，正常运行期表是只读的。</summary>
        public void Invalidate()
        {
            byId = null;
            sealableIds = null;
            maskIds = null;
            killableIds = null;
        }

        private void EnsureBuilt()
        {
            if (byId != null)
            {
                return;
            }

            IReadOnlyList<global::cfg.yao.Yao> rows = config.Tables.TbYao.DataList;
            var map = new Dictionary<int, global::cfg.yao.Yao>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                global::cfg.yao.Yao yao = rows[i];
                // 主键重复在 Luban 生成期就会拦下，这里不重复校验，只照抄成字典。
                map.Add(yao.Id, yao);
            }

            // 先校验完 tier 再落缓存：表里有一行写坏时，整份缓存不生效，下次访问重新校验并报同一个错。
            for (int i = 0; i < rows.Count; i++)
            {
                ValidateTier(rows[i]);
            }

            byId = map;
        }

        private IReadOnlyList<int> Collect(ref List<int> cache, Func<global::cfg.yao.Yao, bool> predicate)
        {
            if (cache != null)
            {
                return cache;
            }

            if (!IsReady)
            {
                return Array.Empty<int>();
            }

            IReadOnlyList<global::cfg.yao.Yao> rows = config.Tables.TbYao.DataList;
            var ids = new List<int>();
            for (int i = 0; i < rows.Count; i++)
            {
                if (predicate(rows[i]))
                {
                    ids.Add(rows[i].Id);
                }
            }

            cache = ids;
            return cache;
        }

        // 层级只认字符串 A / B / C（表头写死）。文档里的「A·下 / B·下」是 06_怪物分层.md 表示
        // 「原文写在上一层条目下一级」的排版记号（该文 :126 的读法说明），不是第四个层级，本表不建模：
        // 出现这种值说明有人照抄了排版记号，或者数据真坏了，都该报出来而不是悄悄退化成某一层。
        private static void ValidateTier(global::cfg.yao.Yao yao)
        {
            string tier = yao.Tier;
            if (tier != "A" && tier != "B" && tier != "C")
            {
                throw new ArgumentException(
                    $"妖 {yao.Id} 的 tier 是 \"{tier}\"，只允许 A / B / C（见 Tables/Defines/yao.xml）。"
                    + "文档里的「A·下 / B·下」是 docs/design/features-spotlight/06_怪物分层.md:126 说的排版记号，"
                    + "不是第四个层级，本表不建模。");
            }
        }
    }
}
