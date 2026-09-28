// 职责：从当前玩家与槽位分区取事实；目标由场景显式登记，缺失目标按不可用处理。
using System;
using System.Collections.Generic;
using Game.Core.Save;
using Game.Dialogue;
using Game.Player;

namespace Game.Narrative
{
    public sealed class NarrativeConditionSource : IDialogueConditionSource
    {
        private readonly PlayerModel player;
        private readonly ISaveService saves;
        private readonly Dictionary<string, NarrativeTrigger> targets = new Dictionary<string, NarrativeTrigger>(StringComparer.Ordinal);

        public NarrativeConditionSource(PlayerModel player, ISaveService saves)
        {
            this.player = player ?? throw new ArgumentNullException(nameof(player));
            this.saves = saves ?? throw new ArgumentNullException(nameof(saves));
        }

        public void Register(NarrativeTrigger target)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.TargetId)) throw new ArgumentException("叙事目标身份不可为空");
            if (targets.TryGetValue(target.TargetId, out NarrativeTrigger previous) && previous != null && previous != target)
                throw new ArgumentException("重复叙事目标：" + target.TargetId);
            targets[target.TargetId] = target;
        }

        public void Unregister(NarrativeTrigger target)
        {
            if (target != null && targets.TryGetValue(target.TargetId, out NarrativeTrigger current) && current == target)
                targets.Remove(target.TargetId);
        }

        public NarrativeTrigger Find(string id) => targets.TryGetValue(id, out NarrativeTrigger target) ? target : null;

        public EncounterContext Snapshot(string targetId)
        {
            NarrativeTrigger target = Find(targetId);
            PlayerSnapshot snapshot = player.Snapshot;
            return new EncounterContext(targetId, target == null ? string.Empty : target.TargetKind,
                snapshot.IsAlive, snapshot.IsSneaking, snapshot.IsDisguised,
                target != null && target.isActiveAndEnabled, false, false, saves.Get<NarrativeSaveData>().StoryFlags);
        }
    }
}
