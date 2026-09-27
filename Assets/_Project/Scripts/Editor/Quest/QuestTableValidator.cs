// 职责：任务表的编辑期聚合校验——一次列出全部结构错误与交叉引用问题（中文、目标序号从 1 起），最后用运行时 QuestContent 兜底；另负责从工程里收集交叉引用的已知集合。
// 为什么新建：运行时 QuestContent 遇错即抛、只报第一条，且 Runtime 不能碰文件与场景，不能扩展成聚合校验；AssetAuditWindow 是资产体检（缺 meta、丢脚本、贴图尺寸），任务表规则塞进去名实不符。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Game.Quest;
using UnityEditor;
using UnityEngine;

namespace Game.Editor.Quest
{
    public static class QuestTableValidator
    {
        private const string RuntimePrefix = "运行时校验：";
        private const int MainRangeMin = 1000;
        private const int MainRangeMax = 1999;
        private const int SideRangeMin = 2000;
        private const int SideRangeMax = 2999;

        private static readonly Regex LocationKeyPattern = new Regex(
            @"^\s*locationKey:[ \t]*(.*?)\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>
        /// 纯函数：只看 drafts 与 context，不碰 Unity API / 文件。context 为 null 时跳过对话 / 地点 / 计数键的交叉引用检查。
        /// 结果排序：Error 在前，再按 QuestId、ObjectiveIndex。
        /// </summary>
        public static List<QuestIssue> Validate(IReadOnlyList<QuestDraft> drafts, QuestValidationContext context)
        {
            var issues = new List<QuestIssue>();
            if (drafts == null) return issues;

            var byId = new Dictionary<int, QuestDraft>();
            for (int i = 0; i < drafts.Count; i++)
            {
                QuestDraft draft = drafts[i];
                if (draft == null)
                {
                    issues.Add(new QuestIssue(QuestIssueSeverity.Error, 0, -1, $"第 {i + 1} 条任务为空"));
                    continue;
                }

                if (!byId.ContainsKey(draft.Id)) byId.Add(draft.Id, draft);
            }

            for (int i = 0; i < drafts.Count; i++)
            {
                QuestDraft draft = drafts[i];
                if (draft == null) continue;

                CheckIdentity(draft, drafts, issues);
                if (string.IsNullOrWhiteSpace(draft.Title)) AddError(issues, draft, -1, "标题为空");
                CheckPrerequisites(draft, byId, issues);
                CheckObjectives(draft, context, issues);
            }

            CheckCycles(drafts, byId, issues);
            CheckMainHeads(drafts, byId, issues);

            // 兜底：编辑期规则已报 Error 时 QuestContent 必然也抛，再报一遍只是重复；只在编辑期全绿时跑，保证「编辑器绿 = 运行时能起」。
            if (!issues.Any(issue => issue.Severity == QuestIssueSeverity.Error))
            {
                CheckWithRuntime(drafts, issues);
            }

            return issues
                .OrderBy(issue => issue.Severity)
                .ThenBy(issue => issue.QuestId)
                .ThenBy(issue => issue.ObjectiveIndex)
                .ToList();
        }

        /// <summary>
        /// 编辑器侧收集交叉引用的已知集合：对话编号（Tables/Data/dialogue/*.json 文件名）、
        /// 地点键（扫 Assets 下全部 .unity 文本里 QuestLocation 组件的 locationKey）、计数键（全部 LootConfig 的 CrateQuestKey）。
        /// 有 IO 开销，不要在 OnGUI 里调。
        /// </summary>
        public static QuestValidationContext CollectContext()
        {
            return new QuestValidationContext(CollectDialogueIds(), CollectLocationKeys(), CollectCounterKeys());
        }

        // ---------- 单条任务 ----------

        private static void CheckIdentity(QuestDraft draft, IReadOnlyList<QuestDraft> drafts, List<QuestIssue> issues)
        {
            if (draft.Id <= 0)
            {
                AddError(issues, draft, -1, "编号必须是正整数");
            }
            else
            {
                var others = new List<string>();
                for (int i = 0; i < drafts.Count; i++)
                {
                    QuestDraft other = drafts[i];
                    if (other == null || ReferenceEquals(other, draft) || other.Id != draft.Id) continue;
                    others.Add(string.IsNullOrEmpty(other.SourcePath) ? "未保存的新任务" : Path.GetFileName(other.SourcePath));
                }

                if (others.Count > 0)
                {
                    AddError(issues, draft, -1, $"编号 {Id(draft.Id)} 重复（另见 {string.Join("、", others)}）");
                }

                bool isMain = draft.Kind == QuestKind.Main;
                int min = isMain ? MainRangeMin : SideRangeMin;
                int max = isMain ? MainRangeMax : SideRangeMax;
                if (Enum.IsDefined(typeof(QuestKind), draft.Kind) && (draft.Id < min || draft.Id > max))
                {
                    AddWarning(issues, draft, -1, isMain
                        ? $"主线惯例用 1xxx（{Id(MainRangeMin)}–{Id(MainRangeMax)}），当前编号 {Id(draft.Id)}"
                        : $"支线惯例用 2xxx（{Id(SideRangeMin)}–{Id(SideRangeMax)}），当前编号 {Id(draft.Id)}");
                }
            }

            if (!string.IsNullOrEmpty(draft.SourcePath))
            {
                string fileName = Path.GetFileName(draft.SourcePath);
                string stem = Path.GetFileNameWithoutExtension(draft.SourcePath);
                if (!string.Equals(stem, Id(draft.Id), StringComparison.Ordinal))
                {
                    AddError(issues, draft, -1, $"文件名 {fileName} 与编号 {Id(draft.Id)} 不一致");
                }
            }
        }

        private static void CheckPrerequisites(QuestDraft draft, Dictionary<int, QuestDraft> byId, List<QuestIssue> issues)
        {
            List<int> prerequisites = draft.Prerequisites;
            if (prerequisites == null) return;

            for (int i = 0; i < prerequisites.Count; i++)
            {
                int prerequisite = prerequisites[i];
                if (prerequisite == draft.Id)
                {
                    AddError(issues, draft, -1, $"前置任务不能是自己（{Id(prerequisite)}）");
                }
                else if (!byId.ContainsKey(prerequisite))
                {
                    AddError(issues, draft, -1, $"前置任务 {Id(prerequisite)} 不存在");
                }
            }
        }

        private static void CheckObjectives(QuestDraft draft, QuestValidationContext context, List<QuestIssue> issues)
        {
            List<QuestObjectiveDraft> objectives = draft.Objectives;
            if (objectives == null || objectives.Count == 0)
            {
                AddError(issues, draft, -1, "至少要有一个目标");
                return;
            }

            for (int i = 0; i < objectives.Count; i++)
            {
                QuestObjectiveDraft objective = objectives[i];
                if (objective == null)
                {
                    AddError(issues, draft, i, "目标为空");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(objective.Text)) AddError(issues, draft, i, "文本为空");
                if (objective.Count < 1)
                {
                    AddWarning(issues, draft, i, $"次数 {objective.Count.ToString(CultureInfo.InvariantCulture)} 会按 1 处理");
                }

                string key = objective.Key ?? string.Empty;
                switch (objective.Kind)
                {
                    case QuestObjectiveKind.TalkTo:
                        // 解析方式与 QuestContent 一致，保证两边对「是不是整数」的判断相同。
                        if (!int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dialogueId))
                        {
                            AddError(issues, draft, i, $"对话编号「{key}」不是整数");
                        }
                        else if (context != null && !context.HasDialogue(dialogueId))
                        {
                            AddError(issues, draft, i, $"对话 {Id(dialogueId)} 不在 Tables/Data/dialogue/ 里");
                        }

                        break;

                    case QuestObjectiveKind.ReachLocation:
                        if (string.IsNullOrWhiteSpace(key))
                        {
                            AddError(issues, draft, i, "地点键为空");
                        }
                        else if (context != null && !context.HasLocation(key))
                        {
                            AddWarning(issues, draft, i, $"地点键「{key}」在任何场景里都没找到（现有：{DescribeLocations(context)}）");
                        }

                        break;

                    case QuestObjectiveKind.Counter:
                        if (string.IsNullOrWhiteSpace(key))
                        {
                            AddError(issues, draft, i, "计数键为空");
                        }
                        else if (context != null && !context.HasCounter(key))
                        {
                            AddWarning(issues, draft, i, $"计数键「{key}」没有任何玩法会上报（已知：{DescribeCounters(context)}）");
                        }

                        break;
                }

                string location = objective.Location ?? string.Empty;
                if (location.Length > 0 && context != null && !context.HasLocation(location))
                {
                    AddWarning(issues, draft, i, $"指引地点「{location}」在任何场景里都没找到（现有：{DescribeLocations(context)}）");
                }
            }
        }

        // ---------- 跨任务 ----------

        // 三色 DFS 找出全部环（自环已由前置规则单独报，这里跳过）；同一条环只报一次，挂在环上最小编号的任务下。
        private static void CheckCycles(IReadOnlyList<QuestDraft> drafts, Dictionary<int, QuestDraft> byId, List<QuestIssue> issues)
        {
            var color = new Dictionary<int, int>();
            var path = new List<int>();
            var reported = new HashSet<string>(StringComparer.Ordinal);

            foreach (int root in byId.Keys.OrderBy(id => id))
            {
                if (color.TryGetValue(root, out int rootColor) && rootColor != 0) continue;
                Visit(root);
            }

            void Visit(int id)
            {
                color[id] = 1;
                path.Add(id);
                List<int> prerequisites = byId[id].Prerequisites;
                if (prerequisites != null)
                {
                    for (int i = 0; i < prerequisites.Count; i++)
                    {
                        int next = prerequisites[i];
                        if (next == id || !byId.ContainsKey(next)) continue;

                        color.TryGetValue(next, out int nextColor);
                        if (nextColor == 0)
                        {
                            Visit(next);
                        }
                        else if (nextColor == 1)
                        {
                            ReportCycle(path.GetRange(path.IndexOf(next), path.Count - path.IndexOf(next)));
                        }
                    }
                }

                path.RemoveAt(path.Count - 1);
                color[id] = 2;
            }

            void ReportCycle(List<int> cycle)
            {
                // 旋转到最小编号开头，作为去重键。
                int start = cycle.IndexOf(cycle.Min());
                var ordered = new List<int>(cycle.Count + 1);
                for (int i = 0; i < cycle.Count; i++) ordered.Add(cycle[(start + i) % cycle.Count]);
                ordered.Add(ordered[0]);

                string text = string.Join(" → ", ordered.Select(Id));
                if (!reported.Add(text)) return;

                AddError(issues, byId[ordered[0]], -1, $"前置关系成环：{text}");
            }
        }

        // 链首 = 没有主线前置的主线里编号最小的；其余没有主线前置的主线会与它同时可接。
        private static void CheckMainHeads(IReadOnlyList<QuestDraft> drafts, Dictionary<int, QuestDraft> byId, List<QuestIssue> issues)
        {
            var heads = new List<QuestDraft>();
            for (int i = 0; i < drafts.Count; i++)
            {
                QuestDraft draft = drafts[i];
                if (draft == null || draft.Kind != QuestKind.Main) continue;

                bool hasMainPrerequisite = false;
                if (draft.Prerequisites != null)
                {
                    for (int p = 0; p < draft.Prerequisites.Count; p++)
                    {
                        if (byId.TryGetValue(draft.Prerequisites[p], out QuestDraft prerequisite) &&
                            prerequisite.Kind == QuestKind.Main)
                        {
                            hasMainPrerequisite = true;
                            break;
                        }
                    }
                }

                if (!hasMainPrerequisite) heads.Add(draft);
            }

            if (heads.Count <= 1) return;

            heads.Sort((a, b) => a.Id.CompareTo(b.Id));
            QuestDraft first = heads[0];
            for (int i = 1; i < heads.Count; i++)
            {
                AddWarning(issues, heads[i], -1,
                    $"主线 {Id(heads[i].Id)} 没有接在任何主线后面，会与 {Id(first.Id)} 同时可接（编号小的先）");
            }
        }

        // 把草稿翻译成运行时定义构造一次 QuestContent；它抛的就是运行时会抛的，原文照抄。
        private static void CheckWithRuntime(IReadOnlyList<QuestDraft> drafts, List<QuestIssue> issues)
        {
            var definitions = new QuestDefinition[drafts.Count];
            for (int i = 0; i < drafts.Count; i++)
            {
                definitions[i] = ToDefinition(drafts[i]);
            }

            try
            {
                _ = new QuestContent(definitions);
            }
            catch (ArgumentException e)
            {
                issues.Add(new QuestIssue(QuestIssueSeverity.Error, 0, -1, RuntimePrefix + e.Message));
            }
        }

        private static QuestDefinition ToDefinition(QuestDraft draft)
        {
            if (draft == null) return null;

            var objectives = new List<QuestObjectiveDefinition>();
            if (draft.Objectives != null)
            {
                for (int i = 0; i < draft.Objectives.Count; i++)
                {
                    QuestObjectiveDraft objective = draft.Objectives[i] ?? new QuestObjectiveDraft();
                    objectives.Add(new QuestObjectiveDefinition(
                        objective.Text, objective.Kind, objective.Key, objective.Count, objective.Location));
                }
            }

            return new QuestDefinition(
                draft.Id,
                draft.Kind,
                draft.Title,
                draft.Description,
                draft.Prerequisites ?? new List<int>(),
                objectives);
        }

        // ---------- 收集已知集合（编辑器侧） ----------

        private static List<int> CollectDialogueIds()
        {
            var ids = new List<int>();
            string directory = Path.Combine(GetProjectRoot(), "Tables", "Data", "dialogue");
            if (!Directory.Exists(directory)) return ids;

            foreach (string file in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                if (int.TryParse(Path.GetFileNameWithoutExtension(file), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                {
                    ids.Add(id);
                }
            }

            ids.Sort();
            return ids;
        }

        // 不开场景：直接读 .unity 的 YAML 文本，先按「--- !u!」切成对象块，
        // 只看 m_Script 指向 QuestLocation 脚本的 MonoBehaviour 块，再取块里的 locationKey，避免撞到别的组件同名字段。
        private static List<KeyValuePair<string, string>> CollectLocationKeys()
        {
            var result = new List<KeyValuePair<string, string>>();
            List<string> guids = FindQuestLocationScriptGuids();
            if (guids.Count == 0) return result;

            var scriptPatterns = guids
                .Select(guid => new Regex(
                    @"m_Script:\s*\{fileID:\s*11500000\s*,\s*guid:\s*" + guid + @"\b",
                    RegexOptions.IgnoreCase))
                .ToList();

            string[] scenes = Directory.GetFiles(Application.dataPath, "*.unity", SearchOption.AllDirectories);
            Array.Sort(scenes, StringComparer.Ordinal);
            foreach (string scene in scenes)
            {
                string text;
                try
                {
                    text = File.ReadAllText(scene, Encoding.UTF8);
                }
                catch (IOException)
                {
                    continue;
                }

                string sceneName = Path.GetFileNameWithoutExtension(scene);
                foreach (string block in text.Split(new[] { "\n--- !u!" }, StringSplitOptions.None))
                {
                    if (!scriptPatterns.Any(pattern => pattern.IsMatch(block))) continue;

                    Match match = LocationKeyPattern.Match(block);
                    if (!match.Success) continue;

                    string key = Unquote(match.Groups[1].Value);
                    if (key.Length > 0) result.Add(new KeyValuePair<string, string>(key, sceneName));
                }
            }

            return result;
        }

        private static List<string> FindQuestLocationScriptGuids()
        {
            var guids = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:MonoScript QuestLocation"))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (script != null && script.GetClass() == typeof(QuestLocation)) guids.Add(guid);
            }

            return guids;
        }

        // 故意不引用 Game.Loot：编辑器工具（任务表校验）不该在编译期依赖某个玩法模块是否存在——
        // Loot 可能不在当前检出里（比如别的会话正在开发、还没提交），一旦直接引用 Game.Loot.LootConfig，
        // Loot 缺失时这个文件就编译不过，会拖累整个任务表校验器。改用类型名字符串按资产类型查找
        // （"t:LootConfig"，Loot 不存在时 FindAssets 返回空数组）+ SerializedObject 读字段，绕开类型引用。
        // 耦合点写在这里：字段名 "crateQuestKey" 要和 Game.Loot.LootConfig 里的私有字段名保持一致，
        // 那边改名这里要跟着改。将来其它模块也有计数键时，在这里照这个写法再加一段。
        private static List<string> CollectCounterKeys()
        {
            var keys = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:LootConfig"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null) continue;

                var serialized = new SerializedObject(asset);
                SerializedProperty property = serialized.FindProperty("crateQuestKey");
                if (property == null || property.propertyType != SerializedPropertyType.String) continue;

                string key = property.stringValue;
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (!keys.Contains(key)) keys.Add(key);
            }

            return keys;
        }

        // ---------- 小工具 ----------

        private static string Unquote(string value)
        {
            string trimmed = value.Trim();
            if (trimmed.Length >= 2 &&
                ((trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"') ||
                 (trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\'')))
            {
                return trimmed.Substring(1, trimmed.Length - 2);
            }

            return trimmed;
        }

        private static string DescribeLocations(QuestValidationContext context)
        {
            if (context.LocationKeys.Count == 0) return "无";
            return string.Join("、", context.LocationKeys
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}@{pair.Value}"));
        }

        private static string DescribeCounters(QuestValidationContext context)
        {
            if (context.CounterKeys.Count == 0) return "无";
            return string.Join("、", context.CounterKeys.OrderBy(key => key, StringComparer.Ordinal));
        }

        private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);

        private static void AddError(List<QuestIssue> issues, QuestDraft draft, int objectiveIndex, string message) =>
            issues.Add(new QuestIssue(QuestIssueSeverity.Error, draft.Id, objectiveIndex, message));

        private static void AddWarning(List<QuestIssue> issues, QuestDraft draft, int objectiveIndex, string message) =>
            issues.Add(new QuestIssue(QuestIssueSeverity.Warning, draft.Id, objectiveIndex, message));

        private static string GetProjectRoot() => Directory.GetParent(Application.dataPath).FullName;
    }
}
