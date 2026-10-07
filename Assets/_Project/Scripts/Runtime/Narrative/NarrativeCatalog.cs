// 职责：翻译 Luban 叙事表并在执行前校验跨表引用；不把配置服务带入纯规则。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
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
                        AllowEncounter = node.AllowEncounter, IssueRequest = node.IssueRequest,
                        RequiredParts = node.RequiredParts.ToArray(), BattleResults = node.BattleResults.ToArray(),
                        Conditions = Conditions(node.AnyOf),
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

        /// <summary>
        /// 生产内容校验，<b>本方法是结构类与声明类规则的唯一出处</b>——<see cref="NarrativeContent"/> 的构造函数
        /// 只保证对象自身的不变量，不再判断「该不该声明」「出口够不够」，避免同一条规则两处抛、
        /// 行为取决于调用方是先构造还是先校验。
        /// <para>
        /// 内部顺序有意为之：<b>先声明类、后结构类</b>。声明类（战斗结果、外部请求、多部分行为）
        /// 的信息量更大——写内容的人第一眼要知道「我这行根本不该有这个东西」；出口缺失往往是它的下游后果。
        /// 反过来排会让一条前置检查遮蔽掉真正的原因。
        /// </para>
        /// </summary>
        public static void Validate(IReadOnlyList<NarrativeContent> contents,
            IReadOnlyList<EncounterRules.Rule> rules, DialogueCatalog dialogues)
        {
            var byId = new Dictionary<string, NarrativeContent>(StringComparer.Ordinal);
            foreach (NarrativeContent story in contents)
            {
                byId.Add(story.Id, story);
                foreach (NarrativeContent.Stage stage in story.Stages)
                {
                    // ── 声明类（先）：这个阶段允许声明什么 ───────────────────────────────
                    ValidateBattleResults(story, stage);
                    if (stage.IssueRequest && stage.Exits.Count == 0)
                        throw new ArgumentException("外部请求阶段缺少出口：" + story.Id + "/" + stage.Id);
                    if (stage.RequiredParts.Length > 0 && !stage.Exits.ContainsKey("Success"))
                        throw new ArgumentException("多部分行为缺少 Success 出口：" + story.Id + "/" + stage.Id);
                    var parts = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string part in stage.RequiredParts)
                        if (!parts.Add(part)) throw new ArgumentException("行为部分 ID 重复：" + story.Id + "/" + stage.Id);
                    // ── 结构类（后）：出口够不够 ────────────────────────────────────────
                    if (stage.Kind == NarrativeContent.StageKind.WaitAction && stage.Exits.Count == 0)
                        throw new ArgumentException("等待阶段缺少出口：" + story.Id + "/" + stage.Id);
                    if (stage.Kind == NarrativeContent.StageKind.Battle) ValidateBattleExits(story, stage);
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

        /// <summary>
        /// 声明类规则：战斗阶段必须声明结果集合，每条结果码都要是合法 <see cref="BattleResult"/> 且有出口；
        /// 非战斗阶段不得声明。跨阶段出口目标检查不在这里（见 <see cref="ValidateBattleExits"/>）。
        /// </summary>
        private static void ValidateBattleResults(NarrativeContent story, NarrativeContent.Stage stage)
        {
            if (stage.Kind != NarrativeContent.StageKind.Battle)
            {
                if (stage.BattleResults.Length > 0)
                    throw new ArgumentException("只有战斗阶段可以声明战斗结果：" + story.Id + "/" + stage.Id);
                return;
            }
            if (stage.BattleResults.Length == 0)
                throw new ArgumentException("战斗阶段必须声明至少一个战斗结果：" + story.Id + "/" + stage.Id);
            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in stage.BattleResults)
            {
                if (!BattleResult.TryParse(key, out BattleResult result))
                    throw new ArgumentException("战斗结果非法：" + story.Id + "/" + stage.Id + "/" + key);
                if (!declared.Add(result.ExitKey))
                    throw new ArgumentException("战斗结果重复：" + story.Id + "/" + stage.Id + "/" + result.ExitKey);
                if (!stage.Exits.ContainsKey(result.ExitKey))
                    throw new ArgumentException("战斗结果缺少出口：" + story.Id + "/" + stage.Id + "/" + result.ExitKey);
            }
        }

        /// <summary>
        /// 战斗出口的跨阶段检查：出口目标必须存在，且不能指向另一个战斗阶段。
        /// 战斗阶段只能由外部战斗结果推进，战斗直接进战斗等于永远不回话，属内容错误不是合法串联。
        /// 例外是**自环**：BOSS 换形态时阶段结构不变，只翻一个标记（`BossPhaseChanged:<形态>` → 同一阶段）。
        /// 一个 `Stage` 对应一场遭遇，换形态不该另起一个战斗阶段。
        /// </summary>
        private static void ValidateBattleExits(NarrativeContent story, NarrativeContent.Stage stage)
        {
            foreach (KeyValuePair<string, string> exit in stage.Exits)
            {
                NarrativeContent.Stage next = story.Get(exit.Value);
                if (next.Kind == NarrativeContent.StageKind.Battle && exit.Value != stage.Id)
                    throw new ArgumentException("战斗阶段不能直接进入另一个战斗阶段：" + story.Id + "/" + stage.Id + "→" + exit.Value);
            }
        }

        private static void ValidateFacts(NarrativeCondition[][] groups)
        {
            foreach (NarrativeCondition[] group in groups)
                foreach (NarrativeCondition condition in group)
                {
                    if (condition.Fact == EncounterContext.Fact.TargetHostile || condition.Fact == EncounterContext.Fact.TargetDetected)
                        throw new ArgumentException("目标敌意/感知尚无真实来源，不能进入生产内容");
                    if (condition.Fact == EncounterContext.Fact.StoryFlag) ValidateStoryFlagKey(condition.Key);
                }
        }

        // --------------------------------------------------------------------------------------------
        // 剧情事实字典（V1–V3）；真源 ai-docs/docs/story-facts.md §3.2 与 §4。
        // 改字典与改这三份常量必须是同一次改动（同文件 §5「已登记键的来源」）。
        // 为什么不查「第二段是登记过的事实」：item.<id>.owned 与 combat.phase.<n> 的第二段是实例 ID，
        // 不是状态名，按字典登记项逐字查会把合法键判死；只查命名空间与档位两处机械可判的部分。
        // --------------------------------------------------------------------------------------------

        /// <summary>字典 §3.2 命名空间白名单。</summary>
        private static readonly string[] KnownNamespaces =
            { "identity", "stealth", "chase", "world", "stage", "item", "combat", "route" };

        /// <summary>
        /// 按 id 生成的键：前缀固定、后缀是实例 ID，逐个列举会把每次新增 id 变成一次校验器改动。
        /// 目前是任务完成标记（`narrative_quest_flags.json` 写入）。判定这类键时不再查命名空间白名单，
        /// 命中前缀后只保留 V1 格式检查。主窗口 2026-10-07 拍板：存量键不迁徙，前缀在规则里放行。
        /// </summary>
        private static readonly string[] RegisteredIdKeyPrefixes = { "quest_completed_" };

        /// <summary>字典 §3.2 档位段词表；逐命名空间登记，两个词表不混用（§4.5 combat.* 不走档位）。
        /// 两套词（low/mid/high 与 none/some/full）**都登记**：字典 §3.2 声明了两套，
        /// 只登记在用的那套会让另一套的第三段被当成「实例后缀」放行，于是 `identity.suspision.none`
        /// 这类拼错（状态名写错 + 另一套档位词）会静默通过——而抓这种拼错正是 V1–V3 存在的理由。
        /// 登记后：档位词被认出来 → 继续查状态名是否登记 → 拼错当场拒。</summary>
        private static readonly Dictionary<string, string[]> KnownTiers = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["identity"] = new[] { "low", "mid", "high", "none", "some", "full" },
            ["stealth"] = new[] { "low", "mid", "high", "none", "some", "full" },
            ["world"] = new[] { "low", "mid", "high", "none", "some", "full" },
        };

        /// <summary>带档位段时应登记在字典 §4 的第二段状态名，用于抓 identity.suspision.high 这类拼错。</summary>
        private static readonly Dictionary<string, string[]> RegisteredTierStates = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["identity"] = new[] { "suspicion", "exposedCount" },
            ["stealth"] = new[] { "knockdownCount" },
            ["world"] = new[] { "water", "drunk", "queueConsumed" },
        };

        /// <summary>
        /// 字典 §3.2：&lt;命名空间&gt;.&lt;状态名&gt;[.&lt;档位&gt;]，小写字母数字下划线加点。
        /// 段数 1–3：按 id 生成的历史键（quest_completed_1002）是单段，存量键不迁徙。
        /// </summary>
        private static readonly Regex StoryFlagKeyPattern = new Regex(@"^[a-z][a-z0-9_]*(\.[a-z0-9_]+){0,2}$", RegexOptions.CultureInvariant);

        private static void ValidateStoryFlagKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("剧情标记不可为空");
            if (!StoryFlagKeyPattern.IsMatch(key))
                throw new ArgumentException("V1 剧情标记格式非法（要求 <命名空间>.<状态名>[.<档位>]，小写字母数字下划线加点）：" + key);
            if (HasRegisteredIdPrefix(key)) return;
            string[] segments = key.Split('.');
            // 单段键没有命名空间可查——V1 已经放行（字典 §3.2：允许下划线与单段键，存量键零迁徙），
            // 这里必须一并放行，否则 `knows_elder` 这类剧本里早就在用的标记会被 V2 一律误拒。
            // 它只在「被某个叙事阶段引用」时才会走到本方法，所以是潜伏缺口而不是无害的宽松。
            if (segments.Length == 1) return;
            if (Array.IndexOf(KnownNamespaces, segments[0]) < 0)
                throw new ArgumentException("V2 剧情标记命名空间未知（白名单 " + string.Join("/", KnownNamespaces) + "）：" + key);
            if (segments.Length != 3) return;
            if (!KnownTiers.TryGetValue(segments[0], out string[] tiers) || Array.IndexOf(tiers, segments[2]) < 0)
            {
                // 第三段不是档位词（如 item.tooth.owned）＝实例后缀，字典 §4 允许，不做登记检查。
                return;
            }
            if (Array.IndexOf(RegisteredTierStates[segments[0]], segments[1]) < 0)
                throw new ArgumentException("V3 剧情标记状态名未登记于 story-facts.md §4：" + key);
        }

        private static bool HasRegisteredIdPrefix(string key)
        {
            foreach (string prefix in RegisteredIdKeyPrefixes)
                if (key.StartsWith(prefix, StringComparison.Ordinal) && key.Length > prefix.Length) return true;
            return false;
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
