// 职责：条件求值的只读玩法快照；现有 PlayerSnapshot 不包含目标身份与剧情事实，不能扩进 Core。
using System;
using System.Collections.Generic;

namespace Game.Narrative
{
    public sealed class EncounterContext
    {
        public enum Fact { PlayerAlive, PlayerSneaking, PlayerDisguised, TargetAlive, TargetHostile, TargetDetected, StoryFlag }
        private readonly HashSet<string> flags;
        public EncounterContext(string targetId, string targetKind, bool playerAlive, bool sneaking,
            bool disguised, bool targetAlive, bool hostile, bool detected, IEnumerable<string> storyFlags = null)
        {
            if (string.IsNullOrWhiteSpace(targetId)) throw new ArgumentException("目标身份不可为空", nameof(targetId));
            TargetId = targetId;
            TargetKind = targetKind ?? string.Empty;
            PlayerAlive = playerAlive;
            PlayerSneaking = sneaking;
            PlayerDisguised = disguised;
            TargetAlive = targetAlive;
            TargetHostile = hostile;
            TargetDetected = detected;
            flags = new HashSet<string>(storyFlags ?? Array.Empty<string>(), StringComparer.Ordinal);
        }
        public string TargetId { get; }
        public string TargetKind { get; }
        public bool PlayerAlive { get; }
        public bool PlayerSneaking { get; }
        public bool PlayerDisguised { get; }
        public bool TargetAlive { get; }
        public bool TargetHostile { get; }
        public bool TargetDetected { get; }
        public bool Read(Fact fact, string key)
        {
            switch (fact)
            {
                case Fact.PlayerAlive: return PlayerAlive;
                case Fact.PlayerSneaking: return PlayerSneaking;
                case Fact.PlayerDisguised: return PlayerDisguised;
                case Fact.TargetAlive: return TargetAlive;
                case Fact.TargetHostile: return TargetHostile;
                case Fact.TargetDetected: return TargetDetected;
                case Fact.StoryFlag:
                    if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("剧情标记不可为空");
                    return flags.Contains(key);
                default: throw new ArgumentOutOfRangeException(nameof(fact), "未接入的条件类型");
            }
        }
    }
}
