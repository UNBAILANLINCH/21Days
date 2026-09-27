// 职责：任务表 JSON（Tables/Data/quest/<id>.json）与可编辑草稿 QuestDraft 互转、读写文件；输出与手写文件逐字节一致。
// 为什么新建：现有只有 Luban 生成的只读加载链（Core/Config/Generated → QuestCatalog），没有「把改动写回 JSON 源表」的能力；
// GenerateTablesMenu 管的是起进程跑生成脚本，把源表读写塞进去职责不符，故单开文件。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Quest;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game.Editor.Quest
{
    /// <summary>
    /// 任务表文件层。<see cref="Serialize"/> / <see cref="Deserialize"/> 是纯函数；其余方法读写 <see cref="DataDirectory"/>。
    /// 输出格式：4 空格缩进、LF 换行、字段顺序固定、前置数组单行（<c>[1001, 1002]</c>）、空数组 <c>[]</c>、文件末尾一个换行。
    /// </summary>
    public static class QuestTableFile
    {
        private const int IndentSize = 4;
        private const string NewLine = "\n";

        // 不带 BOM：现有手写文件都没有 BOM，带了 git diff 就不干净。
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>任务表目录：&lt;工程根&gt;/Tables/Data/quest。</summary>
        public static string DataDirectory =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Tables", "Data", "quest");

        /// <summary>读目录下全部 *.json；单个文件坏了把原因（带文件名）记进 problems 并跳过，不整体失败。按文件名排序。</summary>
        public static List<QuestDraft> LoadAll(List<string> problems)
        {
            var drafts = new List<QuestDraft>();
            string directory = DataDirectory;
            if (!Directory.Exists(directory))
            {
                problems?.Add($"找不到任务表目录：Tables/Data/quest");
                return drafts;
            }

            string[] files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.Ordinal);
            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i];
                try
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    drafts.Add(Deserialize(json, path));
                }
                catch (FormatException e)
                {
                    problems?.Add(e.Message);
                }
                catch (IOException e)
                {
                    problems?.Add($"{Path.GetFileName(path)}：读取失败（{e.Message}）");
                }
                catch (UnauthorizedAccessException e)
                {
                    problems?.Add($"{Path.GetFileName(path)}：没有读取权限（{e.Message}）");
                }
            }

            return drafts;
        }

        /// <summary>
        /// 写 &lt;DataDirectory&gt;/&lt;Id&gt;.json；编号改过（与 SourcePath 文件名不一致）时写新文件并删旧文件。
        /// 目标文件已被另一条任务占用时抛 <see cref="InvalidOperationException"/>，不覆盖。
        /// </summary>
        public static void Save(QuestDraft draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            if (draft.Id <= 0) throw new ArgumentException($"编号 {draft.Id} 不是正整数，不能保存");

            string directory = DataDirectory;
            Directory.CreateDirectory(directory);
            string target = Path.GetFullPath(Path.Combine(directory, FileNameOf(draft.Id)));
            string source = ResolveSourcePath(draft.SourcePath);
            bool moved = source != null && !SamePath(source, target);

            if (File.Exists(target) && (source == null || moved))
            {
                throw new InvalidOperationException($"文件 {FileNameOf(draft.Id)} 已存在（编号 {draft.Id} 被占用），未保存");
            }

            File.WriteAllText(target, Serialize(draft), Utf8NoBom);

            // 新文件已是权威副本：先把 SourcePath 切到新文件，再删旧文件——
            // 这样万一下面 Delete 失败，草稿仍指向已经写好的新文件，不会指回旧文件导致下次载入读出两条重复任务。
            draft.SourcePath = target;

            if (moved && File.Exists(source))
            {
                try
                {
                    File.Delete(source);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    throw new InvalidOperationException(
                        $"任务 {draft.Id} 已写入新文件 {FileNameOf(draft.Id)}，但旧文件 {Path.GetFileName(source)} 删不掉：{e.Message}。" +
                        "请手动删掉旧文件，否则下次载入会出现两条同内容的任务。",
                        e);
                }
            }
        }

        /// <summary>删掉草稿对应的文件（新建未保存的草稿什么也不做）。</summary>
        public static void Delete(QuestDraft draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));

            string source = ResolveSourcePath(draft.SourcePath);
            if (source != null && File.Exists(source))
            {
                File.Delete(source);
            }

            draft.SourcePath = null;
        }

        /// <summary>草稿 → JSON 文本。纯函数。</summary>
        public static string Serialize(QuestDraft draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));

            var sb = new StringBuilder(512);
            sb.Append('{').Append(NewLine);
            AppendField(sb, 1, "id", draft.Id.ToString(CultureInfo.InvariantCulture), true);
            AppendField(sb, 1, "kind", Quote(draft.Kind.ToString()), true);
            AppendField(sb, 1, "title", Quote(draft.Title), true);
            AppendField(sb, 1, "description", Quote(draft.Description), true);
            AppendField(sb, 1, "prerequisites", FormatPrerequisites(draft.Prerequisites), true);

            List<QuestObjectiveDraft> objectives = draft.Objectives;
            if (objectives == null || objectives.Count == 0)
            {
                AppendField(sb, 1, "objectives", "[]", false);
            }
            else
            {
                sb.Append(' ', IndentSize).Append("\"objectives\": [").Append(NewLine);
                for (int i = 0; i < objectives.Count; i++)
                {
                    QuestObjectiveDraft objective = objectives[i] ?? new QuestObjectiveDraft();
                    sb.Append(' ', IndentSize * 2).Append('{').Append(NewLine);
                    AppendField(sb, 3, "text", Quote(objective.Text), true);
                    AppendField(sb, 3, "kind", Quote(objective.Kind.ToString()), true);
                    AppendField(sb, 3, "key", Quote(objective.Key), true);
                    AppendField(sb, 3, "count", objective.Count.ToString(CultureInfo.InvariantCulture), true);
                    AppendField(sb, 3, "location", Quote(objective.Location), false);
                    sb.Append(' ', IndentSize * 2).Append('}').Append(i < objectives.Count - 1 ? "," : string.Empty).Append(NewLine);
                }

                sb.Append(' ', IndentSize).Append(']').Append(NewLine);
            }

            sb.Append('}').Append(NewLine);
            return sb.ToString();
        }

        /// <summary>
        /// JSON 文本 → 草稿。纯函数。fileName 只用于报错与 <see cref="QuestDraft.SourcePath"/>。
        /// 缺字段、类型错、枚举名错（区分大小写）一律抛 <see cref="FormatException"/>，消息中文且带文件名与字段名。
        /// </summary>
        public static QuestDraft Deserialize(string json, string fileName)
        {
            string name = string.IsNullOrEmpty(fileName) ? "（未命名）" : Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(json)) throw new FormatException($"{name}：文件为空");

            JToken rootToken;
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)))
                {
                    // 不让 Newtonsoft 把长得像日期的字符串偷偷转成 DateTime。
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.FloatParseHandling = FloatParseHandling.Decimal;
                    rootToken = JToken.ReadFrom(reader);
                }
            }
            catch (JsonException e)
            {
                throw new FormatException($"{name}：JSON 语法错误（{e.Message}）", e);
            }

            if (!(rootToken is JObject root)) throw new FormatException($"{name}：最外层应是一个 {{ }} 对象");

            var draft = new QuestDraft(fileName)
            {
                Id = ReadInt(root, "id", name, string.Empty),
                Kind = ReadEnum<QuestKind>(root, "kind", name, string.Empty),
                Title = ReadString(root, "title", name, string.Empty),
                Description = ReadString(root, "description", name, string.Empty)
            };

            JArray prerequisites = ReadArray(root, "prerequisites", name, string.Empty);
            for (int i = 0; i < prerequisites.Count; i++)
            {
                JToken item = prerequisites[i];
                if (item.Type != JTokenType.Integer)
                {
                    throw new FormatException($"{name}：字段「prerequisites」第 {i + 1} 项应是整数编号，实际是 {Describe(item)}");
                }

                draft.Prerequisites.Add(ToInt(item, "prerequisites", name, string.Empty));
            }

            JArray objectives = ReadArray(root, "objectives", name, string.Empty);
            for (int i = 0; i < objectives.Count; i++)
            {
                string where = $"（第 {i + 1} 个目标）";
                if (!(objectives[i] is JObject item))
                {
                    throw new FormatException($"{name}：字段「objectives」第 {i + 1} 项应是 {{ }} 对象，实际是 {Describe(objectives[i])}");
                }

                draft.Objectives.Add(new QuestObjectiveDraft
                {
                    Text = ReadString(item, "text", name, where),
                    Kind = ReadEnum<QuestObjectiveKind>(item, "kind", name, where),
                    Key = ReadString(item, "key", name, where),
                    Count = ReadInt(item, "count", name, where),
                    Location = ReadString(item, "location", name, where)
                });
            }

            return draft;
        }

        private static string FileNameOf(int id) => id.ToString(CultureInfo.InvariantCulture) + ".json";

        private static string ResolveSourcePath(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath)) return null;
            string path = Path.IsPathRooted(sourcePath) ? sourcePath : Path.Combine(DataDirectory, sourcePath);
            return Path.GetFullPath(path);
        }

        // Windows 文件系统不区分大小写。
        private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static void AppendField(StringBuilder sb, int depth, string field, string rawValue, bool comma)
        {
            sb.Append(' ', IndentSize * depth).Append('"').Append(field).Append("\": ").Append(rawValue);
            if (comma) sb.Append(',');
            sb.Append(NewLine);
        }

        private static string FormatPrerequisites(List<int> prerequisites)
        {
            if (prerequisites == null || prerequisites.Count == 0) return "[]";

            var sb = new StringBuilder("[");
            for (int i = 0; i < prerequisites.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(prerequisites[i].ToString(CultureInfo.InvariantCulture));
            }

            return sb.Append(']').ToString();
        }

        // 转义规则沿用 Newtonsoft 默认：只转引号、反斜杠与控制字符，中文原样输出（与手写文件一致）。
        private static string Quote(string value) => JsonConvert.ToString(value ?? string.Empty);

        private static JToken Require(JObject obj, string field, string name, string where)
        {
            JToken token = obj[field];
            if (token == null) throw new FormatException($"{name}：缺少字段「{field}」{where}");
            return token;
        }

        private static int ReadInt(JObject obj, string field, string name, string where)
        {
            JToken token = Require(obj, field, name, where);
            if (token.Type != JTokenType.Integer)
            {
                throw new FormatException($"{name}：字段「{field}」{where}应是整数，实际是 {Describe(token)}");
            }

            return ToInt(token, field, name, where);
        }

        private static int ToInt(JToken token, string field, string name, string where)
        {
            try
            {
                return token.Value<int>();
            }
            catch (OverflowException)
            {
                throw new FormatException($"{name}：字段「{field}」{where}的数值 {token} 超出整数范围");
            }
        }

        private static string ReadString(JObject obj, string field, string name, string where)
        {
            JToken token = Require(obj, field, name, where);
            if (token.Type != JTokenType.String)
            {
                throw new FormatException($"{name}：字段「{field}」{where}应是字符串（带引号），实际是 {Describe(token)}");
            }

            return token.Value<string>();
        }

        private static JArray ReadArray(JObject obj, string field, string name, string where)
        {
            JToken token = Require(obj, field, name, where);
            if (!(token is JArray array))
            {
                throw new FormatException($"{name}：字段「{field}」{where}应是 [ ] 数组，实际是 {Describe(token)}");
            }

            return array;
        }

        // 严格按名字匹配（区分大小写、不接受数字），与 Luban 读枚举的行为一致。
        private static T ReadEnum<T>(JObject obj, string field, string name, string where) where T : struct, Enum
        {
            string value = ReadString(obj, field, name, where);
            string[] names = Enum.GetNames(typeof(T));
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], value, StringComparison.Ordinal))
                {
                    return (T)Enum.Parse(typeof(T), names[i]);
                }
            }

            throw new FormatException(
                $"{name}：字段「{field}」{where}的值「{value}」不是合法名字（可选：{string.Join("、", names)}，区分大小写）");
        }

        private static string Describe(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Integer: return $"整数 {token}";
                case JTokenType.Float: return $"小数 {token}";
                case JTokenType.String: return $"字符串「{token}」";
                case JTokenType.Boolean: return $"布尔值 {token}";
                case JTokenType.Null: return "null";
                case JTokenType.Array: return "数组";
                case JTokenType.Object: return "对象";
                default: return token.Type.ToString();
            }
        }
    }
}
