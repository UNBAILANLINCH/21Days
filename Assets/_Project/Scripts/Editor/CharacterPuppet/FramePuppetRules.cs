// 职责：序列帧小人生成工具的纯规则——帧文件名解析、按状态分组排序、缺态报错、meta.json 解析、pivot / PPU / fps 计算。
// 为什么新建（project-root.md「加能力的顺序」）：
//   1. 复用不行：SpriteImportProcessor 只管首次导入的全局默认值，不懂「一个角色一组帧、同一锚点」；
//   2. 扩展不行：这些规则要 EditMode 穷举测试，塞进 FramePuppetGenerator（资产读写、EditorWindow）就测不了，所以单独成类。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Game.Editor.CharacterPuppet
{
    /// <summary>序列帧小人的命名、分组与换算规则（不读写资产）。</summary>
    public static class FramePuppetRules
    {
        public const string IdleState = "idle";
        public const string WalkState = "walk";
        public const string MetaFileName = "meta.json";
        public const float DefaultFps = 12f;
        public const float DefaultTargetHeight = 1.5f;

        private static readonly Regex NamePattern = new Regex("^[a-z0-9]+(?:_[a-z0-9]+)*$");
        private static readonly Regex StatePattern = new Regex("^[a-z0-9]+(?:_[a-z0-9]+)*$");
        private static readonly Regex IndexPattern = new Regex("^[0-9]{2,}$");

        /// <summary>角色名是否合法：小写字母 / 数字，可用单个下划线分段（与美术命名规范一致）。</summary>
        public static bool IsValidCharacterName(string name)
        {
            return !string.IsNullOrEmpty(name) && NamePattern.IsMatch(name);
        }

        /// <summary>
        /// 解析 <c>chr_&lt;角色&gt;_&lt;状态&gt;_&lt;NN&gt;.png</c>。角色名由目录决定（它自己可能带下划线），
        /// 剩下部分按最后一个下划线切成状态与序号；序号至少两位、从 1 起。
        /// </summary>
        public static bool TryParseFrameName(string fileName, string characterName, out string state, out int index)
        {
            state = null;
            index = 0;
            if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(characterName))
            {
                return false;
            }

            string prefix = "chr_" + characterName + "_";
            const string Extension = ".png";
            if (!fileName.StartsWith(prefix, StringComparison.Ordinal)
                || !fileName.EndsWith(Extension, StringComparison.Ordinal)
                || fileName.Length <= prefix.Length + Extension.Length)
            {
                return false;
            }

            string body = fileName.Substring(prefix.Length, fileName.Length - prefix.Length - Extension.Length);
            int split = body.LastIndexOf('_');
            if (split <= 0 || split == body.Length - 1)
            {
                return false;
            }

            string statePart = body.Substring(0, split);
            string indexPart = body.Substring(split + 1);
            if (!StatePattern.IsMatch(statePart) || !IndexPattern.IsMatch(indexPart))
            {
                return false;
            }

            int value;
            if (!int.TryParse(indexPart, NumberStyles.None, CultureInfo.InvariantCulture, out value) || value < 1)
            {
                return false;
            }

            state = statePart;
            index = value;
            return true;
        }

        /// <summary>
        /// 把一个角色目录里的文件名按状态分组，组内按序号（数值）升序。
        /// 不认识的文件进 <paramref name="warnings"/>（忽略）；同一状态重复序号进 <paramref name="errors"/>；
        /// 序号不连续或不从 01 起进 <paramref name="warnings"/>（照常生成）。
        /// </summary>
        public static SortedDictionary<string, List<string>> GroupFrames(string characterName,
            IEnumerable<string> fileNames, List<string> errors, List<string> warnings)
        {
            var indexed = new SortedDictionary<string, SortedDictionary<int, string>>(StringComparer.Ordinal);
            foreach (string fileName in fileNames)
            {
                if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string state;
                int index;
                if (!TryParseFrameName(fileName, characterName, out state, out index))
                {
                    warnings.Add(string.Format("忽略 {0}：不符合 chr_{1}_<状态>_<NN>.png（全小写，NN 至少两位、从 01 起）",
                        fileName, characterName));
                    continue;
                }

                SortedDictionary<int, string> frames;
                if (!indexed.TryGetValue(state, out frames))
                {
                    frames = new SortedDictionary<int, string>();
                    indexed.Add(state, frames);
                }

                string existing;
                if (frames.TryGetValue(index, out existing))
                {
                    errors.Add(string.Format("状态 {0} 的第 {1} 帧重复：{2} 与 {3}", state, index, existing, fileName));
                    continue;
                }

                frames.Add(index, fileName);
            }

            var result = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, SortedDictionary<int, string>> pair in indexed)
            {
                var ordered = new List<string>(pair.Value.Values);
                int expected = 1;
                foreach (int index in pair.Value.Keys)
                {
                    if (index != expected)
                    {
                        warnings.Add(string.Format("状态 {0} 的序号不连续：缺第 {1} 帧（按现有顺序照常生成）", pair.Key, expected));
                        break;
                    }

                    expected++;
                }

                result.Add(pair.Key, ordered);
            }

            return result;
        }

        /// <summary>缺 idle / walk 时返回报错文案（给美术看的），齐全返回 null。</summary>
        public static string MissingStatesError(string characterName, ICollection<string> states)
        {
            var missing = new List<string>();
            if (!states.Contains(IdleState))
            {
                missing.Add(IdleState);
            }

            if (!states.Contains(WalkState))
            {
                missing.Add(WalkState);
            }

            if (missing.Count == 0)
            {
                return null;
            }

            var examples = new List<string>();
            foreach (string state in missing)
            {
                examples.Add(string.Format("chr_{0}_{1}_01.png", characterName, state));
            }

            return string.Format("角色 {0} 缺少必需状态 {1}：至少要有待机 idle 与走路 walk 两组帧，例如 {2}",
                characterName, string.Join("、", missing), string.Join("、", examples));
        }

        /// <summary>每单位像素 = 画布高 / 目标高度（同一角色所有帧一致，画布整体高度即显示高度）。</summary>
        public static float PixelsPerUnit(int canvasHeight, float targetHeight)
        {
            if (canvasHeight <= 0)
            {
                throw new ArgumentOutOfRangeException("canvasHeight", canvasHeight, "画布高必须为正");
            }

            if (targetHeight <= 0f)
            {
                throw new ArgumentOutOfRangeException("targetHeight", targetHeight, "目标高度必须为正");
            }

            return canvasHeight / targetHeight;
        }

        /// <summary>
        /// 脚底锚点（归一化，y 以底边为 0）：优先 meta.pivot，其次 meta.pivotPx / 画布尺寸，都没有取底边正中 (0.5, 0)。
        /// </summary>
        public static Vector2 ResolvePivot(FramePuppetMeta meta, int canvasWidth, int canvasHeight)
        {
            if (meta != null && meta.HasPivot)
            {
                return new Vector2(Clamp01(meta.PivotX), Clamp01(meta.PivotY));
            }

            if (meta != null && meta.HasPivotPx && canvasWidth > 0 && canvasHeight > 0)
            {
                return new Vector2(Clamp01(meta.PivotPxX / canvasWidth), Clamp01(meta.PivotPxY / canvasHeight));
            }

            return new Vector2(0.5f, 0f);
        }

        /// <summary>帧率：显式指定（&gt; 0）优先，其次 meta.fps，都没有取 12。</summary>
        public static float ResolveFps(float requested, FramePuppetMeta meta)
        {
            if (requested > 0f)
            {
                return requested;
            }

            if (meta != null && meta.Fps > 0f)
            {
                return meta.Fps;
            }

            return DefaultFps;
        }

        /// <summary>同一角色所有帧须同一画布尺寸；不一致返回报错文案，一致返回 null。</summary>
        public static string CanvasSizeError(IList<string> fileNames, IList<Vector2Int> sizes)
        {
            if (sizes.Count == 0)
            {
                return null;
            }

            Vector2Int first = sizes[0];
            for (int i = 1; i < sizes.Count; i++)
            {
                if (sizes[i] != first)
                {
                    return string.Format("同一角色所有帧必须同一画布尺寸：{0} 是 {1}x{2}，{3} 是 {4}x{5}",
                        fileNames[0], first.x, first.y, fileNames[i], sizes[i].x, sizes[i].y);
                }
            }

            return null;
        }

        /// <summary>Animator 状态名：idle → Idle、walk → Walk，其余原样（与已删除的分件小人控制器命名一致，Showcase 按此名断言）。</summary>
        public static string AnimatorStateName(string state)
        {
            if (state == IdleState)
            {
                return "Idle";
            }

            return state == WalkState ? "Walk" : state;
        }

        /// <summary>解析 meta.json；空串或解析失败返回 null（meta 可选，缺了走默认值）。</summary>
        public static FramePuppetMeta ParseMeta(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return FramePuppetMeta.FromJson(json);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static float Clamp01(float value)
        {
            return value < 0f ? 0f : value > 1f ? 1f : value;
        }
    }
}
