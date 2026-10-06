// 职责：妖物表（Tables/Defines/yao.xml）的只读查询——把「整条配置」「怪物层级 A/B/C」「能否常规击杀」
//   「怎么杀 / 有没有替代途径」「掉落」「能否收押」「能否制面具」「族属」这些列读通，供收押、画皮、账簿、
//   调查面板与怪物分层按 id 查。
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
    /// 怪物分层读 <see cref="TierOf"/> / <see cref="IsKillable"/> / <see cref="DefeatMethodOf"/> / <see cref="DropItemsOf"/>。
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
    /// <para>
    /// <b>怎么杀也只认白名单</b>（<see cref="DefeatMethodOf"/>，2026-10-07 加）：defeat_method 列的五个取值
    /// <c>可击杀（方式没写）</c> / <c>暗杀</c> / <c>特殊条件</c> / <c>需收服</c> / <c>不可杀</c>
    /// 全部从 <c>docs/design/features-spotlight/06_怪物分层.md</c> 表 3.2–3.5 的「可否击杀 / 怎么杀」列与
    /// 该文 :121 R9 归纳，逐条出处见 <see cref="ValidateDefeatMethod"/>。它和 <see cref="IsKillable"/> 分工不同：
    /// killable 答「能不能常规击杀」，本列答「为什么不能常规杀、以及有没有替代途径」，两列一起才说清楚
    /// 查勘使（常态不可击杀，但能用地形隐匿击杀）这类例外。
    /// </para>
    /// <para>
    /// <b>列白名单校验只做一次</b>（B13，2026-10-07 收口）：tier 与 defeat_method 两张白名单在
    /// **首次读表的那一次**对全表跑一遍（<see cref="EnsureTableRead"/>），之后每次查询都直接读缓存，
    /// 不再回头重扫全表。数据坏了仍是当场抛 <see cref="ArgumentException"/>（消息带 id、原值与
    /// <c>Tables/Defines/yao.xml</c>）——只是从「每次查询都校验」改成「读表那次校验过就不再看」，
    /// 因为本表是只读的生成物，运行期没有旁路改写，重复校验换不来新信息，只会让每个查询方都背上一次全表扫描。
    /// <see cref="Invalidate"/> 的语义不变（测试换过表数据后调它重读），它同时清掉缓存与「已校验」标记，
    /// 下一次访问会重新读表并重新校验一遍。
    /// </para>
    /// </summary>
    public sealed class YaoCatalog
    {
        /// <summary>
        /// <c>defeat_method</c> 白名单里「暗杀」那一项的原文：**只能绕背处决，走不了常规击杀**
        /// （<c>Tables/Defines/yao.xml</c> 的 defeat_method 列注释原文）。
        /// <para>
        /// 背后处决的物种门槛（<see cref="Game.Stealth.ExecutionRules.SpeciesExecutable"/>）比的就是这个值。
        /// 公开成常量而不是让判定侧再手打一遍：白名单的唯一权威是下面那个数组，两处各写一份「暗杀」的话，
        /// 表里改了值而代码没跟上就会变成**静默不可处决**（玩家按 F 没反应，日志里也看不出来）。
        /// </para>
        /// </summary>
        public const string AssassinationMethod = "暗杀";

        // defeat_method 列的取值白名单（Tables/Defines/yao.xml 的 defeat_method 那一行）。五个值全部从
        // docs/design/features-spotlight/ 归纳，逐条出处见 ValidateDefeatMethod 的注释；这里只落名单，不解释。
        private static readonly string[] DefeatMethods =
        {
            "可击杀（方式没写）",
            AssassinationMethod,
            "特殊条件",
            "需收服",
            "不可杀",
        };

        private readonly IConfigService config;
        private Dictionary<int, global::cfg.yao.Yao> byId;
        private List<int> sealableIds;
        private List<int> maskIds;
        private List<int> killableIds;

        // 「整张表已经读过一遍并校验过」的标记。byId 与它同生共死：要么都在（表已读、校验已过），
        // 要么都为空（还没读，或 Invalidate 刚清过）。留着它才能让「校验只做一次」是个能断言的显式状态，
        // 而不是「缓存碰巧还在」的副作用。
        private bool validated;

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
                    EnsureTableRead();
                    validated = true;
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
        /// 取值不是 A / B / C 时，<see cref="EnsureTableRead"/> 已经在读表那一次抛了 <see cref="ArgumentException"/>，
        /// 所以这里读到的一定是白名单里的值（见类文档「表校验只做一次」）。
        /// </summary>
        public string TierOf(int yaoId) => TryGet(yaoId, out global::cfg.yao.Yao yao) ? yao.Tier : null;

        /// <summary>能否常规击杀（[06] R9）：只能暗杀、不可击杀、持有篮子时不可击杀都是 false；查不到也是 false。</summary>
        public bool IsKillable(int yaoId) => TryGet(yaoId, out global::cfg.yao.Yao yao) && yao.Killable;

        /// <summary>
        /// 怎么杀 / 有没有替代途径：defeat_method 列的原文，取值只可能是
        /// <c>可击杀（方式没写）</c> / <c>暗杀</c> / <c>特殊条件</c> / <c>需收服</c> / <c>不可杀</c>
        /// （出处见 <see cref="ValidateDefeatMethod"/>）；表里没有这只妖返回 null。
        /// 取值不在白名单里时同样在读表那一次就抛 <see cref="ArgumentException"/>，见类文档。
        /// <para>
        /// 与 <see cref="IsKillable"/> 一起用才完整：查勘使是 <c>IsKillable=false</c> + <c>特殊条件</c>
        /// （常态杀不了，但能用地形隐匿击杀），籍中吏是 <c>false</c> + <c>不可杀</c>（没有任何途径）。
        /// 只读 killable 会把两者看成一回事。
        /// </para>
        /// </summary>
        public string DefeatMethodOf(int yaoId) => TryGet(yaoId, out global::cfg.yao.Yao yao) ? yao.DefeatMethod : null;

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

        /// <summary>
        /// 清掉惰性缓存与「已校验」标记。语义不变：只在测试里换过表数据后用得到，正常运行期表是只读的；
        /// 调用之后下一次访问会重新读表、并把两张列白名单重新校验一遍（见类文档「表校验只做一次」）。
        /// </summary>
        public void Invalidate()
        {
            byId = null;
            validated = false;
            sealableIds = null;
            maskIds = null;
            killableIds = null;
        }

        // 读表 + 全表校验，**只做一次**（B13）：校验与读表绑在同一道门上，门一开就是一个完整的显式状态
        // （byId 与 validated 同时成立），之后的查询只读缓存，不再每次回头重扫全表。
        // 表里有一行写坏时这份状态不生效：异常当场抛出，byId / validated 都不落，下次访问重新校验并报同一个错。
        // 本工程是 Unity，没有「表被别处改写」的旁路，所以不需要额外的失效钩子——唯一的换表入口是 Invalidate。
        private void EnsureTableRead()
        {
            if (validated)
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
                ValidateTier(yao);
                ValidateDefeatMethod(yao);
            }

            byId = map;
            validated = true;
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
        // 只在 EnsureTableRead 里每行叫一次（表校验只做一次）。
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

        // defeat_method 只认白名单里的五个值（表头写死）。**取值只从真源归纳，不发明新词**，逐条出处：
        //   可击杀（方式没写） 06_怪物分层.md:132 / :154 / :156 / :169 / :180 / :183 / :184
        //                     （表 3.2–3.4 与 3.5 写「可击杀（方式没写）」的那些；:170 特定信众的
        //                     「其他与普通信众相同 → 可击杀」原文按 :169 的信众处理，同一格写法）。
        //   暗杀               03_潜行与暗杀.md:39（殁吏「暗杀通行」）、:36（市令「暗杀，难度很高」）、
        //                     :46（执事「暗杀后可以获得执事皮」）；06 表里对应 :141 / :157 / :171。
        //                     注：:40 录事的「较难击杀」只说难度、没说只能暗杀，按「可击杀（方式没写）」记。
        //   特殊条件           03_潜行与暗杀.md:43（查勘使「可通过地形隐匿等方式击杀」）、
        //                     06_怪物分层.md:159（拾骨人「持有篮子时不可击杀；『此后』可击杀」）、
        //                     03_潜行与暗杀.md:47（蜃师「通过地图解谜找出击杀」）、
        //                     06_怪物分层.md:173（食教者「击败后进入第二阶段」——阶段本身就是条件）、
        //                     :160（老吏「被击败（方式没写）；查勘使面具可直接制服」）、:187（钱塘君走战斗）。
        //   需收服             03_潜行与暗杀.md:38（户绝民「无法被常规击杀，可以被对应溺者刻度的湿皮收服」）；
        //                     06 表里对应 :155。
        //   不可杀             06_怪物分层.md:185（籍中吏「不可击杀」）；03_潜行与暗杀.md:48 同步。
        // R9（06:121）自己收的四种说法「不可击杀 / 常态不可击杀 / 无法被常规击杀 / 持有篮子时不可击杀」是
        // 「常态能不能杀」的口径，落在 killable 列（都填 false），所以本列里不写「常态」二字：那半句归 killable，
        // 本列只回答「怎么杀 / 有没有替代途径」。原始说法与哪一列的对应关系见 Tables/Defines/yao.xml 的 defeat_method。
        private static void ValidateDefeatMethod(global::cfg.yao.Yao yao)
        {
            string method = yao.DefeatMethod;
            for (int i = 0; i < DefeatMethods.Length; i++)
            {
                if (method == DefeatMethods[i])
                {
                    return;
                }
            }

            throw new ArgumentException(
                $"妖 {yao.Id} 的 defeat_method 是 \"{method}\"，只允许 可击杀（方式没写） / 暗杀 / 特殊条件 / 需收服 / 不可杀"
                + "（见 Tables/Defines/yao.xml）。这五个取值只从 docs/design/features-spotlight/06_怪物分层.md:130 表 3.2–3.5 的"
                + "「可否击杀 / 怎么杀」列与该文 :121 R9 归纳，不要发明新词；文档里的排版记号也不能照抄进来。");
        }
    }
}
