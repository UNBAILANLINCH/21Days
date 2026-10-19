// 职责：用已完成的历史预测对手，并平滑调整策略强度；不看本局玩家选择、不依赖 Unity。
// 现有随机流只负责抽样，不拥有玩家历史；划拳判胜规则也不应学习策略，故另设纯 C# 学习器。
using System;
using System.Collections.Generic;
using Game.Core.Simulation;
namespace Game.Tavern
{
    public sealed class TavernOpponentLearning
    {
        public const int HistoryLimit = 24;
        private readonly TavernLearningData state;
        public TavernOpponentLearning(int initialStrength, TavernLearningData saved = null)
        {
            if (initialStrength < 0 || initialStrength > 1000) throw new ArgumentOutOfRangeException(nameof(initialStrength));
            if (saved == null) state = new TavernLearningData { Strength = initialStrength };
            else
            {
                if (saved.History == null || saved.History.Count > HistoryLimit || saved.Performance < 0 || saved.Performance > 1000 || saved.Strength < 0 || saved.Strength > 1000)
                    throw new ArgumentException("划拳学习记录损坏");
                foreach (int sample in saved.History) if (sample < 0 || sample >= 36) throw new ArgumentException("划拳历史不是合法选择");
                state = new TavernLearningData { Strength = saved.Strength, Performance = saved.Performance, History = new List<int>(saved.History) };
            }
        }
        public int Performance => state.Performance;
        public int Strength => state.Strength;
        public int Samples => state.History.Count;
        public TavernLearningData Capture() => new TavernLearningData { Performance = state.Performance, Strength = state.Strength, History = new List<int>(state.History) };
        public void Observe(int fingers, int call, TavernFingerGuessRules.Outcome result)
        {
            if (!TavernFingerGuessRules.IsLegal(fingers, call)) throw new ArgumentException("不能学习非法拳局");
            if (state.History.Count == HistoryLimit) state.History.RemoveAt(0);
            state.History.Add(fingers * 6 + call - fingers);
            if (result != TavernFingerGuessRules.Outcome.Tie)
                state.Performance += ((result == TavernFingerGuessRules.Outcome.PlayerWins ? 1000 : 0) - state.Performance) / 8;
        }
        public void AdjustStrength(int desired, int maximumChange)
        {
            if (desired < 0 || desired > 1000 || maximumChange < 0) throw new ArgumentOutOfRangeException(nameof(desired));
            int delta = desired - state.Strength;
            state.Strength += delta > maximumChange ? maximumChange : delta < -maximumChange ? -maximumChange : delta;
        }
        public void Choose(IRandomStream random, int favoriteFinger, int favoriteGuess, int habitChance, out int fingers, out int call)
        {
            if (random == null || favoriteFinger < 0 || favoriteFinger > 5 || favoriteGuess < 0 || favoriteGuess > 5 || habitChance < 0 || habitChance > 1000)
                throw new ArgumentException("划拳策略配置不合法");
            fingers = random.Range(0, 1000) < habitChance ? favoriteFinger : random.Range(0, 6);
            int guess = random.Range(0, 1000) < habitChance ? favoriteGuess : random.Range(0, 6);
            call = fingers + guess;
            if (state.History.Count < 3 || random.Range(0, 1000) >= state.Strength) return;
            int best = int.MinValue, ties = 0;
            for (int finger = 0; finger <= 5; finger++)
            for (int predicted = 0; predicted <= 5; predicted++)
            {
                int score = 0;
                for (int i = 0; i < state.History.Count; i++)
                {
                    int otherFinger = state.History[i] / 6, otherGuess = state.History[i] % 6;
                    bool ownCorrect = predicted == otherFinger, playerCorrect = otherGuess == finger;
                    if (ownCorrect != playerCorrect) score += (ownCorrect ? 1 : -1) * (i + 1);
                }
                if (score < best) continue;
                if (score > best) { best = score; ties = 0; }
                if (random.Range(0, ++ties) == 0) { fingers = finger; call = finger + predicted; }
            }
        }
    }
}
