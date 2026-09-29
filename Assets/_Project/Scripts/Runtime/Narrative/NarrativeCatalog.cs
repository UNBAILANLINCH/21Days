// 职责：翻译 Luban 叙事表并在执行前校验跨表引用；不把配置服务带入纯规则。
using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core.Config;
using Game.Dialogue;

namespace Game.Narrative
{
    public sealed class NarrativeCatalog
    {
        private readonly IConfigService config;
        private readonly DialogueCatalog dialogues;
        private List<NarrativeContent> stories;
        private List<EncounterRules.Rule> encounters;
        private Dictionary<int, string> questFlags;

        public NarrativeCatalog(IConfigService config, DialogueCatalog dialogues)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.dialogues = dialogues ?? throw new ArgumentNullException(nameof(dialogues));
        }

        public IReadOnlyList<NarrativeContent> Stories { get { EnsureBuilt(); return stories; } }
        public IReadOnlyList<EncounterRules.Rule> Encounters { get { EnsureBuilt(); return encounters; } }
        public IReadOnlyDictionary<int, string> QuestFlags { get { EnsureBuilt(); return questFlags; } }

        private void EnsureBuilt()
        {
            if (stories != null) return;
            var builtStories = new List<NarrativeContent>();
            var builtEncounters = new List<EncounterRules.Rule>();
            var builtFlags = new Dictionary<int, string>();
            foreach (var row in config.Tables.TbNarrativeStory.DataList)
            {
                var stages = new List<NarrativeContent.Stage>();
                foreach (var node in row.Stages)
                {
                    var stage = new NarrativeContent.Stage
                    {
                        Id = node.Id, Kind = Parse<NarrativeContent.StageKind>(node.Kind), PayloadId = node.Payload,
                        AllowEncounter = node.AllowEncounter, Conditions = Conditions(node.AnyOf),
                        Outcome = node.Outcome, SetFlags = node.SetFlags.ToArray(),
                    };
                    foreach (var exit in node.Exits) stage.Exits.Add(exit.Result, exit.Next);
                    stages.Add(stage);
                }
                builtStories.Add(new NarrativeContent(row.Id, row.Entry, stages));
            }
            foreach (var row in config.Tables.TbNarrativeEncounter.DataList)
                builtEncounters.Add(new EncounterRules.Rule
                {
                    Id = row.Id, TriggerKind = row.Trigger, TargetKind = row.TargetKind, Priority = row.Priority,
                    StoryId = row.Story, EntryStageId = row.Entry, Repeat = Parse<EncounterRules.RepeatPolicy>(row.Repeat),
                    Conditions = Conditions(row.AnyOf),
                });
            foreach (var row in config.Tables.TbNarrativeQuestFlag.DataList)
            {
                if (!config.Tables.TbQuest.DataMap.ContainsKey(row.QuestId) || string.IsNullOrWhiteSpace(row.Flag))
                    throw new ArgumentException("任务标记引用非法：" + row.QuestId);
                builtFlags.Add(row.QuestId, row.Flag);
            }
            Validate(builtStories, builtEncounters, dialogues);
            encounters = builtEncounters;
            questFlags = builtFlags;
            stories = builtStories;
        }

        /// <summary>生产接入只支持 Condition / Dialogue / 无外部请求的 WaitAction / End。</summary>
        public static void Validate(IReadOnlyList<NarrativeContent> contents,
            IReadOnlyList<EncounterRules.Rule> rules, DialogueCatalog dialogues)
        {
            var byId = new Dictionary<string, NarrativeContent>(StringComparer.Ordinal);
            foreach (NarrativeContent story in contents)
            {
                byId.Add(story.Id, story);
                foreach (NarrativeContent.Stage stage in story.Stages)
                {
                    if (stage.Kind == NarrativeContent.StageKind.Battle || stage.IssueRequest || stage.RequiredParts.Length > 0)
                        throw new ArgumentException("尚未接入的剧情能力：" + story.Id + "/" + stage.Id);
                    if (stage.Kind == NarrativeContent.StageKind.WaitAction && stage.Exits.Count == 0)
                        throw new ArgumentException("等待阶段缺少出口：" + story.Id + "/" + stage.Id);
                    ValidateFacts(stage.Conditions);
                    if (stage.Kind != NarrativeContent.StageKind.Dialogue) continue;
                    if (!int.TryParse(stage.PayloadId, NumberStyles.None, CultureInfo.InvariantCulture, out int id) ||
                        !dialogues.TryGet(id, out DialogueContent dialogue))
                        throw new ArgumentException("剧情引用未知对白：" + stage.PayloadId);
                    foreach (DialogueContent.Node node in dialogue.Nodes)
                    {
                        if (node.Kind == DialogueContent.NodeKind.End && !stage.Exits.ContainsKey(node.Outcome))
                            throw new ArgumentException("剧情未映射对白出口：" + node.Outcome);
                        foreach (DialogueContent.Choice choice in node.Choices)
                        {
                            ValidateFacts(choice.Conditions);
                            if (!string.IsNullOrEmpty(choice.Outcome) && !stage.Exits.ContainsKey(choice.Outcome))
                                throw new ArgumentException("剧情未映射选项出口：" + choice.Outcome);
                        }
                    }
                }
            }
            var narrative = new NarrativeRules(contents, null);
            _ = new EncounterRules(rules, narrative);
            for (int i = 0; i < rules.Count; i++)
            {
                EncounterRules.Rule rule = rules[i];
                if (!byId.TryGetValue(rule.StoryId, out NarrativeContent story))
                    throw new ArgumentException("遭遇引用未知剧情：" + rule.StoryId);
                story.Get(rule.EntryStageId);
                ValidateFacts(rule.Conditions);
                for (int j = 0; j < i; j++)
                    if (rules[j].TriggerKind == rule.TriggerKind && rules[j].Priority == rule.Priority &&
                        (rules[j].TargetKind == rule.TargetKind || string.IsNullOrEmpty(rules[j].TargetKind) || string.IsNullOrEmpty(rule.TargetKind)))
                        throw new ArgumentException("遭遇优先级可能冲突：" + rule.Id);
            }
        }

        private static void ValidateFacts(NarrativeCondition[][] groups)
        {
            foreach (NarrativeCondition[] group in groups)
                foreach (NarrativeCondition condition in group)
                    if (condition.Fact == EncounterContext.Fact.TargetHostile || condition.Fact == EncounterContext.Fact.TargetDetected)
                        throw new ArgumentException("目标敌意/感知尚无真实来源，不能进入生产内容");
        }

        private static NarrativeCondition[][] Conditions(List<global::cfg.dialogue.ConditionGroup> source)
        {
            var result = new NarrativeCondition[source.Count][];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = new NarrativeCondition[source[i].All.Count];
                for (int j = 0; j < result[i].Length; j++)
                {
                    var condition = source[i].All[j];
                    result[i][j] = new NarrativeCondition
                    {
                        Fact = Parse<EncounterContext.Fact>(condition.Fact.ToString()), Key = condition.Key, Expected = condition.Expected,
                    };
                }
            }
            return result;
        }

        private static T Parse<T>(string value) where T : struct
        {
            if (!Enum.TryParse(value, false, out T result) || !Enum.IsDefined(typeof(T), result))
                throw new ArgumentException("未知叙事枚举：" + value);
            return result;
        }
    }
}
