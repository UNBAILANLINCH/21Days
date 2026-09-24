// 职责：纯 C# 的对白推进、选择与阅读状态；UIView 和通用 GameFlow 都不应承载台词规则。
using System;
using System.Collections.Generic;
using Game.Core.Telemetry;
using Game.Narrative;
using Newtonsoft.Json;

namespace Game.Dialogue
{
    public sealed class DialogueRules
    {
        // 同步快进的句数上限；超过即视为内容环路，避免 Skip 死循环。
        private const int MaxSkipLines = 4096;
        private readonly DialogueReadData read;
        private readonly int historyLimit;
        private readonly ITelemetryScope telemetry;
        private DialogueContent content;
        private DialogueSaveData state = new DialogueSaveData();
        private int visibleCharacters;
        private int totalCharacters;

        public DialogueRules(DialogueReadData read, int historyLimit, ITelemetryScope telemetry)
        {
            this.read = read ?? throw new ArgumentNullException(nameof(read));
            read.Validate();
            if (historyLimit < 1) throw new ArgumentOutOfRangeException(nameof(historyLimit));
            this.historyLimit = historyLimit;
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        public long Generation { get; private set; }
        public long Visit => state.Visit;
        public DialogueSaveData.Phase Phase => state.CurrentPhase;
        public DialogueContent.Node Current => content == null || string.IsNullOrEmpty(state.NodeId) ? null : content.Get(state.NodeId);
        public string Text => state.ResolvedText;
        public string Speaker => state.ResolvedSpeaker;
        public string Outcome => state.Outcome;
        public int VisibleCharacters => visibleCharacters;
        public bool Blocking => Current != null && (Current.Blocking || Phase == DialogueSaveData.Phase.AwaitChoice);
        public IReadOnlyList<DialogueSaveData.HistoryEntry> History => state.History;
        public IReadOnlyList<DialogueContent.Portrait> Portraits => state.Portraits;
        public bool HistoryTruncated => state.HistoryTruncated;
        public event Action<DialogueContent.Choice> OnChoiceSelected;

        public void Start(DialogueContent conversation)
        {
            content = conversation ?? throw new ArgumentNullException(nameof(conversation));
            Generation++;
            state.ConversationId = content.Id;
            state.Outcome = string.Empty;
            state.Portraits = Array.Empty<DialogueContent.Portrait>();
            Enter(content.Entry);
            telemetry.Track("started", ("conversation", content.Id));
        }

        // TMP 设置完整文本并解析后传入实际字符数；规则不截取富文本字符串。
        public void Ready(long generation, long visit, int characters, string resolvedText, string resolvedSpeaker)
        {
            if (!Matches(generation, visit) || Phase != DialogueSaveData.Phase.Preparing) return;
            if (characters < 0 || resolvedText == null || resolvedSpeaker == null) throw new ArgumentException("文本准备结果非法");
            state.ResolvedText = resolvedText;
            state.ResolvedSpeaker = resolvedSpeaker;
            totalCharacters = characters;
            visibleCharacters = 0;
            state.CurrentPhase = DialogueSaveData.Phase.Typing;
            if (characters == 0) RevealAll();
        }

        public void RevealTo(int count)
        {
            if (Phase != DialogueSaveData.Phase.Typing) return;
            visibleCharacters = Math.Max(visibleCharacters, Math.Min(totalCharacters, Math.Max(0, count))); // lint-ok: TMP 可见字符为表现层整数计数，不参与玩法数值或回放
            if (visibleCharacters == totalCharacters) RevealAll();
        }

        public bool Apply(in DialogueIntent intent, EncounterContext context)
        {
            if (!Matches(intent.Generation, intent.Visit)) return false;
            if (intent.Kind == DialogueIntent.Action.Advance)
            {
                if (Phase == DialogueSaveData.Phase.Typing) { RevealAll(); return true; }
                if (Phase != DialogueSaveData.Phase.AwaitAdvance) return false;
                Enter(Current.Next);
                return true;
            }
            if (intent.Kind != DialogueIntent.Action.Choose || Phase != DialogueSaveData.Phase.AwaitChoice) return false;
            DialogueContent.Choice selected = null;
            foreach (DialogueContent.Choice choice in Current.Choices)
                if (string.Equals(choice.Id, intent.ChoiceId, StringComparison.Ordinal)) selected = choice;
            if (selected == null || !NarrativeCondition.Matches(selected.Conditions, context))
            {
                telemetry.TrackWarn("choice_rejected");
                return false;
            }
            Append(selected.Text, string.Empty, true);
            telemetry.Track("choice_selected", ("choice", selected.Id));
            if (!string.IsNullOrEmpty(selected.Next)) Enter(selected.Next);
            else Complete(selected.Outcome);
            OnChoiceSelected?.Invoke(selected);
            return true;
        }

        // 同步快进：Line 节点逐句「补全 + 记历史 + 标已读 + 进下一句」，停在 AwaitChoice / Completed / Closed。
        // Preparing 阶段不等 TMP 字数：历史记 node.Text 与 resolveSpeaker 解析出的说话者。
        public int Skip(long generation, Func<DialogueContent.Node, string> resolveSpeaker)
        {
            if (generation != Generation) return 0;
            if (resolveSpeaker == null) throw new ArgumentNullException(nameof(resolveSpeaker));
            int count = 0;
            while (Phase == DialogueSaveData.Phase.Preparing || Phase == DialogueSaveData.Phase.Typing ||
                   Phase == DialogueSaveData.Phase.AwaitAdvance)
            {
                if (count >= MaxSkipLines) throw new InvalidOperationException("跳过句数超过上限，对白内容可能成环：" + content.Id);
                DialogueContent.Node node = Current;
                if (Phase == DialogueSaveData.Phase.Preparing)
                {
                    state.ResolvedText = node.Text;
                    state.ResolvedSpeaker = resolveSpeaker(node) ?? string.Empty;
                    totalCharacters = 0;
                }
                if (Phase != DialogueSaveData.Phase.AwaitAdvance) RevealAll();
                count++;
                Enter(node.Next);
            }
            if (count > 0) telemetry.Track("skipped", ("count", count));
            return count;
        }

        public DialogueSaveData Capture()
        {
            if (Phase == DialogueSaveData.Phase.Preparing) throw new InvalidOperationException("对白尚在准备，存档须等待 Ready 稳定点");
            return JsonConvert.DeserializeObject<DialogueSaveData>(JsonConvert.SerializeObject(state));
        }

        public void Restore(DialogueContent conversation, DialogueSaveData saved)
        {
            if (conversation == null || saved == null || saved.ConversationId != conversation.Id || saved.History == null ||
                saved.Portraits == null || saved.Visit < 1 || saved.ResolvedText == null || saved.ResolvedSpeaker == null ||
                saved.CurrentPhase == DialogueSaveData.Phase.Preparing || !Enum.IsDefined(typeof(DialogueSaveData.Phase), saved.CurrentPhase))
                throw new ArgumentException("对白快照非法");
            DialogueContent.Node node = conversation.Get(saved.NodeId);
            if (node.Revision != saved.NodeRevision) throw new ArgumentException("当前台词版本不兼容");
            if (saved.History.Count > historyLimit || saved.Portraits.Length > DialogueContent.SlotCount) throw new ArgumentException("对白快照超出限制");
            foreach (DialogueSaveData.HistoryEntry entry in saved.History)
                if (entry == null || entry.Text == null) throw new ArgumentException("历史记录损坏");
            var slots = new HashSet<int>();
            foreach (DialogueContent.Portrait portrait in saved.Portraits)
                if (portrait == null || portrait.Slot < 0 || portrait.Slot >= DialogueContent.SlotCount || !slots.Add(portrait.Slot) ||
                    portrait.Action != DialogueContent.PortraitAction.Show || string.IsNullOrWhiteSpace(portrait.CharacterId) ||
                    string.IsNullOrWhiteSpace(portrait.ExpressionId))
                    throw new ArgumentException("立绘快照损坏");
            bool linePhase = saved.CurrentPhase == DialogueSaveData.Phase.Preparing || saved.CurrentPhase == DialogueSaveData.Phase.Typing ||
                saved.CurrentPhase == DialogueSaveData.Phase.AwaitAdvance;
            if ((linePhase && node.Kind != DialogueContent.NodeKind.Line) ||
                (saved.CurrentPhase == DialogueSaveData.Phase.AwaitChoice && node.Kind != DialogueContent.NodeKind.Choice))
                throw new ArgumentException("对白阶段与内容不符");
            if (saved.CurrentPhase == DialogueSaveData.Phase.Completed)
            {
                bool valid = node.Kind == DialogueContent.NodeKind.End && saved.Outcome == node.Outcome;
                foreach (DialogueContent.Choice choice in node.Choices)
                    valid |= node.Kind == DialogueContent.NodeKind.Choice && !string.IsNullOrEmpty(choice.Outcome) && saved.Outcome == choice.Outcome;
                if (!valid) throw new ArgumentException("完成状态没有合法出口");
            }
            state = JsonConvert.DeserializeObject<DialogueSaveData>(JsonConvert.SerializeObject(saved));
            content = conversation;
            Generation++;
            visibleCharacters = int.MaxValue;
            if (linePhase)
            {
                state.CurrentPhase = DialogueSaveData.Phase.AwaitAdvance;
                RecordLine();
            }
            telemetry.Track("restored", ("node", state.NodeId));
        }

        public void Cancel()
        {
            Generation++;
            state.CurrentPhase = DialogueSaveData.Phase.Closed;
            state.Outcome = string.Empty;
            telemetry.Track("cancelled");
        }

        private bool Matches(long generation, long visit) => generation == Generation && visit == state.Visit;
        private void Enter(string id)
        {
            DialogueContent.Node node = content.Get(id);
            state.NodeId = id;
            state.NodeRevision = node.Revision;
            state.Visit++;
            state.Recorded = false;
            state.ResolvedText = node.Text;
            state.ResolvedSpeaker = node.SpeakerName;
            visibleCharacters = 0;
            var portraits = new List<DialogueContent.Portrait>(state.Portraits);
            foreach (DialogueContent.Portrait change in node.Portraits)
            {
                if (change.Action == DialogueContent.PortraitAction.Keep) continue;
                portraits.RemoveAll(p => p.Slot == change.Slot);
                if (change.Action == DialogueContent.PortraitAction.Show)
                    portraits.Add(new DialogueContent.Portrait { Slot = change.Slot, Action = change.Action,
                        CharacterId = change.CharacterId, ExpressionId = change.ExpressionId });
            }
            state.Portraits = portraits.ToArray();
            state.CurrentPhase = node.Kind == DialogueContent.NodeKind.Line ? DialogueSaveData.Phase.Preparing :
                node.Kind == DialogueContent.NodeKind.Choice ? DialogueSaveData.Phase.AwaitChoice : DialogueSaveData.Phase.Completed;
            if (node.Kind == DialogueContent.NodeKind.End) Complete(node.Outcome);
            telemetry.Track("node_entered", ("node", id));
        }
        private void RevealAll()
        {
            visibleCharacters = totalCharacters;
            state.CurrentPhase = DialogueSaveData.Phase.AwaitAdvance;
            RecordLine();
        }
        private void RecordLine()
        {
            if (!state.Recorded)
            {
                Append(state.ResolvedText, state.ResolvedSpeaker, false);
                state.Recorded = true;
            }
            read.Keys.Add(DialogueReadData.Key(content.Id, state.NodeId, state.NodeRevision));
        }
        private void Append(string text, string speaker, bool choice)
        {
            state.History.Add(new DialogueSaveData.HistoryEntry { Visit = state.Visit, NodeId = state.NodeId,
                Text = text, Speaker = speaker, IsChoice = choice });
            if (state.History.Count > historyLimit)
            {
                state.History.RemoveAt(0);
                state.HistoryTruncated = true;
            }
        }
        private void Complete(string outcome)
        {
            state.Outcome = outcome ?? string.Empty;
            state.CurrentPhase = DialogueSaveData.Phase.Completed;
        }
    }
}
