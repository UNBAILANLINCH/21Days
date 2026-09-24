// 职责：对白表现参数；通用 UIConfig 不包含阅读速度，内容仍由内容表提供。
using System.Collections.Generic;
using UnityEngine;

namespace Game.Dialogue
{
    [CreateAssetMenu(menuName = "21Days/Dialogue/Config")]
    public sealed class DialogueConfig : ScriptableObject
    {
        [SerializeField, Min(1)] private float charactersPerSecond = 35f;
        [SerializeField, Min(1)] private int historyLimit = 500;
        [SerializeField] private float[] speedSteps = { 1f, 2f, 4f };
        [SerializeField, Min(1)] private int revealTapCount = 3;
        [SerializeField, Min(0.05f)] private float tapWindowSeconds = 0.5f;
        [SerializeField, Min(0)] private float autoAdvanceSeconds = 1.5f;
        public float CharactersPerSecond => charactersPerSecond;
        public int HistoryLimit => historyLimit;
        public IReadOnlyList<float> SpeedSteps => speedSteps;
        public int RevealTapCount => revealTapCount;
        public float TapWindowSeconds => tapWindowSeconds;
        public float AutoAdvanceSeconds => autoAdvanceSeconds;

        // 非法配置（如空倍速表）在这里抛 ArgumentException，由调用方在对话开始前暴露。
        public DialoguePlaybackSettings ToPlaybackSettings() =>
            new DialoguePlaybackSettings(charactersPerSecond, speedSteps, revealTapCount, tapWindowSeconds, autoAdvanceSeconds);
    }
}
