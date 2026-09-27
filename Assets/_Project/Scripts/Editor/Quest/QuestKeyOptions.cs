// 职责：任务编辑器里「键 / 指引地点」下拉的选项表（显示文字 + 实际值），含对话下拉的台词预览
//   （读 Tables/Data/dialogue/<id>.json 的 nodes[0] 与 dialogue_character.json 的显示名）。
//   只在载入 / 保存后 / 重新载入时构建一次，OnGUI 里只读缓存好的 string[]。
// 为什么新建（project-root.md「加能力的顺序」）：
//   1. 复用不行：QuestValidationContext 只有「有哪些键」，没有给人看的标签；对话台词预览工程里没有现成读法
//      （运行时对话走 Luban 生成物，编辑器窗口不该依赖运行时容器）。
//   2. 扩展不行：塞进 QuestValidationContext 会让纯数据的校验上下文背上 IO 与显示职责；
//      塞进 QuestEditorWindow 会把几百行窗口再撑大，且「选项怎么排、标签怎么拼」与「画界面」是两件事。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Game.Quest;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game.Editor.Quest
{
    /// <summary>任务编辑器下拉选项。每组 <see cref="Choices"/> 的最后一个标签都是「手动输入…」。</summary>
    public sealed class QuestKeyOptions
    {
        public const string ManualLabel = "手动输入…";
        public const string AutoLabel = "（自动）";

        /// <summary>台词预览取前多少个字。</summary>
        private const int PreviewLength = 12;

        private QuestKeyOptions(Choices dialogues, Choices locations, Choices counters, Choices guideLocations)
        {
            Dialogues = dialogues;
            Locations = locations;
            Counters = counters;
            GuideLocations = guideLocations;
        }

        /// <summary>空选项表（载入前占位用）。</summary>
        public static QuestKeyOptions Empty { get; } = new QuestKeyOptions(
            new Choices(Array.Empty<string>(), Array.Empty<string>()),
            new Choices(Array.Empty<string>(), Array.Empty<string>()),
            new Choices(Array.Empty<string>(), Array.Empty<string>()),
            new Choices(new[] { string.Empty }, new[] { AutoLabel }));

        /// <summary>对话编号：值 "1001"，标签「1001 · 老者：人都跑光了…」。</summary>
        public Choices Dialogues { get; }

        /// <summary>地点键：值 "camp"，标签「camp（SampleScene）」。</summary>
        public Choices Locations { get; }

        /// <summary>计数键：值即标签。</summary>
        public Choices Counters { get; }

        /// <summary>指引地点：首项「（自动）」= 空串，其余同 <see cref="Locations"/>。</summary>
        public Choices GuideLocations { get; }

        /// <summary>按目标类型取「键」下拉的选项。</summary>
        public Choices ForKind(QuestObjectiveKind kind)
        {
            switch (kind)
            {
                case QuestObjectiveKind.TalkTo: return Dialogues;
                case QuestObjectiveKind.ReachLocation: return Locations;
                default: return Counters;
            }
        }

        /// <summary>从校验上下文构建全部选项；对话台词预览会读文件（有 IO，别在 OnGUI 里调）。任何解析失败只退化成显示编号，不抛。</summary>
        public static QuestKeyOptions Build(QuestValidationContext context)
        {
            if (context == null) return Empty;

            string dataRoot = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Tables", "Data");
            Dictionary<string, string> characterNames = ReadCharacterNames(Path.Combine(dataRoot, "dialogue_character.json"));
            string dialogueDirectory = Path.Combine(dataRoot, "dialogue");

            int[] dialogueIds = context.DialogueIds.OrderBy(id => id).ToArray();
            var dialogueValues = new string[dialogueIds.Length];
            var dialogueLabels = new string[dialogueIds.Length];
            for (int i = 0; i < dialogueIds.Length; i++)
            {
                string id = dialogueIds[i].ToString(CultureInfo.InvariantCulture);
                dialogueValues[i] = id;
                string preview = ReadDialoguePreview(Path.Combine(dialogueDirectory, id + ".json"), characterNames);
                dialogueLabels[i] = string.IsNullOrEmpty(preview) ? id : $"{id} · {preview}";
            }

            KeyValuePair<string, string>[] locations = context.LocationKeys
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToArray();
            var locationValues = new string[locations.Length];
            var locationLabels = new string[locations.Length];
            for (int i = 0; i < locations.Length; i++)
            {
                locationValues[i] = locations[i].Key;
                locationLabels[i] = string.IsNullOrEmpty(locations[i].Value)
                    ? locations[i].Key
                    : $"{locations[i].Key}（{locations[i].Value}）";
            }

            string[] counterValues = context.CounterKeys.OrderBy(key => key, StringComparer.Ordinal).ToArray();

            var guideValues = new string[locationValues.Length + 1];
            var guideLabels = new string[locationLabels.Length + 1];
            guideValues[0] = string.Empty;
            guideLabels[0] = AutoLabel;
            Array.Copy(locationValues, 0, guideValues, 1, locationValues.Length);
            Array.Copy(locationLabels, 0, guideLabels, 1, locationLabels.Length);

            return new QuestKeyOptions(
                new Choices(dialogueValues, dialogueLabels),
                new Choices(locationValues, locationLabels),
                new Choices(counterValues, (string[])counterValues.Clone()),
                new Choices(guideValues, guideLabels));
        }

        /// <summary>dialogue_character.json：[{ "id": "elder", "displayName": "老者" }, …] → id → 显示名。读不了就返回空表。</summary>
        private static Dictionary<string, string> ReadCharacterNames(string path)
        {
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (!File.Exists(path)) return names;
                if (!(JToken.Parse(File.ReadAllText(path, Encoding.UTF8)) is JArray characters)) return names;

                foreach (JToken token in characters)
                {
                    if (!(token is JObject character)) continue;
                    string id = (string)character["id"];
                    string displayName = (string)character["displayName"];
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(displayName) && !names.ContainsKey(id))
                    {
                        names.Add(id, displayName);
                    }
                }
            }
            catch (Exception)
            {
                // 预览只是锦上添花：角色表坏了就退化成显示原始 speaker，不打断窗口载入。
                names.Clear();
            }

            return names;
        }

        /// <summary>「老者：人都跑光了，对谁负责…」；读不到返回空串（调用方只显示编号）。</summary>
        private static string ReadDialoguePreview(string path, Dictionary<string, string> characterNames)
        {
            try
            {
                if (!File.Exists(path)) return string.Empty;
                if (!(JToken.Parse(File.ReadAllText(path, Encoding.UTF8)) is JObject dialogue)) return string.Empty;
                if (!(dialogue["nodes"] is JArray nodes) || nodes.Count == 0 || !(nodes[0] is JObject first)) return string.Empty;

                string text = ((string)first["text"] ?? string.Empty).Replace('\n', ' ').Trim();
                if (text.Length > PreviewLength) text = text.Substring(0, PreviewLength) + "…";

                string speaker = (string)first["speakerName"];
                if (string.IsNullOrEmpty(speaker))
                {
                    string speakerId = (string)first["speaker"] ?? string.Empty;
                    speaker = characterNames.TryGetValue(speakerId, out string displayName) ? displayName : speakerId;
                }

                return string.IsNullOrEmpty(speaker) ? text : $"{speaker}：{text}";
            }
            catch (Exception)
            {
                // 同上：单个对话文件坏了只影响它自己的标签。
                return string.Empty;
            }
        }

        /// <summary>
        /// 一组下拉选项。<see cref="Labels"/> 比 <see cref="Values"/> 多一项：末尾的「手动输入…」，下标 = <see cref="ManualIndex"/>。
        /// 标签里的「/」会被 Unity 的 Popup 当成子菜单分隔符，构造时换成全角「／」。
        /// </summary>
        public sealed class Choices
        {
            private readonly string[] values;

            public Choices(string[] values, string[] labels)
            {
                this.values = values ?? Array.Empty<string>();
                int count = this.values.Length;
                Labels = new string[count + 1];
                for (int i = 0; i < count; i++)
                {
                    string label = labels != null && i < labels.Length ? labels[i] : this.values[i];
                    Labels[i] = (label ?? string.Empty).Replace('/', '／');
                }

                Labels[count] = ManualLabel;
            }

            /// <summary>显示文字，末项为「手动输入…」。</summary>
            public string[] Labels { get; }

            public int ManualIndex => values.Length;

            /// <summary>值在列表里的下标；不在列表里返回 -1。</summary>
            public int IndexOf(string value)
            {
                if (value == null) return -1;
                for (int i = 0; i < values.Length; i++)
                {
                    if (string.Equals(values[i], value, StringComparison.Ordinal)) return i;
                }

                return -1;
            }

            /// <summary>下标对应的值；越界（含「手动输入…」）返回 null。</summary>
            public string ValueAt(int index) => index >= 0 && index < values.Length ? values[index] : null;
        }
    }
}
