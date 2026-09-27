// PlaceholderAssetGuardRules —— 占位素材闸门的纯逻辑：给定「根 → 依赖路径」与禁止前缀，算出命中清单与报告文本。
//
// 为什么单独成文件：不碰 AssetDatabase / Addressables，EditMode 测试可以直接喂内存数据验证；
// 收集层（PlaceholderAssetGuard）只负责把工程里的真实依赖整理成这里要的输入。
// 为什么新建（复用 → 扩展 → 新建）：工程里没有「按路径前缀扫依赖」的现成工具；
// 塞进 BuildScript 会让打包流程文件同时承担素材合规规则，职责说不通，也没法脱离打包单测。

using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Editor
{
    /// <summary>
    /// 占位素材闸门的判定规则与报告格式。全部是纯函数，无副作用。
    /// </summary>
    public static class PlaceholderAssetGuardRules
    {
        /// <summary>
        /// 默认禁止进正式包的路径前缀。以后有新的仅限开发期的素材目录，往这里加一行。
        /// </summary>
        public static readonly IReadOnlyList<string> DefaultForbiddenPrefixes = new[]
        {
            // 明日方舟基建小人渲成的序列帧，版权归鹰角，仅开发期占位（见该目录 README.md）
            "Assets/_Project/Art/Sprites/Characters/Ark/",
        };

        /// <summary>一条引用链：哪个根（场景 / Addressables 条目）依赖到了哪个禁用资产。</summary>
        public readonly struct Hit
        {
            public Hit(string root, string assetPath)
            {
                Root = root;
                AssetPath = assetPath;
            }

            /// <summary>根的显示名（场景路径或 Addressables 条目描述），原样来自输入。</summary>
            public string Root { get; }

            /// <summary>命中的资产路径，已归一化为正斜杠。</summary>
            public string AssetPath { get; }
        }

        /// <summary>路径归一化：反斜杠换正斜杠、去首尾空白。大小写不动，比较时再忽略。</summary>
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return path.Trim().Replace('\\', '/');
        }

        /// <summary>
        /// 前缀归一化：在 <see cref="NormalizePath"/> 基础上保证以 / 结尾，
        /// 免得 "…/Ark" 误伤同级的 "…/Arknights"。
        /// </summary>
        public static string NormalizePrefix(string prefix)
        {
            string normalized = NormalizePath(prefix);
            if (normalized.Length == 0)
            {
                return string.Empty;
            }

            return normalized.EndsWith("/", StringComparison.Ordinal) ? normalized : normalized + "/";
        }

        /// <summary>路径是否落在任一禁止前缀下（忽略大小写与斜杠方向）。</summary>
        public static bool IsForbidden(string assetPath, IEnumerable<string> forbiddenPrefixes)
        {
            string normalized = NormalizePath(assetPath);
            if (normalized.Length == 0 || forbiddenPrefixes == null)
            {
                return false;
            }

            foreach (string prefix in forbiddenPrefixes)
            {
                string normalizedPrefix = NormalizePrefix(prefix);
                if (normalizedPrefix.Length > 0
                    && normalized.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 逐个根检查依赖，返回全部命中。顺序：根按输入顺序，同一根内按资产路径排序；
        /// 同一根里重复出现的同一资产（大小写 / 斜杠不同也算同一个）只记一次。
        /// </summary>
        /// <param name="rootDependencies">根显示名 → 该根的全部依赖路径（通常含根自身）。</param>
        /// <param name="forbiddenPrefixes">禁止前缀；传 null 用 <see cref="DefaultForbiddenPrefixes"/>。</param>
        public static List<Hit> FindHits(
            IEnumerable<KeyValuePair<string, IEnumerable<string>>> rootDependencies,
            IEnumerable<string> forbiddenPrefixes)
        {
            List<Hit> hits = new List<Hit>();
            if (rootDependencies == null)
            {
                return hits;
            }

            List<string> prefixes = new List<string>(forbiddenPrefixes ?? DefaultForbiddenPrefixes);

            foreach (KeyValuePair<string, IEnumerable<string>> root in rootDependencies)
            {
                if (root.Value == null)
                {
                    continue;
                }

                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                List<string> rootHits = new List<string>();
                foreach (string dependency in root.Value)
                {
                    string normalized = NormalizePath(dependency);
                    if (IsForbidden(normalized, prefixes) && seen.Add(normalized))
                    {
                        rootHits.Add(normalized);
                    }
                }

                rootHits.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string assetPath in rootHits)
                {
                    hits.Add(new Hit(root.Key, assetPath));
                }
            }

            return hits;
        }

        private static int CountRoots(IReadOnlyList<Hit> hits)
        {
            HashSet<string> roots = new HashSet<string>(StringComparer.Ordinal);
            foreach (Hit hit in hits)
            {
                roots.Add(hit.Root);
            }

            return roots.Count;
        }

        /// <summary>一条引用链的单行文本：「根 → 资产」。</summary>
        public static string FormatHit(Hit hit)
        {
            return $"{hit.Root} → {hit.AssetPath}";
        }

        /// <summary>
        /// 生成中文报告。没有命中时返回一行通过说明；有命中时首行是结论，随后每条引用链一行，末尾是处理办法。
        /// </summary>
        /// <param name="hits">命中清单。</param>
        /// <param name="blocking">true = Release 出包会被拦下；false = 开发版只警告 / 手动检查。</param>
        public static string BuildReport(IReadOnlyList<Hit> hits, bool blocking)
        {
            if (hits == null || hits.Count == 0)
            {
                return "占位素材闸门：通过，进包内容没有引用任何仅限开发期的占位素材。";
            }

            HashSet<string> assets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Hit hit in hits)
            {
                assets.Add(hit.AssetPath);
            }

            string verdict = blocking
                ? "Release 出包已拦下"
                : "开发版放行，仅警告；Release 出包会被拦下";

            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"占位素材闸门：{CountRoots(hits)} 个进包根引用了 {assets.Count} 个仅限开发期的占位素材"
                               + $"（共 {hits.Count} 条引用链），{verdict}。引用链（根 → 占位资产）：");
            // 按根汇总放在明细之后：引用链可能上百行，build.ps1 失败时只回显日志末尾，汇总要落在那里
            List<string> rootOrder = new List<string>();
            Dictionary<string, int> perRoot = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Hit hit in hits)
            {
                builder.AppendLine("  " + FormatHit(hit));
                if (!perRoot.ContainsKey(hit.Root))
                {
                    perRoot[hit.Root] = 0;
                    rootOrder.Add(hit.Root);
                }

                perRoot[hit.Root]++;
            }

            builder.AppendLine("按根汇总：");
            foreach (string root in rootOrder)
            {
                builder.AppendLine($"  {root}：{perRoot[root]} 个占位资产");
            }

            builder.Append("处理：把引用换成正式美术，或把该场景移出 Build Settings / 该条目移出 Addressables 组；"
                           + "开发自测可加 -Development 出开发版（只警告不拦）。");
            return builder.ToString();
        }
    }
}
