// 职责：Luban 任务表（cfg.quest.TbQuest）→ QuestContent 的翻译与缓存，首次访问才读表并校验。
// 为什么新建：任务系统首次落地（PRP/quest-system 3.2）；DialogueCatalog 只认识对话表，职责不同不能塞。
using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core.Config;
using Game.Core.Telemetry;

namespace Game.Quest
{
    /// <summary>
    /// 任务内容目录（根作用域单例）。首次访问 <see cref="Content"/> 时才读表并翻译（惰性）：
    /// <see cref="IConfigService.Tables"/> 在配置服务初始化完成前访问会抛，所以构造时不碰表。
    /// <para>翻译规则：</para>
    /// <list type="bullet">
    /// <item>任务类别、目标类别按<b>名字</b>映射到本模块枚举，映射不到抛 <see cref="ArgumentException"/>（带任务 id）。</item>
    /// <item>id / 前置 / 环 / 目标文本等校验由 <see cref="QuestContent"/> 构造完成。</item>
    /// <item>额外校验：每个 TalkTo 目标的对话编号必须在对话表里存在。</item>
    /// </list>
    /// 校验失败埋 <c>catalog_invalid</c> 后原样抛出，不落缓存，下次访问会重新翻译并再报同一个错。
    /// </summary>
    public sealed class QuestCatalog
    {
        private readonly IConfigService config;
        private readonly ITelemetryScope telemetry;
        private QuestContent content;

        public QuestCatalog(IConfigService config, ITelemetryScope telemetry)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>全部任务定义；首次访问时读表、翻译并校验，内容非法抛 <see cref="ArgumentException"/>。</summary>
        public QuestContent Content
        {
            get
            {
                if (content == null)
                {
                    content = Build();
                }

                return content;
            }
        }

        /// <summary>按任务 id 取定义；表里没有返回 false。</summary>
        public bool TryGet(int id, out QuestDefinition definition) => Content.TryGet(id, out definition);

        private QuestContent Build()
        {
            try
            {
                global::cfg.Tables tables = config.Tables;
                IReadOnlyList<global::cfg.quest.Quest> rows = tables.TbQuest.DataList;

                var quests = new List<QuestDefinition>(rows.Count);
                foreach (global::cfg.quest.Quest row in rows)
                {
                    quests.Add(TranslateQuest(row));
                }

                // QuestContent 构造期已保证 TalkTo 的键是整数，这里只查对话表里有没有这一条。
                var built = new QuestContent(quests);
                ValidateTalkToDialogues(built, tables.TbDialogue.DataMap);

                telemetry.Track("catalog_built", ("quests", built.All.Count));
                return built;
            }
            catch (ArgumentException e)
            {
                telemetry.TrackWarn("catalog_invalid", TelemetryProps.Of(("reason", e.Message)));
                throw;
            }
        }

        private static QuestDefinition TranslateQuest(global::cfg.quest.Quest row)
        {
            QuestKind kind = TranslateQuestKind(row.Id, row.Kind);

            var objectives = new QuestObjectiveDefinition[row.Objectives.Count];
            for (int i = 0; i < objectives.Length; i++)
            {
                global::cfg.quest.Objective objective = row.Objectives[i];
                objectives[i] = new QuestObjectiveDefinition(
                    objective.Text,
                    TranslateObjectiveKind(row.Id, objective.Kind),
                    objective.Key,
                    objective.Count,
                    objective.Location);
            }

            return new QuestDefinition(row.Id, kind, row.Title, row.Description, row.Prerequisites, objectives);
        }

        private static void ValidateTalkToDialogues(QuestContent built,
            IReadOnlyDictionary<int, global::cfg.dialogue.Dialogue> dialogues)
        {
            foreach (QuestDefinition quest in built.All)
            {
                for (int i = 0; i < quest.Objectives.Count; i++)
                {
                    QuestObjectiveDefinition objective = quest.Objectives[i];
                    if (objective.Kind != QuestObjectiveKind.TalkTo)
                    {
                        continue;
                    }

                    int dialogueId = int.Parse(objective.Key, NumberStyles.Integer, CultureInfo.InvariantCulture);
                    if (!dialogues.ContainsKey(dialogueId))
                    {
                        throw new ArgumentException($"任务 {quest.Id}：第 {i + 1} 个目标引用的对话 {dialogueId} 不在对话表里");
                    }
                }
            }
        }

        // 按名字映射而不是强转整数：两边枚举哪天顺序漂了，这里会报错而不是悄悄读错类别。
        private static QuestKind TranslateQuestKind(int questId, global::cfg.quest.QuestKind kind)
        {
            if (!Enum.TryParse(kind.ToString(), false, out QuestKind result)
                || !Enum.IsDefined(typeof(QuestKind), result))
            {
                throw new ArgumentException($"任务 {questId}：表里的任务类别 {kind} 在 QuestKind 里没有同名项");
            }

            return result;
        }

        private static QuestObjectiveKind TranslateObjectiveKind(int questId, global::cfg.quest.ObjectiveKind kind)
        {
            if (!Enum.TryParse(kind.ToString(), false, out QuestObjectiveKind result)
                || !Enum.IsDefined(typeof(QuestObjectiveKind), result))
            {
                throw new ArgumentException($"任务 {questId}：表里的目标类别 {kind} 在 QuestObjectiveKind 里没有同名项");
            }

            return result;
        }
    }
}
