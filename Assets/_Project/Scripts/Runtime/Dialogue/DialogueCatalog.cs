// 职责：把 Luban 生成的对话表（TbDialogue / TbDialogueCharacter）翻译成 DialogueContent / DialogueCharacter 并缓存。
// 为什么新建：DialogueContent / DialogueCharacter 是与表无关的内容模型，塞进表适配会让模型依赖 cfg；
//   IConfigService 只递表根不做翻译；现有 Dialogue 目录下没有「表 → 模型」的适配层可扩展。
using System;
using System.Collections.Generic;
using Game.Core.Config;
using Game.Narrative;

namespace Game.Dialogue
{
    /// <summary>
    /// 对话内容目录。首次访问时才读表并翻译（惰性）：<see cref="IConfigService.Tables"/> 在配置服务
    /// 初始化完成前访问会抛，所以构造时不碰表。
    /// <para>翻译规则（PRP 3.1 / 3.5）：</para>
    /// <list type="bullet">
    /// <item><c>side</c> → 该槽 <c>Show</c>（角色 = speaker，表情为空取角色默认表情）；<c>clearOther</c> → 另一槽 <c>Clear</c>。</item>
    /// <item><c>speaker</c> 为空（旁白）不产生任何立绘变化。</item>
    /// <item><c>speakerName</c> 为空时用角色显示名；<c>revision</c> 小于 1 按 1。</item>
    /// <item><c>performance</c>（节点前插播的演出 id）去首尾空白；空串 = 不插播。</item>
    /// <item>条件 <c>anyOf[].all[]</c> → <c>NarrativeCondition[][]</c>（外层 OR、内层 AND），事实按名字映射。</item>
    /// </list>
    /// 内容非法时抛 <see cref="ArgumentException"/>，消息带对话 id，原始异常挂在 InnerException。
    /// </summary>
    public sealed class DialogueCatalog
    {
        private const int LeftSlot = 0;
        private const int RightSlot = 1;

        private readonly IConfigService config;
        private Dictionary<int, DialogueContent> contents;
        private List<DialogueCharacter> characters;

        public DialogueCatalog(IConfigService config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>全部对话角色（表顺序）。</summary>
        public IReadOnlyList<DialogueCharacter> Characters
        {
            get
            {
                EnsureBuilt();
                return characters;
            }
        }

        /// <summary>按对话树 id 取内容；表里没有返回 false。</summary>
        public bool TryGet(int id, out DialogueContent content)
        {
            EnsureBuilt();
            return contents.TryGetValue(id, out content);
        }

        private void EnsureBuilt()
        {
            if (contents != null)
            {
                return;
            }

            global::cfg.Tables tables = config.Tables;

            var characterList = new List<DialogueCharacter>(tables.TbDialogueCharacter.DataList.Count);
            var characterMap = new Dictionary<string, DialogueCharacter>(StringComparer.Ordinal);
            foreach (global::cfg.dialogue.Character row in tables.TbDialogueCharacter.DataList)
            {
                DialogueCharacter character = TranslateCharacter(row);
                characterList.Add(character);
                characterMap.Add(character.Id, character);
            }

            var contentMap = new Dictionary<int, DialogueContent>(tables.TbDialogue.DataList.Count);
            foreach (global::cfg.dialogue.Dialogue row in tables.TbDialogue.DataList)
            {
                contentMap.Add(row.Id, TranslateDialogue(row, characterMap));
            }

            // 两份都翻译成功才落缓存，半途抛出时下次访问会重新翻译并再次报同一个错。
            characters = characterList;
            contents = contentMap;
        }

        private static DialogueCharacter TranslateCharacter(global::cfg.dialogue.Character row)
        {
            var expressions = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (global::cfg.dialogue.Expression expression in row.Expressions)
            {
                if (expressions.ContainsKey(expression.Id))
                {
                    throw new ArgumentException($"对话角色 {row.Id} 的表情重复：{expression.Id}");
                }

                expressions.Add(expression.Id, expression.Sprite);
            }

            try
            {
                return new DialogueCharacter(row.Id, row.DisplayName, row.DefaultExpression, expressions);
            }
            catch (ArgumentException e)
            {
                throw new ArgumentException($"对话角色 {row.Id} 内容非法：{e.Message}", e);
            }
        }

        private static DialogueContent TranslateDialogue(global::cfg.dialogue.Dialogue row,
            IReadOnlyDictionary<string, DialogueCharacter> characterMap)
        {
            try
            {
                var nodes = new List<DialogueContent.Node>(row.Nodes.Count);
                foreach (global::cfg.dialogue.Node node in row.Nodes)
                {
                    nodes.Add(TranslateNode(node, characterMap));
                }

                return new DialogueContent(row.Id.ToString(), row.Entry, nodes);
            }
            catch (ArgumentException e)
            {
                throw new ArgumentException($"对话 {row.Id} 内容非法：{e.Message}", e);
            }
        }

        private static DialogueContent.Node TranslateNode(global::cfg.dialogue.Node row,
            IReadOnlyDictionary<string, DialogueCharacter> characterMap)
        {
            string speakerName = row.SpeakerName ?? string.Empty;
            DialogueContent.Portrait[] portraits = Array.Empty<DialogueContent.Portrait>();

            if (!string.IsNullOrWhiteSpace(row.Speaker))
            {
                if (!characterMap.TryGetValue(row.Speaker, out DialogueCharacter speaker))
                {
                    throw new ArgumentException($"节点 {row.Id} 的说话者不在角色表里：{row.Speaker}");
                }

                if (string.IsNullOrEmpty(speakerName))
                {
                    speakerName = speaker.DisplayName;
                }

                portraits = TranslatePortraits(row, speaker);
            }

            var choices = new DialogueContent.Choice[row.Choices.Count];
            for (int i = 0; i < choices.Length; i++)
            {
                choices[i] = TranslateChoice(row.Choices[i]);
            }

            return new DialogueContent.Node
            {
                Id = row.Id,
                Revision = row.Revision < 1 ? 1 : row.Revision,
                Kind = TranslateKind(row.Kind),
                SpeakerId = row.Speaker ?? string.Empty,
                SpeakerName = speakerName,
                Text = row.Text ?? string.Empty,
                Next = row.Next ?? string.Empty,
                Outcome = row.Outcome ?? string.Empty,
                Blocking = row.Blocking,
                PerformanceId = (row.Performance ?? string.Empty).Trim(),
                Portraits = portraits,
                Choices = choices,
            };
        }

        private static DialogueContent.Portrait[] TranslatePortraits(global::cfg.dialogue.Node row, DialogueCharacter speaker)
        {
            int slot = row.Side == global::cfg.dialogue.PortraitSide.Right ? RightSlot : LeftSlot;
            string expression = string.IsNullOrEmpty(row.Expression) ? speaker.DefaultExpression : row.Expression;
            if (!speaker.TrySprite(expression, out _))
            {
                throw new ArgumentException($"节点 {row.Id} 引用了角色 {speaker.Id} 不存在的表情：{expression}");
            }

            var show = new DialogueContent.Portrait
            {
                Slot = slot,
                Action = DialogueContent.PortraitAction.Show,
                CharacterId = speaker.Id,
                ExpressionId = expression,
            };

            if (!row.ClearOther)
            {
                return new[] { show };
            }

            var clear = new DialogueContent.Portrait
            {
                Slot = slot == LeftSlot ? RightSlot : LeftSlot,
                Action = DialogueContent.PortraitAction.Clear,
            };
            return new[] { show, clear };
        }

        private static DialogueContent.Choice TranslateChoice(global::cfg.dialogue.Choice row)
        {
            var groups = new NarrativeCondition[row.AnyOf.Count][];
            for (int g = 0; g < groups.Length; g++)
            {
                List<global::cfg.dialogue.Condition> all = row.AnyOf[g].All;
                var group = new NarrativeCondition[all.Count];
                for (int c = 0; c < group.Length; c++)
                {
                    group[c] = new NarrativeCondition
                    {
                        Fact = TranslateFact(all[c].Fact),
                        Key = all[c].Key ?? string.Empty,
                        Expected = all[c].Expected,
                    };
                }

                groups[g] = group;
            }

            return new DialogueContent.Choice
            {
                Id = row.Id,
                Text = row.Text ?? string.Empty,
                Next = row.Next ?? string.Empty,
                Outcome = row.Outcome ?? string.Empty,
                HideWhenUnavailable = row.HideWhenUnavailable,
                UnavailableReason = row.UnavailableReason ?? string.Empty,
                IconKey = row.Icon ?? string.Empty,
                Conditions = groups,
            };
        }

        private static DialogueContent.NodeKind TranslateKind(global::cfg.dialogue.NodeKind kind)
        {
            if (!Enum.TryParse(kind.ToString(), false, out DialogueContent.NodeKind result)
                || !Enum.IsDefined(typeof(DialogueContent.NodeKind), result))
            {
                throw new ArgumentException($"表里的节点类型 {kind} 在 DialogueContent.NodeKind 里没有同名项");
            }

            return result;
        }

        private static EncounterContext.Fact TranslateFact(global::cfg.dialogue.ConditionFact fact)
        {
            // 按名字映射而不是强转整数：两边枚举哪天顺序漂了，这里会报错而不是悄悄读错事实。
            if (!Enum.TryParse(fact.ToString(), false, out EncounterContext.Fact result)
                || !Enum.IsDefined(typeof(EncounterContext.Fact), result))
            {
                throw new ArgumentException($"表里的条件事实 {fact} 在 EncounterContext.Fact 里没有同名项");
            }

            return result;
        }
    }
}
