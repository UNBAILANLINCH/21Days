// 职责：酒肆意图处理和结果埋点；回合制内核不拥有场外灌酒、离区与重试。
using System;
using System.Collections.Generic;
using Game.Core.Telemetry;
using Game.Core.Simulation;
namespace Game.Tavern
{
    public sealed class TavernController
    {
        private readonly ITelemetryScope telemetry;
        private readonly Func<ulong> seedSource;
        private readonly TavernConfig config;
        private ulong roundOrdinal;
        private readonly Dictionary<string, TavernOpponentLearning> opponents = new Dictionary<string, TavernOpponentLearning>(StringComparer.Ordinal);
        public TavernDrunkRules Rules { get; }
        public TavernFingerGuessRules Guess { get; } = new TavernFingerGuessRules();
        public TavernController(TavernConfig config, ITelemetryScope telemetry, Func<ulong> seedSource = null)
        { if (config == null) throw new ArgumentNullException(nameof(config)); this.config = config; this.telemetry = telemetry ?? NullTelemetryScope.Instance; this.seedSource = seedSource; Rules = config.CreateRules(); }
        public bool CanChallenge => Rules.BossDrunk >= config.ChallengeDrunk;
        public string UnlockedClue(string targetId) => targetId == "shop_customer_a" && Rules.CustomerDrunkFor(targetId) >= config.CustomerClueDrunk ? config.CustomerAClue
            : targetId == "shop_customer_b" && Rules.CustomerDrunkFor(targetId) >= config.CustomerClueDrunk ? config.CustomerBClue : "";
        public long BeginGuess(string targetId)
        {
            if (!IsOpponent(targetId)) throw new ArgumentException("划拳对手不存在");
            // 会话派生流与回合制相同，不污染探索 tick 随机或借表现流决定罚酒。
            var random = new XorShiftRandomStream((seedSource?.Invoke() ?? 0UL) ^ 0x54415645524EUL ^ ++roundOrdinal);
            Learner(targetId).Choose(random, config.FavoriteFinger(targetId), config.FavoriteGuess(targetId), config.HabitChance, out int fingers, out int call);
            long round = Guess.Begin(targetId, fingers, call);
            telemetry.Track("guess_started", ("target", targetId), ("round", round)); return round;
        }
        public bool ResolveGuess(in TavernIntent intent, out TavernFingerGuessRules.Result result, out string reason)
        {
            if (!IsOpponent(intent.TargetId) || !Guess.TryResolve(in intent, out result, out reason))
            { result = default; reason = "本局无效，请重新入席"; telemetry.TrackWarn("guess_rejected"); return false; }
            if (result.Value != TavernFingerGuessRules.Outcome.Tie)
            {
                TavernAction action = result.Value == TavernFingerGuessRules.Outcome.OpponentWins ? TavernAction.DrinkPlayer
                    : intent.TargetId == "shop_boss" ? TavernAction.DrinkBoss : TavernAction.DrinkCustomer;
                var drink = new TavernIntent(action, true, intent.TargetId);
                if (!Rules.Apply(in drink, out reason)) throw new InvalidOperationException(reason);
            }
            var learner = Learner(intent.TargetId);
            learner.Observe(intent.Fingers, intent.Call, result.Value);
            learner.AdjustStrength(config.DesiredStrength(intent.TargetId, learner.Performance), config.MaximumStrengthChange);
            telemetry.Track("guess_resolved", ("target", intent.TargetId), ("outcome", (int)result.Value), ("player", Rules.PlayerDrunk), ("boss", Rules.BossDrunk));
            telemetry.Track("difficulty_updated", ("target", intent.TargetId), ("strength", learner.Strength), ("performance", learner.Performance), ("samples", learner.Samples)); return true;
        }
        private TavernOpponentLearning Learner(string id)
        {
            if (!opponents.TryGetValue(id, out var learner)) { learner = new TavernOpponentLearning(config.InitialStrength(id)); opponents.Add(id, learner); }
            return learner;
        }
        public Dictionary<string, TavernLearningData> CaptureLearning()
        {
            var result = new Dictionary<string, TavernLearningData>(StringComparer.Ordinal);
            foreach (var entry in opponents) result.Add(entry.Key, entry.Value.Capture());
            return result;
        }
        public void RestoreLearning(Dictionary<string, TavernLearningData> saved)
        {
            var restored = new Dictionary<string, TavernOpponentLearning>(StringComparer.Ordinal);
            if (saved != null) foreach (var entry in saved)
            {
                if (!IsOpponent(entry.Key) || entry.Value == null) throw new ArgumentException("划拳学习目标不存在");
                restored.Add(entry.Key, new TavernOpponentLearning(config.InitialStrength(entry.Key), entry.Value));
            }
            Guess.Cancel(); opponents.Clear();
            foreach (var entry in restored) opponents.Add(entry.Key, entry.Value);
        }
        private static bool IsOpponent(string id) => id == "shop_boss" || id == "shop_customer_a" || id == "shop_customer_b";
        public string Apply(TavernAction action, string role, string targetId = "customer")
        {
            var intent = new TavernIntent(action, role == "guest", targetId);
            bool ok = Rules.Apply(in intent, out string reason);
            telemetry.Track("intent_resolved", ("action", (int)action), ("ok", ok), ("player", Rules.PlayerDrunk), ("boss", Rules.BossDrunk));
            return !ok ? reason : action == TavernAction.Leave ? "已离开酒肆，场外醉酒清零" : "饮酒完成，观察移动与感知范围变化";
        }
        public void CancelGuess() { Guess.Cancel(); }
        public void Reset() { Guess.Cancel(); Rules.Reset(); telemetry.Track("reset"); }
    }
}
